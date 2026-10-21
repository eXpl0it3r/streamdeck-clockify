using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Newtonsoft.Json.Linq;

// ReSharper disable AsyncVoidMethod - Async overridse for SdTools
namespace Clockify;

[PluginActionId("dev.duerrenberger.clockify.toggle")]
public class ToggleAction : KeypadBase
{
    private readonly Logger _logger;
    private readonly ClockifyService _clockifyService;

    private PluginSettings _settings;
    private int _tickRunning;
    private uint? _shownState;
    private string _shownTitle;

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        _logger = new Logger(BarRaider.SdTools.Logger.Instance);
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

        var result = await _clockifyService.ToggleTimerAsync();

        if (result is ToggleResult.Failed or ToggleResult.RateLimited)
        {
            await Connection.ShowAlert();
        }

        await StoreResolvedIdsAsync();

        // The Stream Deck flips the button state on key press by itself
        _shownState = null;
        await RenderAsync();
    }

    public override async void OnTick()
    {
        // A slow request shouldn't pile up ticks
        if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
        {
            return;
        }

        try
        {
            if (await _clockifyService.TryResolveAsync(userInitiated: false))
            {
                await StoreResolvedIdsAsync();
                await _clockifyService.RefreshAsync();
            }

            await RenderAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError($"Updating button failed: {exception.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _tickRunning, 0);
        }
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
            TagIds = [..resolvedIds.TagIds],
            Plan = resolvedIds.Plan
        };

        await Connection.SetSettingsAsync(JObject.FromObject(_settings));
    }

    private async Task RenderAsync()
    {
        var timer = _clockifyService.GetRunningTimer();
        var timerTime = string.Empty;
        var state = DisplayState.Inactive;

        if (timer?.Start is not null)
        {
            var elapsed = DateTimeOffset.UtcNow - timer.Start.Value;
            timerTime = $"{(int)elapsed.TotalHours:d2}:{elapsed.Minutes:d2}:{elapsed.Seconds:d2}";
            state = DisplayState.Active;
        }

        var title = TextFormatter.CreateTimerText(_settings, timerTime);

        if (_shownState != state)
        {
            await Connection.SetStateAsync(state);
            _shownState = state;
        }

        if (_shownTitle != title)
        {
            await Connection.SetTitleAsync(title);
            _shownTitle = title;
        }
    }
}
