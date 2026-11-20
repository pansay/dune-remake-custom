namespace Cryogenic.Player;

public sealed record GameModIdea(string Id, string Name, string Description, bool Implemented = false);

/// <summary>Available and planned optional gameplay changes.</summary>
public static class GameModIdeas {
    public static IReadOnlyList<GameModIdea> All { get; } = [
        new("no-chani-kidnapping", "No Chani kidnapping", "Chani cures the Fremen and stays available. The story continues without a kidnapping or rescue.", Implemented: true),
        new("full-visions-reveal-map", "Full visions reveal the whole map", "After the Water of Life, reveal every sietch and fortress. Discovered locations stay known.", Implemented: true),
        new("continue-after-harkonnen-defeat", "Continue after the Harkonnen defeat", "After the victory scene, keep exploring, managing troops and restoring Arrakis.", Implemented: true),
        new("sardaukar-surprise-attacks", "Sardaukar surprise attacks", "Sardaukar arrive late in the game with surprise attacks."),
        new("emperor-new-territories", "Emperor's palace and new territories", "After the Harkonnen defeat, conquer new territories and a Sardaukar palace with the Emperor.")
    ];
}
