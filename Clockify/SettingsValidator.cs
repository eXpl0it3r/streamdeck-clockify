using System;

namespace Clockify;

public static class SettingsValidator
{
    public static (bool IsValid, string Error) Validate(PluginSettings settings)
    {
        if (!Uri.IsWellFormedUriString(settings.ServerUrl, UriKind.Absolute))
            return (false, "Server URL is invalid");
            
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || settings.ApiKey.Length != 48)
            return (false, "Invalid API key format");
            
        return (true, string.Empty);
    }
    
    // ClockifyClient expects the server URL to end with "/api" instead of "/api/v1"
    public static void MigrateServerUrl(PluginSettings settings)
    {
        settings.ServerUrl = settings.ServerUrl?.Replace("/api/v1", "/api");
    }

    public static bool HasChanged(PluginSettings left, PluginSettings right)
    {
        return left.ApiKey != right.ApiKey
            || left.WorkspaceName != right.WorkspaceName
            || left.ProjectName != right.ProjectName
            || left.TaskName != right.TaskName
            || left.TimerName != right.TimerName
            || left.Tags != right.Tags
            || left.ClientName != right.ClientName
            || left.Billable != right.Billable
            || left.ServerUrl != right.ServerUrl;
    }
}