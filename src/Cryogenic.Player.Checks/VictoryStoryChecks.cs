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

internal static class VictoryStoryChecks {
    private static ushort Word(byte[] state, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(offset));
    private static void SetWord(byte[] state, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(offset), value);
    private static void Apply(byte[] state, IReadOnlyList<DuneStateChange> changes) { foreach (var change in changes) state[change.Offset] = change.Value; }

    public static void CheckPolicy(Action<bool, string> check, Action<Action, string> invalid) {
        byte[] state = new byte[DuneSaveCodec.GameStateLength];
        state[FullVisionMapReveal.LocationsEnd] = state[FullVisionMapReveal.LocationsEnd + 1] = 0xFF;
        state[0x124] = 0x30;
        state[0x2A] = 0x6C; state[0xC2] = 7;
        check(HarkonnenVictoryContinuation.PlanVictory(state, true).Count == 0, "Continuation never grants victory before the original ending");
        state[0x2A] = 0xC8;
        check(HarkonnenVictoryContinuation.PlanVictory(state, false).Count == 0, "Disabled option preserves the original ending");
        state[0xC2] = 6;
        check(HarkonnenVictoryContinuation.PlanVictory(state, true).Count == 0, "The shield must have fallen before continuation is allowed");
        state[0xC2] = 7; state[0xBF] = 0xF7;
        SetWord(state, 0xA0, 12345); SetWord(state, 0xBC, 500); SetWord(state, 0xC0, 300);
        SetWord(state, 0x10, 0xA0);
        state[0x1057] = 0x40;
        byte[] original = state.ToArray();
        var plan = HarkonnenVictoryContinuation.PlanVictory(state, true);
        Apply(state, plan);
        check(HarkonnenVictoryContinuation.HasContinuedVictory(state) && state[0xC2] == 0, "A durable victory marker restores the simulation");
        check(state[0x124] == 0x30 && (state[0x126] & 0x18) == 0x18, "The conquered palace remains a visited palace under friendly control");
        check(Word(state, 4) == 0x3001 && Word(state, 6) == 0x0280 && Word(state, 0x10) == 0xA0, "Paul and his companions return to the palace entrance");
        check(Word(state, 0x1048) == 0x3001 && Word(state, 0x104A) == 0x0280, "Following Chani keeps her party status and joins the return to play");
        check(new[] { state[0x1152], state[0x1153] }.All(p => p < 9 && (Word(state, 0x10) & (1 << p)) != 0) && state[0x1152] != state[0x1153], "Companion portraits are restored for the continued game");
        byte[] selectedState = original.ToArray();
        Apply(selectedState, HarkonnenVictoryContinuation.PlanVictory(selectedState, true, preferredHudCompanions: 0x0705));
        check(Word(selectedState, 0x1152) == 0x0705, "Existing valid companion portrait selection is retained");
        check(Word(state, 0x106A) == 0xFF80 && Word(state, 0x107A) == 0xFF80 && Word(state, 0x108A) == 0xFF80, "The exiled final opponents leave the palace scene");
        check(Word(state, 0xA0) == 12345 && state[0xBF] == 0x47 && Word(state, 0xBC) == 0 && Word(state, 0xC0) == 0, "Imperial demands end without changing the spice stock or unrelated event flags");
        check(state.AsSpan(0x8AA, 68 * 27).SequenceEqual(original.AsSpan(0x8AA, 68 * 27)), "Victory preserves every troop record");
        check(Enumerable.Range(0, 70).Where(i => i != 1).All(i => state.AsSpan(0x100 + i * 28, 28).SequenceEqual(original.AsSpan(0x100 + i * 28, 28))), "Victory preserves every other location");
        check(HarkonnenVictoryContinuation.PlanVictory(state, true).Count == 0 && HarkonnenVictoryContinuation.PlanMaintenance(state).Count == 0, "Completed victory is idempotent");
        state[0xC2] = 1;
        Apply(state, HarkonnenVictoryContinuation.PlanMaintenance(state));
        check(state[0xC2] == 0 && Word(state, 4) == 0x3001, "Maintenance prevents a repeated final assault without moving Paul");
        byte[] unsupported = original.ToArray(); unsupported[0x124] = 0x28;
        invalid(() => HarkonnenVictoryContinuation.PlanVictory(unsupported, true), "Unsupported palace layouts are not modified");
    }

