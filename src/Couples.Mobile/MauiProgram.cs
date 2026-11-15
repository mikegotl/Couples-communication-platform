using System.Reflection;
using Couples.Client;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace Couples.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();
        builder.Services.AddBridgeTogetherApiClient(options =>
        {
            options.BaseAddress = new Uri(ApiBaseAddress());
#if DEBUG
            // Local development against `dotnet run` on the same Mac only.
            options.AllowInsecureHttp = true;
#endif
        });

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>Set at build time from the ApiBaseAddress MSBuild property (see the project file).</summary>
    private static string ApiBaseAddress() =>
        typeof(MauiProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ApiBaseAddress").Value!;
}
