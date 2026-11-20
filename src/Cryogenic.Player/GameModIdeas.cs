namespace Cryogenic.Player;

public sealed record GameModIdea(string Id, string Name, string Description);

/// <summary>Planned, unavailable options. No game behaviour or saved preferences are attached yet.</summary>
public static class GameModIdeas {
    public static IReadOnlyList<GameModIdea> All { get; } = [
        new("no-chani-kidnapping", "No Chani kidnapping", "Prevent Chani's kidnapping during the story."),
        new("full-visions-reveal-map", "Full visions reveal the whole map", "Once Paul has full visions, reveal every sietch and fortress."),
        new("continue-after-harkonnen-defeat", "Continue after the Harkonnen defeat", "Keep playing after conquering the Harkonnen palace."),
        new("sardaukar-surprise-attacks", "Sardaukar surprise attacks", "Sardaukar arrive late in the game with surprise attacks."),
        new("emperor-new-territories", "Emperor's palace and new territories", "After the Harkonnen defeat, conquer new territories and a Sardaukar palace with the Emperor.")
    ];
}
