using System;
using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace Clockify
{
    [PluginActionId("dev.duerrenberger.clockify.toggle")]
    public class ToggleAction : PluginBase
    {
        private static readonly uint InactiveState = 0;
        private static readonly uint ActiveState = 1;
        private readonly PluginSettings _settings;
        private readonly ClockifyContext _clockifyContext;

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
                await _clockifyContext.ToggleTimerAsync(_settings.WorkspaceName, _settings.ProjectName, _settings.TimeName);
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
                var timer = await _clockifyContext.GetRunningTimerAsync(_settings.WorkspaceName, _settings.ProjectName, _settings.TimeName);
                if (timer?.TimeInterval.Start != null)
                {
                    var timeDifference = DateTime.UtcNow - timer.TimeInterval.Start.Value.UtcDateTime;
                    var title = $"{timeDifference.Hours:d2}:{timeDifference.Minutes:d2}:{timeDifference.Seconds:d2}";
                    title = string.IsNullOrEmpty(_settings.TimeName) ? $"{_settings.ProjectName}\n{title}" : $"{_settings.TimeName}\n{title}";

                    await Connection.SetTitleAsync(title);
                    await Connection.SetStateAsync(ActiveState);
                }
                else
                {
                    await Connection.SetTitleAsync(string.IsNullOrEmpty(_settings.TimeName) ? $"{_settings.ProjectName}" : $"{_settings.TimeName}");
                    await Connection.SetStateAsync(InactiveState);
                }
            }
            else if (_settings.ApiKey.Length == 48)
            {
                await _clockifyContext.SetApiKeyAsync(_settings.ApiKey);
            }
        }

        public override async void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(_settings, payload.Settings);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Settings Received: {_settings}");
            await SaveSettings();
            await _clockifyContext.SetApiKeyAsync(_settings.ApiKey);
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
        {
        }

        private Task SaveSettings()
        {
            return Connection.SetSettingsAsync(JObject.FromObject(_settings));
        }
    }
}