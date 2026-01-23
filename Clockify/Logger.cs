using BarRaider.SdTools;

namespace Clockify;

public class Logger(BarRaider.SdTools.Logger logger)
{
    public void LogDebug(string message)
    {
        logger.LogMessage(TracingLevel.DEBUG, message);
    }

    public void LogInfo(string message)
    {
        logger.LogMessage(TracingLevel.INFO, message);
    }

    public void LogWarn(string message)
    {
        logger.LogMessage(TracingLevel.WARN, message);
    }

    public void LogError(string message)
    {
        logger.LogMessage(TracingLevel.ERROR, message);
    }

    public void LogFatal(string message)
    {
        logger.LogMessage(TracingLevel.FATAL, message);
    }
}