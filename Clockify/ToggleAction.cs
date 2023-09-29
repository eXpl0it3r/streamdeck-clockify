#nullable enable
using System;
using System.Threading.Tasks;
using BarRaider.SdTools;

namespace Clockify;

[PluginActionId("dev.spons.clockify.toggle")]
public class ToggleAction : KeypadBase
{
    private const uint InactiveState = 0;
    private const uint ActiveState = 1;
    private readonly ClockifyGateway _clockifyGateway;

    private readonly PluginSettings _settings;

    // SDK does not allow us to set the tick interval, this is a workaround to only poke the API every 10 ticks (seconds)
    private int _tickCount = 10;
    private TimeSpan? _cachedTimeSpan;
    private string? _cachedProjectName;

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        Connection.SetTitleAsync("Loading...").Wait();

        _clockifyGateway = new ClockifyGateway();
        _settings = new PluginSettings();

        Tools.AutoPopulateSettings(_settings, payload.Settings);

        UpdateValuesFromApi().Wait();
    }

    public override void Dispose()
    {
    }

    public override void KeyPressed(KeyPayload payload)
    {
    }

    public override async void KeyReleased(KeyPayload payload)
    {
        ResetCachedValues();

        if (!_clockifyGateway.IsValid())
        {
            await Connection.ShowAlert();
        }
        else if (_settings.ShowWeekTime)
        {
            await UpdateWeekTimeFromApi();
        }
        else if (_settings.ShowDayTime)
        {
            await UpdateDayTimeFromApi();
        }
        else
        {
            await _clockifyGateway.ToggleTimerAsync();
            await UpdateRunningTimerFromApi();
        }
    }

    public override async void OnTick()
    {
        if (_tickCount < 10)
        {
            _tickCount++;
            if (_settings.ShowWeekTime || _settings.ShowDayTime)
            {
                await Connection.SetTitleAsync(GetCachedStaticTimerText());
                return;
            }

            await Connection.SetTitleAsync(GetRunningCachedTimerText());
            return;
        }

        ResetCachedValues();

        await UpdateValuesFromApi();

        _tickCount++;
    }

    public override async void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        Tools.AutoPopulateSettings(_settings, payload.Settings);
        await _clockifyGateway.UpdateSettings(_settings);
    }

    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
    {
    }

    private async Task UpdateValuesFromApi()
    {
        if (!_clockifyGateway.IsValid())
        {
            await _clockifyGateway.UpdateSettings(_settings);
            return;
        }

        if (_settings.ShowWeekTime)
        {
            await UpdateWeekTimeFromApi();
            return;
        }

        if (_settings.ShowDayTime)
        {
            await UpdateDayTimeFromApi();
            return;
        }

        await UpdateRunningTimerFromApi();
    }

    private async Task UpdateRunningTimerFromApi()
    {
        var timer = await _clockifyGateway.GetRunningTimerAsync();
        TimeSpan? timeDifference = null;
        if (timer?.TimeEntry?.TimeInterval.Start != null)
        {
            timeDifference = DateTime.UtcNow - timer.TimeEntry.TimeInterval.Start.Value.UtcDateTime;
            await Connection.SetStateAsync(ActiveState);
        }
        else
        {
            await Connection.SetStateAsync(InactiveState);
        }

        await Connection.SetTitleAsync(UpdateCachedValueAndGetTimeText(timeDifference, timer?.ProjectName));
    }

    private async Task UpdateWeekTimeFromApi()
    {
        var totalTimeInSeconds = await _clockifyGateway.GetCurrentWeekTotalTimeAsync();
        await Connection.SetStateAsync(ActiveState);
        await Connection.SetTitleAsync(UpdateCachedValueAndGetTimeText(totalTimeInSeconds != null ? TimeSpan.FromSeconds(totalTimeInSeconds!.Value) : null, null));
    }

    private async Task UpdateDayTimeFromApi()
    {
        var totalTimeInSeconds = await _clockifyGateway.GetCurrentDayTimeAsync();

        await Connection.SetStateAsync(ActiveState);
        await Connection.SetTitleAsync(UpdateCachedValueAndGetTimeText(totalTimeInSeconds != null ? TimeSpan.FromSeconds(totalTimeInSeconds!.Value) : null, null));
    }

    private string UpdateCachedValueAndGetTimeText(TimeSpan? timeSpan, string? projectName)
    {
        _cachedTimeSpan = timeSpan;
        _cachedProjectName = projectName;

        var timerTime = string.Empty;
        if (_cachedTimeSpan.HasValue)
        {
            timerTime = GetTimerTime();
        }

        return FormatTimerText(timerTime, projectName);
    }

    private string GetRunningCachedTimerText()
    {
        var timerTime = string.Empty;
        if (!_cachedTimeSpan.HasValue)
        {
            return FormatTimerText(timerTime, _cachedProjectName);
        }

        _cachedTimeSpan = _cachedTimeSpan.Value.Add(TimeSpan.FromSeconds(1));

        timerTime = GetTimerTime();

        return FormatTimerText(timerTime, _cachedProjectName);
    }

    private string GetCachedStaticTimerText()
    {
        var timerTime = string.Empty;
        if (!_cachedTimeSpan.HasValue)
        {
            return FormatTimerText(timerTime, null);
        }

        timerTime = GetTimerTime();

        return FormatTimerText(timerTime, null);
    }

    private string GetTimerTime()
    {
        var totalTimeInSeconds = (int?)_cachedTimeSpan?.TotalSeconds;

        if (totalTimeInSeconds == null)
        {
            return string.Empty;
        }

        var hours = totalTimeInSeconds / 3600;
        var minutes = (totalTimeInSeconds % 3600) / 60;
        var seconds = totalTimeInSeconds % 60;

        var timerTime = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        return timerTime;
    }

    private string FormatTimerText(string timerTime, string? projectName)
    {
        if (!string.IsNullOrEmpty(_settings.TitleFormat))
        {
            return _settings.TitleFormat
                .Replace("{projectName}", projectName != null ? GetFirstWord(projectName) : string.Empty)
                .Replace("{timer}", timerTime);
        }

        var timerText = string.Empty;

        if (!string.IsNullOrEmpty(timerTime))
        {
            timerText += $"\n{timerTime}";
        }

        return timerText;
    }

    private static string GetFirstWord(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var words = input.Split(' ');
        return words[0];
    }

    private void ResetCachedValues()
    {
        _tickCount = 0;
        _cachedTimeSpan = null;
        _cachedProjectName = null;
    }
}
