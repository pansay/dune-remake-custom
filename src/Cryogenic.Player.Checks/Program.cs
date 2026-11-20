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

if (args.Length >= 3 && args[0] is "--integration" or "--map-mod-integration") {
    Environment.SetEnvironmentVariable("CRYOGENIC_ENGINE_HEADLESS", "1");
    Environment.SetEnvironmentVariable("CRYOGENIC_CAPTURE_FRAMES", "1");
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
    if (args[0] == "--map-mod-integration") {
        int stateOffset = DuneSaveCodec.PayloadLength - DuneSaveCodec.GameStateLength;
        int sourceSlot = controller.Saves.Read(2) != null ? 2 : 1;
        var source = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(sourceSlot)));
        byte[] early = source.Payload.ToArray();
        early[stateOffset + FullVisionMapReveal.PowersOffset] = (byte)((early[stateOffset + FullVisionMapReveal.PowersOffset] | 1) & ~FullVisionMapReveal.WaterOfLifePower);
        controller.Saves.Write(97, "Test: first visions", DuneSaveCodec.Encode(source.GameTime, early));
        byte[] full = early.ToArray();
        full[stateOffset + FullVisionMapReveal.PowersOffset] |= FullVisionMapReveal.WaterOfLifePower;
        controller.Saves.Write(96, "Test: Water of Life", DuneSaveCodec.Encode(source.GameTime, full));
        byte[] State(int slot) => DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(slot))).Payload[stateOffset..];
        int HiddenLocations(byte[] state) => Enumerable.Range(0, FullVisionMapReveal.LocationCount).Count(i => (state[FullVisionMapReveal.LocationsOffset + i * FullVisionMapReveal.LocationSize + FullVisionMapReveal.LocationFlagsOffset] & FullVisionMapReveal.Hidden) != 0);
        async Task LoadAndPause(int slot, bool enabled) {
            controller.Settings.FullVisionMapReveal = enabled;
            controller.SettingsStore.Save(controller.Settings);
            await controller.Start("load", slot);
            await WaitFor(() => controller.Status?.Ready == true, "Mod scenario reaches gameplay");
            controller.Send("pause", name: "save");
            await WaitFor(() => controller.Paused, "Mod scenario pauses safely");
        }
        await LoadAndPause(97, true);
        await controller.Save(95, "Test: enabled before full powers");
        Check(HiddenLocations(State(95)) == HiddenLocations(State(97)), "First visions do not reveal the map");
        await LoadAndPause(96, false);
        await controller.Save(94, "Test: full powers, mod off");
        Check(HiddenLocations(State(94)) == HiddenLocations(State(96)), "Full powers keep vanilla discovery when the mod is off");
        controller.Settings.FullVisionMapReveal = true;
        controller.SettingsStore.Save(controller.Settings);
        controller.Send("resume");
        await WaitFor(() => controller.Status?.State == "running", "Mod can be enabled in a live game");
        controller.Send("pause", name: "save");
        await WaitFor(() => controller.Paused, "Live revealed game can pause");
        await controller.Save(93, "Test: full map revealed");
        byte[] before = State(94), revealed = State(93);
        Check(HiddenLocations(revealed) == 0, "All 70 locations become known after Water of Life");
        bool preserved = true;
        for (int i = 0; i < FullVisionMapReveal.LocationCount; i++) {
            int start = FullVisionMapReveal.LocationsOffset + i * FullVisionMapReveal.LocationSize;
            for (int n = 0; n < FullVisionMapReveal.LocationSize; n++) {
                if (n is 10 or 11) continue;
                if (before[start + n] != revealed[start + n]) preserved = false;
            }
            if ((before[start + 10] & 0x7F) != revealed[start + 10]) preserved = false;
        }
        Check(preserved, "Reveal preserves coordinates, names, ownership, visits, troops and prospecting");
        Check(before[0x25] == revealed[0x25] && before[0x2A] == revealed[0x2A], "Reveal does not advance visits or story phase");
        controller.Settings.FullVisionMapReveal = false;
        controller.SettingsStore.Save(controller.Settings);
        controller.Send("resume");
        await WaitFor(() => controller.Status?.State == "running", "Mod can be turned off");
        controller.Send("pause", name: "save");
        await WaitFor(() => controller.Paused, "Game pauses after disabling the mod");
        await controller.Save(92, "Test: mod disabled after discovery");
        Check(HiddenLocations(State(92)) == 0, "Turning the mod off preserves discovered locations");
        await LoadAndPause(93, false);
        await controller.Save(91, "Test: native save reload");
        Check(HiddenLocations(State(91)) == 0, "Revealed locations survive native save and reload without the mod");
        await LoadAndPause(96, true);
        await controller.Save(90, "Test: full powers on initial load");
        Check(HiddenLocations(State(90)) == 0, "Enabling the mod also works when loading an existing full-power save");
        await controller.Stop();
        Check(!controller.Running, "Map mod test engine exits cleanly");
        Console.WriteLine($"Passed {checks} full-map game checks. Isolated data: {temporary}");
        return;
    }
    await controller.Start("new");
    await WaitFor(() => controller.Status?.Ready == true, "New game reaches input loop");
    await Task.Delay(1500);
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
    await Task.Delay(1500);
    controller.Send("pause", name: "save");
    await WaitFor(() => controller.Paused, "Loaded game can save again");
    await controller.Save(99, "Loaded save round trip");
    Check(controller.Saves.Read(99)?.GameTime == controller.Saves.Read(100)?.GameTime, "Native loader restores elapsed game time");
    byte[] beforeMap = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(100))).Payload;
    byte[] afterMap = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(99))).Payload;
    Check(beforeMap.AsSpan(0, DuneSaveCodec.PackedMapLength).SequenceEqual(afterMap.AsSpan(0, DuneSaveCodec.PackedMapLength)), "Native loader restores all packed map flags");
    string diagnostics = Path.Combine(PlayerPaths.Home, "diagnostics");
    byte[] Palette(int slot) {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(diagnostics, $"slot-{slot:D3}.json")));
        return json.RootElement.GetProperty("palette").GetBytesFromBase64();
    }
    Check(Palette(100).SequenceEqual(Palette(99)), "Loading preserves the scene's complete colour palette");
    using (var beforeFrame = SkiaSharp.SKBitmap.Decode(Path.Combine(diagnostics, "slot-100.png")))
    using (var loadedFrame = SkiaSharp.SKBitmap.Decode(Path.Combine(diagnostics, "slot-099.png"))) {
        bool sameInterface = true;
        // Permanent interface artwork: exclude portraits, text, pointers and animations.
        for (int y = 154; y < 200; y++) for (int x = 20; x < 80; x++) {
            if (beforeFrame.GetPixel(x, y) != loadedFrame.GetPixel(x, y)) sameInterface = false;
        }
        Check(sameInterface, "Loading restores the original interface artwork and its colours");
    }
    int importedSlot = controller.Saves.Read(2) != null ? 2 : 1;
    var original = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(importedSlot)));
    await controller.Start("load", importedSlot);
    await WaitFor(() => controller.Status?.Ready == true, "Original imported save reaches gameplay");
    await Task.Delay(1500);
    controller.Send("pause", name: "save");
    await WaitFor(() => controller.Paused, "Original imported save can pause");
    await controller.Save(98, "Imported save round trip");
    var importedAgain = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(98)));
    Check(importedAgain.GameTime == original.GameTime, "Original imported save keeps its game time");
    Check(importedAgain.Payload.AsSpan(0, DuneSaveCodec.PackedMapLength).SequenceEqual(original.Payload.AsSpan(0, DuneSaveCodec.PackedMapLength)), "Original imported save keeps its map flags");
    await controller.Stop();
    Check(!controller.Running, "Engine exits cleanly");
    Check(!controller.SettingsStore.Load().SubtitleBalloons, "Changed text preference survives engine restarts");
    Console.WriteLine($"Passed {checks} end-to-end game checks. Isolated data: {temporary}");
    return;
}

