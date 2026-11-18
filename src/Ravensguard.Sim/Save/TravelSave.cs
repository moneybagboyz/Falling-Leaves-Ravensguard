using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Save;

[MessagePackObject] public sealed class InterruptRowSave { [Key(0)] public long Sec { get; set; } [Key(1)] public byte Kind { get; set; } [Key(2)] public byte Policy { get; set; } [Key(3)] public long X { get; set; } [Key(4)] public long Y { get; set; } [Key(5)] public string Text { get; set; } = ""; [Key(6)] public bool Quiet { get; set; } }
[MessagePackObject] public sealed class EncounterRowSave { [Key(0)] public long Sec { get; set; } [Key(1)] public string Id { get; set; } = ""; [Key(2)] public string Kind { get; set; } = ""; [Key(3)] public long X { get; set; } [Key(4)] public long Y { get; set; } [Key(5)] public long SX { get; set; } [Key(6)] public long SY { get; set; } [Key(7)] public int Leg { get; set; } [Key(8)] public ulong[] Cast { get; set; } = []; [Key(9)] public string Bark { get; set; } = ""; }
[MessagePackObject] public sealed class JourneyLogSave { [Key(0)] public long Start { get; set; } [Key(1)] public long End { get; set; } [Key(2)] public string Dest { get; set; } = ""; [Key(3)] public long DistM { get; set; } [Key(4)] public long Coin { get; set; } [Key(5)] public long Food { get; set; } [Key(6)] public string[] Enc { get; set; } = []; [Key(7)] public bool Done { get; set; } }
[MessagePackObject] public sealed class TravellerSave { [Key(0)] public ulong Id { get; set; } [Key(1)] public string Archetype { get; set; } = ""; [Key(2)] public string Encounter { get; set; } = ""; [Key(3)] public long Sec { get; set; } [Key(4)] public long X { get; set; } [Key(5)] public long Y { get; set; } }
[MessagePackObject] public sealed class CampSave
{
    [Key(0)] public ulong Id { get; set; } [Key(1)] public long X { get; set; } [Key(2)] public long Y { get; set; } [Key(3)] public long MadeAt { get; set; } [Key(4)] public bool Lit { get; set; } [Key(5)] public long Fuel { get; set; }
    [Key(6)] public long Since { get; set; } [Key(7)] public long Last { get; set; } [Key(8)] public long Acc { get; set; } [Key(9)] public byte Shelter { get; set; } [Key(10)] public ulong[] Members { get; set; } = [];
    [Key(11)] public ulong[] Sentries { get; set; } = []; [Key(12)] public bool Broken { get; set; } [Key(13)] public long BrokenAt { get; set; } [Key(14)] public bool Hung { get; set; }
    [Key(15)] public ushort[] StockGood { get; set; } = []; [Key(16)] public long[] StockQty { get; set; } = [];
}
[MessagePackObject] public sealed class MemberSave { [Key(0)] public ulong Id { get; set; } [Key(1)] public string Name { get; set; } = ""; [Key(2)] public int Loyalty { get; set; } [Key(3)] public int Familiarity { get; set; } [Key(4)] public int Mood { get; set; } [Key(5)] public int Perception { get; set; } [Key(6)] public int Fatigue { get; set; } [Key(7)] public int Cooking { get; set; } [Key(8)] public int Social { get; set; } [Key(9)] public int Ambition { get; set; } [Key(10)] public bool Backstory { get; set; } [Key(11)] public int LastTalk { get; set; } [Key(12)] public int LastMeal { get; set; } }
[MessagePackObject] public sealed class CarcassSave { [Key(0)] public ulong Id { get; set; } [Key(1)] public string Species { get; set; } = ""; [Key(2)] public long Mass { get; set; } [Key(3)] public long Death { get; set; } [Key(4)] public int Tile { get; set; } [Key(5)] public bool Hung { get; set; } [Key(6)] public long X { get; set; } [Key(7)] public long Y { get; set; } }
[MessagePackObject] public sealed class SnareSave { [Key(0)] public int Id { get; set; } [Key(1)] public int Tile { get; set; } [Key(2)] public long Set { get; set; } [Key(3)] public long LastHour { get; set; } [Key(4)] public bool Caught { get; set; } [Key(5)] public long CatchSec { get; set; } [Key(6)] public string Species { get; set; } = ""; [Key(7)] public long Mass { get; set; } [Key(8)] public long XMm { get; set; } [Key(9)] public long YMm { get; set; } }
[MessagePackObject]
public sealed class JourneySave
{
    [Key(0)] public string Dest { get; set; } = ""; [Key(1)] public long StartSec { get; set; } [Key(2)] public long[] PathX { get; set; } = []; [Key(3)] public long[] PathY { get; set; } = [];
    [Key(4)] public long Progress { get; set; } [Key(5)] public long Sec { get; set; } [Key(6)] public byte State { get; set; } [Key(7)] public long SlotDone { get; set; } [Key(8)] public int SlotSpeed { get; set; }
    [Key(9)] public long BreakUntil { get; set; } [Key(10)] public int Leg { get; set; } [Key(11)] public long Coin { get; set; } [Key(12)] public long Food { get; set; } [Key(13)] public string[] Enc { get; set; } = [];
}

