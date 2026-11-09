using MudBlazor;

namespace Couples.Web.Components.Layout;

/// <summary>
/// The "Warm" look (2026-11-09): cream background, terracotta actions, sage information, Fraunces
/// headings and Nunito text (both self-hosted, SIL OFL). Every text colour meets WCAG 1.4.3 (4.5:1)
/// on both the cream background and white cards, and white text meets it on each filled colour.
/// </summary>
public static class AppTheme
{
    public const string Cream = "#FAF5EE";
    private static readonly string[] Text = ["Nunito", "Helvetica Neue", "Arial", "sans-serif"];
    private static readonly string[] Headings = ["Fraunces", "Georgia", "serif"];

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#A94A33",
            PrimaryDarken = "#8E3C28",
            PrimaryLighten = "#C76B53",
            Secondary = "#4F6B5A",
            Info = "#3F5C4B",
            Success = "#1b6e20",
            Warning = "#8a4b00",
            Error = "#b00020",
            Background = Cream,
            Surface = "#FFFFFF",
            AppbarBackground = Cream,
            AppbarText = "#2F2925",
            TextPrimary = "#2F2925",
            TextSecondary = "#665A52",
            LinesDefault = "#E7DDD2",
            LinesInputs = "#8C7D72",
            Divider = "#E7DDD2",
            ActionDefault = "#5C4F47",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Text },
            H4 = new H4Typography { FontFamily = Headings, FontWeight = "500", LetterSpacing = "-.01em" },
            H5 = new H5Typography { FontFamily = Headings, FontWeight = "500" },
            H6 = new H6Typography { FontFamily = Headings, FontWeight = "500" },
            Button = new ButtonTypography { FontFamily = Text, FontWeight = "600", TextTransform = "none", LetterSpacing = "0" },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "14px" },
    };
}
