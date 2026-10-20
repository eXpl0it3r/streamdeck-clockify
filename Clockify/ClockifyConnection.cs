using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ClockifyClient;
using ClockifyClient.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;

namespace Clockify;

/// <summary>
/// One connection per API key and server, shared by all buttons, so they also share the request budget.
/// Clockify's Free plan only allows 30 requests per hour for the whole workspace.
/// </summary>
public sealed class ClockifyConnection
{
    // Used when Clockify doesn't send a Retry-After header
    private static readonly TimeSpan DefaultBackOff = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<(string ApiKey, string ServerUrl), ClockifyConnection> Connections = new();

    private readonly Logger _logger;
    private readonly ClockifyApiClient _client;
    private readonly SemaphoreSlim _lookupLock = new(1, 1);
    private readonly ConcurrentDictionary<(string WorkspaceId, string UserId), RunningTimers> _runningTimers = new();
    private readonly Queue<DateTimeOffset> _recentRequests = new();

    private UserDtoV1 _user;
    private List<WorkspaceDtoV1> _workspaces;
    private long _blockedUntilTicks = DateTimeOffset.MinValue.UtcTicks;

    private ClockifyConnection(string apiKey, string serverUrl, Logger logger)
    {
        _logger = logger;

        // Kiota retries 429 responses by default, which only burns more of the budget.
        // ShouldRetry is called for every response, not just the retryable ones, so it has to check the status itself.
        var retryOption = new RetryHandlerOption
        {
            ShouldRetry = (_, _, response) => response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
        };
        var httpClient = KiotaClientFactory.Create(KiotaClientFactory.GetDefaultHttpMessageHandler(), [retryOption]);
        var adapter = new HttpClientRequestAdapter(new ApiKeyAuthenticationProvider(apiKey), httpClient: httpClient)
        {
            BaseUrl = serverUrl
        };

        _client = new ClockifyApiClient(adapter);
    }

    public DateTimeOffset BlockedUntil => new(Interlocked.Read(ref _blockedUntilTicks), TimeSpan.Zero);

    public bool IsBlocked => DateTimeOffset.UtcNow < BlockedUntil;

    /// <summary>
    /// Set once Clockify answered with 429, buttons then poll less often for the rest of the session.
    /// </summary>
    public bool WasRateLimited { get; private set; }

    public static ClockifyConnection Get(string apiKey, string serverUrl, Logger logger)
    {
        return Connections.GetOrAdd((apiKey, serverUrl), key => new ClockifyConnection(key.ApiKey, key.ServerUrl, logger));
    }

    public RunningTimers GetRunningTimers(string workspaceId, string userId)
    {
        return _runningTimers.GetOrAdd((workspaceId, userId), key => new RunningTimers(this, key.WorkspaceId, key.UserId));
    }

    /// <summary>
    /// Sends a request unless the rate limit is active.
    /// Throws <see cref="RateLimitedException"/> instead of sending, or when Clockify answers with 429.
    /// </summary>
    public async Task<T> SendAsync<T>(string description, Func<ClockifyApiClient, Task<T>> request)
    {
        var blockedUntil = BlockedUntil;
        if (DateTimeOffset.UtcNow < blockedUntil)
        {
            throw new RateLimitedException(blockedUntil);
        }

        _logger.LogInfo($"Clockify request: {description} ({CountRequest()} in the last hour)");

        try
        {
            return await request(_client);
        }
        catch (ApiException exception) when (exception.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
        {
            blockedUntil = DateTimeOffset.UtcNow + GetRetryAfter(exception);
            Interlocked.Exchange(ref _blockedUntilTicks, blockedUntil.UtcTicks);
            WasRateLimited = true;

            _logger.LogWarn($"Clockify rate limit reached, pausing all requests until {blockedUntil.ToLocalTime():HH:mm:ss}");
            throw new RateLimitedException(blockedUntil);
        }
    }

    public async Task<UserDtoV1> GetUserAsync()
    {
        await _lookupLock.WaitAsync();
        try
        {
            return _user ??= await SendAsync("get user", c => c.V1.User.GetAsync());
        }
        finally
        {
            _lookupLock.Release();
        }
    }

    /// <summary>
    /// Workspaces rarely change, so they're fetched once per connection.
    /// When a name can't be found the list is fetched again, in case the workspace was just created or renamed.
    /// </summary>
    public async Task<WorkspaceDtoV1> FindWorkspaceAsync(string name)
    {
        await _lookupLock.WaitAsync();
        try
        {
            var workspace = _workspaces?.SingleOrDefault(w => w.Name == name);
            if (workspace is not null)
            {
                return workspace;
            }

            _workspaces = await SendAsync("get workspaces", c => c.V1.Workspaces.GetAsync()) ?? [];
            return _workspaces.SingleOrDefault(w => w.Name == name);
        }
        finally
        {
            _lookupLock.Release();
        }
    }

    private int CountRequest()
    {
        lock (_recentRequests)
        {
            var now = DateTimeOffset.UtcNow;
            _recentRequests.Enqueue(now);

            while (_recentRequests.Peek() < now.AddHours(-1))
            {
                _recentRequests.Dequeue();
            }

            return _recentRequests.Count;
        }
    }

    private static TimeSpan GetRetryAfter(ApiException exception)
    {
        var value = exception.ResponseHeaders?
            .FirstOrDefault(h => string.Equals(h.Key, "Retry-After", StringComparison.OrdinalIgnoreCase))
            .Value?.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultBackOff;
        }

        // Retry-After is either a number of seconds or an HTTP date
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) && date > DateTimeOffset.UtcNow)
        {
            return date - DateTimeOffset.UtcNow;
        }

        return DefaultBackOff;
    }
}
