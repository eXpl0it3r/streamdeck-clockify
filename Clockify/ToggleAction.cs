using System;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Newtonsoft.Json.Linq;

namespace Clockify;

[PluginActionId("dev.duerrenberger.clockify.toggle")]
public class ToggleAction : KeypadBase
{
    private const uint InactiveState = 0;
    private const uint ActiveState = 1;

    private readonly ClockifyContext _clockifyContext;
    private readonly PluginSettings _settings;

    public ToggleAction(ISDConnection connection, InitialPayload payload)
        : base(connection, payload)
    {
        if (payload.Settings == null || payload.Settings.Count == 0)
        {
            _settings = new PluginSettings();
            SaveSettings();
        }
        else
        {
            _settings = payload.Settings.ToObject<PluginSettings>();
        }

        _clockifyContext = new ClockifyContext();
    }

    public override void Dispose()
    {
        Logger.Instance.LogMessage(TracingLevel.INFO, "Destructor called");
    }

    public override void KeyPressed(KeyPayload payload)
    {
        Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
    }

    public override async void KeyReleased(KeyPayload payload)
    {
        Logger.Instance.LogMessage(TracingLevel.INFO, "Key Released");

        if (_clockifyContext.IsValid())
        {
            await _clockifyContext.ToggleTimerAsync(_settings.WorkspaceName, _settings.ProjectName, _settings.TaskName, _settings.TimerName);
        }
        else
        {
            await Connection.ShowAlert();
        }
    }

    public override async void OnTick()
    {
        if (_clockifyContext.IsValid())
        {
            var timer = await _clockifyContext.GetRunningTimerAsync(_settings.WorkspaceName, _settings.ProjectName, _settings.TimerName);
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
        else if (_settings.ApiKey.Length == 48)
        {
            await _clockifyContext.SetApiKeyAsync(_settings.ServerUrl, _settings.ApiKey);
        }
    }

    public override async void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        Tools.AutoPopulateSettings(_settings, payload.Settings);
        Logger.Instance.LogMessage(TracingLevel.INFO, $"Settings Received: {_settings}");
        await SaveSettings();
        await _clockifyContext.SetApiKeyAsync(_settings.ServerUrl, _settings.ApiKey);
    }

    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
    {
    }

    private string CreateTimerText(string timerTime)
    {
        if (string.IsNullOrEmpty(_settings.TitleFormat))
        {
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

        return _settings.TitleFormat
                        .Replace("{projectName}", _settings.ProjectName)
                        .Replace("{taskName}", _settings.TaskName)
                        .Replace("{timerName}", _settings.TimerName)
                        .Replace("{timer}", timerTime);
    }

    private Task SaveSettings()
    {
        return Connection.SetSettingsAsync(JObject.FromObject(_settings));
    }
}