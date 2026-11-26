using MessagePack;
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

    public static NavalSave? CaptureOrNull(NavalBook? b) => b is { IsEmpty: false } ? Capture(b) : null;

    public static NavalSave Capture(NavalBook b) => new()
    {
        NextRaid = b.Next,
        Raids = [.. b.Raids.Values.Select(r => new SeaRaidSave
        {
            Id = r.Id, Realm = r.Realm, Victim = r.Victim, Region = r.Region, Settlement = r.Settlement, Culture = r.Culture, War = r.War, DeclEntry = r.DeclEntry, PlanShips = r.PlanShips, PlanMen = r.PlanMen,
            BeachXmm = r.BeachXmm, BeachYmm = r.BeachYmm, PlannedHour = r.PlannedHour, LandHour = r.LandHour, LootHour = r.LootHour, BellHour = r.BellHour, WithdrawHour = r.WithdrawHour, EndHour = r.EndHour,
            State = (byte)r.State, Ships = [.. r.Ships], Raiders = [.. r.Raiders], Guards = [.. r.Guards], Captives = [.. r.Captives], TakenG = r.TakenG, TakenCoin = r.TakenCoin, Burned = r.Burned,
            Dead = r.Dead, Households = r.Households, Entry = r.Entry, Fought = r.Fought, Defender = r.Defender, FightHour = r.FightHour,
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
                State = (RaidState)r.State, TakenG = r.TakenG, TakenCoin = r.TakenCoin, Burned = r.Burned, Dead = r.Dead, Households = r.Households, Entry = r.Entry, Fought = r.Fought, Defender = r.Defender, FightHour = r.FightHour,
            };
            x.Ships.AddRange(r.Ships); x.Raiders.AddRange(r.Raiders); x.Guards.AddRange(r.Guards); x.Captives.AddRange(r.Captives);
            b.Raids[x.Id] = x;
        }
        return b;
    }
}
