using System;

namespace Clockify;

public class RateLimitedException(DateTimeOffset blockedUntil)
    : Exception($"Clockify API rate limit reached, requests are paused until {blockedUntil.ToLocalTime():HH:mm:ss}")
{
    public DateTimeOffset BlockedUntil { get; } = blockedUntil;
}
