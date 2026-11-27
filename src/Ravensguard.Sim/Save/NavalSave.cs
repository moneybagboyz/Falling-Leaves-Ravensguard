using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Save;

/// <summary>One sea raid of the book (ships-and-naval-war 3.4, 7): who, whom, where, when, what it holds and what it did. The ships are rows of the <c>mounts</c> section and the men rows of the people diff, so only their ids are here.</summary>
[MessagePackObject]
public sealed class SeaRaidSave
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public int Realm { get; set; }
    [Key(2)] public int Victim { get; set; }
    [Key(3)] public int Region { get; set; }
    [Key(4)] public int Settlement { get; set; }
    [Key(5)] public int Culture { get; set; }
    [Key(6)] public bool War { get; set; }
    [Key(7)] public ulong DeclEntry { get; set; }
    [Key(8)] public int PlanShips { get; set; }
    [Key(9)] public int PlanMen { get; set; }
    [Key(10)] public long BeachXmm { get; set; }
    [Key(11)] public long BeachYmm { get; set; }
    [Key(12)] public long PlannedHour { get; set; }
    [Key(13)] public long LandHour { get; set; }
    [Key(14)] public long LootHour { get; set; }
    [Key(15)] public long BellHour { get; set; }
    [Key(16)] public long WithdrawHour { get; set; }
    [Key(17)] public long EndHour { get; set; }
    [Key(18)] public byte State { get; set; }
    [Key(19)] public ulong[] Ships { get; set; } = [];
    [Key(20)] public ulong[] Raiders { get; set; } = [];
    [Key(21)] public ulong[] Guards { get; set; } = [];
    [Key(22)] public ulong[] Captives { get; set; } = [];
    [Key(23)] public long TakenG { get; set; }
    [Key(24)] public long TakenCoin { get; set; }
    [Key(25)] public int Burned { get; set; }
    [Key(26)] public int Dead { get; set; }
    [Key(27)] public int Households { get; set; }
    [Key(28)] public ulong Entry { get; set; }
    [Key(29)] public bool Fought { get; set; }
    [Key(30)] public int Defender { get; set; }
    [Key(31)] public long FightHour { get; set; }
    /// <summary>P8-LW-2 (optional key): the hour a live beach fight opened; null when none is open.</summary>
    [Key(32)] public long? LiveSince { get; set; }
}

/// <summary>A ship's fight row (P7-SH-D): its end, what burns, the fire's counters. Its hull's hit points and water are in the <c>mounts</c> section's part rows.</summary>
[MessagePackObject]
public sealed class ShipRowSave
{
    [Key(0)] public ulong Ship { get; set; }
    [Key(1)] public byte State { get; set; }
    [Key(2)] public ulong Captor { get; set; }
    [Key(3)] public ulong Entry { get; set; }
    [Key(4)] public uint Serial { get; set; }
    [Key(5)] public long BurnCarry { get; set; }
    [Key(6)] public bool SailDown { get; set; }
    [Key(7)] public int Bailers { get; set; }
    [Key(8)] public int[] Burning { get; set; } = [];
}

/// <summary>Two ships bound by grapples (P7-SH-D).</summary>
[MessagePackObject]
public sealed class BindSave
{
    [Key(0)] public ulong A { get; set; }
    [Key(1)] public ulong B { get; set; }
    [Key(2)] public int Lines { get; set; }
    [Key(3)] public int HaulMen { get; set; }
    [Key(4)] public int HaulS { get; set; }
    [Key(5)] public bool Joined { get; set; }
    [Key(6)] public int OffLx { get; set; }
    [Key(7)] public int OffLy { get; set; }
    [Key(8)] public int OffHeading { get; set; }
    [Key(9)] public int Throws { get; set; }
}

/// <summary>A ship at sea or in harbour as a frame (P7-SH-D): the <see cref="ShipFrameState"/> in saved form.</summary>
[MessagePackObject]
public sealed class FrameSave
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public int X { get; set; }
    [Key(2)] public int Y { get; set; }
    [Key(3)] public int Heading { get; set; }
    [Key(4)] public int Speed { get; set; }
    [Key(5)] public int TurnPerTick { get; set; }
    [Key(6)] public long AccX { get; set; }
    [Key(7)] public long AccY { get; set; }
    [Key(8)] public byte[] RiderKinds { get; set; } = [];
    [Key(9)] public ulong[] RiderIds { get; set; } = [];
    [Key(10)] public int[] RiderLx { get; set; } = [];
    [Key(11)] public int[] RiderLy { get; set; } = [];
    [Key(12)] public byte[] RiderDecks { get; set; } = [];
}

/// <summary>
/// The optional <c>naval</c> section (a name of <c>Phase7Seeding.SectionNames</c>; no version bump): P7-SH-C puts the sea raids here, written only once a raid was ever planned so a world that saw none keeps its bytes. SH-D adds its
/// ships at sea and in harbour under later keys.
/// </summary>
[MessagePackObject]
public sealed class NavalSave
{
    public const string SectionName = "naval";

