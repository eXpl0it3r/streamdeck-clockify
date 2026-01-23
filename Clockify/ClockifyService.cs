using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ClockifyClient;
using ClockifyClient.Models;
using Microsoft.Kiota.Abstractions;

namespace Clockify;

public class ClockifyService(Logger logger)
{
    private const int MaxPageSize = 5000;

    private PluginSettings _settings = new();

    private ClockifyApiClient _clockifyClient;
    private UserDtoV1 _currentUser = new();
    private WorkspaceDtoV1 _workspace = new();
    private ProjectDtoV1 _project = new();
    private List<string> _tags = [];
    private TaskDtoV1 _task = new();
    private ClientWithCurrencyDtoV1 _client = new();

    public bool IsValid => _clockifyClient is not null
                           && !string.IsNullOrWhiteSpace(_settings.WorkspaceName)
                           && _workspace is not null;

    public async Task<bool> ToggleTimerAsync()
    {
        logger.LogInfo("Toggling timer...");
        
        if (!IsValid)
        {
            logger.LogError($"Toggling trimer failed, invalid settings: {_settings}");
            return false;
        }

        var runningTimer = await StopRunningTimerAsync();

        if (runningTimer is not null)
        {
            logger.LogInfo("Toggling trimer successful, timer has been stopped");
            return true;
        }

        try
        {
            var timeEntryRequest = await CreateTimeEntryRequestAsync();
            await _clockifyClient.V1.Workspaces[_workspace.Id].TimeEntries.PostAsync(timeEntryRequest);
            
            logger.LogInfo("Toggling trimer successful, timer has been started");
            return true;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogError($"Toggling trimer failed, TimeEntry creation failed: {exception.Message}");
            return false;
        }
    }

    public async Task<TimeEntryWithRatesDtoV1> GetRunningTimerAsync()
    {
        logger.LogInfo("Fetching running timer...");
        
        if (!IsValid)
        {
            logger.LogError($"Fetching running timer failed, invalid settings: {_settings}");
            return null;
        }

        try
        {
            var timeEntries = await _clockifyClient.V1.Workspaces[_workspace.Id].User[_currentUser.Id].TimeEntries
                .GetAsync(p => p.QueryParameters.InProgress = true);
            
            if (string.IsNullOrEmpty(_settings.ProjectName))
            {
                return timeEntries?.FirstOrDefault(t => string.IsNullOrEmpty(_settings.TimerName) || t.Description == _settings.TimerName);
            }
            
            if (_project is null)
            {
                logger.LogError($"Fetching running timer failed, no project in workspace matching {_settings.ProjectName}");
                return null;
            }

            return timeEntries?.FirstOrDefault(t => t.ProjectId == _project.Id
                                                    && (string.IsNullOrEmpty(_settings.TimerName) || t.Description == _settings.TimerName)
                                                    && (string.IsNullOrEmpty(_settings.TaskName) || string.IsNullOrEmpty(_task?.Id) || t.TaskId == _task.Id)
                                                    && ((t.TagIds is null && _tags is null) || t.TagIds is not null && _tags is not null && t.TagIds.OrderBy(s => s, StringComparer.InvariantCulture)
                                                        .SequenceEqual(_tags.OrderBy(s => s, StringComparer.InvariantCulture)))
                                                    && t.Billable == _settings.Billable);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogError($"Fetching running timer failed, TimeEntry request failed: {exception.Message}");
            return null;
        }
    }

    public async Task UpdateSettingsAsync(PluginSettings settings)
    {
        logger.LogInfo("Updating settings...");
        
        SettingsValidator.MigrateServerUrl(settings);
        
        var cacheInvalidationRequired = SettingsValidator.HasChanged(_settings, settings);
        
        // Do we need to recreate the client?
        if (!IsValid || _settings.ApiKey != settings.ApiKey || _settings.ServerUrl != settings.ServerUrl)
        {
            logger.LogInfo("Updating settings, recreate Clockify client");
            
            var validation = SettingsValidator.Validate(settings);

            if (!validation.IsValid)
            {
                logger.LogError($"Updating settings failed, settings validation failed: {validation.Error}");
                return;
            }

            _clockifyClient = ClockifyApiClientFactory.Create(settings.ApiKey, settings.ServerUrl);

            if (!await TestConnectionAsync())
            {
                logger.LogError("Updating settings failed, invalid server URL or API key");
                _clockifyClient = null;
                _currentUser = new UserDtoV1();
                return;
            }

            logger.LogInfo("Updating settings successful, connection to Clockify established");
            cacheInvalidationRequired = true;
        }

        _settings = settings;
        
        if (cacheInvalidationRequired)
        {
            await ReloadCacheAsync();
        }
    }

    private async Task<CreateTimeEntryRequest> CreateTimeEntryRequestAsync()
    {
        var timeEntryRequest = new CreateTimeEntryRequest
        {
            Description = _settings.TimerName ?? string.Empty,
            Start = DateTimeOffset.UtcNow,
            TagIds = _tags,
            Billable = _settings.Billable
        };

        if (string.IsNullOrEmpty(_settings.ProjectName) || _project is null)
        {
            return timeEntryRequest;
        }
        
        timeEntryRequest.ProjectId = _project.Id;

        if (string.IsNullOrEmpty(_settings.TaskName))
        {
            return timeEntryRequest;
        }
        
        var taskId = await FindOrCreateTaskAsync(_workspace.Id, _project.Id, _settings.TaskName);
        if (taskId is not null)
        {
            timeEntryRequest.TaskId = taskId;
        }

        return timeEntryRequest;
    }

