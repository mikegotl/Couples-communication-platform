using System.Collections.Concurrent;
using Markdig;

namespace Couples.Web.Legal;

/// <summary>
/// Renders the legal texts kept as Markdown in docs/legal (embedded at build). Raw HTML in the
/// Markdown is not passed through, so the output is safe to render as markup.
/// </summary>
public static class LegalDocuments
{
    public const string PrivacyNotice = "legal/privacy-notice.md";

    public const string PilotTerms = "legal/pilot-terms.md";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Markdown(string name) =>
        Cache.GetOrAdd("md:" + name, n =>
        {
            using var stream = typeof(LegalDocuments).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded document {name}.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

    public static string Html(string name) => Cache.GetOrAdd("html:" + name, _ => Markdig.Markdown.ToHtml(Markdown(name), Pipeline));
}
