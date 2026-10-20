using System;
using System.Collections.Generic;
using ClockifyClient.Models;

namespace Clockify;

public record RunningEntry(
    string Id,
    DateTimeOffset? Start,
    string ProjectId,
    string TaskId,
    string Description,
    IReadOnlyList<string> TagIds,
    bool? Billable)
{
    public static RunningEntry From(TimeEntryWithRatesDtoV1 entry)
    {
        return new RunningEntry(entry.Id, entry.TimeInterval?.Start, entry.ProjectId, entry.TaskId, entry.Description, entry.TagIds ?? [], entry.Billable);
    }

    public static RunningEntry From(TimeEntryDtoImplV1 entry)
    {
        return new RunningEntry(entry.Id, entry.TimeInterval?.Start, entry.ProjectId, entry.TaskId, entry.Description, entry.TagIds ?? [], entry.Billable);
    }
}
