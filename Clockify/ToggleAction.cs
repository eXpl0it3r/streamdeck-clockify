using System;
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

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        _logger = new Logger(BarRaider.SdTools.Logger.Instance);
        _clockifyContext = new ClockifyContext(_logger);
        _settings = new PluginSettings();

        Tools.AutoPopulateSettings(_settings, payload.Settings);

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

        if (_clockifyContext.IsValid())
        {
            await _clockifyContext.ToggleTimerAsync();
        }
        else
        {
            await Connection.ShowAlert();
        }
    }

    public override async void OnTick()
    {
        if (!_clockifyContext.IsValid())
        {
            await _clockifyContext.UpdateSettings(_settings);
            return;
        }

        var timer = await _clockifyContext.GetRunningTimerAsync();
        var timerTime = string.Empty;

        if (timer?.TimeInterval.Start != null)
        {
            var timeDifference = DateTime.UtcNow - timer.TimeInterval.Start.Value.UtcDateTime;
            timerTime = $"{timeDifference.Hours:d2}:{timeDifference.Minutes:d2}:{timeDifference.Seconds:d2}";
            await Connection.SetStateAsync(ActiveState);
        }
        else
        {
            await Connection.SetStateAsync(InactiveState);
        }

        await Connection.SetTitleAsync(CreateTimerText(timerTime));
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

    private string CreateTimerText(string timerTime)
    {
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