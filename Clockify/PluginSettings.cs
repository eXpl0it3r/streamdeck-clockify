using BarRaider.SdTools;
using Newtonsoft.Json;

namespace Clockify
{
    public class PluginSettings
    {
        [JsonProperty(PropertyName = "apiKey")]
        public string ApiKey { get; set; } = string.Empty;
        
        [FilenameProperty]
        [JsonProperty(PropertyName = "outputFileName")]
        public string OutputFileName { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "inputString")]
        public string InputString { get; set; } = string.Empty;
    }
}