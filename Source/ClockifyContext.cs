using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Clockify.Net;
using Clockify.Net.Models.Projects;
using Clockify.Net.Models.TimeEntries;
using Clockify.Net.Models.Users;
using Clockify.Net.Models.Workspaces;

namespace Clockify
{
    public class ClockifyContext
    {
        private string _apiKey = string.Empty;
        private ClockifyClient _clockifyClient;
        private CurrentUserDto _currentUser = new();
        private List<WorkspaceDto> _workspaces = new();
        private Dictionary<string, List<ProjectDtoImpl>> _projects = new();

        public bool IsValid()
        {
            return _clockifyClient != null;
        }

        public async Task ToggleTimerAsync(string workspaceName, string projectName = null, string timerName = null)
        {
            if (_clockifyClient == null
                || _workspaces.All(w => w.Name != workspaceName)
                || !string.IsNullOrEmpty(projectName) && (!_projects.ContainsKey(workspaceName) || _projects[workspaceName].All(p => p.Name != projectName)))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Invalid settings for toggle {workspaceName}, {projectName}, {timerName}");
                return;
            }

            var runningTimer = await GetRunningTimerAsync(workspaceName, projectName, timerName);

            if (runningTimer != null)
            {
                await StopRunningTimerAsync(workspaceName);
                return;
            }
            
            await StopRunningTimerAsync(workspaceName);
            
            var workspace = _workspaces.Single(w => w.Name == workspaceName);
            var timeEntryRequest = new TimeEntryRequest
            {
                UserId = _currentUser.Id,
                WorkspaceId = workspace.Id,
                Description = timerName,
                Start = DateTimeOffset.UtcNow
            };

            if (!string.IsNullOrEmpty(projectName))
            {
                var project = _projects[workspaceName].Single(p => p.Name == projectName);
                timeEntryRequest.ProjectId = project.Id;
            }
            
            await _clockifyClient.CreateTimeEntryAsync(workspace.Id, timeEntryRequest);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Toggle Timer {workspaceName}, {projectName}, {timerName}");
        }

        public async Task StopRunningTimerAsync(string workspaceName)
        {
            if (_clockifyClient == null || _workspaces.All(w => w.Name != workspaceName))
            {
                return;
            }
            
            var workspace = _workspaces.Single(w => w.Name == workspaceName);
            var runningTimer = await GetRunningTimerAsync(workspaceName);
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
                Description = runningTimer.Description
            };
            
            await _clockifyClient.UpdateTimeEntryAsync(workspace.Id, runningTimer.Id, timerUpdate);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Timer Stopped {workspaceName}, {runningTimer.ProjectId}, {runningTimer.Description}");
        }

        public async Task<TimeEntryDtoImpl> GetRunningTimerAsync(string workspaceName, string projectName = null, string timeName = null)
        {
            if (_clockifyClient == null
                || _workspaces.All(w => w.Name != workspaceName)
                || !string.IsNullOrEmpty(projectName) && (!_projects.ContainsKey(workspaceName) || _projects[workspaceName].All(p => p.Name != projectName)))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Invalid settings for running timer {workspaceName}");
                return null;
            }

            var workspace = _workspaces.Single(w => w.Name == workspaceName);
            var timeEntries = await _clockifyClient.FindAllTimeEntriesForUserAsync(workspace.Id, _currentUser.Id, inProgress: true);
            if (!timeEntries.IsSuccessful)
            {
                return null;
            }
            
            if (string.IsNullOrEmpty(projectName))
            {
                return string.IsNullOrEmpty(timeName) ? timeEntries.Data.FirstOrDefault() : timeEntries.Data.FirstOrDefault(t => t.Description == timeName);
            }
            
            var project = _projects[workspaceName].Single(p => p.Name == projectName);
            return string.IsNullOrEmpty(timeName) ? timeEntries.Data.FirstOrDefault(t => t.ProjectId == project.Id) : timeEntries.Data.FirstOrDefault(t => t.ProjectId == project.Id && t.Description == timeName);
        }

        public async Task<bool> SetApiKeyAsync(string apiKey)
        {
            if (_clockifyClient == null || apiKey != _apiKey)
            {
                if (apiKey.Length != 48)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "Invalid API key format");
                    return false;
                }

                _apiKey = apiKey;
                _clockifyClient = new ClockifyClient(_apiKey);
            }

            if (await TestConnectionAsync())
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "API key successfully set");

                await UpdateWorkspacesAsync();
                foreach (var workspace in _workspaces)
                {
                    await UpdateProjectsAsync(workspace.Name);
                }
                
                return true;
            }

            Logger.Instance.LogMessage(TracingLevel.WARN, "Invalid API key");
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

            var projectResponse = await _clockifyClient.FindAllProjectsOnWorkspaceAsync(workspace.Id);
            if (!projectResponse.IsSuccessful)
            {
                return;
            }

            _projects[workspace.Name] = projectResponse.Data;
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
}