/// <summary>
/// The "travel" section (travel-encounters-camp spec 5, 9): the journey in progress, the director's memory, interrupts and their log, camps (stock as rows unless the economy holds the stores), companions,
/// carcasses, snares and the travellers made up. Absent in a world nobody has travelled in, so older saves keep their bytes. Rows are in a fixed order, so save, load, save is byte-identical.
/// </summary>
[MessagePackObject]
public sealed class TravelSave
{
    public const string SectionName = "travel";
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public long GameStartSec { get; set; } [Key(2)] public ulong HeroId { get; set; } [Key(3)] public ulong NextTraveller { get; set; } [Key(4)] public ulong NextCamp { get; set; }
    [Key(5)] public ulong NextCarcass { get; set; } [Key(6)] public int NextSnare { get; set; } [Key(7)] public long LastEncounterSec { get; set; } [Key(8)] public long LastMealSec { get; set; } [Key(9)] public int LegSerial { get; set; }
    [Key(10)] public string[] LastIds { get; set; } = []; [Key(11)] public long[] LastTimes { get; set; } = []; [Key(12)] public string[] LegFired { get; set; } = [];
    [Key(13)] public byte[] Policy { get; set; } = []; [Key(14)] public bool[] Quiet { get; set; } = []; [Key(15)] public uint Active { get; set; } [Key(16)] public List<InterruptRowSave> Log { get; set; } = [];
    [Key(17)] public InterruptRowSave? Paused { get; set; } [Key(18)] public bool Capped { get; set; } [Key(19)] public string CapReason { get; set; } = ""; [Key(20)] public long SlowUntil { get; set; }
    [Key(21)] public JourneySave? Journey { get; set; } [Key(22)] public List<JourneyLogSave> Logs { get; set; } = []; [Key(23)] public List<EncounterRowSave> Encounters { get; set; } = [];
    [Key(24)] public List<TravellerSave> Travellers { get; set; } = []; [Key(25)] public CampSave? Camp { get; set; } [Key(26)] public List<CampSave> OldCamps { get; set; } = [];
    [Key(27)] public List<MemberSave> Companions { get; set; } = []; [Key(28)] public List<CarcassSave> Carcasses { get; set; } = []; [Key(29)] public List<SnareSave> Snares { get; set; } = [];
    [Key(30)] public bool Autopilot { get; set; } [Key(31)] public int LatitudeTenths { get; set; } = 480;

    private static InterruptRowSave Row(InterruptRecord r) => new() { Sec = r.Sec, Kind = (byte)r.Kind, Policy = (byte)r.Policy, X = r.XMm, Y = r.YMm, Text = r.Text, Quiet = r.Quiet };
    private static InterruptRecord Rec(InterruptRowSave r) => new(r.Sec, (InterruptKind)r.Kind, (InterruptPolicy)r.Policy, r.X, r.Y, r.Text, r.Quiet);

    private static CampSave CampRow(TravelState t, Camp c, bool rows)
    {
        var o = new CampSave { Id = c.Id.Value, X = c.XMm, Y = c.YMm, MadeAt = c.MadeAtSec, Lit = c.FireLit, Fuel = c.FuelG, Since = c.FireSinceSec, Last = c.FireLastSec, Acc = c.BurnAccGs, Shelter = (byte)c.Shelter, Members = [.. c.Members.Select(m => m.Value)], Sentries = [.. c.Sentries.Select(m => m.Value)], Broken = c.Broken, BrokenAt = c.BrokenAtSec, Hung = c.Hung };
        if (rows && t.TryStore(c.Id, out var s)) { var r = s.Rows(); o.StockGood = [.. r.Select(x => x.Good)]; o.StockQty = [.. r.Select(x => x.Qty)]; }
        return o;
    }

