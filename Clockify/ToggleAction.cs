using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Newtonsoft.Json.Linq;

// ReSharper disable AsyncVoidMethod - Async overridse for SdTools
namespace Clockify;

[PluginActionId("dev.duerrenberger.clockify.toggle")]
public class ToggleAction : KeypadBase
{
    private readonly Logger _logger;
    private readonly ButtonState _buttonState;
    private readonly ClockifyService _clockifyService;

    private PluginSettings _settings;

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        _logger = new Logger(BarRaider.SdTools.Logger.Instance);
        _buttonState = new ButtonState();
        _clockifyService = new ClockifyService(_logger);

        _logger.LogInfo("Creating ToggleAction...");

        ApplySettings(payload.Settings);
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

        if (!await _clockifyService.ToggleTimerAsync())
        {
            await Connection.ShowAlert();
        }

        await StoreResolvedIdsAsync();

        // Immediately update the button
        _buttonState.Ticks = _settings.RefreshRate;
        OnTick();
    }

    public override async void OnTick()
    {
        if (!await _clockifyService.TryResolveAsync(userInitiated: false))
        {
            return;
        }

        await StoreResolvedIdsAsync();

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

    public override void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        ApplySettings(payload.Settings);
    }

    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
    {
        _logger.LogInfo("Global Settings Received");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Plugin is trimmed with TrimMode=partial")]
    private void ApplySettings(JObject settings)
    {
        _settings = settings?.ToObject<PluginSettings>() ?? new PluginSettings();
        _logger.LogInfo($"Settings Received: {_settings}");

        _clockifyService.UpdateSettings(_settings);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Plugin is trimmed with TrimMode=partial")]
    private async Task StoreResolvedIdsAsync()
    {
        var resolvedIds = _clockifyService.ResolvedIds;

        if (resolvedIds is null
            || (_settings.ResolvedIds?.Fingerprint == resolvedIds.Fingerprint && _settings.ResolvedIds.TaskId == resolvedIds.TaskId))
        {
            return;
        }

        _settings.ResolvedIds = new ResolvedIds
        {
            Fingerprint = resolvedIds.Fingerprint,
            UserId = resolvedIds.UserId,
            WorkspaceId = resolvedIds.WorkspaceId,
            ProjectId = resolvedIds.ProjectId,
            TaskId = resolvedIds.TaskId,
            TagIds = [..resolvedIds.TagIds]
        };

        await Connection.SetSettingsAsync(JObject.FromObject(_settings));
    }
}