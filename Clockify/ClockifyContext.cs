using System;
using System.Linq;
using System.Threading.Tasks;
using Clockify.Net;
using Clockify.Net.Models.Projects;
using Clockify.Net.Models.Tasks;
using Clockify.Net.Models.TimeEntries;
using Clockify.Net.Models.Users;

namespace Clockify;

public class ClockifyContext
{
    private readonly Logger _logger;

    private ClockifyClient _clockifyClient;
    private CurrentUserDto _currentUser = new();
    
    private string _apiKey = string.Empty;
    private string _clientName = string.Empty;
    private string _projectName = string.Empty;
    private string _serverUrl = string.Empty;
    private string _taskName = string.Empty;
    private string _timerName = string.Empty;
    private string _workspaceName = string.Empty;

    public ClockifyContext(Logger logger)
    {
        _logger = logger;
    }

    public bool IsValid()
    {
        return _clockifyClient != null;
    }

    public async Task ToggleTimerAsync()
    {
        // TODO Validation for project
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceName))
        {
            _logger.LogWarn($"Invalid settings for toggle {_workspaceName}, {_projectName}, {_timerName}");
            return;
        }

        var runningTimer = await GetRunningTimerAsync();

        await StopRunningTimerAsync();

        if (runningTimer != null)
        {
            return;
        }
        
        var workspaces = await _clockifyClient.GetWorkspacesAsync();
        if (!workspaces.IsSuccessful || workspaces.Data is null)
        {
            _logger.LogWarn("Unable to retrieve available workspaces");
            return;
        }

        var workspace = workspaces.Data.Single(w => w.Name == _workspaceName);
        var timeEntryRequest = new TimeEntryRequest
        {
            UserId = _currentUser.Id,
            WorkspaceId = workspace.Id,
            Description = _timerName ?? string.Empty,
            Start = DateTimeOffset.UtcNow
        };

        if (!string.IsNullOrEmpty(_projectName))
        {
            var project = await FindMatchingProjectAsync(workspace.Id);

            if (project is null)
            {
                return;
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

        await _clockifyClient.CreateTimeEntryAsync(workspace.Id, timeEntryRequest);
        _logger.LogInfo($"Toggle Timer {_workspaceName}, {_projectName}, {_taskName}, {_timerName}");
    }

    public async Task<TimeEntryDtoImpl> GetRunningTimerAsync()
    {
        // TODO Validation for project
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceName))
        {
            _logger.LogWarn($"Invalid settings for running timer {_workspaceName}");
            return null;
        }

        var workspaces = await _clockifyClient.GetWorkspacesAsync();
        if (!workspaces.IsSuccessful || workspaces.Data is null)
        {
            _logger.LogWarn("Unable to retrieve available workspaces");
            return null;
        }

        var workspace = workspaces.Data.Single(w => w.Name == _workspaceName);
        var timeEntries = await _clockifyClient.FindAllTimeEntriesForUserAsync(workspace.Id, _currentUser.Id, inProgress: true);
        if (!timeEntries.IsSuccessful || timeEntries.Data is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(_projectName))
        {
            return string.IsNullOrEmpty(_timerName)
                ? timeEntries.Data.FirstOrDefault()
                : timeEntries.Data.FirstOrDefault(t => t.Description == _timerName);
        }

        var project = await FindMatchingProjectAsync(workspace.Id);

        if (project is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(_timerName)
            ? timeEntries.Data.FirstOrDefault(t => t.ProjectId == project.Id)
            : timeEntries.Data.FirstOrDefault(t => t.ProjectId == project.Id && t.Description == _timerName);
    }

    public async Task UpdateSettings(PluginSettings settings)
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

            _clockifyClient = new ClockifyClient(_apiKey, settings.ServerUrl);
        }

        _workspaceName = settings.WorkspaceName;
        _projectName = settings.ProjectName;
        _taskName = settings.TaskName;
        _timerName = settings.TimerName;
        _clientName = settings.ClientName;

        if (await TestConnectionAsync())
        {
            _logger.LogInfo("Connection to Clockify successfully established");
            return;
        }

        _logger.LogWarn("Invalid server URL or API key");
        _clockifyClient = null;
        _currentUser = new CurrentUserDto();
    }

    private async Task StopRunningTimerAsync()
    {
        if (_clockifyClient == null || string.IsNullOrWhiteSpace(_workspaceName))
        {
            return;
        }

        var workspaces = await _clockifyClient.GetWorkspacesAsync();
        if (!workspaces.IsSuccessful || workspaces.Data is null)
        {
            _logger.LogWarn("Unable to retrieve available workspaces");
            return;
        }

        var workspace = workspaces.Data.Single(w => w.Name == _workspaceName);
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
            Description = runningTimer.Description
        };

        await _clockifyClient.UpdateTimeEntryAsync(workspace.Id, runningTimer.Id, timerUpdate);
        _logger.LogInfo($"Timer Stopped {_workspaceName}, {runningTimer.ProjectId}, {runningTimer.TaskId}, {runningTimer.Description}");
    }

    private async Task<ProjectDtoImpl> FindMatchingProjectAsync(string workspaceId)
    {
        var projects = await _clockifyClient.FindAllProjectsOnWorkspaceAsync(workspaceId, false, _projectName, pageSize: 5000);
        if (!projects.IsSuccessful || projects.Data is null)
        {
            _logger.LogWarn($"Unable to retrieve project {_projectName} on workspace {_workspaceName}");
            return null;
        }

        var project = projects.Data
                              .Where(p => p.Name == _projectName && (string.IsNullOrWhiteSpace(_clientName) || p.ClientName == _clientName))
                              .ToList();

        if (project.Count > 1)
        {
            _logger.LogWarn($"Multiple projects with the name {_projectName} on workspace {_workspaceName}, consider setting a client name");
            return null;
        }

        if (!project.Any())
        {
            _logger.LogWarn($"Unable to find project {_projectName} on workspace {_workspaceName} for client {_clientName}");
            return null;
        }

        return project.Single();
    }

    private async Task<string> FindOrCreateTaskAsync(string workspaceId, string projectId, string taskName)
    {
        var taskResponse = await _clockifyClient.FindAllTasksAsync(workspaceId, projectId, name: taskName, pageSize: 5000);

        if (!taskResponse.IsSuccessful || taskResponse.Data == null)
        {
            return null;
        }

        if (taskResponse.Data.Any())
        {
            return taskResponse.Data.First().Id;
        }

        var taskRequest = new TaskRequest
        {
            Name = taskName
        };

        var creationResponse = await _clockifyClient.CreateTaskAsync(workspaceId, projectId, taskRequest);

        if (!creationResponse.IsSuccessful || creationResponse.Data == null)
        {
            return null;
        }

        return creationResponse.Data.Id;
    }

    private async Task<bool> TestConnectionAsync()
    {
        if (_clockifyClient == null)
        {
            return false;
        }

        var user = await _clockifyClient.GetCurrentUserAsync();
        if (!user.IsSuccessful || user.Data is null)
        {
            return false;
        }

        _currentUser = user.Data;
        return true;
    }
}