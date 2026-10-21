using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClockifyClient.Models;
using Microsoft.Kiota.Abstractions;

namespace Clockify;

public enum ToggleResult
{
    Started,
    Stopped,
    Failed,
    RateLimited
}

public class ClockifyService(Logger logger)
{
    private const int MaxPageSize = 5000;

    // Typing in the property inspector sends the settings on every key stroke
    private static readonly TimeSpan SettingsDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);

    // Before toggling, the running timer is fetched again if it's older than this
    private static readonly TimeSpan MaxStateAgeOnToggle = TimeSpan.FromSeconds(60);

    // Polling interval used after Clockify answered with 429, no matter the refresh rate setting
    private static readonly TimeSpan RateLimitedRefreshRate = TimeSpan.FromMinutes(5);

    // Automatic refresh rates, the Free plan allows 30 requests per hour, paid plans 20 per second
    private static readonly TimeSpan FreePlanRefreshRate = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PaidPlanRefreshRate = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _timeOut = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private PluginSettings _settings = new();
    private string _fingerprint = string.Empty;
    private bool _settingsValid;

    private ClockifyConnection _connection;
    private RunningTimers _runningTimers;
    private ResolvedIds _resolvedIds;

    private DateTimeOffset _nextResolveAttempt = DateTimeOffset.MinValue;
    private TimeSpan _retryDelay = MinRetryDelay;

    public bool IsValid => _resolvedIds is not null && _runningTimers is not null;

    public ResolvedIds ResolvedIds => _resolvedIds;

    public bool IsRateLimited => _connection?.IsBlocked == true;

    public void UpdateSettings(PluginSettings settings)
    {
        settings = new PluginSettings(settings);
        SettingsValidator.MigrateServerUrl(settings);

        var fingerprint = ResolvedIds.CreateFingerprint(settings);
        var fingerprintChanged = fingerprint != _fingerprint;

        _settings = settings;
        _fingerprint = fingerprint;

        if (!fingerprintChanged)
        {
            return;
        }

        logger.LogInfo($"Updating settings: {settings}");

        var validation = SettingsValidator.Validate(settings);
        _settingsValid = validation.IsValid;
        if (!_settingsValid)
        {
            logger.LogError($"Updating settings failed, settings validation failed: {validation.Error}");
        }

        _connection = _settingsValid ? ClockifyConnection.Get(settings.ApiKey, settings.ServerUrl, logger) : null;
        _resolvedIds = null;
        _runningTimers = null;
        _retryDelay = MinRetryDelay;
        _nextResolveAttempt = DateTimeOffset.UtcNow + SettingsDebounce;

        // IDs stored by an earlier run, no need to look them up again
        if (_settingsValid && settings.ResolvedIds?.Fingerprint == fingerprint && settings.ResolvedIds.IsComplete)
        {
            logger.LogInfo("Updating settings, using stored IDs");
            UseResolvedIds(settings.ResolvedIds);
        }
    }

    public async Task<bool> TryResolveAsync(bool userInitiated)
    {
        if (IsValid)
        {
            return true;
        }

        if (!_settingsValid || (!userInitiated && DateTimeOffset.UtcNow < _nextResolveAttempt))
        {
            return false;
        }

        if (!await _cacheLock.WaitAsync(_timeOut))
        {
            return false;
        }

        try
        {
            return await ResolveAsync();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task RefreshAsync()
    {
        if (!IsValid || _settings.RefreshRate == 0)
        {
            return;
        }

        var refreshRate = GetRefreshRate();
        if (_connection.WasRateLimited && refreshRate < RateLimitedRefreshRate)
        {
            refreshRate = RateLimitedRefreshRate;
        }

        try
        {
            await _runningTimers.SyncAsync(refreshRate);
        }
        catch (RateLimitedException exception)
        {
            logger.LogWarn($"Fetching running timer skipped: {exception.Message}");
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogError($"Fetching running timer failed: {exception.Message}");
        }
    }

    public RunningEntry GetRunningTimer()
    {
        return IsValid ? _runningTimers.Entries.FirstOrDefault(Matches) : null;
    }

    public async Task<ToggleResult> ToggleTimerAsync()
    {
        logger.LogInfo("Toggling timer...");

        if (!await _cacheLock.WaitAsync(_timeOut))
        {
            return ToggleResult.Failed;
        }

        try
        {
            if (!IsValid && (!_settingsValid || !await ResolveAsync()))
            {
                logger.LogError($"Toggling timer failed, invalid settings: {_settings}");
                return IsRateLimited ? ToggleResult.RateLimited : ToggleResult.Failed;
            }

            // Only fetch the running timer if it might be outdated, e.g. when it was changed in the web app
            await _runningTimers.SyncAsync(MaxStateAgeOnToggle, force: true);

            return GetRunningTimer() is not null
                ? await StopTimerAsync()
                : await StartTimerAsync();
        }
        catch (RateLimitedException exception)
        {
            logger.LogWarn($"Toggling timer failed: {exception.Message}");
            return ToggleResult.RateLimited;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogError($"Toggling timer failed: {exception.Message}");
            return ToggleResult.Failed;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<ToggleResult> StopTimerAsync()
    {
        try
        {
            // Stops whatever timer runs for the user, which is the matching one, see the sync above
            await _connection.SendAsync("stop timer",
                c => c.V1.Workspaces[_resolvedIds.WorkspaceId].User[_resolvedIds.UserId].TimeEntries
                      .PatchAsync(new StopTimeEntryRequest { End = DateTimeOffset.UtcNow }));
        }
        catch (ApiException exception) when (exception.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            logger.LogInfo("Stopping timer, timer was already stopped");
        }

        _runningTimers.SetStopped();
        logger.LogInfo("Toggling timer successful, timer has been stopped");
        return ToggleResult.Stopped;
    }

    private async Task<ToggleResult> StartTimerAsync()
    {
        if (!string.IsNullOrEmpty(_settings.TaskName) && string.IsNullOrEmpty(_resolvedIds.TaskId))
        {
            _resolvedIds.TaskId = await CreateTaskAsync();
        }

        var timeEntryRequest = new CreateTimeEntryRequest
        {
            Description = _settings.TimerName ?? string.Empty,
            Start = DateTimeOffset.UtcNow,
            TagIds = _resolvedIds.TagIds,
            Billable = _settings.Billable,
            ProjectId = _resolvedIds.ProjectId,
            TaskId = _resolvedIds.TaskId
        };

        try
        {
            var timeEntry = await _connection.SendAsync("start timer",
                c => c.V1.Workspaces[_resolvedIds.WorkspaceId].TimeEntries.PostAsync(timeEntryRequest));

            _runningTimers.SetStarted(timeEntry is not null
                ? RunningEntry.From(timeEntry)
                : new RunningEntry(null, timeEntryRequest.Start, timeEntryRequest.ProjectId, timeEntryRequest.TaskId, timeEntryRequest.Description, timeEntryRequest.TagIds, timeEntryRequest.Billable));

            logger.LogInfo("Toggling timer successful, timer has been started");
            return ToggleResult.Started;
        }
        catch (ApiException exception) when (exception.ResponseStatusCode is (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.NotFound)
        {
            // The stored IDs might be outdated, e.g. the project was deleted, look them up again next time
            logger.LogError($"Starting timer failed, looking up the IDs again on the next try: {exception.Message}");
            ForgetResolvedIds();
            return ToggleResult.Failed;
        }
    }

    private async Task<bool> ResolveAsync()
    {
        if (IsValid)
        {
            return true;
        }

        logger.LogInfo("Looking up Clockify IDs...");

        try
        {
            var resolvedIds = await LookUpIdsAsync();

            if (resolvedIds is null)
            {
                ScheduleRetry();
                return false;
            }

            UseResolvedIds(resolvedIds);
            logger.LogInfo($"Looking up Clockify IDs successful, workspace plan '{resolvedIds.Plan}'");
            return true;
        }
        catch (RateLimitedException exception)
        {
            logger.LogWarn($"Looking up Clockify IDs skipped: {exception.Message}");
            _nextResolveAttempt = exception.BlockedUntil;
            return false;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogError($"Looking up Clockify IDs failed, check the API key and server URL: {exception.Message}");
            ScheduleRetry();
            return false;
        }
    }

    private async Task<ResolvedIds> LookUpIdsAsync()
    {
        var user = await _connection.GetUserAsync();
        if (string.IsNullOrEmpty(user?.Id))
        {
            logger.LogError("Looking up Clockify IDs failed, unable to get the user of the API key");
            return null;
        }

        var workspace = await _connection.FindWorkspaceAsync(_settings.WorkspaceName);
        if (workspace is null)
        {
            logger.LogError($"Looking up Clockify IDs failed, no workspace named '{_settings.WorkspaceName}'");
            return null;
        }

        var resolvedIds = new ResolvedIds
        {
            Fingerprint = _fingerprint,
            UserId = user.Id,
            WorkspaceId = workspace.Id,
            Plan = workspace.FeatureSubscriptionType
        };

        if (!string.IsNullOrEmpty(_settings.ProjectName))
        {
            string clientId = null;

            if (!string.IsNullOrEmpty(_settings.ClientName))
            {
                clientId = (await FindMatchingClientAsync(workspace.Id, _settings.ClientName))?.Id;
                if (clientId is null)
                {
                    logger.LogError($"Looking up Clockify IDs failed, no client named '{_settings.ClientName}'");
                    return null;
                }
            }

            resolvedIds.ProjectId = (await FindMatchingProjectAsync(workspace.Id, _settings.ProjectName, clientId))?.Id;
            if (resolvedIds.ProjectId is null)
            {
                return null;
            }

            // A missing task gets created when the timer is started
            if (!string.IsNullOrEmpty(_settings.TaskName))
            {
                resolvedIds.TaskId = (await FindMatchingTaskAsync(workspace.Id, resolvedIds.ProjectId, _settings.TaskName))?.Id;
            }
        }

        resolvedIds.TagIds = await FindMatchingTagsAsync(workspace.Id, _settings.Tags);

        return resolvedIds;
    }

    private void UseResolvedIds(ResolvedIds resolvedIds)
    {
        _resolvedIds = resolvedIds;
        _runningTimers = _connection.GetRunningTimers(resolvedIds.WorkspaceId, resolvedIds.UserId);
        _retryDelay = MinRetryDelay;
    }

    private void ForgetResolvedIds()
    {
        _resolvedIds = null;
        _runningTimers = null;
        _nextResolveAttempt = DateTimeOffset.UtcNow;
    }

    private TimeSpan GetRefreshRate()
    {
        if (_settings.RefreshRate > 0)
        {
            return TimeSpan.FromSeconds(_settings.RefreshRate);
        }

        // A server that doesn't report a plan is treated like Free, to be on the safe side
        var isFreePlan = string.IsNullOrEmpty(_resolvedIds.Plan) || _resolvedIds.Plan.StartsWith("FREE", StringComparison.OrdinalIgnoreCase);
        return isFreePlan ? FreePlanRefreshRate : PaidPlanRefreshRate;
    }

    private void ScheduleRetry()
    {
        _nextResolveAttempt = DateTimeOffset.UtcNow + _retryDelay;
        logger.LogWarn($"Looking up Clockify IDs again at {_nextResolveAttempt.ToLocalTime():HH:mm:ss}, or when the button is pressed or the settings change");

        _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
    }

    private bool Matches(RunningEntry entry)
    {
        var timerNameMatches = string.IsNullOrEmpty(_settings.TimerName) || entry.Description == _settings.TimerName;

        if (string.IsNullOrEmpty(_settings.ProjectName))
        {
            return timerNameMatches;
        }

        return timerNameMatches
               && entry.ProjectId == _resolvedIds.ProjectId
               && (string.IsNullOrEmpty(_settings.TaskName) || string.IsNullOrEmpty(_resolvedIds.TaskId) || entry.TaskId == _resolvedIds.TaskId)
               && entry.TagIds.ToHashSet().SetEquals(_resolvedIds.TagIds ?? [])
               && entry.Billable == _settings.Billable;
    }

    private async Task<ProjectDtoV1> FindMatchingProjectAsync(string workspaceId, string projectName, string clientId)
    {
        var projects = await _connection.SendAsync("find project", c => c.V1.Workspaces[workspaceId].Projects
            .GetAsync(q =>
            {
                q.QueryParameters.Name = projectName;
                q.QueryParameters.StrictNameSearch = true;
                q.QueryParameters.PageSize = MaxPageSize;
                q.QueryParameters.Archived = false;

                if (clientId is not null)
                {
                    q.QueryParameters.Clients = [clientId];
                }
            }));

        if (projects is null || projects.Count == 0)
        {
            logger.LogError($"Finding matching project failed, no project named '{projectName}'");
            return null;
        }

        if (projects.Count > 1)
        {
            logger.LogError($"Finding matching project failed, multiple projects named '{projectName}' found, consider setting a client name");
            return null;
        }

        return projects.Single();
    }

    private async Task<ClientWithCurrencyDtoV1> FindMatchingClientAsync(string workspaceId, string clientName)
    {
        var clients = await _connection.SendAsync("find client", c => c.V1.Workspaces[workspaceId].Clients
            .GetAsync(q =>
            {
                q.QueryParameters.Name = clientName;
                q.QueryParameters.PageSize = MaxPageSize;
                q.QueryParameters.Archived = "false";
            }));

        return clients?.FirstOrDefault();
    }

    private async Task<TaskDtoV1> FindMatchingTaskAsync(string workspaceId, string projectId, string taskName)
    {
        var tasks = await _connection.SendAsync("find task", c => c.V1.Workspaces[workspaceId].Projects[projectId].Tasks
            .GetAsync(q =>
            {
                q.QueryParameters.Name = taskName;
                q.QueryParameters.StrictNameSearch = true;
                q.QueryParameters.PageSize = MaxPageSize;
            }));

        return tasks?.FirstOrDefault();
    }

    private async Task<string> CreateTaskAsync()
    {
        try
        {
            var task = await _connection.SendAsync("create task",
                c => c.V1.Workspaces[_resolvedIds.WorkspaceId].Projects[_resolvedIds.ProjectId].Tasks
                      .PostAsync(new TaskRequestV1 { Name = _settings.TaskName }));

            return task?.Id;
        }
        catch (ApiException exception)
        {
            logger.LogWarn($"Creating task failed, creation request failed for task {_settings.TaskName}: {exception.Message}");
            return null;
        }
    }

    private async Task<List<string>> FindMatchingTagsAsync(string workspaceId, string tags)
    {
        if (string.IsNullOrEmpty(tags))
        {
            return [];
        }

        // █ is used as temporary escape character
        var tagList = tags.Replace("\\,", "█")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace("█", ","))
            .ToArray();

        if (tagList.Length == 0)
        {
            return [];
        }

        var tagsOnWorkspace = await _connection.SendAsync("get tags",
            c => c.V1.Workspaces[workspaceId].Tags.GetAsync(q => q.QueryParameters.PageSize = MaxPageSize));

        var tagIds = tagsOnWorkspace?.Where(t => tagList.Contains(t.Name)).Select(t => t.Id).ToList() ?? [];
        if (tagIds.Count != tagList.Length)
        {
            logger.LogWarn($"Finding matching tags, not all tags of '{tags}' exist in the workspace");
        }

        return tagIds;
    }
}
