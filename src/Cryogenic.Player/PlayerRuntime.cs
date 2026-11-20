namespace Cryogenic.Player;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>Small, version-checked hooks around the original CD game. No game assets are modified.</summary>
public sealed class PlayerRuntime {
    private readonly Machine machine;
    private readonly SessionOptions options;
    private readonly object gate = new();
    private readonly SaveStore saves = new(PlayerPaths.Saves);
    private ushort codeSegment;
    private uint dataBase;
    private bool ready;
    private bool gameInitialized;
    private bool introCompleted;
    private bool stopped;
    private string? pausePage;
    private string? lastCommand;
    private byte[]? capturedSave;
    private int? pendingTextMode;

    private PlayerRuntime(Machine machine, SessionOptions options) {
        this.machine = machine;
        this.options = options;
    }

    public static void Attach(Machine machine) {
        if (Environment.GetEnvironmentVariable("CRYOGENIC_PLAYER_SESSION") != "1") return;
        SessionOptions options = AtomicJson.Read<SessionOptions>(SessionFiles.Options)
            ?? throw new InvalidOperationException("The player session is missing its launch settings.");
        PlayerRuntime runtime = new(machine, options);
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(new UnconditionalBreakPoint(
            BreakPointType.MACHINE_START, _ => runtime.Initialize(), true), true);
        _ = Task.Run(runtime.PollCommands);
        Dispatcher.UIThread.Post(runtime.InstallHotkeys);
    }

    private void Initialize() {
        codeSegment = machine.CpuState.CS;
        dataBase = (uint)(codeSegment + 0xF4B) << 4;
        Require(0, [0xB8, 0x1D, 0xDD]);
        Require(0x000D, [0xE8, 0x70, 0x05]);
        Require(0x0024, [0xE8, 0x5C, 0x00]);
        if (options.Mode != "intro") {
            // Keep the intro's cleanup, including the initial palette setup.
            WriteCode(0x000D, [0xE8, 0xED, 0x05]);
            Hook(0x0010, () => machine.CpuState.ZeroFlag = true, once: true);
            Hook(0x0013, () => machine.CpuState.ZeroFlag = true, once: true);
            if (options.Mode == "load") {
                // Initialize the normal world view, including its persistent interface
                // graphics and palette buffers, before invoking the game's native loader.
                // Loading at 0024 would skip that setup and leave blank interface panels.
                Require(0x0034, [0xE8, 0x87, 0xB2]);
                ushort relative = unchecked((ushort)(0xB3B0 - 0x0037));
                WriteCode(0x0034, [0xE8, (byte)relative, (byte)(relative >> 8)]);
                Hook(0x0034, () => {
                    machine.CpuState.CL = 0;
                    // The native loader preserves the current world/book view, rather than
                    // restoring that transient mode from a save. Boot into the world view.
                    machine.Memory[dataBase + 0xFB] = 1;
                    // The original Load screen holds the world clock. Match that guard
                    // and start its countdown afresh instead of inheriting boot elapsed time.
                    machine.Memory[dataBase + 0x2788] = 1;
                    machine.Memory.UInt16[dataBase + 0x46DB] = machine.Memory.UInt16[dataBase + 0x146E];
                }, once: true);
            }
        } else {
            Require(0x05A1, [0xEB, 0xDD]);
            WriteCode(0x05A1, [0xEB, 0x5A]);
            Hook(0x05A1, () => introCompleted = true);
            Hook(0x05FD, () => { if (introCompleted) machine.CpuState.ZeroFlag = false; });
            Hook(0x0016, Stop, once: true);
        }
        Hook(0x0037, () => {
            if (options.Mode == "load") machine.Memory.UInt16[dataBase + 0x46DB] = machine.Memory.UInt16[dataBase + 0x146E];
            gameInitialized = true;
            ApplyTextMode(options.DialogueTextMode);
        }, once: true);
        // The mixer commits its text choice here. Other changes to 28E7 are temporary cutscene modes.
        Hook(0xA541, PersistTextMode);
        Hook(0xA5C8, PersistTextMode);
        // Route the original Save/Load menu commands to the player's 100-slot browser.
        // Their native RET still unwinds the DOS call normally before the next safe pause.
        Require(0xB28C, [0xBD, 0x7A, 0x20]);
        Require(0xB29E, [0xB9, 0x00, 0x40]);
        WriteCode(0xB28C, [0xC3]);
        WriteCode(0xB29E, [0xC3]);
        Hook(0xB28C, () => { lock (gate) pausePage = "save"; });
        Hook(0xB29E, () => { lock (gate) pausePage = "load"; });
        Hook(0xD820, SafeTick);
        Publish("running");
    }

