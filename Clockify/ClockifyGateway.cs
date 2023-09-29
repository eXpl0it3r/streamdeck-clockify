using System;
using System.Linq;
using System.Threading.Tasks;
using Clockify.Dtos;
using Clockify.Net;
using Clockify.Net.Models.Reports;
using Clockify.Net.Models.TimeEntries;
using Clockify.Net.Models.Users;
using Newtonsoft.Json.Linq;

namespace Clockify;

public class ClockifyGateway
{
    private ClockifyClient _clockifyClient;
    private CurrentUserDto _currentUser = new();

    private string _apiKey = string.Empty;
    private string _serverUrl = string.Empty;

    private string _workspaceId = string.Empty;
    private string _projectId = string.Empty;

    public bool IsValid()
    {
        return _clockifyClient != null;
    }

    public async Task ToggleTimerAsync()
    {
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceId))
        {
            return;
        }

        var runningTimer = await GetRunningTimerAsync();

        await StopRunningTimerAsync(runningTimer.TimeEntry);

        if (runningTimer.TimeEntry != null)
        {
            return;
        }

        var timeEntryRequest = new TimeEntryRequest
        {
            UserId = _currentUser.Id,
            WorkspaceId = _workspaceId,
            Description = string.Empty,
            Start = DateTimeOffset.UtcNow
        };

        if (!string.IsNullOrEmpty(_projectId))
        {
            timeEntryRequest.ProjectId = _projectId;
        }

        await _clockifyClient.CreateTimeEntryAsync(_workspaceId, timeEntryRequest);
    }

    public async Task<RunningTimer> GetRunningTimerAsync()
    {
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceId))
        {
            return null;
        }

        var timeEntries = await _clockifyClient.FindAllTimeEntriesForUserAsync(_workspaceId, _currentUser.Id, inProgress: true);
        if (!timeEntries.IsSuccessful || timeEntries.Data is null)
        {
            return null;
        }

        TimeEntryDtoImpl timeEntry;
        if (string.IsNullOrEmpty(_projectId))
        {
            timeEntry = timeEntries.Data.FirstOrDefault();
            return new RunningTimer(timeEntries.Data.FirstOrDefault(), timeEntry?.ProjectId, timeEntry != null ? await FindProjectName(timeEntry.ProjectId) : null);
        }

        timeEntry = timeEntries.Data.FirstOrDefault(t => t.ProjectId == _projectId);

        return new RunningTimer(timeEntry, timeEntry?.ProjectId, timeEntry != null ? await FindProjectName(timeEntry.ProjectId) : null);
    }

    public async Task<int?> GetCurrentWeekTotalTimeAsync()
    {
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceId))
        {
            return null;
        }

        var now = DateTimeOffset.Now;
        var dayOfWeek = now.DayOfWeek;

        var daysToSubtract = dayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => throw new ArgumentOutOfRangeException()
        };

        var startOfWeek = now.AddDays(-daysToSubtract);
        startOfWeek = startOfWeek.AddHours(-startOfWeek.Hour).AddMinutes(-startOfWeek.Minute).AddSeconds(-startOfWeek.Second).AddMilliseconds(-startOfWeek.Millisecond);

        var endOfWeek = startOfWeek.AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var weeklyReport = await _clockifyClient.GetWeeklyReportAsync(_workspaceId, new WeeklyReportRequest()
        {
            DateRangeStart = startOfWeek,
            DateRangeEnd = endOfWeek,
            AmountShown = AmountShownType.HIDE_AMOUNT,
            Description = string.Empty,
            WithoutDescription = false,
            Rounding = false,
            SortOrder = SortOrderType.ASCENDING,
            WeeklyFilter = new WeeklyFilterDto()
            {
                Group = WeeklyGroupType.PROJECT,
                Subgroup = WeeklySubgroupType.TIME
            },
        });
        if (!weeklyReport.IsSuccessful || weeklyReport.Data is null)
        {
            return null;
        }

        var totalSeconds = weeklyReport.Data.Totals.SingleOrDefault()?.TotalTime ?? 0;

        return totalSeconds;
    }

    public async Task<int?> GetCurrentDayTimeAsync()
    {
        if (_clockifyClient is null || string.IsNullOrWhiteSpace(_workspaceId))
        {
            return null;
        }

        var now = DateTimeOffset.Now;
        var dayOfWeek = now.DayOfWeek;

        var daysToSubtract = dayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => throw new ArgumentOutOfRangeException()
        };

        var startOfWeek = now.AddDays(-daysToSubtract);
        startOfWeek = startOfWeek.AddHours(-startOfWeek.Hour).AddMinutes(-startOfWeek.Minute).AddSeconds(-startOfWeek.Second).AddMilliseconds(-startOfWeek.Millisecond);

        var endOfWeek = startOfWeek.AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var weeklyReport = await _clockifyClient.GetWeeklyReportAsync(_workspaceId, new WeeklyReportRequest()
        {
            DateRangeStart = startOfWeek,
            DateRangeEnd = endOfWeek,
            AmountShown = AmountShownType.HIDE_AMOUNT,
            Description = string.Empty,
            WithoutDescription = false,
            Rounding = false,
            SortOrder = SortOrderType.ASCENDING,
            WeeklyFilter = new WeeklyFilterDto()
            {
                Group = WeeklyGroupType.PROJECT,
                Subgroup = WeeklySubgroupType.TIME
            },
        });
        if (!weeklyReport.IsSuccessful || weeklyReport.Data is null)
        {
            return null;
        }

        var totalSeconds = weeklyReport.Data.TotalsByDay.FirstOrDefault(x => x.Date.DayOfWeek == dayOfWeek)?.Duration ?? 0;

        return unchecked((int)totalSeconds);
    }

    public async Task UpdateSettings(PluginSettings settings)
    {
        if (_clockifyClient == null || settings.ApiKey != _apiKey || settings.ServerUrl != _serverUrl)
        {
            if (!Uri.IsWellFormedUriString(settings.ServerUrl, UriKind.Absolute))
            {
                return;
            }

            if (settings.ApiKey.Length != 48)
            {
                return;
            }

            _serverUrl = settings.ServerUrl;
            _apiKey = settings.ApiKey;

            _clockifyClient = new ClockifyClient(_apiKey, settings.ServerUrl);
        }

        await UpdateWorkspaceId(settings.WorkspaceName);
        await UpdateProjectId(settings.ProjectName);

        if (await TestConnectionAsync())
        {
            return;
        }

        _clockifyClient = null;
        _currentUser = new CurrentUserDto();
    }

    private async Task StopRunningTimerAsync(TimeEntryDtoImpl runningTimer)
    {
        if (_clockifyClient == null || string.IsNullOrWhiteSpace(_workspaceId))
        {
            return;
        }

        var workspaces = await _clockifyClient.GetWorkspacesAsync();
        if (!workspaces.IsSuccessful || workspaces.Data is null)
        {
            return;
        }

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

        await _clockifyClient.UpdateTimeEntryAsync(_workspaceId, runningTimer.Id, timerUpdate);
    }

    private async Task UpdateWorkspaceId(string workspaceName)
    {
        var workspaces = await _clockifyClient.GetWorkspacesAsync();
        if (!workspaces.IsSuccessful || workspaces.Data is null)
        {
            return;
        }

        var workspace = workspaces.Data.Single(w => w.Name == workspaceName);

        _workspaceId = workspace.Id;
    }

    private async Task UpdateProjectId(string projectName)
    {
        var projects = await _clockifyClient.FindAllProjectsOnWorkspaceAsync(_workspaceId, false, projectName, pageSize: 5000);
        if (!projects.IsSuccessful || projects.Data is null)
        {
            return;
        }

        var project = projects.Data
            .Where(p => p.Name == projectName)
            .ToList();

        if (project.Count > 1)
        {
            return;
        }

        if (!project.Any())
        {
            return;
        }

        _projectId = project.Single().Id;
    }

    private async Task<string> FindProjectName(string projectId)
    {
        var projects = await _clockifyClient.FindProjectByIdAsync(_workspaceId, projectId);
        if (!projects.IsSuccessful)
        {
            return string.Empty;
        }

        var serializedResponse = JObject.Parse(projects.Content);
        var projectName = serializedResponse["name"].ToString();

        return projectName;
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
