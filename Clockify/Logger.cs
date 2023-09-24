using BarRaider.SdTools;

namespace Clockify;

public class Logger
{
    private readonly BarRaider.SdTools.Logger _logger;

    public Logger(BarRaider.SdTools.Logger logger)
    {
        _logger = logger;
    }

    public void LogDebug(string message)
    {
        _logger.LogMessage(TracingLevel.DEBUG, message);
    }

    public void LogInfo(string message)
    {
        _logger.LogMessage(TracingLevel.INFO, message);
    }

    public void LogWarn(string message)
    {
        _logger.LogMessage(TracingLevel.WARN, message);
    }

    public void LogError(string message)
    {
        _logger.LogMessage(TracingLevel.ERROR, message);
    }

    public void LogFatal(string message)
    {
        _logger.LogMessage(TracingLevel.FATAL, message);
    }
}
