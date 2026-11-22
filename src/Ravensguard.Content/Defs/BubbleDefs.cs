namespace Ravensguard.Content;

/// <summary>
/// The battle bubble's constants (battle-bubble-and-formations 3.1, 4.1; P7-BB-C; <c>battle/bubble.jsonc</c>, one <c>bubble_rules</c> row). Distances in mm, ticks 20 Hz fight ticks, <c>_pm</c> permille.
/// </summary>
[ContentType("bubble_rules")]
public sealed record BubbleRulesDef : ContentDef
{
    /// <summary>The box is the engagement's extent plus this margin (150 m), at most <see cref="MaxAreaM2"/> and <see cref="MaxSideM"/> a side.</summary>
    public int BoxMarginMm { get; init; } = 150_000;
    public long MaxAreaM2 { get; init; } = 1_000_000;
    public int MaxSideM { get; init; } = 1_400;
    /// <summary>Neighbouring chunks within this distance of the box have their ambient simulation paused; a chunk is this many metres square.</summary>
    public int PausedMarginMm { get; init; } = 200_000;
    public int ChunkM { get; init; } = 32;
    /// <summary>The battle-size setting: default and bounds, and the share each side keeps live at least (one over <see cref="MinCapDiv"/> of the total) while it has the men.</summary>
    public int CapTotalDefault { get; init; } = 1_000;
    public int CapTotalMin { get; init; } = 200;
    public int CapTotalMax { get; init; } = 2_000;
    public int MinCapDiv { get; init; } = 4;
    /// <summary>People who enter the bubble at the edge in one tick (the LODBridge's 50 a frame).</summary>
    public int PromotePerTick { get; init; } = 50;
    /// <summary>A side's wave check runs every this many ticks (once a second); a wave is at most one in <see cref="WaveGapTicks"/> (5 s), sent when the side's live count or a formation's is under <see cref="WaveUnderPm"/> of its start, and at most one over <see cref="WaveDiv"/> of the battle size.</summary>
    public int WaveCheckTicks { get; init; } = 20;
    public int WaveGapTicks { get; init; } = 100;
    public int WaveUnderPm { get; init; } = 700;
    public int WaveDiv { get; init; } = 4;
    /// <summary>A wave's entry jitter along the entry road, and how far inside the box it forms.</summary>
    public int EntryJitterMm { get; init; } = 20_000;
    public int EntryInsetMm { get; init; } = 6_000;
    /// <summary>The battle closes when both sides have been out of contact this long (60 s for a battle), or if the sides never meet in this long.</summary>
    public int QuietCloseS { get; init; } = 60;
    public int NoContactCloseS { get; init; } = 1_200;
    /// <summary>How often the close conditions are looked at (ticks); the room between formations on a line, the longest line and the gap between a side's lines at the opening.</summary>
    public int CloseCheckTicks { get; init; } = 20;
    public int FormationGapMm { get; init; } = 4_000;
    public int LineWidthMaxMm { get; init; } = 700_000;
    public int LineGapMm { get; init; } = 14_000;
}