Random random = new(1992);
byte[] mapState = new byte[DuneSaveCodec.GameStateLength];
random.NextBytes(mapState);
mapState[FullVisionMapReveal.PowersOffset] = 1;
mapState[FullVisionMapReveal.KnownSietchesOffset] = 4;
for (int i = 0; i < FullVisionMapReveal.LocationCount; i++) {
    int start = FullVisionMapReveal.LocationsOffset + i * FullVisionMapReveal.LocationSize;
    mapState[start + 8] = (byte)((i % 4) switch { 0 => 0x08, 1 => 0x20, 2 => 0x21, _ => 0x28 });
    mapState[start + 10] = (byte)((mapState[start + 10] & 0x7F) | (i % 3 == 0 ? 0 : 0x80));
}
mapState[FullVisionMapReveal.LocationsEnd] = 0xFF;
mapState[FullVisionMapReveal.LocationsEnd + 1] = 0xFF;
Check(FullVisionMapReveal.Plan(mapState, enabled: true).Changes.Count == 0, "Map mod waits beyond the first visions");
mapState[FullVisionMapReveal.PowersOffset] |= FullVisionMapReveal.WaterOfLifePower;
Check(FullVisionMapReveal.Plan(mapState, enabled: false).Changes.Count == 0, "Disabled mod preserves vanilla discovery even with full powers");
byte[] originalMapState = mapState.ToArray();
MapRevealResult revealPlan = FullVisionMapReveal.Plan(mapState, enabled: true);
foreach (DuneStateChange change in revealPlan.Changes) mapState[change.Offset] = change.Value;
Check(Enumerable.Range(0, 70).All(i => (mapState[0x100 + i * 28 + 10] & 0x80) == 0), "All original locations can be discovered");
bool unrelatedStatePreserved = true;
for (int offset = 0; offset < mapState.Length; offset++) {
    bool discoveryField = offset >= 0x100 && offset < FullVisionMapReveal.LocationsEnd && (offset - 0x100) % 28 is 10 or 11;
    if (offset != 0x27 && !discoveryField && mapState[offset] != originalMapState[offset]) unrelatedStatePreserved = false;
}
Check(unrelatedStatePreserved, "Discovery changes preserve unrelated native game state");
Check(Enumerable.Range(0, 70).All(i => (originalMapState[0x100 + i * 28 + 10] & 0x7F) == mapState[0x100 + i * 28 + 10]), "Visit, ownership, military and prospecting flags stay intact");
Check(mapState[0x27] == originalMapState[0x27] + revealPlan.RevealedSietches, "Known-sietch counter follows native discovery");
Check(FullVisionMapReveal.Plan(mapState, enabled: true).Changes.Count == 0, "Repeated reveal is idempotent");
byte[] malformedLocations = mapState.ToArray(); malformedLocations[FullVisionMapReveal.LocationsEnd] = 0;
Invalid(() => FullVisionMapReveal.Plan(malformedLocations, enabled: true), "An unsupported location table is not modified");
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
Check(window.GetVisualDescendants().OfType<RadioButton>().Single(r => Equals(r.Content, "Balloons")).IsChecked == true, "Balloon preference is on in the fresh menu");
foreach (string choice in new[] { "Subtitles", "No subtitles", "Balloons" }) {
    window.GetVisualDescendants().OfType<RadioButton>().Single(r => Equals(r.Content, choice)).IsChecked = true;
    Find("Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Find("Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check(window.GetVisualDescendants().OfType<RadioButton>().Single(r => r.IsChecked == true).Content?.ToString() == choice, "Original subtitle choice persists: " + choice);
}
Render("settings");
Find("Game mods").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
CheckBox mapOption = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Tag, "full-visions-reveal-map"));
Check(mapOption.IsEnabled && mapOption.IsChecked == false, "Full map reveal is available and off by default");
mapOption.IsChecked = true;
Find("Save mods").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Find("Game mods").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
Check(window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Tag, "full-visions-reveal-map")).IsChecked == true, "Full map reveal preference is remembered");
window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Tag, "full-visions-reveal-map")).IsChecked = false;
Find("Save mods").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Find("Game mods").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Render("game-mods");
Find("Back to settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
