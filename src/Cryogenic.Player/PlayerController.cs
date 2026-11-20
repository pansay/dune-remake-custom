namespace Cryogenic.Player;

using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

public sealed class PlayerController : IDisposable {
    private Process? game;
    private StreamWriter? gameLog;
    private readonly object logGate = new();
    private readonly FileStream instanceLock;
    private readonly string? engineExecutable;
    private string? sessionId;
    public SettingsStore SettingsStore { get; } = new(PlayerPaths.SettingsFile);
    public SaveStore Saves { get; } = new(PlayerPaths.Saves);
    public PlayerSettings Settings { get; }
    public SessionStatus? Status { get; private set; }
    public bool Running => game is { HasExited: false };
    public bool Paused => Running && Status?.State == "paused";

    public PlayerController(string? engineExecutable = null) {
        this.engineExecutable = engineExecutable;
        PlayerPaths.Ensure();
        instanceLock = new FileStream(Path.Combine(PlayerPaths.Home, "player.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Settings = SettingsStore.Load();
        Settings.Cycles = Math.Clamp(Settings.Cycles, 500, 30000);
        SettingsStore.Save(Settings);
    }

    public string FindGameDirectory() {
        List<string?> candidates = [Environment.GetEnvironmentVariable("CRYOGENIC_GAME_DATA"), Settings.GameDataDirectory];
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder != null; folder = folder.Parent) {
            candidates.Add(Path.Combine(folder.FullName, "dune"));
        }
        return candidates.FirstOrDefault(p => p != null && File.Exists(Path.Combine(p, "DNCDPRG.EXE")) && File.Exists(Path.Combine(p, "DUNE.DAT")))
            ?? throw new FileNotFoundException("Choose the folder containing DNCDPRG.EXE and DUNE.DAT from your Dune CD edition in Settings.");
    }

    public void ImportSaves() {
        string directory = Environment.GetEnvironmentVariable("CRYOGENIC_IMPORT_SAVES") ?? FindGameDirectory();
        Saves.ImportExisting(directory);
    }

    public async Task Start(string mode, int? slot = null) {
        if (mode is not ("new" or "intro" or "load")) throw new ArgumentException("Unknown game mode.", nameof(mode));
        string source = FindGameDirectory();
        byte[] checksum = SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "DNCDPRG.EXE")));
        if (Convert.ToHexString(checksum) != "5F30AEB84D67CF2E053A83C09C2890F010F2E25EE877EBEC58EA15C5B30CFFF9") {
            throw new InvalidDataException("This player supports the Dune CD 3.7 executable. The chosen DNCDPRG.EXE is a different edition.");
        }
        if (mode == "load") _ = DuneSaveCodec.Decode(File.ReadAllBytes(Saves.SavePath(slot ?? throw new ArgumentException("Choose a save slot."))));
        await Stop();
        await Task.Run(() => {
            foreach (string name in new[] { "DNCDPRG.EXE", "DUNE.DAT" }) {
                string destination = Path.Combine(PlayerPaths.WorkingGame, name);
                bool replace = !File.Exists(destination) || new FileInfo(destination).Length != new FileInfo(Path.Combine(source, name)).Length;
                if (!replace && name == "DNCDPRG.EXE") replace = !SHA256.HashData(File.ReadAllBytes(destination)).SequenceEqual(checksum);
                if (replace) {
                    // CD files may be read-only. Only the isolated working copy can be replaced.
                    if (File.Exists(destination)) File.SetAttributes(destination, FileAttributes.Normal);
                    File.Copy(Path.Combine(source, name), destination, overwrite: true);
                }
            }
        });
        if (mode == "load") File.Copy(Saves.SavePath(slot!.Value), Path.Combine(PlayerPaths.WorkingGame, "DUNE37S1.SAV"), overwrite: true);
        sessionId = Guid.NewGuid().ToString("N");
        Status = null;
        if (File.Exists(SessionFiles.Command)) File.Delete(SessionFiles.Command);
        AtomicJson.Write(SessionFiles.Options, new SessionOptions(sessionId, mode, Settings.SubtitleBalloons, Settings.DialogueTextMode, Settings.FullVisionMapReveal));
        ProcessStartInfo start = new(engineExecutable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the game launcher.")) {
            WorkingDirectory = PlayerPaths.WorkingGame,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) {
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        }
        string[] arguments = ["--engine", "--Exe", Path.Combine(PlayerPaths.WorkingGame, "DNCDPRG.EXE"),
            "--Cycles", Settings.Cycles.ToString(System.Globalization.CultureInfo.InvariantCulture), "--UseCodeOverride", "true",
            "-p", "4096", "-a", "ADP330 SBP2227", "--HttpApiPort", "0", "--GdbPort", "0", "--McpHttpPort", "0"];
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (Environment.GetEnvironmentVariable("CRYOGENIC_ENGINE_HEADLESS") == "1") {
            start.ArgumentList.Add("--HeadlessMode"); start.ArgumentList.Add("Avalonia");
        }
        start.Environment["CRYOGENIC_PLAYER_SESSION"] = "1";
        start.Environment["CRYOGENIC_PLAYER_HOME"] = PlayerPaths.Home;
        start.Environment["DUNE_AUDIO_PREBUFFER_MS"] = "120";
        gameLog?.Dispose();
        gameLog = new StreamWriter(Path.Combine(PlayerPaths.Home, "last-session.log"), append: false) { AutoFlush = true };
        game = new Process { StartInfo = start };
        game.OutputDataReceived += (_, e) => Log(e.Data);
        game.ErrorDataReceived += (_, e) => Log(e.Data);
        game.Start();
        game.BeginOutputReadLine();
        game.BeginErrorReadLine();
    }

    private void Log(string? line) {
        if (line == null) return;
        lock (logGate) gameLog?.WriteLine(line);
    }

    public SessionStatus? Poll() {
        if (!Running) return null;
        try {
            SessionStatus? status = AtomicJson.Read<SessionStatus>(PlayerPaths.StatusFile);
            if (status != null && status.SessionId == sessionId) {
                if (status.State == "paused" && Status?.State != "paused") Settings.DialogueTextMode = SettingsStore.Load().DialogueTextMode;
                Status = status;
            }
        } catch (IOException) { }
        return Status;
    }

    public string Send(string action, int slot = 0, string? name = null) {
        string id = Guid.NewGuid().ToString("N");
        AtomicJson.Write(SessionFiles.Command, new PlayerCommand(id, action, slot, name, Settings.SubtitleBalloons, Settings.DialogueTextMode, Settings.FullVisionMapReveal));
        return id;
    }

    public async Task Save(int slot, string name) {
        string id = Send("save", slot, name);
        for (int attempt = 0; attempt < 100; attempt++) {
            await Task.Delay(50);
            if (!Running) throw new InvalidOperationException("The game has closed. The slot was not changed.");
            if (Poll()?.CompletedCommand == id) {
                if (Status?.Message?.StartsWith("Saved in slot ", StringComparison.Ordinal) != true) {
                    throw new InvalidOperationException(Status?.Message ?? "The save could not be completed.");
                }
                return;
            }
        }
        throw new TimeoutException("The game did not finish saving. Your previous save is still available.");
    }

    public async Task Stop() {
        if (Running) {
            Send("exit");
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            try { await game!.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (Running) game!.Kill(entireProcessTree: true); }
        }
        game?.Dispose();
        game = null;
        Status = null;
    }

    public void Dispose() {
        game?.Dispose();
        lock (logGate) {
            gameLog?.Dispose();
            gameLog = null;
        }
        instanceLock.Dispose();
    }
}
