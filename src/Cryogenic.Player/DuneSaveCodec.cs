namespace Cryogenic.Player;

using System.Buffers.Binary;

/// <summary>
/// Native Dune CD 3.7 save format. Recovered from CS:B389, B427, B473, B4BB and B4EA.
/// Saves remain compatible with the original game; they are not emulator snapshots.
/// </summary>
public static class DuneSaveCodec {
    public const int PayloadLength = 0x567A;
    public const int MapLength = 0xC5FC;
    public const int PackedMapLength = MapLength / 4;
    public const int CodeStateLength = 0xA2;
    public const int WorldStateLength = 0x11F8;
    public const int GameStateLength = 0x1261;
    private const byte Marker = 0xF7;

    public static byte[] Encode(ushort gameTime, ReadOnlySpan<byte> payload) {
        if (payload.Length != PayloadLength) throw new ArgumentException("Unexpected Dune save payload length.", nameof(payload));
        using MemoryStream output = new();
        output.Write(new byte[6]);
        int index = 0;
        while (index < payload.Length) {
            byte value = payload[index];
            int run = 1;
            while (index + run < payload.Length && payload[index + run] == value && run < 255) run++;
            if (value == Marker || run >= 3) {
                output.WriteByte(Marker);
                output.WriteByte((byte)run);
                output.WriteByte(value);
            } else {
                for (int n = 0; n < run; n++) output.WriteByte(value);
            }
            index += run;
        }
        byte[] result = output.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result, gameTime);
        result[2] = Marker;
        result[3] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)(result.Length - 2)));
        return result;
    }

    public static (ushort GameTime, byte[] Payload) Decode(ReadOnlySpan<byte> savedGame) {
        if (savedGame.Length < 6 || savedGame.Length > ushort.MaxValue) throw new InvalidDataException("This is not a Dune CD save game.");
        ushort gameTime = BinaryPrimitives.ReadUInt16LittleEndian(savedGame);
        int declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(savedGame[4..]);
        if (declaredLength + 2 != savedGame.Length) throw new InvalidDataException("The save game is incomplete.");
        byte marker = savedGame[2];
        byte[] payload = new byte[PayloadLength];
        int read = 6, write = 0;
        while (read < savedGame.Length) {
            byte value = savedGame[read++];
            int count = 1;
            if (value == marker) {
                if (read + 2 > savedGame.Length) throw new InvalidDataException("The save game contains a truncated run.");
                count = savedGame[read++];
                value = savedGame[read++];
                if (count == 0) throw new InvalidDataException("The save game contains an empty run.");
            }
            if (write + count > payload.Length) throw new InvalidDataException("The save game contains too much state.");
            payload.AsSpan(write, count).Fill(value);
            write += count;
        }
        if (write != PayloadLength) throw new InvalidDataException("The save game does not contain a complete game state.");
        return (gameTime, payload);
    }

    public static byte[] PackState(ReadOnlySpan<byte> map, ReadOnlySpan<byte> codeState, ReadOnlySpan<byte> worldState, ReadOnlySpan<byte> gameState) {
        if (map.Length != MapLength || codeState.Length != CodeStateLength || worldState.Length != WorldStateLength || gameState.Length != GameStateLength) {
            throw new ArgumentException("Unexpected Dune state block length.");
        }
        byte[] payload = new byte[PayloadLength];
        for (int group = 0; group < PackedMapLength; group++) {
            int packed = 0;
            for (int n = 0; n < 4; n++) packed = (packed << 2) | ((map[group * 4 + n] >> 4) & 3);
            payload[group] = (byte)packed;
        }
        codeState.CopyTo(payload.AsSpan(PackedMapLength));
        worldState.CopyTo(payload.AsSpan(PackedMapLength + CodeStateLength));
        gameState.CopyTo(payload.AsSpan(PackedMapLength + CodeStateLength + WorldStateLength));
        return payload;
    }
}