    public static async Task Run(string installedEngine, string originalData, string temporary, Action<bool, string> check) {
        Environment.SetEnvironmentVariable("CRYOGENIC_ENGINE_HEADLESS", "1");
        Environment.SetEnvironmentVariable("CRYOGENIC_CAPTURE_FRAMES", "1");
        int stateOffset = DuneSaveCodec.PayloadLength - DuneSaveCodec.GameStateLength;
        byte[] State(int slot) => DuneSaveCodec.Decode(File.ReadAllBytes(Path.Combine(PlayerPaths.Saves, $"slot-{slot:D3}.sav"))).Payload[stateOffset..];
        async Task Wait(PlayerController player, Func<bool> condition, string message) {
            for (int n = 0; n < 2400; n++) {
                player.Poll();
                if (condition()) { check(true, message); return; }
                if (!player.Running) throw new Exception(message + ": " + File.ReadAllText(Path.Combine(temporary, "last-session.log")));
                await Task.Delay(50);
            }
            throw new Exception("Timed out: " + message + ". " + File.ReadAllText(Path.Combine(temporary, "last-session.log")));
        }
        int growSite;
        using (PlayerController player = new()) {
            try {
                player.Settings.GameDataDirectory = Path.GetFullPath(originalData);
                player.ImportSaves();
                var source = DuneSaveCodec.Decode(File.ReadAllBytes(player.Saves.SavePath(2)));
                byte[] fixture = source.Payload[stateOffset..];
                fixture[0x2A] = 0x6C; fixture[0xC2] = 6; fixture[0x29] = 120; fixture[0xA] = 0x7B;
                fixture[0xBF] = 0xB7; fixture[0xCF] = 1;
                SetWord(fixture, 0xBC, 1500); SetWord(fixture, 0xC0, 500);
                SetWord(fixture, 4, 0x3001); SetWord(fixture, 6, 0x0280);
                fixture[8] = 0x30; fixture[0xB] = 1;
                SetWord(fixture, 0x114E, 0x11C); SetWord(fixture, 0x1150, 0x11C);
                SetWord(fixture, 0x10, 0xB6); SetWord(fixture, 0x0E, 0x1FF);
                SetWord(fixture, 0xFDA, 0xFF80); // Leto has already died in this late-game fixture.
                foreach (int person in new[] { 1, 2, 4, 5, 7 }) {
                    int entry = 0xFD8 + person * 16;
                    SetWord(fixture, entry, 0x3001); SetWord(fixture, entry + 2, 0x0280);
                    fixture[entry + 15] = (byte)((fixture[entry + 15] | 0x40) & ~2);
                }
                for (int index = 2; index < 70; index++) {
                    int site = 0x100 + index * 28;
                    if (fixture[site + 8] >= 0x28) fixture[site + 10] |= 8;
                }
                int grower = 0x8C5;
                growSite = Word(fixture, grower + 4);
                fixture[0x8AD] = 0;
                fixture[grower + 3] = 10;
                SetWord(fixture, grower + 18, (ushort)(Word(fixture, grower + 18) & ~0x430));
                fixture[growSite + 26] = 0; fixture[0xEC] = 0xFE;
                void Write(int slot, byte[] state) {
                    byte[] payload = source.Payload.ToArray(); state.CopyTo(payload, stateOffset);
                    player.Saves.Write(slot, "Isolated victory fixture", DuneSaveCodec.Encode(source.GameTime, payload));
                }
                Write(97, fixture);
                async Task<(byte[] State, VictoryObservation Observation)> Scenario(int slot, int output, string script, bool enabled, bool otherMods = false) {
                    Environment.SetEnvironmentVariable("CRYOGENIC_VICTORY_CHECK", script);
                    player.Settings.ContinueAfterHarkonnenDefeat = enabled;
                    player.Settings.NoChaniKidnapping = otherMods;
                    player.Settings.FullVisionMapReveal = otherMods;
                    player.SettingsStore.Save(player.Settings);
                    await player.Start("load", slot);
                    await Wait(player, () => player.Status?.Ready == true, "Victory scenario reached gameplay: " + script);
                    player.Send("pause", name: "save");
                    await Wait(player, () => player.Paused, "Victory scenario paused: " + script);
                    await player.Save(output, "Victory result: " + script);
                    var observation = AtomicJson.Read<VictoryObservation>(Path.Combine(temporary, "victory-check.json"))!;
                    Console.WriteLine("Completed native scenario: " + script + ", option " + (enabled ? "on" : "off"));
                    return (State(output), observation);
                }
                var vanilla = await Scenario(97, 99, "win", false);
                check(vanilla.Observation.VictoryConversationStarted && vanilla.Observation.VictoryArtPlayed && vanilla.Observation.CreditsStarted, "Original victory and credits run with the option off");
                check(vanilla.State[0x2A] == 0xC8 && vanilla.State[0xC2] >= 7, "The original completed campaign remains frozen in the control scenario");
                var continued = await Scenario(97, 90, "win", true, otherMods: true);
                check(continued.Observation.VictoryConversationStarted && continued.Observation.VictoryArtPlayed && continued.Observation.VictoryDialogueLines > 0, "The normal victory conversation and artwork are preserved");
                check(!continued.Observation.CreditsStarted && continued.Observation.WorldReturned, "Continuation returns through native scene cleanup before the credits");
                check(HarkonnenVictoryContinuation.HasContinuedVictory(continued.State) && continued.State[0xC2] == 0, "Victory leaves the world simulation active");
                check(Word(continued.State, 4) == 0x3001 && Word(continued.State, 6) == 0x0280 && (continued.State[0x126] & 8) != 0, "Paul returns to the conquered palace entrance");
                check(Word(continued.State, 0x10) == 0xB6 && (continued.State[0x1057] & 0x40) != 0, "All gathered companions, including Chani, remain with Paul");
                check(continued.State[0x1152] < 9 && continued.State[0x1153] < 9 && continued.State[0x1152] != continued.State[0x1153], "The victory scene restores the visible companion portraits");
                check((continued.State[0xBF] & 0xB0) == 0 && Word(continued.State, 0xC0) == 0, "The former Emperor's tribute obligation is retired");
                check(Enumerable.Range(0, 70).All(i => (continued.State[0x100 + i * 28 + 10] & 0x80) == 0), "All three mods work together after victory");
                var growth = await Scenario(90, 89, "tick", false);
                check(Word(growth.State, 2) == Word(continued.State, 2) + 1 && growth.State[0xEC] == 0xFF, "Native time and ecology work advance after victory with the setting off");
                var moreGrowth = await Scenario(89, 88, "tick", false);
                check(moreGrowth.State[growSite + 26] == 16, "The native ecology worker finishes growing bulbs after victory");
                var days = await Scenario(90, 87, "three-days", false);
                check(Word(days.State, 2) == Word(continued.State, 2) + 48 && days.State[0x124] == 0x30 && (days.State[0x126] & 8) != 0, "Days can pass without converting the conquered palace into a sietch");
                check(Word(days.State, 0xA0) > Word(continued.State, 0xA0), "Native spice production continues after victory");
                check(days.State[0xC2] == 0 && (days.State[0xBF] & 0xB0) == 0, "The final assault and imperial tax do not restart over time");
                var revisit = await Scenario(90, 86, "throne", false);
                check(HarkonnenVictoryContinuation.HasContinuedVictory(revisit.State) && Word(revisit.State, 4) == 0x3002 && !revisit.Observation.VictoryConversationStarted, "Returning to the throne room does not replay the ending");
                var travel = await Scenario(90, 85, "travel", false);
                check(HarkonnenVictoryContinuation.HasContinuedVictory(travel.State) && Word(travel.State, 6) == 0x0180, "Native travel can leave the conquered palace");
                var orders = await Scenario(90, 84, "orders", false);
                check((orders.State[0x8AD] & 15) == 4, "A native troop order still works after victory");
                var palace = await Scenario(90, 83, "palace-work", false);
                check(palace.State[0x124] == 0x30 && (palace.State[0x126] & 8) != 0, "The original fort conversion code preserves the conquered palace");
            } finally {
                await player.Stop();
                Environment.SetEnvironmentVariable("CRYOGENIC_VICTORY_CHECK", null);
            }
            check(!player.Running, "Victory story driver exits cleanly");
        }
        using PlayerController packaged = new(Path.GetFullPath(installedEngine));
        try {
            packaged.Settings.GameDataDirectory = Path.GetFullPath(originalData);
            packaged.Settings.ContinueAfterHarkonnenDefeat = false;
            packaged.SettingsStore.Save(packaged.Settings);
            async Task LoadAndSave(int input, int output) {
                await packaged.Start("load", input);
                await Wait(packaged, () => packaged.Status?.Ready == true, "Packaged player loads a continued campaign");
                packaged.Send("pause", name: "save");
                await Wait(packaged, () => packaged.Paused, "Packaged continued campaign can pause");
                await packaged.Save(output, "Packaged continued campaign");
            }
            await LoadAndSave(90, 82);
            check(HarkonnenVictoryContinuation.HasContinuedVictory(State(82)) && Word(State(82), 6) == 0x0280, "The published player loads and saves continuation with the option off");
            check(Word(State(82), 0x10) == Word(State(90), 0x10) && Word(State(82), 0xA0) == Word(State(90), 0xA0), "Native reload preserves companions and spice stock");
            check(Word(State(82), 0x1152) == Word(State(90), 0x1152), "Native reload preserves the visible companion portrait selection");
            byte[] Palette(int slot) {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary, "diagnostics", $"slot-{slot:D3}.json")));
                return json.RootElement.GetProperty("palette").GetBytesFromBase64();
            }
            check(Palette(90).AsSpan(224 * 3, 48).SequenceEqual(Palette(82).AsSpan(224 * 3, 48)), "Companion portraits use the normal interface colours immediately after victory and on reload");
            using (var diagnostic = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary, "diagnostics", "slot-082.json")))) {
                check(diagnostic.RootElement.GetProperty("gameSuspensionCount").GetInt32() == 0, "The published campaign resumes with the normal game clock unsuspended");
            }
            packaged.Settings.ContinueAfterHarkonnenDefeat = true;
            packaged.SettingsStore.Save(packaged.Settings);
            await LoadAndSave(99, 81);
            check(HarkonnenVictoryContinuation.HasContinuedVictory(State(81)) && State(81)[0xC2] == 0, "A victory-state save can also be resumed through the published player");
        } finally {
            await packaged.Stop();
        }
        check(!packaged.Running, "Packaged victory session exits cleanly");
    }
}

