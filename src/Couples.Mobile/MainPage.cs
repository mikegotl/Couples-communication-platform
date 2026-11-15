using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Couples.Mobile;

/// <summary>The single native page: a BlazorWebView hosting the shared Razor UI.</summary>
public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#FAF5EE");

        var webView = new BlazorWebView { HostPage = "wwwroot/index.html" };
        webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Components.Routes) });
        Content = webView;
    }
}
