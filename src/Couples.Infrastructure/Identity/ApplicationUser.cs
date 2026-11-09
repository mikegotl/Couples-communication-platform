using Microsoft.AspNetCore.Identity;

namespace Couples.Infrastructure.Identity;

/// <summary>
/// An individual account. Each partner has their own; accounts are never shared and a
/// workspace membership grants no access to another account's data.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser() => Id = Guid.CreateVersion7();
}
