using System.Buffers.Binary;
using Cryogenic;
using Cryogenic.Player;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

/// <summary>Runs real DOS story routines in isolated saves; the driver is absent from the player build.</summary>
internal static class NativeStoryChecks {
    public static void RunEngine(string[] args) {
        string[] engineArgs = args.Where(a => a != "--engine")
            .Append($"--{nameof(Configuration.ProvidedAsmHandlersSegment)}={DriverLoadToolbox.INTERRUPT_HANDLER_SEGMENT}").ToArray();
        const string checksum = "5F30AEB84D67CF2E053A83C09C2890F010F2E25EE877EBEC58EA15C5B30CFFF9";
        if (Environment.GetEnvironmentVariable("CRYOGENIC_VICTORY_CHECK") != null) Spice86.Program.RunWithOverrides<VictoryStoryCheckSupplier>(engineArgs, checksum);
        else Spice86.Program.RunWithOverrides<ChaniStoryCheckSupplier>(engineArgs, checksum);
    }

    public static async Task Run(string installedEngine, string originalData, string temporary, Action<bool, string> check) {
        Environment.SetEnvironmentVariable("CRYOGENIC_ENGINE_HEADLESS", "1");
        Environment.SetEnvironmentVariable("CRYOGENIC_CAPTURE_FRAMES", "1");
        int stateOffset = DuneSaveCodec.PayloadLength - DuneSaveCodec.GameStateLength;
        byte[] State(int slot) => DuneSaveCodec.Decode(File.ReadAllBytes(Path.Combine(PlayerPaths.Saves, $"slot-{slot:D3}.sav"))).Payload[stateOffset..];
        ushort Word(byte[] state, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(offset));
        void SetWord(byte[] state, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(offset), value);
        byte[] baseline;
        ushort originalTime;
        int site, siteIndex;
        using (PlayerController controller = new()) {
            try {
                controller.Settings.GameDataDirectory = Path.GetFullPath(originalData);
                controller.SettingsStore.Save(controller.Settings);
                controller.ImportSaves();
                check(controller.Saves.Read(2) != null, "Original test save imported into isolation");
                var source = DuneSaveCodec.Decode(File.ReadAllBytes(controller.Saves.SavePath(2)));
                originalTime = source.GameTime;
                baseline = source.Payload;
                byte[] initial = baseline[stateOffset..];
                site = Word(initial, NoChaniKidnapping.TroopsOffset + 4);
                siteIndex = (site - FullVisionMapReveal.LocationsOffset) / FullVisionMapReveal.LocationSize;
                check(siteIndex is >= 0 and < 70 && initial[site + 8] < 0x20, "Native fixture has an allied sietch and a recruited troop");

                byte[] Treatment(byte progress, bool paulStays = false) {
                    byte[] state = initial.ToArray();
                    state[0x2A] = 0x5D;
                    state[0x29] = 100;
                    state[0x0A] |= 0x21;
                    state[0xF8] = 1;
                    state[0xF9] = progress;
                    SetWord(state, 0x11DB, (ushort)site);
                    SetWord(state, NoChaniKidnapping.TroopsOffset + 18, (ushort)((Word(state, NoChaniKidnapping.TroopsOffset + 18) & ~0x800) | 0x400));
                    SetWord(state, 0x0010, (ushort)(Word(state, 0x0010) & ~0x80));
                    SetWord(state, 0x1048, (ushort)((state[site + 8] << 8) | 2));
                    SetWord(state, 0x104A, (ushort)(((siteIndex + 1) << 8) | 0x80));
                    state[0x1057] &= 0xBD;
                    if (paulStays) {
                        SetWord(state, 4, Word(state, 0x1048));
                        SetWord(state, 6, Word(state, 0x104A));
                        state[8] = state[site + 8];
                        state[0xB] = 2;
                    }
                    return state;
                }
                void Fixture(int slot, byte[] state) {
                    byte[] payload = baseline.ToArray();
                    state.CopyTo(payload, stateOffset);
                    controller.Saves.Write(slot, "Isolated Chani story fixture", DuneSaveCodec.Encode(originalTime, payload));
                }
                async Task WaitFor(Func<bool> condition, string message) {
                    for (int n = 0; n < 600; n++) {
                        controller.Poll();
                        if (condition()) { check(true, message); return; }
                        if (!controller.Running) throw new Exception(message + ": " + File.ReadAllText(Path.Combine(temporary, "last-session.log")));
                        await Task.Delay(50);
                    }
                    throw new Exception("Timed out: " + message + ". " + File.ReadAllText(Path.Combine(temporary, "last-session.log")));
                }
                async Task<(byte[] State, StoryObservation Probe)> Scenario(int fixture, int output, string script, bool enabled, bool map = false) {
                    Environment.SetEnvironmentVariable("CRYOGENIC_STORY_CHECK", script);
                    controller.Settings.NoChaniKidnapping = enabled;
                    controller.Settings.FullVisionMapReveal = map;
                    controller.SettingsStore.Save(controller.Settings);
                    await controller.Start("load", fixture);
                    await WaitFor(() => controller.Status?.Ready == true, "Native scenario reached gameplay: " + script);
                    controller.Send("pause", name: "save");
                    await WaitFor(() => controller.Paused, "Native scenario paused: " + script);
                    await controller.Save(output, "Chani story result: " + script);
                    StoryObservation probe = AtomicJson.Read<StoryObservation>(Path.Combine(temporary, "story-check.json"))!;
                    return (State(output), probe);
                }

                Fixture(97, Treatment(0));
                var mid = await Scenario(97, 90, "tick", true);
                check(mid.State[0x2A] == 0x5D && mid.State[0xF8] == 1 && mid.State[0xF9] == 8, "Mod preserves the original treatment duration");
                check(Word(mid.State, NoChaniKidnapping.TroopsOffset + 18) is var midFlags && (midFlags & 0x400) != 0 && (midFlags & 0x800) == 0, "Troops remain ill until Chani finishes");
                Fixture(96, Treatment(0xF8));
                var vanilla = await Scenario(96, 89, "tick", false);
                check(vanilla.State[0x2A] == 0x60 && Word(vanilla.State, 0x104A) == 0x0280, "Mod off retains the original kidnapping at cure completion");
                check(vanilla.Probe.KidnappingExecuted, "The original kidnapping routine actually ran in the control scenario");
                var cured = await Scenario(96, 88, "tick", true);
                check(cured.State[0x2A] == 0x6C && cured.State[0xF8] == 0 && Word(cured.State, 0x11DB) == 0, "Native cure completes and advances past the rescue chapter");
                check(!cured.Probe.KidnappingExecuted && !cured.Probe.FortressAssignmentExecuted, "Chani is never moved into captivity with the mod on");
                check(Word(cured.State, 0x104A) == ((siteIndex + 1) << 8 | 0x80) && cured.State[0x1048] == 2, "Chani stays at the sietch she cured");
                check((Word(cured.State, NoChaniKidnapping.TroopsOffset + 18) & 0xC00) == 0x800, "Native cure clears disease and records successful treatment");
                check(cured.Probe.HealingVisionQueued, "The normal vision announcing the successful cure is queued");
                check(cured.State[0xC2] == vanilla.State[0xC2], "Skipping the rescue does not start or skip the final assault");
                byte[] safe = cured.State.ToArray();
                SetWord(safe, 4, Word(safe, 0x1048)); SetWord(safe, 6, Word(safe, 0x104A)); safe[8] = safe[site + 8]; safe[0xB] = 2;
                Fixture(95, safe);
                var follow = await Scenario(95, 87, "follow", true);
                check((Word(follow.State, 0x10) & 0x80) != 0 && (follow.State[0x1057] & 0x40) != 0, "The original come-with-me action lets Chani rejoin Paul after the cure");
                var reports = await Scenario(95, 86, "reports", true);
                check(reports.Probe.SelectedDialogueRecords.Contains(NoChaniKidnapping.CuredReportRecord), "Cured Fremen can still thank Chani after the skipped chapter");
                check(reports.Probe.ConditionResults.All(p => p.Value == 0), "Kidnapping, disappearance and rescue dialogue conditions are false");
                Fixture(94, Treatment(0xF8, paulStays: true));
                var staying = await Scenario(94, 85, "tick", true);
                check(staying.State[0x2A] == 0x5D && staying.State[0xF9] == 0xF8 && staying.State[0xF8] == 1, "Paul still has to leave Chani to work on the cure");
                byte[] captive = cured.State.ToArray();
                captive[0x2A] = 0x64;
                captive[0x0A] |= FullVisionMapReveal.WaterOfLifePower;
                SetWord(captive, 0x1048, 0x3003); SetWord(captive, 0x104A, 0x0280);
                Fixture(93, captive);
                var restored = await Scenario(93, 84, "none", true, map: true);
                check(restored.State[0x2A] == 0x6C && Word(restored.State, 0x104A) == ((siteIndex + 1) << 8 | 0x80), "Existing captivity saves restore Chani and continue the story");
                check(Enumerable.Range(0, 70).All(i => (restored.State[0x100 + i * 28 + 10] & 0x80) == 0), "No kidnapping and full-map reveal work together");
                var vanillaReload = await Scenario(84, 83, "none", false);
                check(vanillaReload.State[0x2A] == 0x6C && Word(vanillaReload.State, 0x104A) == Word(restored.State, 0x104A), "No-kidnapping progress survives native reload with the option off");
            } finally {
                await controller.Stop();
            }
            check(!controller.Running, "Native story driver exits cleanly");
        }

        // Also load the same fixtures through the actual published executable.
        Environment.SetEnvironmentVariable("CRYOGENIC_STORY_CHECK", null);
        using PlayerController packaged = new(Path.GetFullPath(installedEngine));
        try {
            packaged.Settings.GameDataDirectory = Path.GetFullPath(originalData);
            packaged.Settings.NoChaniKidnapping = true;
            packaged.Settings.FullVisionMapReveal = true;
            packaged.SettingsStore.Save(packaged.Settings);
            async Task PackagedWait(Func<bool> condition, string message) {
                for (int n = 0; n < 600; n++) {
                    packaged.Poll();
                    if (condition()) { check(true, message); return; }
                    await Task.Delay(50);
                }
                throw new Exception("Timed out: " + message);
            }
            async Task PackagedLoadAndPause() {
                await packaged.Start("load", 93);
                await PackagedWait(() => packaged.Status?.Ready == true, "Published player loads the existing captivity fixture");
                packaged.Send("pause", name: "save");
                await PackagedWait(() => packaged.Paused, "Published player pauses safely");
            }
            await PackagedLoadAndPause();
            await packaged.Save(82, "Published player recovery");
            byte[] actual = State(82);
            check(actual[0x2A] == 0x6C && Word(actual, 0x104A) == ((siteIndex + 1) << 8 | 0x80), "Published player restores Chani without freezing progression");
            packaged.Settings.NoChaniKidnapping = false;
            packaged.SettingsStore.Save(packaged.Settings);
            await PackagedLoadAndPause();
            await packaged.Save(81, "Published player vanilla captivity");
            check(State(81)[0x2A] == 0x64 && Word(State(81), 0x104A) == 0x0280, "Published player preserves the original captivity when the option is off");
            packaged.Settings.NoChaniKidnapping = true;
            packaged.SettingsStore.Save(packaged.Settings);
            packaged.Send("resume");
            await PackagedWait(() => packaged.Status?.State == "running", "Published player applies the option during a live session");
            packaged.Send("pause", name: "save");
            await PackagedWait(() => packaged.Paused, "Live recovered session can save");
            await packaged.Save(80, "Published player live recovery");
            check(State(80)[0x2A] == 0x6C && Word(State(80), 0x104A) == ((siteIndex + 1) << 8 | 0x80), "Live enable restores Chani without reloading");
        } finally {
            await packaged.Stop();
        }
        check(!packaged.Running, "Published player exits cleanly");
    }
}

