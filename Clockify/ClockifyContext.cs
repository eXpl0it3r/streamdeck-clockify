using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ClockifyClient;
using ClockifyClient.Models;
using Microsoft.Kiota.Abstractions;

namespace Clockify;

public class ClockifyContext
{
    private const int MaxPageSize = 5000;

    private readonly Logger _logger;

    private string _apiKey = string.Empty;
    private bool _billable = true;
    private string _clientName = string.Empty;

    private ClockifyApiClient _clockifyClient;
    private UserDtoV1 _currentUser = new();
    private string _projectName = string.Empty;
    private string _serverUrl = string.Empty;
    private string _tags = string.Empty;
    private string _taskName = string.Empty;
    private string _timerName = string.Empty;
    private string _workspaceName = string.Empty;
    private List<WorkspaceDtoV1> _workspaces = new();

    public ClockifyContext(Logger logger)
    {
        _logger = logger;
    }

    public bool IsValid()
    {
        return _clockifyClient != null;
    }

    public async Task<bool> ToggleTimerAsync()
    {
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceName))
        {
            _logger.LogWarn($"Invalid settings for toggle {_workspaceName}, {_projectName}, {_timerName}");
            return false;
        }

        var runningTimer = await GetRunningTimerAsync();

        await StopRunningTimerAsync();

        if (runningTimer != null)
        {
            _logger.LogDebug("Toggle successful, timer has been stopped");
            return true;
        }

        var workspace = _workspaces.SingleOrDefault(w => w.Name == _workspaceName);
        if (workspace == null)
        {
            return false;
        }

        var tags = await FindMatchingTagsAsync(workspace.Id, _tags);

        var timeEntryRequest = new CreateTimeEntryRequest
        {
            Description = _timerName ?? string.Empty,
            Start = DateTimeOffset.UtcNow,
            TagIds = tags,
            Billable = _billable
        };

        if (!string.IsNullOrEmpty(_projectName))
        {
            var project = await FindMatchingProjectAsync(workspace.Id);

            if (project is null)
            {
                _logger.LogDebug("There's no match project");
                return false;
            }

            timeEntryRequest.ProjectId = project.Id;

            if (!string.IsNullOrEmpty(_taskName))
            {
                var taskId = await FindOrCreateTaskAsync(workspace.Id, project.Id, _taskName);
                if (taskId is not null)
                {
                    timeEntryRequest.TaskId = taskId;
                }
            }
        }

        try
        {
            await _clockifyClient.V1.Workspaces[workspace.Id].TimeEntries.PostAsync(timeEntryRequest);
            _logger.LogInfo($"Toggle Timer {_workspaceName}, {_projectName}, {_taskName}, {_timerName}");
            return true;
        }
        catch (ApiException exception)
        {
            _logger.LogError($"TimeEntry creation failed: {exception.Message}");
            return false;
        }
    }

    public async Task<TimeEntryWithRatesDtoV1> GetRunningTimerAsync()
    {
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceName))
        {
            _logger.LogWarn($"Invalid settings for running timer {_workspaceName}");
            return null;
        }

        var workspace = _workspaces.SingleOrDefault(w => w.Name == _workspaceName);
        if (workspace == null)
        {
            return null;
        }

        try
        {
            var timeEntries = await _clockifyClient.V1.Workspaces[workspace.Id].User[_currentUser.Id].TimeEntries
                .GetAsync(p => p.QueryParameters.InProgress = true);
            
            if (string.IsNullOrEmpty(_projectName))
            {
                return timeEntries.FirstOrDefault(t => string.IsNullOrEmpty(_timerName) || t.Description == _timerName);
            }
            
            var project = await FindMatchingProjectAsync(workspace.Id);

            if (project is null)
            {
                return null;
            }

            var task = await FindMatchingTaskAsync(workspace.Id, project.Id, _taskName);

            var tags = await FindMatchingTagsAsync(workspace.Id, _tags);

            return timeEntries.FirstOrDefault(t => t.ProjectId == project.Id
                                                   && (string.IsNullOrEmpty(_timerName) || t.Description == _timerName)
                                                   && (string.IsNullOrEmpty(_taskName) || string.IsNullOrEmpty(task) ||
                                                       t.TaskId == task)
                                                   && t.TagIds.OrderBy(s => s, StringComparer.InvariantCulture)
                                                       .SequenceEqual(tags.OrderBy(s => s, StringComparer.InvariantCulture))
                                                   && t.Billable == _billable);
        }
        catch (Exception exception) when ( exception is ApiException or HttpRequestException)
        {
            return null;
        }
    }

    public async Task UpdateSettingsAsync(PluginSettings settings)
    {
        if (_clockifyClient == null || settings.ApiKey != _apiKey || settings.ServerUrl != _serverUrl)
        {
            if (!Uri.IsWellFormedUriString(settings.ServerUrl, UriKind.Absolute))
            {
                _logger.LogWarn("Server URL is invalid");
                return;
            }

            if (settings.ApiKey.Length != 48)
            {
                _logger.LogWarn("Invalid API key format");
                return;
            }

            _serverUrl = settings.ServerUrl;
            _apiKey = settings.ApiKey;

            _clockifyClient = ClockifyApiClientFactory.Create(_apiKey, settings.ServerUrl);

            if (!await TestConnectionAsync())
            {
                _logger.LogWarn("Invalid server URL or API key");
                _clockifyClient = null;
                _currentUser = new UserDtoV1();
                return;
            }

            _logger.LogInfo("Connection to Clockify successfully established");
        }

        if (!_workspaces.Any() || settings.WorkspaceName != _workspaceName)
        {
            await ReloadCacheAsync();
        }

        _workspaceName = settings.WorkspaceName;
        _projectName = settings.ProjectName;
        _taskName = settings.TaskName;
        _timerName = settings.TimerName;
        _clientName = settings.ClientName;
        _tags = settings.Tags;
        _billable = settings.Billable;
    }

    private async Task ReloadCacheAsync()
    {
        try
        {
            var workspaces = await _clockifyClient.V1.Workspaces.GetAsync();
            _workspaces = workspaces;
        }
        catch (ApiException exception)
        {
            _logger.LogWarn($"Unable to retrieve available workspaces: {exception.Message}");
        }
    }

    private async Task StopRunningTimerAsync()
    {
        if (_clockifyClient == null || string.IsNullOrWhiteSpace(_workspaceName))
        {
            return;
        }

        var workspace = _workspaces.SingleOrDefault(w => w.Name == _workspaceName);
        if (workspace == null)
        {
            return;
        }

        var runningTimer = await GetRunningTimerAsync();
        if (runningTimer == null)
        {
            return;
        }

        var timerUpdate = new UpdateTimeEntryRequest
        {
            Billable = runningTimer.Billable,
            Start = runningTimer.TimeInterval.Start,
            End = DateTimeOffset.UtcNow,
            ProjectId = runningTimer.ProjectId,
            TaskId = runningTimer.TaskId,
            Description = runningTimer.Description,
            TagIds = runningTimer.TagIds
        };

        try
        {
            await _clockifyClient.V1.Workspaces[workspace.Id].TimeEntries[runningTimer.Id].PutAsync(timerUpdate);
            _logger.LogInfo($"Timer Stopped {_workspaceName}, {runningTimer.ProjectId}, {runningTimer.TaskId}, {runningTimer.Description}");
        }
        catch (ApiException exception)
        {
            _logger.LogWarn($"Failed to stop running timer {_workspaceName}, {runningTimer.ProjectId}, {runningTimer.TaskId}, {runningTimer.Description}: {exception.Message}");
        }
    }

    private async Task<ProjectDtoV1> FindMatchingProjectAsync(string workspaceId)
    {
        try
        {
            string clientId = null;

            if (!string.IsNullOrWhiteSpace(_clientName))
            {
                clientId = await FindMatchingClientAsync(workspaceId, _clientName);
            }

            var projects = await _clockifyClient.V1.Workspaces[workspaceId].Projects
                .GetAsync(q =>
                {
                    q.QueryParameters.Name = _projectName;
                    q.QueryParameters.StrictNameSearch = "true";
                    q.QueryParameters.PageSize = MaxPageSize.ToString();

                    if (clientId is not null)
                    {
                        q.QueryParameters.Clients = clientId;
                    }
                });

            if (projects.Count > 1)
            {
                _logger.LogWarn($"Multiple projects with the name {_projectName} on workspace {_workspaceName}, consider setting a client name");
                return null;
            }

            if (!projects.Any())
            {
                _logger.LogWarn($"Unable to find project {_projectName} on workspace {_workspaceName} for client {_clientName}");
                return null;
            }

            return projects.Single();
        }
        catch (ApiException exception)
        {
            _logger.LogWarn($"Unable to retrieve project {_projectName} on workspace {_workspaceName}: {exception.Message}");
            return null;
        }
    }

    private async Task<string> FindMatchingClientAsync(string workspaceId, string clientName)
    {
        try
        {
            var clientResponse = await _clockifyClient.V1.Workspaces[workspaceId].Clients
                .GetAsync(q =>
                {
                    q.QueryParameters.Name = clientName;
                    q.QueryParameters.PageSize = MaxPageSize.ToString();
                });
            
            return clientResponse.FirstOrDefault()?.Id;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private async Task<string> FindMatchingTaskAsync(string workspaceId, string projectId, string taskName)
    {
        try
        {
            var taskResponse = await _clockifyClient.V1.Workspaces[workspaceId].Projects[projectId].Tasks
                .GetAsync(q =>
                {
                    q.QueryParameters.Name = taskName;
                    q.QueryParameters.StrictNameSearch = "true";
                    q.QueryParameters.PageSize = MaxPageSize.ToString();
                });

            return taskResponse.FirstOrDefault()?.Id;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private async Task<string> FindOrCreateTaskAsync(string workspaceId, string projectId, string taskName)
    {
        var task = await FindMatchingTaskAsync(workspaceId, projectId, taskName);

        if (!string.IsNullOrEmpty(task))
        {
            return task;
        }

        var taskRequest = new TaskRequestV1
        {
            Name = taskName
        };

        try
        {
            var creationResponse = await _clockifyClient.V1.Workspaces[workspaceId].Projects[projectId].Tasks.PostAsync(taskRequest);
            return creationResponse.Id;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private async Task<List<string>> FindMatchingTagsAsync(string workspaceId, string tags)
    {
        var tagList = tags.Replace("\\,", "█")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace("█", ","))
            .ToArray();

        if (tagList.Length == 0)
        {
            return new List<string>();
        }

        try
        {
            var tagsOnWorkspace = await _clockifyClient.V1.Workspaces[workspaceId].Tags
                .GetAsync(q => q.QueryParameters.PageSize = MaxPageSize.ToString());
            return tagsOnWorkspace.Where(t => tagList.Contains(t.Name)).Select(t => t.Id).ToList();
        }
        catch (ApiException)
        {
            return new List<string>();
        }
    }

    private async Task<bool> TestConnectionAsync()
    {
        if (_clockifyClient == null)
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
            _logger.LogDebug($"Connection test failed for: {exception.Message}");
            return false;
        }
    }
}