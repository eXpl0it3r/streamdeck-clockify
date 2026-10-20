using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Clockify;

public class ResolvedIds
{
    /// <summary>
    /// Hash of the settings the IDs were looked up for, the IDs are only used while it matches.
    /// </summary>
    [JsonProperty(PropertyName = "fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "workspaceId")]
    public string WorkspaceId { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "projectId")]
    public string ProjectId { get; set; }

    [JsonProperty(PropertyName = "taskId")]
    public string TaskId { get; set; }

    [JsonProperty(PropertyName = "tagIds")]
    public List<string> TagIds { get; set; } = [];

    public bool IsComplete => !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(WorkspaceId);

    public static string CreateFingerprint(PluginSettings settings)
    {
        // Hashed, so the API key itself isn't stored a second time
        var text = string.Join('\n', settings.ApiKey, settings.ServerUrl, settings.WorkspaceName, settings.ClientName, settings.ProjectName, settings.TaskName, settings.Tags);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
