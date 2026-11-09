namespace Couples.Application.Abstractions;

/// <summary>
/// Server-side feature flags (<see cref="FeatureFlags"/>), for code below the web layer. Services
/// check flags themselves so a flag that is off also closes the backend, not just the UI.
/// </summary>
public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default);
}
