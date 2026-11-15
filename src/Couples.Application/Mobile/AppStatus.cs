namespace Couples.Application.Mobile;

/// <summary>
/// Wire contract for <c>GET /api/v1/app-status</c>, shared by the server and the phone app's API
/// client (docs/adr/0020). Holds nothing about any user, so it is safe to serve anonymously; the
/// phone app calls it at start to check it can reach the service and is still a supported version.
/// </summary>
/// <param name="Service">Product name, so the app can tell it reached the right server.</param>
/// <param name="ApiVersion">Major version of the <c>/api</c> surface; bumped only on breaking changes.</param>
/// <param name="MinimumAppVersion">Oldest phone app version the server still supports (for example "0.1.0").</param>
public sealed record AppStatus(string Service, int ApiVersion, string MinimumAppVersion)
{
    /// <summary>Current major version of the <c>/api</c> surface.</summary>
    public const int CurrentApiVersion = 1;

    /// <summary>
    /// True when <paramref name="appVersion"/> is at least <see cref="MinimumAppVersion"/>. An
    /// unreadable version on either side counts as supported, so a bad setting can't lock everyone out.
    /// </summary>
    public bool Supports(Version appVersion)
    {
        ArgumentNullException.ThrowIfNull(appVersion);
        return !Version.TryParse(MinimumAppVersion, out var minimum) || appVersion >= minimum;
    }
}

/// <summary>Settings for the phone app (configuration section <c>MobileApp</c>).</summary>
public sealed class MobileAppOptions
{
    public const string SectionName = "MobileApp";

    /// <summary>Oldest phone app version still supported; raise it to make older apps ask for an update.</summary>
    public string MinimumVersion { get; set; } = "0.1.0";
}
