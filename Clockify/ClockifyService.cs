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

public class ClockifyService(Logger logger)
{
    private const int MaxPageSize = 5000;

    // Typing in the property inspector sends the settings on every key stroke
    private static readonly TimeSpan SettingsDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);

    private readonly TimeSpan _timeOut = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private PluginSettings _settings = new();
    private string _fingerprint = string.Empty;
    private bool _settingsValid;

    private ClockifyConnection _connection;
    private ResolvedIds _resolvedIds;

    private DateTimeOffset _nextResolveAttempt = DateTimeOffset.MinValue;
    private TimeSpan _retryDelay = MinRetryDelay;

    public bool IsValid => _resolvedIds is not null;

    public ResolvedIds ResolvedIds => _resolvedIds;

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

    public async Task<TimeEntryWithRatesDtoV1> FetchRunningTimerAsync()
    {
        if (!IsValid || !await _cacheLock.WaitAsync(_timeOut))
        {
            return null;
        }

        try
        {
            return await GetRunningTimerAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or RateLimitedException)
        {
            logger.LogError($"Fetching running timer failed: {exception.Message}");
            return null;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<bool> ToggleTimerAsync()
    {
        logger.LogInfo("Toggling timer...");

        if (!await _cacheLock.WaitAsync(_timeOut))
        {
            return false;
        }

        try
        {
            if (!IsValid && (!_settingsValid || !await ResolveAsync()))
            {
                logger.LogError($"Toggling timer failed, invalid settings: {_settings}");
                return false;
            }

            var runningTimer = await GetRunningTimerAsync();

            return runningTimer is not null
                ? await StopTimerAsync(runningTimer)
                : await StartTimerAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or RateLimitedException)
        {
            logger.LogError($"Toggling timer failed: {exception.Message}");
            return false;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<TimeEntryWithRatesDtoV1> GetRunningTimerAsync()
    {
        var timeEntries = await _connection.SendAsync("get running timer",
            c => c.V1.Workspaces[_resolvedIds.WorkspaceId].User[_resolvedIds.UserId].TimeEntries.GetAsync(p => p.QueryParameters.InProgress = "true"));

        return timeEntries?.FirstOrDefault(Matches);
    }

    private async Task<bool> StopTimerAsync(TimeEntryWithRatesDtoV1 runningTimer)
    {
        var timerUpdate = new UpdateTimeEntryRequest
        {
            Billable = runningTimer.Billable,
            Start = runningTimer.TimeInterval?.Start,
            End = DateTimeOffset.UtcNow,
            ProjectId = runningTimer.ProjectId,
            TaskId = runningTimer.TaskId,
            Description = runningTimer.Description,
            TagIds = runningTimer.TagIds
        };

        await _connection.SendAsync("stop timer",
            c => c.V1.Workspaces[_resolvedIds.WorkspaceId].TimeEntries[runningTimer.Id].PutAsync(timerUpdate));

        logger.LogInfo("Toggling timer successful, timer has been stopped");
        return true;
    }

    private async Task<bool> StartTimerAsync()
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
            await _connection.SendAsync("start timer",
                c => c.V1.Workspaces[_resolvedIds.WorkspaceId].TimeEntries.PostAsync(timeEntryRequest));

            logger.LogInfo("Toggling timer successful, timer has been started");
            return true;
        }
        catch (ApiException exception) when (exception.ResponseStatusCode is (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.NotFound)
        {
            // The stored IDs might be outdated, e.g. the project was deleted, look them up again next time
            logger.LogError($"Starting timer failed, looking up the IDs again on the next try: {exception.Message}");
            ForgetResolvedIds();
            return false;
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
            logger.LogInfo("Looking up Clockify IDs successful");
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
            WorkspaceId = workspace.Id
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
        _retryDelay = MinRetryDelay;
    }

    private void ForgetResolvedIds()
    {
        _resolvedIds = null;
        _nextResolveAttempt = DateTimeOffset.UtcNow;
    }

    private void ScheduleRetry()
    {
        _nextResolveAttempt = DateTimeOffset.UtcNow + _retryDelay;
        logger.LogWarn($"Looking up Clockify IDs again at {_nextResolveAttempt.ToLocalTime():HH:mm:ss}, or when the button is pressed or the settings change");

        _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
    }

    private bool Matches(TimeEntryWithRatesDtoV1 entry)
    {
        var timerNameMatches = string.IsNullOrEmpty(_settings.TimerName) || entry.Description == _settings.TimerName;

        if (string.IsNullOrEmpty(_settings.ProjectName))
        {
            return timerNameMatches;
        }

        return timerNameMatches
               && entry.ProjectId == _resolvedIds.ProjectId
               && (string.IsNullOrEmpty(_settings.TaskName) || string.IsNullOrEmpty(_resolvedIds.TaskId) || entry.TaskId == _resolvedIds.TaskId)
               && (entry.TagIds ?? []).ToHashSet().SetEquals(_resolvedIds.TagIds ?? [])
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
