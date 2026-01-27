using System;

namespace Clockify;

public static class TextFormatter
{
    public static string CreateTimerText(PluginSettings settings, string timerTime)
    {
        if (!string.IsNullOrEmpty(settings.TitleFormat))
        {
            return settings.TitleFormat
                .Replace("{workspaceName}", settings.WorkspaceName, StringComparison.InvariantCultureIgnoreCase)
                .Replace("{projectName}", settings.ProjectName, StringComparison.InvariantCultureIgnoreCase)
                .Replace("{taskName}", settings.TaskName, StringComparison.InvariantCultureIgnoreCase)
                .Replace("{timerName}", settings.TimerName, StringComparison.InvariantCultureIgnoreCase)
                .Replace("{clientName}", settings.ClientName, StringComparison.InvariantCultureIgnoreCase)
                .Replace("{timer}", timerTime, StringComparison.InvariantCultureIgnoreCase);
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