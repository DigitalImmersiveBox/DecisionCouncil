namespace DecisionCouncil.App.Controls;

/// <summary>
/// A council member's pixel body, in the style of Session Deck's mascots. B is the agent's colour, D a darker
/// shade of it; eyes are drawn on top at <see cref="Eyes"/> (1 cell wide, 2 tall) so they can blink and smile.
/// </summary>
public sealed record AvatarSprite(
    string Key,
    string[] Body,
    int[] Legs,
    (int Col, int Row)[] Eyes,
    bool LightEyes = false,
    bool Floats = false,
    int[]? JumpColumns = null)
{
    public int Columns => Body[0].Length;
    public int Rows => Body.Length + (Legs.Length > 0 ? 2 : 0);
}

public static class AvatarSprites
{
    public const int MaxColumns = 17;

    private static readonly AvatarSprite Crab = new("crab",
    [
        "..BBBBBBBBB..",
        "..BBBBBBBBB..",
        "BBBBBBBBBBBBB",
        "BBBBBBBBBBBBB",
        "..BBBBBBBBB..",
        "..BBBBBBBBB..",
    ], [3, 5, 7, 9], [(4, 1), (8, 1)]);

    private static readonly AvatarSprite Antenna = new("antenna",
    [
        "......D......",
        "......D......",
        "...BBBBBBB...",
        "..BBBBBBBBB..",
        ".BBBBBBBBBBB.",
        ".BBBBBBBBBBB.",
        "..BBBBBBBBB..",
    ], [4, 8], [(4, 3), (8, 3)]);

    private static readonly AvatarSprite Robot = new("robot",
    [
        "...D.....D...",
        "...B.....B...",
        "..BBBBBBBBB..",
        "..BDDDDDDDB..",
        "..BDDDDDDDB..",
        "..BBBBBBBBB..",
        "BBBBBBBBBBBBB",
        "..BBBBBBBBB..",
    ], [4, 8], [(4, 3), (8, 3)], LightEyes: true);

    private static readonly AvatarSprite Cat = new("cat",
    [
        "..B.......B..",
        "..BB.....BB..",
        "..BBBBBBBBB..",
        "..BBBBBBBBB..",
        "..BBBBBBBBB..",
        "..BBBBBBBBB.D",
        "..BBBBBBBBBD.",
    ], [3, 5, 7, 9], [(4, 2), (8, 2)]);

    private static readonly AvatarSprite Owl = new("owl",
    [
        "...DDDDDDD...",
        ".DDDDDDDDDDD.",
        "..BBBBBBBBB..",
        ".BBBBBBBBBBB.",
        ".BBBBBBBBBBB.",
        ".BBBBBBBBBBB.",
        "..BBBBBBBBB..",
    ], [4, 8], [(4, 3), (8, 3)]);

    private static readonly AvatarSprite Ghost = new("ghost",
    [
        "...BBBBBBB...",
        "..BBBBBBBBB..",
        ".BBBBBBBBBBB.",
        ".BBBBBBBBBBB.",
        ".BBBBBBBBBBB.",
        ".BBBBBBBBBBB.",
        ".BBBBBBBBBBB.",
        ".B.BB.B.BB.B.",
    ], [], [(4, 2), (8, 2)], Floats: true);

    // The dib logo with legs (Session Deck's dib mascot); the i in the middle jumps when it is happy
    private static readonly AvatarSprite Dib = new("dib",
    [
        "....BBB.B.BBB....",
        "....BBB.B.BBB....",
        "....BBB.B.BBB....",
        ".BBBBBB.B.BBBBBB.",
        "BBB.BBB.B.BBB.BBB",
        "BB..BBB.B.BBB..BB",
        "BBBBBBBBBBBBBBBBB",
    ], [4, 6, 10, 12], [(5, 1), (11, 1)], JumpColumns: [8]);

    private static readonly AvatarSprite[] All = [Crab, Antenna, Robot, Cat, Owl, Ghost, Dib];

    /// <summary>The named sprite, or one picked from the agent id so every agent keeps the same face.</summary>
    public static AvatarSprite For(string? key, string? agentId)
    {
        if (!string.IsNullOrWhiteSpace(key) && All.FirstOrDefault(s => s.Key == key) is { } named) return named;
        if (agentId == "synthesizer") return Dib;
        var hash = 0;
        foreach (var c in agentId ?? "") hash = hash * 31 + c;
        return All[Math.Abs(hash) % (All.Length - 1)]; // dib is kept for the synthesizer
    }
}