    public static TravelSave Capture(TravelState t)
    {
        bool rows = !t.Hosted;
        var (pol, quiet, active, log, paused, capped, capReason, slow) = t.Bus.Capture();
        var o = new TravelSave
        {
            GameStartSec = t.GameStartSec, HeroId = t.HeroId.Value, NextTraveller = t.NextTraveller, NextCamp = t.NextCamp, NextCarcass = t.NextCarcass, NextSnare = t.NextSnare, LastEncounterSec = t.LastEncounterSec, LastMealSec = t.LastMealSec,
            LegSerial = t.LegSerial, LastIds = [.. t.LastById.Keys], LastTimes = [.. t.LastById.Values], LegFired = [.. t.LegFired], Policy = pol, Quiet = quiet, Active = active, Log = [.. log.Select(Row)], Paused = paused is { } p ? Row(p) : null,
            Capped = capped, CapReason = capReason, SlowUntil = slow, Autopilot = t.Bus.Autopilot, LatitudeTenths = t.LatitudeTenths,
        };
        if (t.Journey is { } j)
            o.Journey = new JourneySave { Dest = j.DestName, StartSec = j.StartSec, PathX = j.Path.X, PathY = j.Path.Y, Progress = j.ProgressMm, Sec = j.Sec, State = (byte)j.State, SlotDone = j.SlotDone, SlotSpeed = j.SlotSpeedMmPerGs, BreakUntil = j.BreakUntilSec, Leg = j.Leg, Coin = j.CoinSpent, Food = j.FoodUsedG, Enc = [.. j.EncounterIds] };
        o.Logs = [.. t.Logs.Select(l => new JourneyLogSave { Start = l.StartSec, End = l.EndSec, Dest = l.Dest, DistM = l.DistM, Coin = l.CoinSpent, Food = l.FoodUsedG, Enc = l.Encounters, Done = l.Completed })];
        o.Encounters = [.. t.Encounters.Select(e => new EncounterRowSave { Sec = e.Sec, Id = e.Id, Kind = e.Kind, X = e.XMm, Y = e.YMm, SX = e.SpawnXMm, SY = e.SpawnYMm, Leg = e.Leg, Cast = e.Cast, Bark = e.Bark })];
        o.Travellers = [.. t.Travellers.Select(x => new TravellerSave { Id = x.Id.Value, Archetype = x.Archetype, Encounter = x.Encounter, Sec = x.Sec, X = x.XMm, Y = x.YMm })];
        o.Camp = t.Camp == null ? null : CampRow(t, t.Camp, rows);
        o.OldCamps = [.. t.OldCamps.Select(c => CampRow(t, c, false))];
        o.Companions = [.. t.Companions.Select(m => new MemberSave { Id = m.Person.Value, Name = m.Name, Loyalty = m.Loyalty, Familiarity = m.Familiarity, Mood = m.Mood, Perception = m.Perception, Fatigue = m.FatiguePm, Cooking = m.Cooking, Social = m.SocialPm, Ambition = m.AmbitionProgress, Backstory = m.BackstoryTold, LastTalk = m.LastTalkNight, LastMeal = m.LastMealDay })];
        o.Carcasses = [.. t.Carcasses.Select(c => new CarcassSave { Id = c.Id.Value, Species = c.Species, Mass = c.MassG, Death = c.DeathSec, Tile = c.Tile, Hung = c.Hung, X = c.XMm, Y = c.YMm })];
        o.Snares = [.. t.Snares.Select(s => new SnareSave { Id = s.Id, Tile = s.Tile, Set = s.SetSec, LastHour = s.LastHour, Caught = s.Caught, CatchSec = s.CatchSec, Species = s.Species, Mass = s.MassG, XMm = s.XMm, YMm = s.YMm })];
        return o;
    }

    private static Camp CampOf(CampSave r) => new()
    {
        Id = new EntityId(r.Id), XMm = r.X, YMm = r.Y, MadeAtSec = r.MadeAt, FireLit = r.Lit, FuelG = r.Fuel, FireSinceSec = r.Since, FireLastSec = r.Last, BurnAccGs = r.Acc, Shelter = (ShelterKind)r.Shelter, Broken = r.Broken, BrokenAtSec = r.BrokenAt, Hung = r.Hung,
    };

