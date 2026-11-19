using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// One cell of a generated site level (exploration-and-sites 3.2). Walls and rock block; a door blocks until opened; a container (chest, niche, bier, stash) blocks and is used
/// from beside it; water is floor with a depth; rubble blocks until dug; a hole is a collapsed floor (a fall to the level below). Append only (saved).
/// </summary>
public enum SiteTile : byte { Rock, Wall, Floor, Door, DoorOpen, StairsUp, StairsDown, ShaftDown, ShaftUp, Pillar, Container, Water, Rubble, Hole, Ore }

public enum ContainerKind : byte { Chest, Niche, Bier, Stash, Remnant, Scatter, Hoard }

public enum PortalKindSite : byte { Stairs, Ladder }

public enum InhabitantState : byte { Home, Asleep, Dormant, Alert, Hunt, Investigate, Return, Fighting, Fled, Dead, Away }

/// <summary>A thing found underground (4.2): an item of a loot table, its value and mass, its condition, a name when it is a named piece of history.</summary>
public sealed record LootItem(string Item, string Material, int ValueMites, int MassG, int ConditionPm, string Named = "", bool Dirty = false, bool Grave = false);

/// <summary>A chest, niche, bier, stash or scatter cell with what is in it. A niche holds a named dead person.</summary>
public sealed class SiteContainer
{
    public int Id;
    public int Cell;
    public ContainerKind Kind;
    public int Room = -1;
    public List<LootItem> Items = [];
    public string Dead = "";
    public bool Opened;
    public bool Grave;
}

public sealed class SitePortal
{
    public int Id;
    public PortalKindSite Kind;
    /// <summary>The upper end (level index L, the cell of the stairs down or the shaft head) and the lower end (level L + 1). Level -1 is the surface: the site's mouth.</summary>
    public int UpperCell, LowerCell;
    public bool Enabled = true;
}

public sealed class SiteTrap
{
    public int Id;
    public string Def = "";
    public int Cell;
    public bool Armed = true;
    public bool Detected;
    public int Triggered;
    /// <summary>The site's own people know where it is and step round it.</summary>
    public bool Owners = true;
}

/// <summary>A span of roof that needs its props (4.5.3): the cells, its instability (permille), the props in it.</summary>
public sealed class UnstableZone
{
    public int Id;
    public int Level;
    public int X0, Y0, X1, Y1;
    public int InstabilityPm;
    public bool Collapsed;
    public bool Contains(int level, int x, int y) => level == Level && x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
}

/// <summary>A person or animal living in the site (4.3), with an id (named people are persons; monsters have a legend).</summary>
public sealed class Inhabitant
{
    public ulong Id;
    public string Role = "";
    public string Kit = "";
    public string Monster = "";
    public string Name = "";
    public int Home;
    public int Bed = -1;
    /// <summary>Position in millimetres on the site grid and the level index.</summary>
    public int X, Y, Level;
    public InhabitantState State;
    public int Skill = 30;
    /// <summary>Sim-seconds counters: time the wake trigger has held, the lunge clock, time without a target, investigation left.</summary>
    public int TriggerS, LungeS, LostS, InvestigateS, StayS;
    /// <summary>The game day it fled (it returns after three).</summary>
    public int FledDay = -1;
    /// <summary>Blood lost (permille) carried between fights (a beast that fled recovers at its lair).</summary>
    public int LossPm;
    public bool Hostile = true;
    public int TargetX, TargetY, TargetLevel = -1;
    public List<Cell3>? Path;
    public int PathIdx;
    public bool Legend;
}

/// <summary>A pin on the auto-map (Q menu Mark) or a chalk mark.</summary>
public readonly record struct MapPin(int Level, int Cell, byte Kind);

public static class SiteTiles
{
    /// <summary>Walkable cells (a closed door opens as someone walks into it; the pathfinder and the reach check count it as open).</summary>
    public static bool Walkable(SiteTile t) => t is SiteTile.Floor or SiteTile.Door or SiteTile.DoorOpen or SiteTile.StairsUp or SiteTile.StairsDown or SiteTile.ShaftDown or SiteTile.ShaftUp or SiteTile.Water or SiteTile.Hole;
    public static bool Opaque(SiteTile t) => t is SiteTile.Rock or SiteTile.Wall or SiteTile.Door or SiteTile.Ore or SiteTile.Rubble;
    /// <summary>A support for the structure rule (plan 2.2a): a wall, pillar or prop within 3 tiles.</summary>
    public static bool Support(SiteTile t) => t is SiteTile.Rock or SiteTile.Wall or SiteTile.Pillar or SiteTile.Ore;
}
