using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cryogenic.Player;

string temporary = Path.Combine(Path.GetTempPath(), "cryogenic-checks-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("CRYOGENIC_PLAYER_HOME", temporary);
Directory.CreateDirectory(temporary);
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
void Invalid(Action action, string name) { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception(name); }

if (args.Length >= 3 && args[0] == "--integration") {
    Environment.SetEnvironmentVariable("CRYOGENIC_ENGINE_HEADLESS", "1");
    using PlayerController controller = new(Path.GetFullPath(args[1]));
    controller.Settings.GameDataDirectory = Path.GetFullPath(args[2]);
    controller.SettingsStore.Save(controller.Settings);
    controller.ImportSaves();
    Check(controller.Saves.Read(1) != null, "Original saves imported");
    async Task WaitFor(Func<bool> condition, string description) {
        for (int n = 0; n < 400; n++) {
            controller.Poll();
            if (condition()) { checks++; return; }
            if (!controller.Running) throw new Exception(description + ": game closed. " + File.ReadAllText(Path.Combine(PlayerPaths.Home, "last-session.log")));
            await Task.Delay(50);
        }
        throw new Exception("Timed out: " + description);
    }
    await controller.Start("new");
    await WaitFor(() => controller.Status?.Ready == true, "New game reaches input loop");
    controller.Send("pause", name: "save");
    await WaitFor(() => controller.Paused, "Game pauses for save menu");
    await controller.Save(100, "End-to-end slot 100");
    Check(controller.Saves.Read(100)?.Name == "End-to-end slot 100", "Slot 100 saved by real game");
    controller.Settings.SubtitleBalloons = false;
    controller.SettingsStore.Save(controller.Settings);
    controller.Send("resume");
    await WaitFor(() => controller.Status?.State == "running", "Game resumes");
    controller.Send("pause", name: "menu");
    await WaitFor(() => controller.Paused, "Game pauses again");
    await controller.Start("load", 100);
    await WaitFor(() => controller.Status?.Ready == true, "Native loader restores slot 100 in a fresh process");
    controller.Send("pause", name: "save");
    await WaitFor(() => controller.Paused, "Loaded game can save again");
    await controller.Save(99, "Loaded save round trip");
    Check(controller.Saves.Read(99)?.GameTime == controller.Saves.Read(100)?.GameTime, "Native loader restores elapsed game time");
    byte[] beforeMap = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(100))).Payload;
    byte[] afterMap = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(99))).Payload;
    Check(beforeMap.AsSpan(0, DuneSaveCodec.PackedMapLength).SequenceEqual(afterMap.AsSpan(0, DuneSaveCodec.PackedMapLength)), "Native loader restores all packed map flags");
    await controller.Stop();
    Check(!controller.Running, "Engine exits cleanly");
    Check(!controller.SettingsStore.Load().SubtitleBalloons, "Changed text preference survives engine restarts");
    Console.WriteLine($"Passed {checks} end-to-end game checks. Isolated data: {temporary}");
    return;
}

