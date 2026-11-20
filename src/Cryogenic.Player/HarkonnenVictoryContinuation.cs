namespace Cryogenic.Player;

using System.Buffers.Binary;

/// <summary>
/// Resume Dune CD 3.7 after its victory conversation and final artwork, before
/// CS:167C starts the closing credits and destroys the normal scene state.
/// Phase 70h is unused by the original story and is below its C8h simulation stop.
/// </summary>
public static class HarkonnenVictoryContinuation {
    public const int StoryPhaseOffset = 0x002A;
    public const byte OriginalVictoryPhase = 0xC8;
    public const byte ContinuedCampaignPhase = 0x70;
    public const int FinalAssaultOffset = 0x00C2;
    public const int PalaceOffset = 0x011C;
    public const int PalaceAppearanceOffset = PalaceOffset + 8;
    public const int PalaceFlagsOffset = PalaceOffset + 10;
    public const byte PalaceAppearance = 0x30;
    public const ushort PalaceSlot = 0x0280;
    public const ushort PalaceEntrance = 0x3001;
    public const int PeopleOffset = 0x0FD8;
    public const int PersonSize = 16;

    public static bool HasContinuedVictory(ReadOnlySpan<byte> state) =>
        state.Length > PalaceFlagsOffset && state[StoryPhaseOffset] == ContinuedCampaignPhase &&
        state[PalaceAppearanceOffset] == PalaceAppearance;

    public static IReadOnlyList<DuneStateChange> PlanVictory(ReadOnlySpan<byte> state, bool enabled, ushort? preferredHudCompanions = null) {
        if (!enabled || state.Length <= FinalAssaultOffset || state[StoryPhaseOffset] != OriginalVictoryPhase || state[FinalAssaultOffset] < 7) return Array.Empty<DuneStateChange>();
        Validate(state);
        List<DuneStateChange> changes = [];
        Set(changes, state, StoryPhaseOffset, ContinuedCampaignPhase);
        Set(changes, state, PalaceFlagsOffset, (byte)((state[PalaceFlagsOffset] & ~0x82) | 0x18));
        Set(changes, state, 0xFF, 0);
        SetWord(changes, state, 4, PalaceEntrance);
        SetWord(changes, state, 6, PalaceSlot);
        Set(changes, state, 8, PalaceAppearance);
        Set(changes, state, 0xB, 1);
        Set(changes, state, 0xC, 0);
        Set(changes, state, 0x23, 0);
        Set(changes, state, 0x2B, 0);
        Set(changes, state, 0xFB, 1);
        SetWord(changes, state, 0x114E, PalaceOffset);
        SetWord(changes, state, 0x1150, PalaceOffset);

        ushort party = Word(state, 0x10);
        for (int person = 0; person < 9; person++) {
            int entry = PeopleOffset + person * PersonSize;
            if ((party & (1 << person)) == 0 && (state[entry + 15] & 0x40) == 0) continue;
            party |= (ushort)(1 << person);
            Set(changes, state, entry + 15, (byte)(state[entry + 15] | 0x40));
            SetWord(changes, state, entry, PalaceEntrance);
            SetWord(changes, state, entry + 2, PalaceSlot);
        }
        // The original victory dialogue exiles the Baron, Feyd and Shaddam.
        // FF80h is the game's existing off-map NPC slot, not a new location.
        for (int person = 9; person <= 11; person++) SetWord(changes, state, PeopleOffset + person * PersonSize + 2, 0xFF80);
        party = (ushort)(party & ~0x0E00);
        SetWord(changes, state, 0x10, party);
        // The victory artwork clears these portrait slots. Retain the selection
        // from entry when available, otherwise choose companions still in the party.
        ushort selected = preferredHudCompanions ?? Word(state, 0x1152);
        List<byte> portraits = [];
        foreach (byte person in new byte[] { (byte)selected, (byte)(selected >> 8), 7, 5, 4, 1, 2, 8, 3, 6, 0 }) {
            if (person < 9 && (party & (1 << person)) != 0 && !portraits.Contains(person)) portraits.Add(person);
            if (portraits.Count == 2) break;
        }
        Set(changes, state, 0x1152, portraits.Count > 0 ? portraits[0] : (byte)0xFF);
        Set(changes, state, 0x1153, portraits.Count > 1 ? portraits[1] : (byte)0xFF);
        ClearFinalAssaultAndTribute(changes, state);
        return changes;
    }

    /// <summary>Keep an already won campaign playable, including when the setting is later disabled.</summary>
    public static IReadOnlyList<DuneStateChange> PlanMaintenance(ReadOnlySpan<byte> state) {
        if (!HasContinuedVictory(state)) return Array.Empty<DuneStateChange>();
        Validate(state);
        List<DuneStateChange> changes = [];
        ClearFinalAssaultAndTribute(changes, state);
        return changes;
    }

    private static void ClearFinalAssaultAndTribute(List<DuneStateChange> changes, ReadOnlySpan<byte> state) {
        // Stage >= 7 skips troop updates at CS:1B5E. Stage 0 restores simulation;
        // clearing only imperial-demand flags prevents restarting the old tax timer.
        Set(changes, state, FinalAssaultOffset, 0);
        Set(changes, state, 0xBF, (byte)(state[0xBF] & ~0xB0));
        SetWord(changes, state, 0xBC, 0);
        SetWord(changes, state, 0xC0, 0);
        Set(changes, state, 0xCF, 0);
    }

    private static void Validate(ReadOnlySpan<byte> state) {
        if (state.Length < DuneSaveCodec.GameStateLength || Word(state, FullVisionMapReveal.LocationsEnd) != 0xFFFF || state[PalaceAppearanceOffset] != PalaceAppearance) {
            throw new InvalidDataException("The saved palace does not match Dune CD 3.7.");
        }
    }

    private static ushort Word(ReadOnlySpan<byte> state, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(state[offset..]);
    private static void Set(List<DuneStateChange> changes, ReadOnlySpan<byte> state, int offset, byte value) {
        if (state[offset] != value) changes.Add(new(offset, value));
    }
    private static void SetWord(List<DuneStateChange> changes, ReadOnlySpan<byte> state, int offset, ushort value) {
        Set(changes, state, offset, (byte)value);
        Set(changes, state, offset + 1, (byte)(value >> 8));
    }
}
