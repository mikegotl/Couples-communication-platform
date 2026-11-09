namespace Couples.Web.Components.Layout;

/// <summary>Document titles for routes whose pages render interactively without prerendering.</summary>
public static class RouteTitles
{
    private const string Suffix = " · BridgeTogether";

    private static readonly (string Prefix, string Title)[] Titles =
    [
        ("messages/received/", "Message from your partner"),
        ("messages/draft/", "Write a message"),
        ("messages/new", "Write a message"),
        ("messages", "Messages"),
        ("exercises/attempt/", "Exercise"),
        ("exercises", "Exercises"),
        ("admin/exercises", "Exercise review · Admin"),
        ("settings/pace", "Pace and notifications"),
        ("workspace", "Your workspace"),
        ("intake", "Your private topics"),
        ("safety", "Your safety"),
        ("invite/", "Join a workspace"),
    ];

    public static string For(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var path = relativePath.Split('?', '#')[0].TrimEnd('/');
        foreach (var (prefix, title) in Titles)
        {
            if (path.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) && !prefix.EndsWith('/')
                || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.EndsWith('/'))
            {
                return title + Suffix;
            }
        }

        return "BridgeTogether";
    }
}
