// Fully qualified: inside Couples.*, a bare "Application" resolves to the Couples.Application namespace.
namespace Couples.Mobile;

public sealed class App : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "BridgeTogether" };
}
