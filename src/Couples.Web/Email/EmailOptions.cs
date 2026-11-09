namespace Couples.Web.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// <c>Pickup</c> writes each email as an HTML file to <see cref="PickupDirectory"/> (local
    /// development only). <c>None</c> sends nothing and logs a warning. A real provider is an open
    /// owner decision (see DECISIONS.md).
    /// </summary>
    public string Mode { get; set; } = "None";

    public string PickupDirectory { get; set; } = ".mail";
}
