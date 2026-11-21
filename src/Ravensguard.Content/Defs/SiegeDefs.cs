namespace Ravensguard.Content;

/// <summary>
/// P7-SG-A (sieges-and-engines 3.1; content <c>content/base/siege/walls.jsonc</c>): how one wall-like piece takes blows. The id is the build piece's id; its hit points are the piece's own (<c>build.hp</c>). A blow of momentum M
/// (kg m/s) does <c>max(0, M - threshold) / divisor</c> hit points: one rule for rams, stones and any other blow (there is no default damage). A piece at 0 hit points becomes <see cref="Rubble"/> (a ground piece) where it
/// stood on the ground (z = 0), and is simply gone higher up (<see cref="Rubble"/> empty).
/// </summary>
[ContentType("wall_stat")]
public sealed record WallStatDef : ContentDef
{
    /// <summary>The impact threshold in kg m/s: a blow at or under it does nothing.</summary>
    public int Threshold { get; init; }
    /// <summary>The build piece (kind ground) the cell becomes at z = 0 when the piece is destroyed; empty: the cell is left empty.</summary>
    public string Rubble { get; init; } = "";
}

/// <summary>P7-SG-A (sieges-and-engines 3.1, 4.1): the numbers of the wall rules, in content.</summary>
[ContentType("siege_rules")]
public sealed record SiegeRulesDef : ContentDef
{
    /// <summary>Momentum over the threshold per hit point (integer division): 60.</summary>
    public int DamageDivisor { get; init; } = 60;
    /// <summary>The narrowest run of wall cells torn through that counts as a breach, in metres: 2.</summary>
    public int BreachMinWidthM { get; init; } = 2;
    /// <summary>A wall run longer than this is not a wall's thickness (a field of rubble is not a breach): 8 cells.</summary>
    public int MaxThicknessM { get; init; } = 8;
}