    /// <summary>Fills a fresh <see cref="TravelState"/> (built on the content, seed, goods and ledger) from the section.</summary>
    public void Restore(TravelState t)
    {
        t.GameStartSec = GameStartSec; t.HeroId = new EntityId(HeroId); t.NextTraveller = NextTraveller; t.NextCamp = NextCamp; t.NextCarcass = NextCarcass; t.NextSnare = NextSnare; t.LastEncounterSec = LastEncounterSec; t.LastMealSec = LastMealSec;
        t.LegSerial = LegSerial; t.LatitudeTenths = LatitudeTenths; t.Bus.Autopilot = Autopilot;
        for (int i = 0; i < LastIds.Length; i++) t.LastById[LastIds[i]] = LastTimes[i];
        foreach (var f in LegFired) t.LegFired.Add(f);
        t.Bus.Restore(Policy, Quiet, Active, Log.Select(Rec), Paused is { } p ? Rec(p) : null, Capped, CapReason, SlowUntil);
        if (Journey is { } j)
            t.Journey = new Journey { DestName = j.Dest, StartSec = j.StartSec, Path = new RoutePath(j.PathX, j.PathY), ProgressMm = j.Progress, Sec = j.Sec, State = (JourneyState)j.State, SlotDone = j.SlotDone, SlotSpeedMmPerGs = j.SlotSpeed, BreakUntilSec = j.BreakUntil, Leg = j.Leg, CoinSpent = j.Coin, FoodUsedG = j.Food, EncounterIds = [.. j.Enc], StartXMm = j.PathX[0], StartYMm = j.PathY[0], DestXMm = j.PathX[^1], DestYMm = j.PathY[^1] };
        t.Logs.Clear(); t.Logs.AddRange(Logs.Select(l => new JourneyLog(l.Start, l.End, l.Dest, l.DistM, l.Coin, l.Food, l.Enc, l.Done)));
        t.Encounters.Clear(); t.Encounters.AddRange(Encounters.Select(e => new EncounterEvent(e.Sec, e.Id, e.Kind, e.X, e.Y, e.SX, e.SY, e.Leg, e.Cast, e.Bark)));
        t.Travellers.Clear(); t.Travellers.AddRange(Travellers.Select(x => new Traveller { Id = new EntityId(x.Id), Archetype = x.Archetype, Encounter = x.Encounter, Sec = x.Sec, XMm = x.X, YMm = x.Y }));
        t.OldCamps.Clear(); t.OldCamps.AddRange(OldCamps.Select(c => { var k = CampOf(c); k.Members.AddRange(c.Members.Select(m => new EntityId(m))); k.Sentries.AddRange(c.Sentries.Select(m => new EntityId(m))); return k; }));
        t.Camp = null;
        if (Camp is { } cs)
        {
            var c = CampOf(cs); c.Members.AddRange(cs.Members.Select(m => new EntityId(m))); c.Sentries.AddRange(cs.Sentries.Select(m => new EntityId(m)));
            t.Camp = c; var store = t.StoreOf(c.Id);
            if (!t.Hosted) for (int i = 0; i < cs.StockGood.Length; i++) store.Create(cs.StockGood[i], cs.StockQty[i], CreateReason.Seed);
        }
        t.Companions.Clear(); t.Companions.AddRange(Companions.Select(m => new CampMember { Person = new EntityId(m.Id), Name = m.Name, Loyalty = m.Loyalty, Familiarity = m.Familiarity, Mood = m.Mood, Perception = m.Perception, FatiguePm = m.Fatigue, Cooking = m.Cooking, SocialPm = m.Social, AmbitionProgress = m.Ambition, BackstoryTold = m.Backstory, LastTalkNight = m.LastTalk, LastMealDay = m.LastMeal }));
        t.Carcasses.Clear(); t.Carcasses.AddRange(Carcasses.Select(c => new Carcass { Id = new EntityId(c.Id), Species = c.Species, MassG = c.Mass, DeathSec = c.Death, Tile = c.Tile, Hung = c.Hung, XMm = c.X, YMm = c.Y }));
        t.Snares.Clear(); t.Snares.AddRange(Snares.Select(s => new Snare { Id = s.Id, Tile = s.Tile, SetSec = s.Set, LastHour = s.LastHour, Caught = s.Caught, CatchSec = s.CatchSec, Species = s.Species, MassG = s.Mass, XMm = s.XMm, YMm = s.YMm }));
    }
}