internal sealed class StoryObservation {
    public bool KidnappingExecuted { get; set; }
    public bool FortressAssignmentExecuted { get; set; }
    public bool HealingVisionQueued { get; set; }
    public List<int> SelectedDialogueRecords { get; set; } = [];
    public Dictionary<int, ushort> ConditionResults { get; set; } = [];
}

/// <summary>
/// Test-only override supplier. Calls the original story functions with native CALL
/// instructions after loading a fixture, then enters the unmodified game loop.
/// No extra test hooks or commands are compiled into the installed player.
/// </summary>
public sealed class ChaniStoryCheckSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILoggerService loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        var functions = new DuneCdOverrideSupplier().GenerateFunctionInformations(loggerService, configuration, programStartAddress, machine);
        // Leave PlayerRuntime's single machine-start callback in place. Add our
        // probes on the first CPU cycle, after its initialization has completed.
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(new UnconditionalBreakPoint(BreakPointType.CPU_CYCLES, _ => Initialize(machine), true), true);
        return functions;
    }

    private static void Initialize(Machine machine) {
        ushort cs = machine.CpuState.CS;
        uint code = (uint)cs << 4;
        uint data = (uint)(cs + 0xF4B) << 4;
        StoryObservation observation = new();
        void Hook(ushort offset, Action action, bool once = false) => machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, code + offset, _ => action(), once), true);
        void Write(ushort offset, byte[] bytes) { for (int i = 0; i < bytes.Length; i++) machine.Memory[code + offset + (uint)i] = bytes[i]; }
        Hook(0x11D0, () => observation.KidnappingExecuted = true);
        Hook(0x1F13, () => observation.FortressAssignmentExecuted = true);
        Hook(0x1EEC, () => observation.HealingVisionQueued = machine.CpuState.AX == 0x709);
        Hook(0x9FD8, () => observation.SelectedDialogueRecords.Add(machine.CpuState.SI));
        Hook(0x0037, () => {
            string script = Environment.GetEnvironmentVariable("CRYOGENIC_STORY_CHECK") ?? "none";
            ushort cursor = 0x60;
            void Emit(params byte[] bytes) { Write(cursor, bytes); cursor += (ushort)bytes.Length; }
            void Call(ushort destination) {
                ushort displacement = unchecked((ushort)(destination - cursor - 3));
                Emit(0xE8, (byte)displacement, (byte)(displacement >> 8));
            }
            if (script == "tick") { Emit(0xB9, 1, 0); Call(0x0FD9); }
            if (script is "follow" or "reports") {
                // Suppress presentation while exercising the original dialogue/action logic.
                machine.Memory[data + 0x46EB] = 1;
                machine.Memory[data + 0xC6] = 0x80;
                machine.Memory.UInt16[data + 0x47C4] = (ushort)(script == "follow" ? 7 : 15);
                machine.Memory.UInt16[data + 0x47A2] = NoChaniKidnapping.ChaniOffset;
                machine.Memory.UInt16[data + 0x47BE] = 7 * 8;
                Emit(0xBE, 0xAA, 0x08); Call(0x31F6);
            }
            if (script == "follow") Call(0x95E2);
            if (script == "reports") {
                // Native one-based condition IDs from the disappearance, kidnapping,
                // gratitude-after-rescue, and rescue lines, not a reimplemented evaluator.
                foreach (ushort condition in new ushort[] { 0x07E, 0x140, 0x141, 0x168, 0x1FB }) {
                    Emit(0xB8, (byte)condition, (byte)(condition >> 8)); Call(0xA396);
                    ushort key = condition;
                    Hook(cursor, () => observation.ConditionResults[key] = machine.CpuState.DX, once: true);
                }
                machine.Memory.UInt16[data + NoChaniKidnapping.CuredReportRecord + 4] = 0xFFFF;
                Emit(0xBE, 0x96, 0xB8); Call(0x9F9E);
            }
            Hook(cursor, () => {
                machine.Memory[data + 0x46EB] = 0;
                machine.Memory[data + 0xC6] = 0;
                AtomicJson.Write(Path.Combine(PlayerPaths.Home, "story-check.json"), observation);
            }, once: true);
            Call(0xD815);
            ushort displacement = unchecked((ushort)(0x003A - cursor - 3));
            Emit(0xE9, (byte)displacement, (byte)(displacement >> 8));
            // This startup error-text region is unused in a successful fixture load.
            // The reports script can extend past 0083; new-game setup has already run.
            Write(0x0037, [0xE8, 0x26, 0]);
        }, once: true);
    }
}