    [Key(0)] public SeaRaidSave[] Raids { get; set; } = [];
    [Key(1)] public int NextRaid { get; set; } = 1;
    // P7-SH-D: ship rows, grapple binds and ships' frames. Optional keys: absent (an older save) reads as none, and a book without them writes the SH-C bytes' shape (empty arrays).
    [Key(2)] public ShipRowSave[] Ships { get; set; } = [];
    [Key(3)] public BindSave[] Binds { get; set; } = [];
    [Key(4)] public FrameSave[] Frames { get; set; } = [];

    public static NavalSave? CaptureOrNull(NavalBook? b) => b is { IsEmpty: false } ? Capture(b) : null;

    public static NavalSave Capture(NavalBook b) => new()
    {
        NextRaid = b.Next,
        Ships = [.. b.Ships.Values.Select(x => new ShipRowSave { Ship = x.Ship, State = (byte)x.State, Captor = x.Captor.Value, Entry = x.Entry, Serial = x.Serial, BurnCarry = x.BurnCarry, SailDown = x.SailDown, Bailers = x.Bailers, Burning = [.. x.Burning] })],
        Binds = [.. b.Binds.Select(x => new BindSave { A = x.A, B = x.B, Lines = x.Lines, HaulMen = x.HaulMen, HaulS = x.HaulS, Joined = x.Joined, OffLx = x.OffLx, OffLy = x.OffLy, OffHeading = x.OffHeading, Throws = x.Throws })],
        Frames = [.. b.Frames.Values.Select(f => new FrameSave { Id = f.Id, X = f.X, Y = f.Y, Heading = f.Heading, Speed = f.Speed, TurnPerTick = f.TurnPerTick, AccX = f.AccX, AccY = f.AccY, RiderKinds = f.RiderKinds, RiderIds = f.RiderIds, RiderLx = f.RiderLx, RiderLy = f.RiderLy, RiderDecks = f.RiderDecks })],
        Raids = [.. b.Raids.Values.Select(r => new SeaRaidSave
        {
            Id = r.Id, Realm = r.Realm, Victim = r.Victim, Region = r.Region, Settlement = r.Settlement, Culture = r.Culture, War = r.War, DeclEntry = r.DeclEntry, PlanShips = r.PlanShips, PlanMen = r.PlanMen,
            BeachXmm = r.BeachXmm, BeachYmm = r.BeachYmm, PlannedHour = r.PlannedHour, LandHour = r.LandHour, LootHour = r.LootHour, BellHour = r.BellHour, WithdrawHour = r.WithdrawHour, EndHour = r.EndHour,
            State = (byte)r.State, Ships = [.. r.Ships], Raiders = [.. r.Raiders], Guards = [.. r.Guards], Captives = [.. r.Captives], TakenG = r.TakenG, TakenCoin = r.TakenCoin, Burned = r.Burned,
            Dead = r.Dead, Households = r.Households, Entry = r.Entry, Fought = r.Fought, Defender = r.Defender, FightHour = r.FightHour, LiveSince = r.LiveSince >= 0 ? r.LiveSince : null,
        })],
    };

    public NavalBook Restore()
    {
        var b = new NavalBook { Next = NextRaid };
        foreach (var r in Raids)
        {
            var x = new SeaRaid
            {
                Id = r.Id, Realm = r.Realm, Victim = r.Victim, Region = r.Region, Settlement = r.Settlement, Culture = r.Culture, War = r.War, DeclEntry = r.DeclEntry, PlanShips = r.PlanShips, PlanMen = r.PlanMen,
                BeachXmm = r.BeachXmm, BeachYmm = r.BeachYmm, PlannedHour = r.PlannedHour, LandHour = r.LandHour, LootHour = r.LootHour, BellHour = r.BellHour, WithdrawHour = r.WithdrawHour, EndHour = r.EndHour,
                State = (RaidState)r.State, TakenG = r.TakenG, TakenCoin = r.TakenCoin, Burned = r.Burned, Dead = r.Dead, Households = r.Households, Entry = r.Entry, Fought = r.Fought, Defender = r.Defender, FightHour = r.FightHour, LiveSince = r.LiveSince ?? -1,
            };
            x.Ships.AddRange(r.Ships); x.Raiders.AddRange(r.Raiders); x.Guards.AddRange(r.Guards); x.Captives.AddRange(r.Captives);
            b.Raids[x.Id] = x;
        }
        foreach (var x in Ships)
        {
            var row = new ShipCombatRow { Ship = x.Ship, State = (ShipState)x.State, Captor = new EntityId(x.Captor), Entry = x.Entry, Serial = x.Serial, BurnCarry = x.BurnCarry, SailDown = x.SailDown, Bailers = x.Bailers };
            row.Burning.AddRange(x.Burning); b.Ships[row.Ship] = row;
        }
        foreach (var x in Binds) b.Binds.Add(new GrappleBind { A = x.A, B = x.B, Lines = x.Lines, HaulMen = x.HaulMen, HaulS = x.HaulS, Joined = x.Joined, OffLx = x.OffLx, OffLy = x.OffLy, OffHeading = x.OffHeading, Throws = x.Throws });
        foreach (var f in Frames) b.Frames[f.Id] = new ShipFrameState(f.Id, f.X, f.Y, f.Heading, f.Speed, f.TurnPerTick, f.AccX, f.AccY, f.RiderKinds, f.RiderIds, f.RiderLx, f.RiderLy, f.RiderDecks);
        return b;
    }
}
