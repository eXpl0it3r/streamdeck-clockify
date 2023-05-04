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

    private string _apiKey = string.Empty;

    private ClockifyClient _clockifyClient;
    private CurrentUserDto _currentUser = new();
    private string _projectName = string.Empty;
    private Dictionary<string, List<ProjectDtoImpl>> _projects = new();
    private string _serverUrl = string.Empty;
    private string _taskName = string.Empty;
    private string _timerName = string.Empty;
    private string _workspaceName = string.Empty;
    private List<WorkspaceDto> _workspaces = new();

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
        if (_clockifyClient == null
            || _workspaces.All(w => w.Name != _workspaceName)
            || !string.IsNullOrEmpty(_projectName) && (!_projects.ContainsKey(_workspaceName) || _projects[_workspaceName].All(p => p.Name != _projectName)))
        {
            _logger.LogWarn($"Invalid settings for toggle {_workspaceName}, {_projectName}, {_timerName}");
            return;
        }

        var runningTimer = await GetRunningTimerAsync();

        if (runningTimer != null)
        {
            await StopRunningTimerAsync();
            return;
        }

        await StopRunningTimerAsync();

        var workspace = _workspaces.Single(w => w.Name == _workspaceName);
        var timeEntryRequest = new TimeEntryRequest
        {
            UserId = _currentUser.Id,
            WorkspaceId = workspace.Id,
            Description = _timerName ?? string.Empty,
            Start = DateTimeOffset.UtcNow
        };

        if (!string.IsNullOrEmpty(_projectName))
        {
            var project = _projects[_workspaceName].Single(p => p.Name == _projectName);
            timeEntryRequest.ProjectId = project.Id;

            if (!string.IsNullOrEmpty(_taskName))
            {
                var taskId = await FindOrCreateTaskAsync(workspace, project, _taskName);
                if (taskId != null)
                {
                    timeEntryRequest.TaskId = taskId;
                }
            }
        }

        await _clockifyClient.CreateTimeEntryAsync(workspace.Id, timeEntryRequest);
        _logger.LogInfo($"Toggle Timer {_workspaceName}, {_projectName}, {_taskName}, {_timerName}");
    }

    public async Task StopRunningTimerAsync()
    {
        if (_clockifyClient == null || _workspaces.All(w => w.Name != _workspaceName))
        {
            return;
        }

        var workspace = _workspaces.Single(w => w.Name == _workspaceName);
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

    public async Task<TimeEntryDtoImpl> GetRunningTimerAsync()
    {
        if (_clockifyClient == null
            || _workspaces.All(w => w.Name != _workspaceName)
            || !string.IsNullOrEmpty(_projectName) && (!_projects.ContainsKey(_workspaceName) || _projects[_workspaceName].All(p => p.Name != _projectName)))
        {
            _logger.LogWarn($"Invalid settings for running timer {_workspaceName}");
            return null;
        }

        var workspace = _workspaces.Single(w => w.Name == _workspaceName);
        var timeEntries = await _clockifyClient.FindAllTimeEntriesForUserAsync(workspace.Id, _currentUser.Id, inProgress: true);
        if (!timeEntries.IsSuccessful || timeEntries.Data == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(_projectName))
        {
            return string.IsNullOrEmpty(_timerName)
                ? timeEntries.Data.FirstOrDefault()
                : timeEntries.Data.FirstOrDefault(t => t.Description == _timerName);
        }

        var project = _projects[_workspaceName].Single(p => p.Name == _projectName);
        return string.IsNullOrEmpty(_timerName)
            ? timeEntries.Data.FirstOrDefault(t => t.ProjectId == project.Id)
            : timeEntries.Data.FirstOrDefault(t => t.ProjectId == project.Id && t.Description == _timerName);
    }

    public async Task<bool> UpdateSettings(PluginSettings settings)
    {
        if (_clockifyClient == null || settings.ApiKey != _apiKey || settings.ServerUrl != _serverUrl)
        {
            if (!Uri.IsWellFormedUriString(settings.ServerUrl, UriKind.Absolute))
            {
                _logger.LogWarn("Server URL is invalid");
                return false;
            }

            if (settings.ApiKey.Length != 48)
            {
                _logger.LogWarn("Invalid API key format");
                return false;
            }

            _serverUrl = settings.ServerUrl;
            _apiKey = settings.ApiKey;

            _clockifyClient = new ClockifyClient(_apiKey, settings.ServerUrl);
        }

        _workspaceName = settings.WorkspaceName;
        _projectName = settings.ProjectName;
        _taskName = settings.TaskName;
        _timerName = settings.TimerName;

        if (await TestConnectionAsync())
        {
            _logger.LogInfo("API key successfully set");

            await UpdateWorkspacesAsync();
            foreach (var workspace in _workspaces)
            {
                await UpdateProjectsAsync(workspace.Name);
            }

            return true;
        }

        _logger.LogWarn("Invalid API key");
        _clockifyClient = null;
        _currentUser = new CurrentUserDto();
        _workspaces = new List<WorkspaceDto>();
        _projects = new Dictionary<string, List<ProjectDtoImpl>>();
        return false;
    }

    public async Task<List<WorkspaceDto>> GetWorkspacesAsync()
    {
        if (!_workspaces.Any())
        {
            await UpdateWorkspacesAsync();
        }

        return _workspaces;
    }

    public async Task<List<ProjectDtoImpl>> GetProjectsAsync(string workspaceName)
    {
        if (!_projects.ContainsKey(workspaceName))
        {
            await UpdateProjectsAsync(workspaceName);
        }

        return _projects[workspaceName];
    }

    private async Task UpdateWorkspacesAsync()
    {
        if (_clockifyClient == null || _workspaces.Any())
        {
            return;
        }

        var workspaceResponse = await _clockifyClient.GetWorkspacesAsync();
        if (workspaceResponse.IsSuccessful)
        {
            _workspaces = workspaceResponse.Data;
        }
    }

    private async Task UpdateProjectsAsync(string workspaceName)
    {
        if (_clockifyClient == null || _projects.ContainsKey(workspaceName))
        {
            return;
        }

        if (_workspaces.All(w => w.Name != workspaceName))
        {
            await UpdateWorkspacesAsync();
        }

        var workspace = _workspaces.SingleOrDefault(w => w.Name == workspaceName);
        if (workspace == null)
        {
            return;
        }

        var projectResponse = await _clockifyClient.FindAllProjectsOnWorkspaceAsync(workspace.Id, pageSize: 5000);
        if (!projectResponse.IsSuccessful)
        {
            return;
        }

        _projects[workspace.Name] = projectResponse.Data;
    }

    private async Task<string> FindOrCreateTaskAsync(WorkspaceDto workspace, ProjectDtoImpl project, string taskName)
    {
        var taskResponse =
            await _clockifyClient.FindAllTasksAsync(workspace.Id, project.Id, name: taskName, pageSize: 5000);

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

        var creationResponse = await _clockifyClient.CreateTaskAsync(workspace.Id, project.Id, taskRequest);

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
        if (!user.IsSuccessful)
        {
            return false;
        }

        _currentUser = user.Data;
        return true;
    }
}