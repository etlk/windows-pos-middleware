namespace MiddlewareApp.Core;

/// <summary>
/// Build-time configuration (spec §2). No login or API keys — the tenant API is
/// unauthenticated and addressed by business code.
/// </summary>
public static class AppConfig
{
    /// <summary>Tenant API host: https://{businessCode}.{BaseDomain}</summary>
    public const string BaseDomain = "cloudpos.lk";

    /// <summary>
    /// Soketi public app key. Subscribing only needs this — the app id and secret are
    /// server-side publish credentials and must never ship in the client.
    /// </summary>
    public const string PusherKey = "x2ihv6ewbwn8200acmsoel0yyss8yotv";
    public const string PusherCluster = "ap1";

    /// <summary>
    /// Custom Soketi host. Empty = official Pusher.com via Cluster.
    /// When set, overrides Cluster (PusherClient Host property).
    /// </summary>
    public const string PusherHost = "ws.cloudpos.lk";

    /// <summary>WebSocket port for PusherHost (443 for TLS).</summary>
    public const int PusherPort = 443;

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

    /// <summary>
    /// Host string for PusherClient options, e.g. "example.com:443".
    /// Null when using official Pusher.com clusters.
    /// </summary>
    public static string? PusherClientHost
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PusherHost)) return null;
            return PusherPort > 0 ? $"{PusherHost}:{PusherPort}" : PusherHost;
        }
    }
}
