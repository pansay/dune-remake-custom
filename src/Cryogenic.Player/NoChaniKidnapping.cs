namespace Cryogenic.Player;

using System.Buffers.Binary;

/// <summary>
/// Dune CD 3.7's cure completion calls CS:11CB, which starts the captivity chapter.
/// The player redirects that call through the native phase setter (CS:121F) to 6Ch.
/// Phases 60h..68h contain only the disappearance, fortress assignment and rescue;
/// 6Ch is beyond the rescue dialogue and has an empty native phase callback.
/// </summary>
public static class NoChaniKidnapping {
    public const int GamePhaseOffset = 0x002A;
    public const byte CaptivityPhase = 0x60;
    public const byte AfterRescuePhase = 0x6C;
    public const int ChaniOffset = 0x1048;
    public const int ChaniFlagsOffset = ChaniOffset + 15;
    public const int IllLocationCountOffset = 0x00F8;
    public const int CureProgressOffset = 0x00F9;
    public const int LatestIllLocationOffset = 0x11DB;
    public const int TroopsOffset = 0x08AA;
    public const int TroopSize = 0x1B;
    public const int TroopCount = 68;
    public const ushort Cured = 0x0800;
    public const int CuredReportRecord = 0xB896;
    public const byte Following = 0x40;

    public static byte RedirectPhase(byte requestedPhase, bool enabled) =>
        enabled && IsCaptivityPhase(requestedPhase) ? AfterRescuePhase : requestedPhase;

    public static bool IsCaptivityPhase(byte phase) => phase >= CaptivityPhase && phase < AfterRescuePhase;

    /// <summary>
    /// Support enabling the option in an existing captivity save. Keep a following
    /// or already safe Chani in place; otherwise return her to the cured sietch,
    /// falling back to the Atreides palace if that sietch has since been lost.
    /// This never cures troops, changes the army or advances the final assault.
    /// </summary>
    public static IReadOnlyList<DuneStateChange> PlanRecovery(ReadOnlySpan<byte> state, bool enabled) {
        if (!enabled || state.Length <= GamePhaseOffset || !IsCaptivityPhase(state[GamePhaseOffset])) return Array.Empty<DuneStateChange>();
        if (state.Length < DuneSaveCodec.GameStateLength || Word(state, FullVisionMapReveal.LocationsEnd) != 0xFFFF || state[ChaniOffset + 14] != 7) {
            throw new InvalidDataException("The saved characters do not match Dune CD 3.7.");
        }
        // A normal captivity save has finished the cure. Do not bypass unfinished treatment.
        if (state[IllLocationCountOffset] != 0 || Word(state, LatestIllLocationOffset) != 0) return Array.Empty<DuneStateChange>();
        List<DuneStateChange> changes = [new(GamePhaseOffset, AfterRescuePhase), new(0x00FF, 0)];
        if ((state[ChaniFlagsOffset] & Following) != 0 || (Word(state, 0x0010) & 0x80) != 0) return changes;
        ushort slot = Word(state, ChaniOffset + 2);
        int currentIndex = (slot >> 8) - 1;
        if ((slot & 0xFF) == 0x80 && currentIndex is >= 0 and < FullVisionMapReveal.LocationCount && IsSafe(state, currentIndex)) return changes;

        int destinationIndex = 0; // The original Atreides palace.
        for (int index = 0; index < TroopCount; index++) {
            int troop = TroopsOffset + index * TroopSize;
            if ((Word(state, troop + 0x12) & Cured) == 0) continue;
            int location = Word(state, troop + 4);
            int delta = location - FullVisionMapReveal.LocationsOffset;
            if (delta < 0 || delta % FullVisionMapReveal.LocationSize != 0) continue;
            int locationIndex = delta / FullVisionMapReveal.LocationSize;
            if (locationIndex >= FullVisionMapReveal.LocationCount || !IsSafe(state, locationIndex)) continue;
            destinationIndex = locationIndex;
            break;
        }
        int destination = FullVisionMapReveal.LocationsOffset + destinationIndex * FullVisionMapReveal.LocationSize;
        changes.Add(new(ChaniOffset, 2));
        changes.Add(new(ChaniOffset + 1, state[destination + 8]));
        changes.Add(new(ChaniOffset + 2, 0x80));
        changes.Add(new(ChaniOffset + 3, (byte)(destinationIndex + 1)));
        return changes;
    }

    private static bool IsSafe(ReadOnlySpan<byte> state, int index) {
        int location = FullVisionMapReveal.LocationsOffset + index * FullVisionMapReveal.LocationSize;
        // The same ownership test as the original CS:5D36 routine.
        return state[location + 8] < 0x28 || (state[location + 10] & 8) != 0;
    }

    private static ushort Word(ReadOnlySpan<byte> state, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(state[offset..]);
}
