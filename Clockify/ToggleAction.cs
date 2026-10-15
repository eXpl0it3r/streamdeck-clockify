using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json.Linq;

// ReSharper disable AsyncVoidMethod - Async overridse for SdTools
namespace Clockify;

[PluginActionId("dev.duerrenberger.clockify.toggle")]
public class ToggleAction : KeypadBase
{
    private readonly Logger _logger;
    private readonly PluginSettings _settings;
    
    private readonly ButtonState _buttonState;
    private readonly ClockifyService _clockifyService;
    private readonly ClockifyLookupService _lookupService;

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        _logger = new Logger(BarRaider.SdTools.Logger.Instance);
        _settings = new PluginSettings();
        
        _buttonState = new ButtonState();
        _clockifyService = new ClockifyService(_logger);
        _lookupService = new ClockifyLookupService(_logger);
        Connection.OnSendToPlugin += OnSendToPlugin;

        Tools.AutoPopulateSettings(_settings, payload.Settings);

        _logger.LogInfo("Creating ToggleAction...");
    }

    public override void Dispose()
    {
        Connection.OnSendToPlugin -= OnSendToPlugin;
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
        _buttonState.Ticks = _settings.RefreshRate;
        OnTick();
    }

    public override async void OnTick()
    {
        if (!await TryInitializingClockifyContext())
        {
            return;
        }

        if (_buttonState.Ticks >= _settings.RefreshRate)
        {
            var timer = await _clockifyService.FetchRunningTimerAsync();
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

    private async void OnSendToPlugin(object sender, SDEventReceivedEventArgs<SendToPlugin> e)
    {
        var payload = e.Event.Payload;
        var request = payload?["request"]?.ToString();
        if (string.IsNullOrEmpty(request))
        {
            return;
        }

        var apiKey = payload["apiKey"]?.ToString() ?? string.Empty;
        var serverUrl = payload["serverUrl"]?.ToString() ?? string.Empty;
        var workspaceName = payload["workspaceName"]?.ToString() ?? string.Empty;
        var clientName = payload["clientName"]?.ToString() ?? string.Empty;
        var projectName = payload["projectName"]?.ToString() ?? string.Empty;

        List<string> items;
        switch (request)
        {
            case "getWorkspaces":
                items = await _lookupService.GetWorkspacesAsync(apiKey, serverUrl);
                break;
            case "getClients":
                items = await _lookupService.GetClientsAsync(apiKey, serverUrl, workspaceName);
                break;
            case "getProjects":
                items = await _lookupService.GetProjectsAsync(apiKey, serverUrl, workspaceName, clientName);
                break;
            case "getTasks":
                items = await _lookupService.GetTasksAsync(apiKey, serverUrl, workspaceName, projectName);
                break;
            default:
                return;
        }

        var response = JObject.FromObject(new { response = request, items });
        await Connection.SendToPropertyInspectorAsync(response);
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