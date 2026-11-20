namespace Cryogenic.Player;

using System.Buffers.Binary;

public readonly record struct DuneStateChange(int Offset, byte Value);
public sealed record MapRevealResult(int RevealedLocations, int RevealedSietches, IReadOnlyList<DuneStateChange> Changes);

/// <summary>
/// Dune CD 3.7 location discovery. The native map builder at CS:5DCE skips flag 80h;
/// CS:425B clears that flag, clears its phase threshold, and counts known sietches.
/// Visiting a location uses the separate 10h flag, which this mod preserves.
/// Successful Water of Life consumption at CS:2CF5 sets DS:000A bit 02h.
/// </summary>
public static class FullVisionMapReveal {
    public const int PowersOffset = 0x000A;
    public const byte WaterOfLifePower = 0x02;
    public const int KnownSietchesOffset = 0x0027;
    public const int LocationsOffset = 0x0100;
    public const int LocationSize = 0x1C;
    public const int LocationCount = 70;
    public const int LocationFlagsOffset = 0x0A;
    public const byte Hidden = 0x80;
    public const int LocationsEnd = LocationsOffset + LocationCount * LocationSize;

    public static bool HasFullVisions(ReadOnlySpan<byte> gameState) =>
        gameState.Length > PowersOffset && (gameState[PowersOffset] & WaterOfLifePower) != 0;

    public static MapRevealResult Plan(ReadOnlySpan<byte> gameState, bool enabled) {
        if (!enabled || !HasFullVisions(gameState)) return new(0, 0, Array.Empty<DuneStateChange>());
        if (gameState.Length < DuneSaveCodec.GameStateLength || BinaryPrimitives.ReadUInt16LittleEndian(gameState[LocationsEnd..]) != 0xFFFF) {
            throw new InvalidDataException("The saved world's location table does not match Dune CD 3.7.");
        }
        List<DuneStateChange> changes = [];
        int revealed = 0, sietches = 0;
        for (int index = 0; index < LocationCount; index++) {
            int location = LocationsOffset + index * LocationSize;
            byte flags = gameState[location + LocationFlagsOffset];
            if ((flags & Hidden) == 0) continue;
            // Preserve every other flag: discovery does not visit, conquer, rally or prospect.
            changes.Add(new(location + LocationFlagsOffset, (byte)(flags & ~Hidden)));
            changes.Add(new(location + LocationFlagsOffset + 1, 0));
            revealed++;
            if (gameState[location + 8] < 0x20) sietches++;
        }
        if (sietches > 0) changes.Add(new(KnownSietchesOffset, (byte)Math.Min(byte.MaxValue, gameState[KnownSietchesOffset] + sietches)));
        return new(revealed, sietches, changes);
    }
}
