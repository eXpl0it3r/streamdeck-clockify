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
}