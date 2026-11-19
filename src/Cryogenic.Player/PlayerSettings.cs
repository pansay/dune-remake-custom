namespace Cryogenic.Player;

using System.Text.Json;

public sealed class PlayerSettings {
    public int Version { get; set; } = 1;
    public bool SubtitleBalloons { get => DialogueTextMode == 1; set => DialogueTextMode = value ? 1 : 0; }
    [System.Text.Json.Serialization.JsonPropertyOrder(10)]
    public int DialogueTextMode { get; set; } = 1;
    public int Cycles { get; set; } = 5500;
    public string? GameDataDirectory { get; set; }
}

public static class PlayerPaths {
    public static string Home { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("CRYOGENIC_PLAYER_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryogenicPlayer"));

    public static string SettingsFile => Path.Combine(Home, "settings.json");
    public static string Saves => Path.Combine(Home, "saves");
    public static string Session => Path.Combine(Home, "session");
    public static string WorkingGame => Path.Combine(Home, "game");
    public static string StatusFile => Path.Combine(Session, "status.json");

    public static void Ensure() {
        foreach (string path in new[] { Home, Saves, Session, WorkingGame }) {
            Directory.CreateDirectory(path);
        }
    }
}

public static class AtomicJson {
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static T? Read<T>(string path) {
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
    }

    public static void Write<T>(string path, T value) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, overwrite: true);
        } finally {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class SettingsStore(string path) {
    public PlayerSettings Load() {
        try {
            return AtomicJson.Read<PlayerSettings>(path) ?? new PlayerSettings();
        } catch (JsonException) {
            // Preserve an unreadable file so preferences can be recovered.
            if (File.Exists(path)) File.Copy(path, path + ".unreadable", overwrite: true);
            return new PlayerSettings();
        }
    }

    public void Save(PlayerSettings settings) => AtomicJson.Write(path, settings);
}
