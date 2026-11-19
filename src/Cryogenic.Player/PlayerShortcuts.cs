namespace Cryogenic.Player;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public static class PlayerShortcuts {
    public static void Install(Window window, Action<string> openPage) {
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => {
            string? page = Page(e.Key);
            if (page == null) return;
            e.Handled = true;
            openPage(page);
        }, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyUpEvent, (_, e) => {
            if (Page(e.Key) != null) e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private static string? Page(Key key) => key switch { Key.F5 => "save", Key.F9 => "load", Key.F10 => "menu", _ => null };
}
