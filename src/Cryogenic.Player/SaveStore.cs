namespace Cryogenic.Player;

public sealed record SaveEntry(int Slot, string Name, DateTimeOffset SavedAt, ushort GameTime) {
    public int Day => (GameTime >> 4) + 1;
}

public sealed class SaveStore(string directory) {
    public const int SlotCount = 100;

    public string SavePath(int slot) {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, SlotCount);
        return Path.Combine(directory, $"slot-{slot:D3}.sav");
    }

    public SaveEntry? Read(int slot) {
        string savedGame = SavePath(slot);
        if (!File.Exists(savedGame)) return null;
        try {
            return AtomicJson.Read<SaveEntry>(Path.ChangeExtension(savedGame, ".json"))
                ?? new SaveEntry(slot, $"Save {slot}", File.GetLastWriteTimeUtc(savedGame), DuneSaveCodec.Decode(File.ReadAllBytes(savedGame)).GameTime);
        } catch (System.Text.Json.JsonException) {
            return new SaveEntry(slot, $"Save {slot}", File.GetLastWriteTimeUtc(savedGame), DuneSaveCodec.Decode(File.ReadAllBytes(savedGame)).GameTime);
        }
    }

    public SaveEntry Write(int slot, string name, byte[] nativeSave) {
        var decoded = DuneSaveCodec.Decode(nativeSave);
        string path = SavePath(slot);
        Directory.CreateDirectory(directory);
        if (File.Exists(path)) File.Copy(path, path + ".previous", overwrite: true);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, nativeSave);
        File.Move(temporary, path, overwrite: true);
        SaveEntry entry = new(slot, string.IsNullOrWhiteSpace(name) ? $"Save {slot}" : name.Trim(), DateTimeOffset.Now, decoded.GameTime);
        AtomicJson.Write(Path.ChangeExtension(path, ".json"), entry);
        return entry;
    }

    public void ImportExisting(string gameDirectory) {
        if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.sav").Any()) return;
        Directory.CreateDirectory(directory);
        int next = 1;
        foreach (string path in Directory.EnumerateFiles(gameDirectory).Where(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "^DUNE37S[0-9]+\\.SAV$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).OrderBy(Path.GetFileName)) {
            try {
                Write(next, Path.GetFileNameWithoutExtension(path).EndsWith('0') ? "Original quick save" : $"Imported {Path.GetFileNameWithoutExtension(path)}", File.ReadAllBytes(path));
                next++;
                if (next > SlotCount) break;
            } catch (InvalidDataException) {
                // An incomplete original save must not block other valid imports.
            }
        }
    }
}
