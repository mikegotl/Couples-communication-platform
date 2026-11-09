using System.Text.RegularExpressions;

namespace Couples.IntegrationTests.Security;

/// <summary>
/// <c>EditForm</c> renders its own antiforgery token. A second <c>&lt;AntiforgeryToken /&gt;</c>
/// inside it posts the token twice and every submit fails with 400 (found on /pilot in the staging
/// rehearsal). Plain <c>&lt;form&gt;</c> elements, in contrast, need one.
/// </summary>
public sealed partial class FormMarkupTests
{
    [Fact]
    public void EditFormsDoNotAddASecondAntiforgeryToken()
    {
        var offenders = RazorFiles()
            .Where(f => EditFormBlock().Matches(File.ReadAllText(f)).Any(m => m.Value.Contains("<AntiforgeryToken", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "EditForm with a duplicate token: " + string.Join(", ", offenders));
    }

    [Fact]
    public void PlainPostFormsCarryAnAntiforgeryToken()
    {
        var offenders = RazorFiles()
            .Where(f => PlainPostForm().Matches(File.ReadAllText(f)).Any(m => !m.Value.Contains("<AntiforgeryToken", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "Plain post form without a token: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> RazorFiles()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var components = Path.Combine(dir.FullName, "src", "Couples.Web", "Components");
            if (Directory.Exists(components))
            {
                return Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories);
            }
        }

        throw new DirectoryNotFoundException("src/Couples.Web/Components");
    }

    [GeneratedRegex(@"<EditForm\b.*?</EditForm>", RegexOptions.Singleline)]
    private static partial Regex EditFormBlock();

    [GeneratedRegex(@"<form\b[^>]*method=""post""[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PlainPostForm();
}
