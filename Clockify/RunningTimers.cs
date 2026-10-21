using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clockify;

public sealed class RunningTimers(ClockifyConnection connection, string workspaceId, string userId)
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly TimeSpan _timeOut = TimeSpan.FromSeconds(20);

    private IReadOnlyList<RunningEntry> _entries = [];
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public IReadOnlyList<RunningEntry> Entries => Volatile.Read(ref _entries);

    public bool IsKnown => _lastSuccess > DateTimeOffset.MinValue;

    public async Task SyncAsync(TimeSpan maxAge, bool force = false)
    {
        if (!IsStale(maxAge, force))
        {
            return;
        }

        if (!await _syncLock.WaitAsync(_timeOut))
        {
            return;
        }

        try
        {
            // Another button may have fetched the timers while we waited
            if (!IsStale(maxAge, force))
            {
                return;
            }

            _lastAttempt = DateTimeOffset.UtcNow;

            var timeEntries = await connection.SendAsync("get running timer",
                c => c.V1.Workspaces[workspaceId].User[userId].TimeEntries.GetAsync(p => p.QueryParameters.InProgress = "true"));

            Volatile.Write(ref _entries, timeEntries?.Select(RunningEntry.From).ToList() ?? []);
            _lastSuccess = DateTimeOffset.UtcNow;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public void SetStarted(RunningEntry entry)
    {
        Volatile.Write(ref _entries, [entry]);
        _lastSuccess = _lastAttempt = DateTimeOffset.UtcNow;
    }

    public void SetStopped()
    {
        Volatile.Write(ref _entries, []);
        _lastSuccess = _lastAttempt = DateTimeOffset.UtcNow;
    }

    private bool IsStale(TimeSpan maxAge, bool force)
    {
        var now = DateTimeOffset.UtcNow;

        return now - _lastSuccess > maxAge
               && (force || now - _lastAttempt > maxAge);
    }
}
