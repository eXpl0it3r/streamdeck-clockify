#nullable enable
using Clockify.Net.Models.TimeEntries;

namespace Clockify.Dtos;

public class RunningTimer
{
    public RunningTimer(TimeEntryDtoImpl? timeEntry, string? projectId, string? projectName)
    {
        TimeEntry = timeEntry;
        ProjectId = projectId;
        ProjectName = projectName;
    }

    public TimeEntryDtoImpl? TimeEntry { get; set; }

    public string? ProjectId { get; set; }

    public string? ProjectName { get; set; }
}
