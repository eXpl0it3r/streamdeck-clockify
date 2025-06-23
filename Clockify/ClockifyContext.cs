using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clockify.Net;
using Clockify.Net.Models.Projects;
using Clockify.Net.Models.Tasks;
using Clockify.Net.Models.TimeEntries;
using Clockify.Net.Models.Users;
using Clockify.Net.Models.Workspaces;

namespace Clockify;

public class ClockifyContext
{
    private readonly Logger _logger;

    private ClockifyClient _clockifyClient;
    private CurrentUserDto _currentUser = new();
    private List<WorkspaceDto> _workspaces = new();
    
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

        var timeEntry = await _clockifyClient.CreateTimeEntryAsync(workspace.Id, timeEntryRequest);
        
        if (!timeEntry.IsSuccessful || timeEntry.Data == null)
        {
            _logger.LogError($"TimeEntry creation failed: {timeEntry.ErrorMessage}");
        }
        
        _logger.LogInfo($"Toggle Timer {_workspaceName}, {_projectName}, {_taskName}, {_timerName}");
        return true;
    }

    public async Task<TimeEntryDtoImpl> GetRunningTimerAsync()
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
        
        var timeEntries = await _clockifyClient.FindAllTimeEntriesForUserAsync(workspace.Id, _currentUser.Id, inProgress: true);
        if (!timeEntries.IsSuccessful || timeEntries.Data is null)
        {
            return null;
        }
        
        if (string.IsNullOrEmpty(_projectName))
        {
            return timeEntries.Data.FirstOrDefault(t => string.IsNullOrEmpty(_timerName) || t.Description == _timerName);
        }

        var project = await FindMatchingProjectAsync(workspace.Id);

        if (project is null)
        {
            return null;
        }
        
        var task = await FindMatchingTaskAsync(workspace.Id, project.Id, _taskName);

        return timeEntries.Data.FirstOrDefault(t => t.ProjectId == project.Id
                                                    && (string.IsNullOrEmpty(_timerName) || t.Description == _timerName)
                                                    && (string.IsNullOrEmpty(_taskName) || string.IsNullOrEmpty(task) || t.TaskId == task));
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

            if (!await TestConnectionAsync())
            {
                _logger.LogWarn("Invalid server URL or API key");
                _clockifyClient = null;
                _currentUser = new CurrentUserDto();
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
    }

    private async Task ReloadCacheAsync()
    {
        var workspaces = await _clockifyClient.GetWorkspacesAsync();
        if (!workspaces.IsSuccessful || workspaces.Data is null)
        {
            _logger.LogWarn($"Unable to retrieve available workspaces: {workspaces.ErrorMessage}");
            return;
        }

        _workspaces = workspaces.Data;
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
            _logger.LogWarn($"Unable to retrieve project {_projectName} on workspace {_workspaceName}: {projects.ErrorMessage}");
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

    private async Task<string> FindMatchingTaskAsync(string workspaceId, string projectId, string taskName)
    {
        var taskResponse = await _clockifyClient.FindAllTasksAsync(workspaceId, projectId, name: taskName, pageSize: 5000);

        if (!taskResponse.IsSuccessful || taskResponse.Data == null || !taskResponse.Data.Any())
        {
            return null;
        }

        return taskResponse.Data.First().Id;
    }

    private async Task<string> FindOrCreateTaskAsync(string workspaceId, string projectId, string taskName)
    {
        var task = await FindMatchingTaskAsync(workspaceId, projectId, taskName);

        if (!string.IsNullOrEmpty(task))
        {
            return task;
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