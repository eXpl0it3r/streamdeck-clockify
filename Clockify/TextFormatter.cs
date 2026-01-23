namespace Clockify;

public static class TextFormatter
{
    public static string CreateTimerText(PluginSettings settings, string timerTime)
    {
        if (!string.IsNullOrEmpty(settings.TitleFormat))
        {
            return settings.TitleFormat
                .Replace("{workspaceName}", settings.WorkspaceName)
                .Replace("{projectName}", settings.ProjectName)
                .Replace("{taskName}", settings.TaskName)
                .Replace("{timerName}", settings.TimerName)
                .Replace("{clientName}", settings.ClientName)
                .Replace("{timer}", timerTime);
        }

        string timerText;
        if (string.IsNullOrEmpty(settings.TimerName))
        {
            if (string.IsNullOrEmpty(settings.ProjectName))
            {
                timerText = string.IsNullOrEmpty(settings.TaskName) ? string.Empty : $"{settings.TaskName}";
            }
            else
            {
                timerText = string.IsNullOrEmpty(settings.TaskName)
                    ? $"{settings.ProjectName}"
                    : $"{settings.ProjectName}:\n{settings.TaskName}";
            }
        }
        else
        {
            timerText = $"{settings.TimerName}";
        }

        if (!string.IsNullOrEmpty(timerTime))
        {
            if (!string.IsNullOrEmpty(timerText))
            {
                timerText += "\n";
            }
            
            timerText += timerTime;
        }

        return timerText;
    }
}