namespace Cryogenic.Player;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

public sealed class PlayerWindow : Window {
    private readonly PlayerController controller = new();
    private readonly ContentControl page = new();
    private readonly TextBlock notice = new() { Foreground = Brush("#E4BD79"), TextWrapping = TextWrapping.Wrap, MinHeight = 24 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool wasRunning;
    private string? shownPause;
    private bool busy;
    private bool closing;

    public PlayerWindow() {
        Title = "Dune · Cryogenic Player";
        Width = 940; Height = 650; MinWidth = 780; MinHeight = 580;
        Background = Brush("#10151B");
        FontFamily = new FontFamily("Inter, Helvetica Neue, sans-serif");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("1.05*,1.3*") };
        layout.Children.Add(CreateArtwork());
        Border body = new() { Padding = new Thickness(34, 34, 34, 24) };
        Grid.SetColumn(body, 1);
        Grid bodyLayout = new() { RowDefinitions = new RowDefinitions("*,Auto") };
        bodyLayout.Children.Add(page);
        Grid.SetRow(notice, 1);
        bodyLayout.Children.Add(notice);
        body.Child = bodyLayout;
        layout.Children.Add(body);
        Content = layout;
        try { controller.ImportSaves(); }
        catch (Exception error) when (error is IOException or InvalidDataException) { notice.Text = error.Message; }
        ShowMenu();
        timer.Tick += (_, _) => Poll();
        timer.Start();
        Closing += async (_, e) => {
            if (closing) return;
            e.Cancel = true;
            await Exit();
        };
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private static Control CreateArtwork() {
        Grid art = new();
        art.Children.Add(new DesertBackdrop());
        StackPanel label = new() { Margin = new Thickness(34), VerticalAlignment = VerticalAlignment.Top, Spacing = 9 };
        label.Children.Add(new TextBlock { Text = "D U N E", FontSize = 46, FontWeight = FontWeight.Light, Foreground = Brush("#F3DFC0") });
        label.Children.Add(new TextBlock { Text = "THE SPICE MUST FLOW", FontSize = 11, LetterSpacing = 2.2, Foreground = Brush("#CAA878") });
        art.Children.Add(label);
        StackPanel caption = new() { Margin = new Thickness(34), VerticalAlignment = VerticalAlignment.Bottom, Spacing = 7 };
        caption.Children.Add(new TextBlock { Text = "ARRAKIS AWAITS", FontSize = 12, LetterSpacing = 2, Foreground = Brush("#E8C58E") });
        caption.Children.Add(new TextBlock { Text = "Cryogenic · Player edition", FontSize = 12, Foreground = Brush("#B4A593") });
        art.Children.Add(caption);
        return art;
    }

    private StackPanel Panel(string title, string subtitle) {
        StackPanel panel = new() { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 29, FontWeight = FontWeight.Light, Foreground = Brush("#F5E9D6") });
        panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, Foreground = Brush("#ACA99F"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) });
        page.Content = panel;
        return panel;
    }

    private Button Button(string title, Func<Task> action, bool enabled = true, bool primary = false) {
        Button button = new() {
            Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(18, 11), MinHeight = 43, IsEnabled = enabled,
            Background = Brush(primary ? "#93602D" : "#202932"), Foreground = Brush("#F3E4CE"),
            BorderBrush = Brush(primary ? "#BE8B4C" : "#39414A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4)
        };
        button.Click += async (_, _) => await Perform(action);
        return button;
    }

    private async Task Perform(Func<Task> action) {
        if (busy) return;
        busy = true;
        try { notice.Text = ""; await action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TimeoutException or System.ComponentModel.Win32Exception) { notice.Text = error.Message; }
        finally { busy = false; }
    }

    private void ShowMenu() {
        StackPanel panel = Panel(controller.Running ? "Journey paused" : "Welcome to Dune", controller.Running ? "Your current game is paused." : "Return to the desert of the original Cryo adventure.");
        if (controller.Running) panel.Children.Add(Button("Resume game", Resume, primary: true));
        panel.Children.Add(Button("New game", async () => {
            if (await AllowReplace()) await Launch("new");
        }, primary: !controller.Running));
        panel.Children.Add(Button("Load game", () => { ShowSaves(false); return Task.CompletedTask; }));
        if (controller.Running) panel.Children.Add(Button("Save game", () => { ShowSaves(true); return Task.CompletedTask; }, controller.Status?.Ready == true));
        panel.Children.Add(Button("Play intro", async () => { if (await AllowReplace()) await Launch("intro"); }));
        panel.Children.Add(Button("Settings", () => { ShowSettings(); return Task.CompletedTask; }));
        panel.Children.Add(Button("Exit", Exit));
        panel.Children.Add(new TextBlock { Text = "During play: F5 save · F9 load · F10 menu", FontSize = 11, Foreground = Brush("#A19E96"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
    }

    private async Task<bool> AllowReplace() => !controller.Running || await Confirm("Leave this game?", "Any progress since your last save will be lost.", "Continue");

    private async Task Launch(string mode, int? slot = null) {
        notice.Text = "Opening Dune…";
        await controller.Start(mode, slot);
        wasRunning = true;
        shownPause = null;
        Hide();
    }

    private Task Resume() {
        controller.Send("resume");
        shownPause = null;
        Hide();
        return Task.CompletedTask;
    }

    private void Poll() {
        bool running = controller.Running;
        SessionStatus? status = controller.Poll();
        if (running && status?.State == "paused" && !busy) {
            string key = status.SessionId + status.Page;
            if (key != shownPause) {
                shownPause = key;
                if (status.Page == "save" && status.Ready) ShowSaves(true);
                else if (status.Page == "load") ShowSaves(false);
                else ShowMenu();
                Show(); Activate();
            }
        }
        if (wasRunning && !running && !closing && !busy) {
            shownPause = null;
            ShowMenu(); Show(); Activate();
            notice.Text = status?.Message ?? "";
        }
        wasRunning = running;
    }

    private sealed record SlotChoice(int Slot, SaveEntry? Save) {
        public override string ToString() => Save == null ? $"{Slot:000}   Empty slot" : $"{Slot:000}   {Save.Name}\n          Day {Save.Day} · {Save.SavedAt.LocalDateTime:g}";
    }

    private void ShowSaves(bool saving) {
        StackPanel panel = Panel(saving ? "Save your journey" : "Load a journey", "100 slots · Your original saves are imported automatically.");
        List<SlotChoice> slots = Enumerable.Range(1, SaveStore.SlotCount).Select(n => new SlotChoice(n, controller.Saves.Read(n))).ToList();
        bool hasSaves = slots.Any(s => s.Save != null);
        ListBox list = new() { Height = 264, ItemsSource = saving ? slots : slots.Where(s => s.Save != null).ToList() };
        list.SelectedIndex = saving ? Math.Max(0, slots.FindIndex(s => s.Save == null)) : 0;
        panel.Children.Add(list);
        if (!saving && !hasSaves) panel.Children.Add(new TextBlock { Text = "No saved journeys yet. Start a new game to begin.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#ACA99F") });
        TextBox name = new() { Watermark = "Name this save", MaxLength = 70, Text = saving ? "Arrakis" : "", IsVisible = saving };
        list.SelectionChanged += (_, _) => { if (saving && list.SelectedItem is SlotChoice { Save: not null } choice) name.Text = choice.Save.Name; };
        panel.Children.Add(name);
        panel.Children.Add(Button(saving ? "Save game" : "Load game", async () => {
            if (list.SelectedItem is not SlotChoice choice) throw new InvalidOperationException("Choose a save slot first.");
            if (saving) {
                if (choice.Save != null && !await Confirm("Replace this save?", $"Slot {choice.Slot:000}: {choice.Save.Name}", "Replace save")) return;
                await controller.Save(choice.Slot, name.Text ?? "");
                ShowSaves(true);
                notice.Text = $"Saved in slot {choice.Slot:000}. You can resume your game.";
            } else if (await AllowReplace()) {
                await Launch("load", choice.Slot);
            }
        }, enabled: saving || hasSaves, primary: true));
        panel.Children.Add(Button("Back", () => { ShowMenu(); return Task.CompletedTask; }));
    }

    private void ShowSettings() {
        controller.Settings.DialogueTextMode = controller.SettingsStore.Load().DialogueTextMode;
        StackPanel panel = Panel("Settings", "Preferences are remembered for your next visit.");
        CheckBox balloons = new() { Content = "Show dialogue in subtitle balloons", IsChecked = controller.Settings.SubtitleBalloons };
        panel.Children.Add(balloons);
        panel.Children.Add(new TextBlock { Text = "Display dialogue text in the original speech balloons.", Foreground = Brush("#ACA99F"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Game speed (cycles)", Margin = new Thickness(0, 12, 0, 0) });
        NumericUpDown cycles = new() { Minimum = 500, Maximum = 30000, Increment = 500, Value = controller.Settings.Cycles, FormatString = "0" };
        panel.Children.Add(cycles);
        panel.Children.Add(new TextBlock { Text = "Recommended: 5500", Foreground = Brush("#ACA99F"), FontSize = 12 });
        TextBlock dataFolder = new() { Text = controller.Settings.GameDataDirectory ?? "Using the local dune folder", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#ACA99F"), MaxHeight = 45 };
        panel.Children.Add(Button("Choose game data folder…", async () => {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Dune CD game files", AllowMultiple = false });
            if (folders.Count == 0) return;
            string? chosen = folders[0].TryGetLocalPath();
            if (chosen == null || !File.Exists(Path.Combine(chosen, "DNCDPRG.EXE")) || !File.Exists(Path.Combine(chosen, "DUNE.DAT"))) {
                throw new InvalidOperationException("Choose the folder containing both DNCDPRG.EXE and DUNE.DAT.");
            }
            controller.Settings.GameDataDirectory = chosen;
            dataFolder.Text = chosen;
        }));
        panel.Children.Add(dataFolder);
        panel.Children.Add(Button("Save settings", () => {
            if (controller.Settings.SubtitleBalloons != (balloons.IsChecked == true)) controller.Settings.SubtitleBalloons = balloons.IsChecked == true;
            controller.Settings.Cycles = (int)(cycles.Value ?? 5500);
            controller.SettingsStore.Save(controller.Settings);
            ShowMenu(); notice.Text = "Settings saved.";
            return Task.CompletedTask;
        }, primary: true));
        panel.Children.Add(Button("Back", () => { ShowMenu(); return Task.CompletedTask; }));
    }

    private async Task<bool> Confirm(string title, string message, string action) {
        Window dialog = new() { Title = title, Width = 400, Height = 205, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#151D25") };
        StackPanel panel = new() { Margin = new Thickness(24), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        Button cancel = new() { Content = "Cancel" };
        Button accept = new() { Content = action };
        cancel.Click += (_, _) => dialog.Close(false);
        accept.Click += (_, _) => dialog.Close(true);
        actions.Children.Add(cancel); actions.Children.Add(accept); panel.Children.Add(actions);
        dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task Exit() {
        if (controller.Running && !await Confirm("Exit Dune?", "Save your progress before exiting if you want to continue later.", "Exit")) return;
        closing = true;
        timer.Stop();
        await controller.Stop();
        controller.Dispose();
        Close();
    }
}

internal sealed class DesertBackdrop : Control {
    public override void Render(DrawingContext context) {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.Parse("#122738"), 0), new GradientStop(Color.Parse("#3E4344"), 0.52), new GradientStop(Color.Parse("#AA7845"), 1)]
        }, Bounds);
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#D4AB6C")), null, new Point(w * .66, h * .37), 53, 53);
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#26343A")), null, new Point(w * .64, h * .35), 48, 48);
        Dune(context, w, h, .63, .14, "#755039");
        Dune(context, w, h, .75, -.16, "#A17040");
        Dune(context, w, h, .88, .13, "#533A2B");
    }

    private static void Dune(DrawingContext context, double w, double h, double height, double slope, string color) {
        StreamGeometry shape = new();
        using (StreamGeometryContext path = shape.Open()) {
            path.BeginFigure(new Point(0, h * height), true);
            path.CubicBezierTo(new Point(w * .32, h * (height + slope)), new Point(w * .60, h * (height - slope)), new Point(w, h * (height + slope / 2)));
            path.LineTo(new Point(w, h)); path.LineTo(new Point(0, h)); path.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.Parse(color)), null, shape);
    }
}
