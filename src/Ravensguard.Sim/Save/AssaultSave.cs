using MessagePack;

namespace Ravensguard.Sim.Save;

/// <summary>A ladder of an assault: its cells, stage and clocks, who carries, waits and climbs (progress in ticks), and the defender at its top with his tries.</summary>
[MessagePackObject]
public sealed class LadderRow
{
    [Key(0)] public int Id { get; set; } [Key(1)] public int Team { get; set; }
    [Key(2)] public int FootX { get; set; } [Key(3)] public int FootY { get; set; } [Key(4)] public int TopX { get; set; } [Key(5)] public int TopY { get; set; }
    [Key(6)] public int Stage { get; set; } [Key(7)] public long StageSince { get; set; }
    [Key(8)] public int[] Carriers { get; set; } = []; [Key(9)] public int[] Queue { get; set; } = [];
    [Key(10)] public int[] Climbers { get; set; } = []; [Key(11)] public int[] Progress { get; set; } = [];
    [Key(12)] public int Pusher { get; set; } = -1; [Key(13)] public int PushTries { get; set; } [Key(14)] public long PushSince { get; set; } [Key(15)] public int Pushed { get; set; }
}

[MessagePackObject]
public sealed class TowerRow
{
    [Key(0)] public ulong Vehicle { get; set; } [Key(1)] public long X { get; set; } [Key(2)] public long Y { get; set; } [Key(3)] public int Facing { get; set; }
    [Key(4)] public bool Down { get; set; } [Key(5)] public bool Fallen { get; set; } [Key(6)] public int Aboard { get; set; }
    [Key(7)] public long[] Deck { get; set; } = []; [Key(8)] public long[] Bridge { get; set; } = [];
}

[MessagePackObject]
public sealed class HoleRow
{
    [Key(0)] public int Team { get; set; } [Key(1)] public int X { get; set; } [Key(2)] public int Y { get; set; } [Key(3)] public int Level { get; set; }
    [Key(4)] public int Stones { get; set; } [Key(5)] public int Pots { get; set; } [Key(6)] public bool Cauldron { get; set; }
    [Key(7)] public long NextTick { get; set; } [Key(8)] public int Dropped { get; set; } [Key(9)] public int Poured { get; set; } [Key(10)] public int Fired { get; set; }
}

[MessagePackObject]
public sealed class SallyRow
{
    [Key(0)] public int Id { get; set; } [Key(1)] public int Team { get; set; } [Key(2)] public int[] Men { get; set; } = []; [Key(3)] public ulong Engine { get; set; }
    [Key(4)] public int[] GateX { get; set; } = []; [Key(5)] public int[] GateY { get; set; } = [];
    [Key(6)] public long HomeX { get; set; } [Key(7)] public long HomeY { get; set; } [Key(8)] public int Stage { get; set; } [Key(9)] public long Since { get; set; }
    [Key(10)] public int Burned { get; set; } [Key(11)] public int Torches { get; set; } [Key(12)] public bool Reported { get; set; }
}

/// <summary>A march along waypoints: the fighter, how far along he is, how near counts, and the points (x, y pairs in mm).</summary>
[MessagePackObject]
public sealed class OrderRow
{
    [Key(0)] public int Fighter { get; set; } [Key(1)] public int Index { get; set; } [Key(2)] public int Stop { get; set; }
    [Key(3)] public long[] Points { get; set; } = [];
}

/// <summary>
/// An assault laid over a fight (sieges-and-engines 7; part of the optional <c>sieges</c> section, no version bump): who stands on which level and rung, the people held by a ladder, a tower or a torch, the ladders, towers, murder holes and sallies,
/// the gates open and the beams set, and the two losses of the wall already announced. The fight's own record has the fighters; wall damage is in the chunks (SG-A); the engines are the engine book's. A save, a load and a continue equal the uninterrupted assault.
/// </summary>
[MessagePackObject]
public sealed class AssaultRecord
{
    [Key(0)] public long OriginX { get; set; } [Key(1)] public long OriginY { get; set; }
    [Key(2)] public int AttackTeam { get; set; } [Key(3)] public int DefendTeam { get; set; }
    /// <summary>Fighters with a level or a rise: id, level, rise, three to a row.</summary>
    [Key(4)] public int[] Levels { get; set; } = [];
    /// <summary>The holds: fighter, kind, reference, until (low, high words), four to a row.</summary>
    [Key(5)] public long[] Holds { get; set; } = [];
    [Key(6)] public LadderRow[] Ladders { get; set; } = [];
    [Key(7)] public TowerRow[] Towers { get; set; } = [];
    [Key(8)] public HoleRow[] Holes { get; set; } = [];
    [Key(9)] public SallyRow[] Sallies { get; set; } = [];
    [Key(10)] public long[] OpenGates { get; set; } = [];
    [Key(11)] public long[] Beams { get; set; } = [];
    [Key(12)] public bool WalkLost { get; set; } [Key(13)] public bool BreachLost { get; set; }
    [Key(14)] public int NextLadder { get; set; } [Key(15)] public int NextSally { get; set; }
    [Key(16)] public ulong Cause { get; set; } [Key(17)] public ulong Place { get; set; }
    /// <summary>The inside box (x0, y0, x1, y1) or empty.</summary>
    [Key(18)] public int[] Inside { get; set; } = [];
    [Key(19)] public OrderRow[] Orders { get; set; } = [];

    public byte[] ToBytes() => SaveCodec.SerializeCompressed(this);
    public static AssaultRecord FromBytes(byte[] b) => SaveCodec.DeserializeCompressed<AssaultRecord>(b);
}
