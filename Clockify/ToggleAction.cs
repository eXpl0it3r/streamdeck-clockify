using System;
using System.Threading.Tasks;
using BarRaider.SdTools;

namespace Clockify;

[PluginActionId("dev.duerrenberger.clockify.toggle")]
public class ToggleAction : KeypadBase
{
    private const uint InactiveState = 0;
    private const uint ActiveState = 1;
    private readonly ClockifyContext _clockifyContext;

    private readonly Logger _logger;
    private readonly PluginSettings _settings;

    // SDK does not allow us to set the tick interval, this is a workaround to only poke the API every 10 ticks (seconds)
    private int _tickCount = 10;
    private TimeSpan? _cachedTimeSpan;

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        Connection.SetTitleAsync("Loading...").Wait();

        _logger = new Logger(BarRaider.SdTools.Logger.Instance);
        _clockifyContext = new ClockifyContext(_logger);
        _settings = new PluginSettings();

        Tools.AutoPopulateSettings(_settings, payload.Settings);

        UpdateValuesFromApi().Wait();

        _logger.LogDebug("Creating ToggleAction...");
    }

    public override void Dispose()
    {
        _logger.LogDebug("Disposing ToggleAction...");
    }

    public override void KeyPressed(KeyPayload payload)
    {
        _logger.LogDebug("Key Pressed");
    }

    public override async void KeyReleased(KeyPayload payload)
    {
        _logger.LogDebug("Key Released");

        _cachedTimeSpan = null;

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

        if (_clockifyContext.IsValid())
        {
            await _clockifyContext.ToggleTimerAsync();
            await UpdateRunningTimerFromApi();
        }
        else
        {
            await Connection.ShowAlert();
        }
    }

    public override async void OnTick()
    {
        if (_tickCount < 10)
        {
            _tickCount++;
            await Connection.SetTitleAsync(GetTimerText(null, true, !_settings.ShowDayTime && !_settings.ShowWeekTime));
            return;
        }

        _cachedTimeSpan = null;
        _tickCount = 0;

        await UpdateValuesFromApi();

        _tickCount++;
    }

    public override async void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        Tools.AutoPopulateSettings(_settings, payload.Settings);
        _logger.LogDebug($"Settings Received: {_settings}");
        await _clockifyContext.UpdateSettings(_settings);
    }

    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
    {
        _logger.LogDebug("Global Settings Received");
    }

    private async Task UpdateValuesFromApi()
    {
        if (!_clockifyContext.IsValid())
        {
            await _clockifyContext.UpdateSettings(_settings);
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
        var timer = await _clockifyContext.GetRunningTimerAsync();
        TimeSpan? timeDifference = null;
        if (timer?.TimeInterval.Start != null)
        {
            timeDifference = DateTime.UtcNow - timer.TimeInterval.Start.Value.UtcDateTime;
            await Connection.SetStateAsync(ActiveState);
        }
        else
        {
            await Connection.SetStateAsync(InactiveState);
        }

        await Connection.SetTitleAsync(GetTimerText(timeDifference));
    }

    private async Task UpdateWeekTimeFromApi()
    {
        var totalTimeInSeconds = await _clockifyContext.GetCurrentWeekTotalTimeAsync();
        await Connection.SetStateAsync(ActiveState);
        await Connection.SetTitleAsync(GetTimerText(totalTimeInSeconds != null ? TimeSpan.FromSeconds(totalTimeInSeconds!.Value) : null));
    }

    private async Task UpdateDayTimeFromApi()
    {
        var totalTimeInSeconds = await _clockifyContext.GetCurrentDayTimeAsync();

        await Connection.SetStateAsync(ActiveState);
        await Connection.SetTitleAsync(GetTimerText(totalTimeInSeconds != null ? TimeSpan.FromSeconds(totalTimeInSeconds!.Value) : null));
    }

    private string GetTimerText(TimeSpan? timeSpan, bool useCachedValue = false, bool runningTimer = false)
    {
        if (timeSpan != null)
        {
            _cachedTimeSpan = timeSpan.Value;
        }

        var timerTime = string.Empty;
        if (_cachedTimeSpan.HasValue)
        {
            if (useCachedValue && runningTimer)
            {
                _cachedTimeSpan = _cachedTimeSpan.Value.Add(TimeSpan.FromSeconds(1));
            }

            var totalTimeInSeconds = (int)_cachedTimeSpan.Value.TotalSeconds;

            var hours = totalTimeInSeconds / 3600;
            var minutes = (totalTimeInSeconds % 3600) / 60;
            var seconds = totalTimeInSeconds % 60;

            timerTime = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        if (!string.IsNullOrEmpty(_settings.TitleFormat))
        {
            return _settings.TitleFormat
                .Replace("{projectName}", _settings.ProjectName)
                .Replace("{taskName}", _settings.TaskName)
                .Replace("{timerName}", _settings.TimerName)
                .Replace("{timer}", timerTime);
        }

        string timerText;
        if (string.IsNullOrEmpty(_settings.TimerName))
        {
            timerText = string.IsNullOrEmpty(_settings.TaskName)
                ? $"{_settings.ProjectName}"
                : $"{_settings.ProjectName}:\n{_settings.TaskName}";
        }
        else
        {
            timerText = $"{_settings.TimerName}";
        }

        if (!string.IsNullOrEmpty(timerTime))
        {
            timerText += $"\n{timerTime}";
        }

        return timerText;
    }
}