    private void Require(ushort offset, byte[] bytes) {
        uint address = ((uint)codeSegment << 4) + offset;
        if (!machine.Memory.ReadRam((uint)bytes.Length, address).SequenceEqual(bytes)) {
            throw new InvalidDataException($"Unsupported Dune code at {offset:X4}. This player requires Dune CD 3.7.");
        }
    }

    private void WriteCode(ushort offset, byte[] bytes) {
        uint address = ((uint)codeSegment << 4) + offset;
        for (int i = 0; i < bytes.Length; i++) machine.Memory[address + (uint)i] = bytes[i];
    }

    private void Hook(ushort offset, Action callback, bool once = false) {
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(
            BreakPointType.CPU_EXECUTION_ADDRESS, ((uint)codeSegment << 4) + offset, _ => callback(), once), true);
    }

    private void SafeTick() {
        lock (gate) {
            if (!gameInitialized) return;
            if (pendingTextMode is int mode) {
                ApplyTextMode(mode);
                pendingTextMode = null;
            }
            if (!ready) {
                ready = true;
                Publish("running");
            }
            if (pausePage == null) return;
            capturedSave = CaptureSave();
            machine.PauseHandler.RequestPause("Dune player menu");
            string page = pausePage;
            pausePage = null;
            Publish("paused", page);
        }
    }

    private byte[] CaptureSave() {
        uint mapBase = (uint)machine.Memory.UInt16[dataBase + 0xDD00] << 4;
        byte[] payload = DuneSaveCodec.PackState(
            machine.Memory.ReadRam(DuneSaveCodec.MapLength, mapBase),
            machine.Memory.ReadRam(DuneSaveCodec.CodeStateLength, ((uint)codeSegment << 4) + 0xAA),
            machine.Memory.ReadRam(DuneSaveCodec.WorldStateLength, dataBase + 0xAA76),
            machine.Memory.ReadRam(DuneSaveCodec.GameStateLength, dataBase));
        return DuneSaveCodec.Encode(machine.Memory.UInt16[dataBase + 2], payload);
    }

    private async Task PollCommands() {
        while (!stopped) {
            try {
                PlayerCommand? command = AtomicJson.Read<PlayerCommand>(SessionFiles.Command);
                if (command != null && command.Id != lastCommand) {
                    lock (gate) {
                        lastCommand = command.Id;
                        switch (command.Action) {
                            case "pause":
                                RequestMenu(command.Name ?? "menu");
                                break;
                            case "resume":
                                pendingTextMode = command.DialogueTextMode;
                                capturedSave = null;
                                pausePage = null;
                                machine.PauseHandler.Resume();
                                Publish("running");
                                Dispatcher.UIThread.Post(() => {
                                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                                        desktop.MainWindow?.Activate();
                                    }
                                });
                                break;
                            case "save":
                                if (!machine.PauseHandler.IsPaused || capturedSave == null) throw new InvalidOperationException("Return to gameplay before saving this scene.");
                                saves.Write(command.Slot, command.Name ?? "", capturedSave);
                                PlayerDiagnostics.Capture(machine, dataBase, command.Slot);
                                Publish("paused", "save", message: $"Saved in slot {command.Slot:000}.");
                                break;
                            case "exit": Stop(); break;
                            default: throw new InvalidOperationException("Unknown player command.");
                        }
                    }
                }
            } catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException) {
                Publish(machine.PauseHandler.IsPaused ? "paused" : "running", message: error.Message);
            }
            await Task.Delay(50);
        }
    }

    private void RequestMenu(string page) {
        if (!ready || options.Mode == "intro") {
            machine.PauseHandler.RequestPause("Dune player menu");
            Publish("paused", "menu");
        } else {
            pausePage = page;
        }
    }

    private void InstallHotkeys() {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        desktop.MainWindow.Title = "Dune · Cryogenic Player";
        PlayerShortcuts.Install(desktop.MainWindow, page => {
            lock (gate) RequestMenu(page);
        });
    }

    private void ApplyTextMode(int mode) {
        byte value = (byte)Math.Clamp(mode, 0, 2); // 0 subtitles, 1 balloons, 2 no text.
        machine.Memory[dataBase + 0x28E7] = value;
        machine.Memory[dataBase + 0x28E8] = value;
    }

    private void PersistTextMode() {
        int mode = machine.Memory[dataBase + 0x28E7];
        if (mode > 2) return;
        SettingsStore store = new(PlayerPaths.SettingsFile);
        PlayerSettings settings = store.Load();
        if (settings.DialogueTextMode == mode) return;
        settings.DialogueTextMode = mode;
        store.Save(settings);
    }

    private void Stop() {
        stopped = true;
        machine.CpuState.IsRunning = false;
        machine.PauseHandler.Resume();
        Publish("stopped");
    }

    private void Publish(string state, string page = "menu", string? message = null) =>
        AtomicJson.Write(PlayerPaths.StatusFile, new SessionStatus(options.Id, state, ready, page, lastCommand, message));
}
