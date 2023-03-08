using Newtonsoft.Json;

namespace Clockify
{
    public class PluginSettings
    {
        [JsonProperty(PropertyName = "apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "workspaceName")]
        public string WorkspaceName { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "projectName")]
        public string ProjectName { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "taskName")]
        public string TaskName { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "timerName")]
        public string TimerName { get; set; } = string.Empty;
        
        [JsonProperty(PropertyName = "titleFormat")]
        public string TitleFormat { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "serverUrl")]
        public string ServerUrl { get; set; } = "https://api.clockify.me/api/v1";
    }
}