Random random = new(1992);
for (int iteration = 0; iteration < 50; iteration++) {
    byte[] payload = new byte[DuneSaveCodec.PayloadLength];
    if (iteration % 3 == 0) random.NextBytes(payload);
    else if (iteration % 3 == 1) Array.Fill(payload, (byte)0xF7);
    else for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i / 17 % 256);
    byte[] encoded = DuneSaveCodec.Encode((ushort)iteration, payload);
    var decoded = DuneSaveCodec.Decode(encoded);
    Check(decoded.GameTime == iteration && decoded.Payload.SequenceEqual(payload), "Native save round trip");
    Invalid(() => DuneSaveCodec.Decode(encoded[..^1]), "Truncated save rejected");
}
byte[] map = new byte[DuneSaveCodec.MapLength];
for (int i = 0; i < map.Length; i++) map[i] = (byte)((i % 4 << 4) | 0xCF);
byte[] packed = DuneSaveCodec.PackState(map, new byte[DuneSaveCodec.CodeStateLength], new byte[DuneSaveCodec.WorldStateLength], new byte[DuneSaveCodec.GameStateLength]);
Check(packed.AsSpan(0, DuneSaveCodec.PackedMapLength).ToArray().All(b => b == 0x1B), "Map bit order matches native save packing");
SettingsStore settings = new(Path.Combine(temporary, "preferences.json"));
Check(settings.Load().SubtitleBalloons && settings.Load().Cycles == 5500, "New preferences default to balloons and 5500 cycles");
settings.Save(new PlayerSettings { SubtitleBalloons = false, Cycles = 5500 });
Check(!settings.Load().SubtitleBalloons && settings.Load().Cycles == 5500, "Changed preferences survive reload");
settings.Save(new PlayerSettings { DialogueTextMode = 2 });
Check(settings.Load().DialogueTextMode == 2 && !settings.Load().SubtitleBalloons, "Native no-text choice survives preference serialization");
SaveStore saves = new(Path.Combine(temporary, "slots"));
byte[] saved = DuneSaveCodec.Encode(11, packed);
saves.Write(100, "Last slot", saved);
Check(saves.Read(100)?.Name == "Last slot", "Slot 100 persists its name");
saves.Write(100, "Replacement", saved);
Check(File.Exists(saves.SavePath(100) + ".previous"), "Replacing a slot retains its previous save");
Invalid(() => saves.Write(100, "Broken", saved[..^1]), "A truncated save cannot replace a valid save");
Check(saves.Read(100)?.Name == "Replacement", "A failed save leaves its predecessor intact");
if (args.Length > 0 && Directory.Exists(args[0])) {
    foreach (string path in Directory.EnumerateFiles(args[0], "DUNE37S*.SAV")) {
        var decoded = DuneSaveCodec.Decode(File.ReadAllBytes(path));
        var again = DuneSaveCodec.Decode(DuneSaveCodec.Encode(decoded.GameTime, decoded.Payload));
        Check(again.Payload.SequenceEqual(decoded.Payload), "Original CD save fixture round trip: " + Path.GetFileName(path));
    }
}

AppBuilder.Configure<PlayerApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
KeyProbeWindow probe = new();
string? requestedPage = null;
PlayerShortcuts.Install(probe, p => requestedPage = p);
foreach (var shortcut in new[] { (Key.F5, "save"), (Key.F9, "load"), (Key.F10, "menu") }) {
    probe.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = shortcut.Item1 });
    Check(requestedPage == shortcut.Item2 && probe.GameKeys == 0, "Shortcut opens its player page before the DOS input handler");
    probe.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = shortcut.Item1 });
    Check(probe.GameKeys == 0, "Shortcut release does not reach DOS");
}
probe.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
Check(probe.GameKeys == 1, "Normal game keys still reach DOS input");
if (args.Length > 0 && Directory.Exists(args[0])) Environment.SetEnvironmentVariable("CRYOGENIC_IMPORT_SAVES", args[0]);
PlayerWindow window = new();
window.Show();
Dispatcher.UIThread.RunJobs();
string output = args.Length > 1 ? args[1] : temporary;
Directory.CreateDirectory(output);
void Render(string name) {
    Dispatcher.UIThread.RunJobs();
    using RenderTargetBitmap bitmap = new(new PixelSize(940, 650));
    bitmap.Render(window);
    bitmap.Save(Path.Combine(output, name + ".png"));
}
Button Find(string label) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
foreach (string label in new[] { "Play intro", "New game", "Load game", "Exit", "Settings" }) Check(Find(label).IsEnabled, "Startup button: " + label);
Render("main-menu");
Find("Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
Check(window.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked == true, "Balloon preference is on in the fresh menu");
Render("settings");
Find("Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Find("Load game").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Render("load-menu");
Console.WriteLine($"Passed {checks} checks. Menu captures: {output}");
// The process owns only temporary preferences; no original game files are needed or changed.

sealed class KeyProbeWindow : Window {
    public int GameKeys { get; private set; }
    protected override void OnKeyDown(KeyEventArgs e) { GameKeys++; e.Handled = true; }
    protected override void OnKeyUp(KeyEventArgs e) { GameKeys++; e.Handled = true; }
}
