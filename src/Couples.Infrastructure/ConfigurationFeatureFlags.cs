using Couples.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Couples.Infrastructure;

/// <summary>
/// Reads <c>FeatureManagement:&lt;flag&gt;</c> as a plain boolean. The web app replaces this with
/// Microsoft.FeatureManagement; both read the same configuration. A missing value is off.
/// </summary>
internal sealed class ConfigurationFeatureFlags(IConfiguration configuration) : IFeatureFlags
{
    public Task<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default) =>
        Task.FromResult(configuration.GetValue<bool>($"FeatureManagement:{flag}"));
}
