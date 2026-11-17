namespace Ravensguard.Content;

/// <summary>One prop of a landmark footprint: a prop kind name (the sim's PropKind, case-insensitive), a cell offset from the landmark centre in metres, and a variant.</summary>
public sealed record LandmarkPropDef
{
    public required string Kind { get; init; }
    public int Dx { get; init; }
    public int Dy { get; init; }
    public int Variant { get; init; }
}

/// <summary>
/// An authored landmark template (plan Direction, 1A, section 3 item 14): a hand-made place that is only ever placed into a generated world, because
/// something happened there. Placement rules say where it may stand (biome, near a road, river or the coast, how far from settlements) and which history
/// event kinds it can attach to; the lore text names the attached event, person and place. The footprint is a small list of props.
/// <para>Lore tokens: {place} (nearest settlement), {event} (event title), {year}, {text} (event text), {person} (first person the event names), {realm}.</para>
/// </summary>
[ContentType("landmark")]
public sealed record LandmarkDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Lore text template when attached to a history event (see the token list on the type).</summary>
    public required string Lore { get; init; }
    /// <summary>Lore text template when it stands on its own (no event to name); must not contain event tokens.</summary>
    public string LoreUnattached { get; init; } = "";
    /// <summary>History event type ids this landmark can attach to (battle, old_battle, plague_begins, saint_rises ...). Empty means it needs no event.</summary>
    public List<string> AttachEvents { get; init; } = [];
    /// <summary>True when the landmark is only placed if a matching event exists in the region's history.</summary>
    public bool RequireAttach { get; init; }
    /// <summary>Biome names (sim Biome enum, snake_case) the centre may stand in; empty means any land.</summary>
    public List<string> Biomes { get; init; } = [];
    public bool NearRoad { get; init; }
    public bool NearRiver { get; init; }
    public bool Coastal { get; init; }
    public bool OnHill { get; init; }
    /// <summary>Stands within sight of a bridge or ford where a road crosses a river.</summary>
    public bool NearCrossing { get; init; }
    /// <summary>Road clearing: the landmark keeps this far (metres) from every road when set; 0 means no limit.</summary>
    public int MinRoadM { get; init; }
    public int MinSettlementM { get; init; } = 600;
    /// <summary>0 means no upper bound.</summary>
    public int MaxSettlementM { get; init; }
    /// <summary>Relative chance this template is picked among the placeable ones.</summary>
    public int Weight { get; init; } = 10;
    /// <summary>At most this many of the template per region.</summary>
    public int MaxPerRegion { get; init; } = 1;
    public required List<LandmarkPropDef> Props { get; init; }
}
