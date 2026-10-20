using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clockify;

/// <summary>
/// The running timers of one user in one workspace, shared by all buttons using that workspace.
/// Buttons render from this state and only one of them fetches it from Clockify when it gets too old.
/// </summary>
public sealed class RunningTimers(ClockifyConnection connection, string workspaceId, string userId)
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly TimeSpan _timeOut = TimeSpan.FromSeconds(20);

    private IReadOnlyList<RunningEntry> _entries = [];
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public IReadOnlyList<RunningEntry> Entries => Volatile.Read(ref _entries);

    public bool IsKnown => _lastSuccess > DateTimeOffset.MinValue;

    /// <summary>
    /// Fetches the running timers if the last successful fetch is older than <paramref name="maxAge"/>.
    /// Unless <paramref name="force"/> is set, a failed fetch isn't repeated within <paramref name="maxAge"/> either,
    /// so a network or API problem doesn't turn into a request every second.
    /// </summary>
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

    /// <summary>
    /// Clockify only runs one timer per user, so a started timer replaces all others.
    /// </summary>
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
