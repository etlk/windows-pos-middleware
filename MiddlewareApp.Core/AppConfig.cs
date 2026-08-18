namespace MiddlewareApp.Core;

/// <summary>
/// Build-time configuration (spec §2). No login or API keys — the tenant API is
/// unauthenticated and addressed by business code.
/// </summary>
public static class AppConfig
{
    public const string BaseDomain = "cloudpos.lk";
    public const string PusherKey = "72e6aeaeb45fc01084ad";
    public const string PusherCluster = "ap1";

    /// <summary>
    /// Laravel broadcast event name. Empty ⇒ bind all events and filter by payload command.
    /// </summary>
    public const string PusherEvent = "LOCATION_COMMANDS";

    /// <summary>
    /// Dev override (spec §3.4): set MIDDLEWARE_DEV_BASE_URL to e.g. http://192.168.1.50:3000
    /// to hit the Express mock server. Production behavior stays https subdomain.
    /// </summary>
    public static string? DevBaseUrlOverride =>
        Environment.GetEnvironmentVariable("MIDDLEWARE_DEV_BASE_URL");

    public static string NormalizeBusinessCode(string businessCode) =>
        businessCode.Trim().ToLowerInvariant();

    public static string BaseUrlFor(string businessCode)
    {
        var dev = DevBaseUrlOverride;
        if (!string.IsNullOrWhiteSpace(dev))
            return dev.TrimEnd('/');
        return $"https://{NormalizeBusinessCode(businessCode)}.{BaseDomain}";
    }
}
