namespace Couples.Client;

/// <summary>Where the phone app finds the server.</summary>
public sealed class BridgeTogetherApiOptions
{
    /// <summary>
    /// Base address of the web app, for example <c>https://app.bridgetogether.example/</c>. Must be
    /// https outside local development so tokens and messages never travel in clear text.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Allows plain http (local development against <c>dotnet run</c> only).</summary>
    public bool AllowInsecureHttp { get; set; }
}
