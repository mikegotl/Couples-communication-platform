using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Couples.Web;

/// <summary>Resolves the signed-in user's id for passing to Application services.</summary>
public sealed class CurrentUser(AuthenticationStateProvider authenticationStateProvider)
{
    /// <summary>The signed-in user's id, or null when anonymous.</summary>
    public async Task<Guid?> GetIdAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var value = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
