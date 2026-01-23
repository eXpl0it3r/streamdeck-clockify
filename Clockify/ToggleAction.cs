using System;
using System.Threading.Tasks;
using BarRaider.SdTools;

// ReSharper disable AsyncVoidMethod - Async overridse for SdTools
namespace Clockify;

[PluginActionId("dev.duerrenberger.clockify.toggle")]
public class ToggleAction : KeypadBase
{
    private readonly Logger _logger;
    private readonly PluginSettings _settings;
    
    private readonly ButtonState _buttonState;
    private readonly ClockifyService _clockifyService;

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        _logger = new Logger(BarRaider.SdTools.Logger.Instance);
        _settings = new PluginSettings();
        
        _buttonState = new ButtonState();
        _clockifyService = new ClockifyService(_logger);

        Tools.AutoPopulateSettings(_settings, payload.Settings);

        _logger.LogInfo("Creating ToggleAction...");
    }

    public override void Dispose()
    {
        _logger.LogInfo("Disposing ToggleAction...");
    }

    public override void KeyPressed(KeyPayload payload)
    {
        _logger.LogInfo("Key Pressed");
    }

    public override async void KeyReleased(KeyPayload payload)
    {
        _logger.LogInfo("Key Released");

        if (!_clockifyService.IsValid || !await _clockifyService.ToggleTimerAsync())
        {
            await Connection.ShowAlert();
        }

        // Immediately update the button
        _buttonState.Ticks = 5;
        OnTick();
    }

    public override async void OnTick()
    {
        if (!await TryInitializingClockifyContext())
        {
            return;
        }

        if (_buttonState.Ticks >= 5)
        {
            var timer = await _clockifyService.GetRunningTimerAsync();
            var timerTime = string.Empty;

            if (timer?.TimeInterval?.Start != null)
            {
                var timeDifference = DateTime.UtcNow - timer.TimeInterval.Start.Value.UtcDateTime;
                timerTime = $"{timeDifference.Hours:d2}:{timeDifference.Minutes:d2}:{timeDifference.Seconds:d2}";
                
                await Connection.SetStateAsync(DisplayState.Active);
                _buttonState.LastStart = timer.TimeInterval.Start.Value.UtcDateTime;
            }
            else
            {
                await Connection.SetStateAsync(DisplayState.Inactive);
                _buttonState.LastStart = null;
            }

            await Connection.SetTitleAsync(TextFormatter.CreateTimerText(_settings, timerTime));
            _buttonState.Ticks = 0;
            return;
        }

        if (_buttonState.LastStart.HasValue)
        {
            var timeDifference = DateTime.UtcNow - _buttonState.LastStart.Value;
            var timerTime = $"{timeDifference.Hours:d2}:{timeDifference.Minutes:d2}:{timeDifference.Seconds:d2}";
                
            await Connection.SetStateAsync(DisplayState.Active);
            await Connection.SetTitleAsync(TextFormatter.CreateTimerText(_settings, timerTime));
        }

        _buttonState.Ticks++;
    }

    public override async void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        Tools.AutoPopulateSettings(_settings, payload.Settings);
        _logger.LogInfo($"Settings Received: {_settings}");
        await _clockifyService.UpdateSettingsAsync(_settings);
    }

    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
    {
        _logger.LogInfo("Global Settings Received");
    }

    private async Task<bool> TryInitializingClockifyContext()
    {
        if (_clockifyService.IsValid)
        {
            return true;   
        }
        
        await _clockifyService.UpdateSettingsAsync(_settings);

        return _clockifyService.IsValid;
    }
}