    private async Task ReloadCacheAsync()
    {
        _workspace = null;
        _project = null;
        _tags = [];
        _task = null;
        _client = null;
        
        try
        {
            var workspaces = await _clockifyClient.V1.Workspaces.GetAsync();
            _workspace = workspaces?.SingleOrDefault(w => w.Name == _settings.WorkspaceName);

            if (_workspace != null)
            {
                _project = await FindMatchingProjectAsync(_workspace.Id, _settings.ProjectName);
                _tags = await FindMatchingTagsAsync(_workspace.Id, _settings.Tags);

                if (_project != null)
                {
                    _task = await FindMatchingTaskAsync(_workspace.Id, _project.Id, _settings.TaskName);
                    _client = await FindMatchingClientAsync(_workspace.Id, _settings.ClientName);
                }
            }
            
            logger.LogInfo("Reloading cache successful");
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Reloading cache failed, unable to retrieve workspaces: {exception.Message}");
        }
    }

    private async Task<TimeEntryWithRatesDtoV1> StopRunningTimerAsync()
    {
        if (!IsValid)
        {
            logger.LogError($"Stopping timer failed, invalid settings: {_settings}");
            return null;
        }

        var runningTimer = await GetRunningTimerAsync();
        if (runningTimer == null)
        {
            // No running timer
            return null;
        }

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

        try
        {
            await _clockifyClient.V1.Workspaces[_workspace.Id].TimeEntries[runningTimer.Id].PutAsync(timerUpdate);
            logger.LogInfo($"Stopping timer successful, timer in workspace {_settings.WorkspaceName}: {runningTimer.ProjectId}, {runningTimer.TaskId}, {runningTimer.Description}");
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogError($"Stopping time failed, timer in workspace {_settings.WorkspaceName}: {runningTimer.ProjectId}, {runningTimer.TaskId}, {runningTimer.Description}: {exception.Message}");
        }

        return runningTimer;
    }

    private async Task<ProjectDtoV1> FindMatchingProjectAsync(string workspaceId, string projectName)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            return null;
        }
        
        try
        {
            var projects = await _clockifyClient.V1.Workspaces[workspaceId].Projects
                .GetAsync(q =>
                {
                    q.QueryParameters.Name = projectName;
                    q.QueryParameters.StrictNameSearch = true;
                    q.QueryParameters.PageSize = MaxPageSize;

                    if (_client is not null)
                    {
                        q.QueryParameters.Clients = [_client.Id];
                    }
                });

            if (projects is null || !projects.Any())
            {
                logger.LogWarn($"Finding matching project failed, unable to find project: {_settings}");
                return null;
            }

            if (projects.Count > 1)
            {
                logger.LogWarn($"Finding matching project failed, multiple projects with the same name {projectName} found, consider setting a client name");
                return null;
            }
            
            logger.LogInfo("Finding matching project successful");
            return projects.Single();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Finding matching project failed, unable to retrieve project {projectName}: {exception.Message}");
            return null;
        }
    }

    private async Task<ClientWithCurrencyDtoV1> FindMatchingClientAsync(string workspaceId, string clientName)
    {
        if (string.IsNullOrEmpty(clientName))
        {
            return null;
        }
        
        try
        {
            var clientResponse = await _clockifyClient.V1.Workspaces[workspaceId].Clients
                .GetAsync(q =>
                {
                    q.QueryParameters.Name = clientName;
                    q.QueryParameters.PageSize = MaxPageSize;
                });
            
            return clientResponse?.FirstOrDefault();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Finding matching client failed, unable to retrieve client {clientName}: {exception.Message}");
            return null;
        }
    }

    private async Task<TaskDtoV1> FindMatchingTaskAsync(string workspaceId, string projectId, string taskName)
    {
        if (string.IsNullOrEmpty(taskName))
        {
            return null;
        }
        
        try
        {
            var taskResponse = await _clockifyClient.V1.Workspaces[workspaceId].Projects[projectId].Tasks
                .GetAsync(q =>
                {
                    q.QueryParameters.Name = taskName;
                    q.QueryParameters.StrictNameSearch = true;
                    q.QueryParameters.PageSize = MaxPageSize;
                });

            return taskResponse?.FirstOrDefault();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Finding matching task failed, unable to retrieve task {taskName}: {exception.Message}");
            return null;
        }
    }

    private async Task<string> FindOrCreateTaskAsync(string workspaceId, string projectId, string taskName)
    {
        if (string.IsNullOrEmpty(taskName))
        {
            return null;
        }
        
        var task = await FindMatchingTaskAsync(workspaceId, projectId, taskName);

        if (!string.IsNullOrEmpty(task?.Id))
        {
            return task.Id;
        }

        var taskRequest = new TaskRequestV1
        {
            Name = taskName
        };

        try
        {
            _task = await _clockifyClient.V1.Workspaces[workspaceId].Projects[projectId].Tasks.PostAsync(taskRequest);
            return _task?.Id;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Creating task failed, creation request failed for task {taskName}: {exception.Message}");
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

        if (!tagList.Any())
        {
            return [];
        }

        try
        {
            var tagsOnWorkspace = await _clockifyClient.V1.Workspaces[workspaceId].Tags
                .GetAsync(q => q.QueryParameters.PageSize = MaxPageSize);

            return tagsOnWorkspace == null ? [] : tagsOnWorkspace.Where(t => tagList.Contains(t.Name)).Select(t => t.Id).ToList();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Finding matching tags failed, unable to retrieve tags for {tags}: {exception.Message}");
            return [];
        }
    }

    private async Task<bool> TestConnectionAsync()
    {
        if (_clockifyClient is null)
        {
            return false;
        }

        try
        {
            var user = await _clockifyClient.V1.User.GetAsync();
            _currentUser = user;

            return true;
        }
        catch (Exception exception) when ( exception is ApiException or HttpRequestException)
        {
            logger.LogDebug($"Testing connection failed, {exception.Message}");
            return false;
        }
    }
}