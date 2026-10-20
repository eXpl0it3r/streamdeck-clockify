using Newtonsoft.Json;

namespace Clockify;

public class PluginSettings
{
    public PluginSettings()
    {}

    public PluginSettings(PluginSettings settings)
    {
        ApiKey = settings.ApiKey;
        WorkspaceName = settings.WorkspaceName;
        ProjectName = settings.ProjectName;
        TaskName = settings.TaskName;
        TimerName = settings.TimerName;
        Tags = settings.Tags;
        ClientName = settings.ClientName;
        Billable = settings.Billable;
        TitleFormat = settings.TitleFormat;
        RefreshRate = settings.RefreshRate;
        ServerUrl = settings.ServerUrl;
        ResolvedIds = settings.ResolvedIds;
    }

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

    [JsonProperty(PropertyName = "tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "clientName")]
    public string ClientName { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "billable")]
    public bool Billable { get; set; } = true;

    [JsonProperty(PropertyName = "titleFormat")]
    public string TitleFormat { get; set; } = string.Empty;

    /// <summary>
    /// Seconds between two checks for timers started or stopped outside the Stream Deck, 0 only checks on key press.
    /// The default stays well within the Free plan's 30 requests per hour.
    /// </summary>
    [JsonProperty(PropertyName = "refreshRate")]
    public uint RefreshRate { get; set; } = 300;

    [JsonProperty(PropertyName = "serverUrl")]
    public string ServerUrl { get; set; } = "https://api.clockify.me/api";

    /// <summary>
    /// The Clockify IDs found for the names above, stored so a restart doesn't have to look them up again.
    /// </summary>
    [JsonProperty(PropertyName = "resolvedIds")]
    public ResolvedIds ResolvedIds { get; set; }

    public override string ToString()
    {
        return $"Workspace: '{WorkspaceName}', Project: '{ProjectName}', Task: '{TaskName}', Timer: '{TimerName}', Tags: '{Tags}', Client: '{ClientName}', Billable: '{Billable}'";
    }
}
