namespace Cryogenic.Player;

public sealed record SessionOptions(string Id, string Mode, bool SubtitleBalloons, int DialogueTextMode = 1, bool FullVisionMapReveal = false);
public sealed record PlayerCommand(string Id, string Action, int Slot = 0, string? Name = null, bool? SubtitleBalloons = null, int DialogueTextMode = 1, bool FullVisionMapReveal = false);
public sealed record SessionStatus(string SessionId, string State, bool Ready, string Page = "menu", string? CompletedCommand = null, string? Message = null);

public static class SessionFiles {
    public static string Options => Path.Combine(PlayerPaths.Session, "options.json");
    public static string Command => Path.Combine(PlayerPaths.Session, "command.json");
}
