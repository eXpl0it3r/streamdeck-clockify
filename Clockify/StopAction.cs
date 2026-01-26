using BarRaider.SdTools;

namespace Clockify;

[PluginActionId("dev.duerrenberger.clockify.stop")]
public class StopAction : KeypadBase
{
    private readonly Logger _logger;
    private readonly PluginSettings _settings;
    
    public StopAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        _logger = new Logger(BarRaider.SdTools.Logger.Instance);
        _settings = new PluginSettings();
        
        _logger.LogInfo("Creating StopAction...");
    }

    public override void KeyPressed(KeyPayload payload)
    {
        _logger.LogInfo("StopAction Key Pressed");
    }

    public override void KeyReleased(KeyPayload payload)
    {
        _logger.LogInfo("StopAction Key Released");
    }

    public override void OnTick()
    {
        throw new System.NotImplementedException();
    }

    public override void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        Tools.AutoPopulateSettings(_settings, payload.Settings);
        _logger.LogInfo($"StopAction Settings Received: {_settings}");
    }

    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
    {
        _logger.LogInfo("StopAction Global Settings Received");
    }

    public override void Dispose()
    {
        _logger.LogInfo("Disposing StopAction...");
    }
}