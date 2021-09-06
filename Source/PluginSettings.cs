using System.Collections.Generic;
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
        
        [JsonProperty(PropertyName = "timerName")]
        public string TimeName { get; set; } = string.Empty;
    }
}