internal sealed class VictoryObservation {
    public bool VictoryConversationStarted { get; set; }
    public bool VictoryArtPlayed { get; set; }
    public bool CreditsStarted { get; set; }
    public bool WorldReturned { get; set; }
    public int VictoryDialogueLines { get; set; }
}

/// <summary>Test-only original victory runner. Never included in the installed player.</summary>
public sealed class VictoryStoryCheckSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILoggerService loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        var functions = new DuneCdOverrideSupplier().GenerateFunctionInformations(loggerService, configuration, programStartAddress, machine);
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(new UnconditionalBreakPoint(BreakPointType.CPU_CYCLES, _ => Initialize(machine), true), true);
        return functions;
    }
    private static void Initialize(Machine machine) {
        ushort cs = machine.CpuState.CS;
        uint code = (uint)cs << 4, data = (uint)(cs + 0xF4B) << 4;
        VictoryObservation observation = new();
        void Hook(ushort offset, Action action, bool once = false) => machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, code + offset, _ => action(), once), true);
        void Write(ushort offset, byte[] bytes) { for (int i = 0; i < bytes.Length; i++) machine.Memory[code + offset + (uint)i] = bytes[i]; }
        Hook(0x1701, () => observation.VictoryConversationStarted = true);
        Hook(0x14C9, () => observation.VictoryArtPlayed = true);
        Hook(0x152D, () => machine.CpuState.AX = 1);
        Hook(0x9FD8, () => { if (machine.Memory[data + 0x2A] == 0xC8) observation.VictoryDialogueLines++; });
        Hook(0x1736, () => { if (machine.Memory[data + 0x2A] == 0x70) observation.WorldReturned = true; });
        Hook(0x168B, () => {
            // The control really reached the original destructive credits setup.
            // Return only this test driver to its own checkpoint instead of waiting
            // through the credits. The normal player's ending has no such hook.
            observation.CreditsStarted = true;
            machine.CpuState.IP = 0x1474;
        });
        Hook(0x0037, () => {
            string script = Environment.GetEnvironmentVariable("CRYOGENIC_VICTORY_CHECK") ?? "none";
            ushort cursor = 0x60;
            void Emit(params byte[] bytes) { Write(cursor, bytes); cursor += (ushort)bytes.Length; }
            void Call(ushort address) {
                ushort displacement = unchecked((ushort)(address - cursor - 3));
                Emit(0xE8, (byte)displacement, (byte)(displacement >> 8));
            }
            if (script == "win") {
                machine.Memory[data + 0x46EB] = 1;
                machine.Memory[data + 0xC6] = 0x80;
                Emit(0xBF, 0x1C, 0x01); Emit(0xBE, 0xAA, 0x08); Call(0x739E);
                Emit(0xBA, 0x02, 0x30); Emit(0xBB, 0x80, 0x02); Call(0x4057);
                ushort loop = cursor;
                Call(0x1707);
                ushort checkpoint = cursor;
                ushort finished = (ushort)(cursor + 3);
                Hook(checkpoint, () => {
                    if (observation.WorldReturned || observation.CreditsStarted) machine.CpuState.IP = finished;
                });
                ushort relative = unchecked((ushort)(loop - cursor - 3));
                Emit(0xE9, (byte)relative, (byte)(relative >> 8));
            }
            if (script is "tick" or "three-days") { Emit(0xB9, (byte)(script == "tick" ? 1 : 48), 0); Call(0x0FD9); }
            if (script == "throne") { Emit(0xBA, 2, 0x30); Emit(0xBB, 0x80, 2); Call(0x4057); }
            if (script == "travel") { Emit(0xBE, 0, 1); Call(0x401F); }
            if (script == "orders") { Emit(0xBE, 0xAA, 8); Emit(0xB1, 4); Call(0x6ACB); }
            if (script == "palace-work") {
                machine.Memory[data + 0x46DE] = 1;
                machine.Memory[data + 0x127] = (byte)((machine.Memory.UInt16[data + 2] >> 4) - 3);
                Emit(0xBE, 0xAA, 8); Emit(0xBF, 0x1C, 1); Call(0x6E20);
            }
            Hook(cursor, () => {
                machine.Memory[data + 0x46EB] = 0;
                machine.Memory[data + 0xC6] = 0;
                AtomicJson.Write(Path.Combine(PlayerPaths.Home, "victory-check.json"), observation);
            }, once: true);
            Call(0xD815);
            ushort exitRelative = unchecked((ushort)(0x003A - cursor - 3));
            Emit(0xE9, (byte)exitRelative, (byte)(exitRelative >> 8));
            Write(0x0037, [0xE8, 0x26, 0]);
        }, once: true);
    }
}
