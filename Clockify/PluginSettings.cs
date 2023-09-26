using Newtonsoft.Json;

namespace Clockify;

public class PluginSettings
{
    [JsonProperty(PropertyName = "apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "workspaceName")]
    public string WorkspaceName { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "titleFormat")]
    public string TitleFormat { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "showWeekTime")]
    public bool ShowWeekTime { get; set; } = false;

    [JsonProperty(PropertyName = "showDayTime")]
    public bool ShowDayTime { get; set; } = false;

    [JsonProperty(PropertyName = "serverUrl")]
    public string ServerUrl { get; set; } = "https://api.clockify.me/api/v1";

    public override string ToString()
    {
        return $"Workspace: {WorkspaceName}, Project: {ProjectName}";
    }
}
