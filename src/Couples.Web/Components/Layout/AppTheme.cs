using MudBlazor;

namespace Couples.Web.Components.Layout;

/// <summary>
/// MudBlazor's default status colours fall below 4.5:1 as text on white (outlined alerts, text
/// buttons). These darker shades meet WCAG 1.4.3 both as text on white and as white on colour.
/// </summary>
public static class AppTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Info = "#0b5cad",
            Success = "#1b6e20",
            Warning = "#8a4b00",
            Error = "#b00020",
            TextSecondary = "rgba(0,0,0,0.68)",
        },
    };
}
