using Microsoft.AspNetCore.Components;

namespace Couples.Web.Components;

/// <summary>
/// Base for interactive pages. A Blazor Server circuit is one DI scope, so pages injecting
/// services directly would share one DbContext for the life of the connection, and two page
/// instances (or a page and a lingering operation) could use it at once. Each page instead gets
/// its own scope, and with it its own DbContext, disposed with the page.
/// </summary>
public abstract class ScopedPage : OwningComponentBase
{
    protected T Service<T>()
        where T : notnull => ScopedServices.GetRequiredService<T>();
}
