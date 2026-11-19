namespace Cryogenic.Player;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

public sealed class PlayerApplication : Application {
    public override void Initialize() {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            desktop.MainWindow = new PlayerWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void Run(string[] args) => AppBuilder.Configure<PlayerApplication>()
        .UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
}
