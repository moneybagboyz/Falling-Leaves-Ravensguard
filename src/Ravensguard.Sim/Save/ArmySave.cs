using MessagePack;
using Ravensguard.Sim.Armies;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class WarbandSave
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public ulong Lord { get; set; }
    [Key(2)] public int Fief { get; set; }
    [Key(3)] public int Realm { get; set; }
    [Key(4)] public int Seat { get; set; }
    [Key(5)] public ulong[] Members { get; set; } = [];
    [Key(6)] public ulong[] PrevPersons { get; set; } = [];
    [Key(7)] public byte[] PrevOccupations { get; set; } = [];
    [Key(8)] public ulong Captain { get; set; }
    [Key(9)] public byte Task { get; set; }
    [Key(10)] public int Target { get; set; }
    [Key(11)] public ulong TargetId { get; set; }
    [Key(12)] public int TaskDay { get; set; }
    [Key(13)] public string Cause { get; set; } = "";
    [Key(14)] public int Here { get; set; }
    [Key(15)] public int From { get; set; }
    [Key(16)] public int To { get; set; }
    [Key(17)] public long DistMm { get; set; }
    [Key(18)] public int Dwell { get; set; }
    [Key(19)] public int[] Stops { get; set; } = [];
    [Key(20)] public int Morale { get; set; }
    [Key(21)] public long ArrearsMites { get; set; }
    [Key(22)] public int RaisedDay { get; set; }
    [Key(23)] public int PaidDay { get; set; }
    [Key(24)] public int ThoughtDay { get; set; }
    [Key(25)] public bool Mounted { get; set; }
    [Key(26)] public int Escorting { get; set; }
    [Key(27)] public int[] VisitedPlaces { get; set; } = [];
    [Key(28)] public int[] VisitedDays { get; set; } = [];
    [Key(29)] public ulong[] VisitLords { get; set; } = [];
    [Key(30)] public int[] VisitDays { get; set; } = [];
    [Key(31)] public long LootG { get; set; }
    [Key(32)] public int Phase { get; set; }
}

[MessagePackObject]
public sealed class ErrandSave
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public byte Kind { get; set; }
    [Key(2)] public int Fief { get; set; }
    [Key(3)] public int From { get; set; }
    [Key(4)] public int To { get; set; }
    [Key(5)] public int DayMade { get; set; }
    [Key(6)] public byte State { get; set; }
    [Key(7)] public long DistMm { get; set; }
    [Key(8)] public int Escort { get; set; }
    [Key(9)] public ulong Entry { get; set; }
}

[MessagePackObject]
public sealed class BandSave
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public ulong Leader { get; set; }
    [Key(2)] public ulong[] Members { get; set; } = [];
    [Key(3)] public ulong[] PrevPersons { get; set; } = [];
    [Key(4)] public byte[] PrevOccupations { get; set; } = [];
    [Key(5)] public int Lair { get; set; }
    [Key(6)] public byte Origin { get; set; }
    [Key(7)] public int FormedDay { get; set; }
    [Key(8)] public int LastRobDay { get; set; }
    [Key(9)] public long LootMites { get; set; }
}

[MessagePackObject]
public sealed class NewsSave
{
    [Key(0)] public int Day { get; set; }
    [Key(1)] public int Place { get; set; }
    [Key(2)] public int Band { get; set; }
    [Key(3)] public int Realm { get; set; }
    [Key(4)] public int Strength { get; set; }
}

[MessagePackObject]
public sealed class TaskLineSave
{
    [Key(0)] public int Day { get; set; }
    [Key(1)] public int Warband { get; set; }
    [Key(2)] public byte Task { get; set; }
    [Key(3)] public int Target { get; set; }
    [Key(4)] public string Cause { get; set; } = "";
}

[MessagePackObject]
public sealed class LevyCompanySave
{
    [Key(0)] public int Fief { get; set; }
    [Key(1)] public int Seat { get; set; }
    [Key(2)] public int Quota { get; set; }
    [Key(3)] public int Heard { get; set; }
    [Key(4)] public int Drawn { get; set; }
    [Key(5)] public int Arrives { get; set; }
    [Key(6)] public int HomeDay { get; set; }
    [Key(7)] public int Evaded { get; set; }
    [Key(8)] public int Asked { get; set; }
    [Key(9)] public bool Walking { get; set; }
    [Key(10)] public ulong Captain { get; set; }
    [Key(11)] public byte Kind { get; set; }
    [Key(12)] public ulong[] Men { get; set; } = [];
}

[MessagePackObject]
public sealed class HostSave
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public int Realm { get; set; }
    [Key(2)] public int War { get; set; }
    [Key(3)] public int Point { get; set; }
    [Key(4)] public int Supply { get; set; }
    [Key(5)] public int SummonsDay { get; set; }
    [Key(6)] public int FormedDay { get; set; }
    [Key(7)] public int FirstArrival { get; set; }
    [Key(8)] public int ReleasedDay { get; set; }
    [Key(9)] public int Expected { get; set; }
    [Key(10)] public int DecidedYear { get; set; }
    [Key(11)] public int FundDays { get; set; }
    [Key(12)] public ulong Commander { get; set; }
    [Key(13)] public ulong Entry { get; set; }
    [Key(14)] public long Worth { get; set; }
    [Key(15)] public bool Paying { get; set; }
    [Key(16)] public bool Raises { get; set; }
    [Key(17)] public byte State { get; set; }
    [Key(18)] public LevyCompanySave[] Companies { get; set; } = [];
    [Key(19)] public int[] JoinWarbands { get; set; } = [];
    [Key(20)] public byte[] JoinKinds { get; set; } = [];
    [Key(21)] public int[] JoinMen { get; set; } = [];
}

[MessagePackObject]
public sealed class HostCampSave
{
    [Key(0)] public long XMm { get; set; }
    [Key(1)] public long YMm { get; set; }
    [Key(2)] public int Near { get; set; }
    [Key(3)] public int HeadX { get; set; }
    [Key(4)] public int HeadY { get; set; }
    [Key(5)] public long SinceSec { get; set; }
    [Key(6)] public int Serial { get; set; }
    [Key(7)] public int Men { get; set; }
    [Key(8)] public int Nights { get; set; }
    [Key(9)] public int Watch { get; set; }
    [Key(10)] public byte Flags { get; set; }
    [Key(11)] public int Pieces { get; set; }
    [Key(12)] public int Sentries { get; set; }
}

/// <summary>P7-AM-C: a host on the road (the keys are appended to the armies section; a section written by AM-A reads with none).</summary>
[MessagePackObject]
public sealed class HostMarchSave
{
    [Key(0)] public int Host { get; set; }
    [Key(1)] public int[] Stops { get; set; } = [];
    [Key(2)] public long DistMm { get; set; }
    [Key(3)] public long RateHourMm { get; set; }
    [Key(4)] public long HourSec { get; set; }
    [Key(5)] public long PrevDistMm { get; set; }
    [Key(6)] public long PrevRateHourMm { get; set; }
    [Key(7)] public long PrevHourSec { get; set; }
    [Key(8)] public byte State { get; set; }
    [Key(9)] public HostCampSave? Camp { get; set; }
    [Key(10)] public int CampsPitched { get; set; }
    [Key(11)] public int StartDay { get; set; }
    [Key(12)] public int Hours { get; set; }
    [Key(13)] public int MarchHours { get; set; }
    [Key(14)] public string Cause { get; set; } = "";
    [Key(15)] public ulong[] OutsiderIds { get; set; } = [];
    [Key(16)] public byte[] OutsiderRoles { get; set; } = [];
    [Key(17)] public bool InRegion { get; set; }
    [Key(18)] public ulong ChronEntry { get; set; }
    [Key(19)] public bool Hold { get; set; }
}

/// <summary>P7-AM-D: a host's rations record (the appended key 13 of the armies section).</summary>
[MessagePackObject]
public sealed class HostRationSave
{
    [Key(0)] public int Host { get; set; }
    /// <summary>Morale, run of short days, last ration (permille), day of the last defeat, stripped place, days on it.</summary>
    [Key(1)] public int[] Fields { get; set; } = [];
    /// <summary>Hungry days, short days, persons deserted, counts deserted.</summary>
    [Key(2)] public long[] Totals { get; set; } = [];
    [Key(3)] public ulong[] WoundedPersons { get; set; } = [];
    [Key(4)] public int[] WoundedUntil { get; set; } = [];
}

/// <summary>
/// The optional "armies" section (a name of <c>Phase7Seeding.SectionNames</c>), written only while the book is not empty, so a world that never raised a warband keeps its bytes. Later armies parts (hosts, camps, intel,
/// war events) append keys: a section written by AM-A reads with them empty. Positions are plain integers over the road graph; the road paths are rebuilt from the region on load.
/// </summary>
[MessagePackObject]
public sealed class ArmySave
{
    public const string SectionName = "armies";

    [Key(0)] public WarbandSave[] Warbands { get; set; } = [];
    [Key(1)] public ErrandSave[] Errands { get; set; } = [];
    [Key(2)] public BandSave[] Bands { get; set; } = [];
    [Key(3)] public NewsSave[] Reports { get; set; } = [];
    [Key(4)] public TaskLineSave[] Log { get; set; } = [];
    [Key(5)] public int NextWarband { get; set; } = 1;
    [Key(6)] public int NextErrand { get; set; } = 1;
    [Key(7)] public int NextBand { get; set; } = 1;
    [Key(8)] public int[] BarrenFiefs { get; set; } = [];
    [Key(9)] public int[] BarrenSeasons { get; set; } = [];
    /// <summary>P7-AM-B (appended): the seasonal hosts with their levy companies and the warbands that joined, and the next id.</summary>
    [Key(10)] public HostSave[] Hosts { get; set; } = [];
    [Key(11)] public int NextHost { get; set; } = 1;
    /// <summary>P7-AM-C: the hosts on the road (empty in a section written before it).</summary>
    [Key(12)] public HostMarchSave[] Marches { get; set; } = [];
    /// <summary>P7-AM-D: the hosts' rations records (empty in a section written before it).</summary>
    [Key(13)] public HostRationSave[] Rations { get; set; } = [];

    public static ArmySave Capture(ArmyBook b) => new()
    {
        Rations = [.. b.Rations.Values.Select(r => new HostRationSave
        {
            Host = r.Host, Fields = [r.Morale, r.HungryRun, r.FedPm, r.DefeatDay, r.StripPlace, r.StripDays], Totals = [r.HungryDays, r.ShortDays, r.DesertedPersons, r.DesertedCounts],
            WoundedPersons = [.. r.Wounded.Keys], WoundedUntil = [.. r.Wounded.Values],
        })],
        Warbands = [.. b.Warbands.Values.Select(w => new WarbandSave
        {
            Id = w.Id, Lord = w.Lord, Fief = w.Fief, Realm = w.Realm, Seat = w.Seat, Members = [.. w.Members],
            PrevPersons = [.. w.Prev.Keys], PrevOccupations = [.. w.Prev.Values], Captain = w.Captain, Task = (byte)w.Task, Target = w.Target, TargetId = w.TargetId, TaskDay = w.TaskDay, Cause = w.Cause,
            Here = w.Here, From = w.From, To = w.To, DistMm = w.DistMm, Dwell = w.Dwell, Stops = [.. w.Stops], Morale = w.Morale, ArrearsMites = w.ArrearsMites, RaisedDay = w.RaisedDay,
            PaidDay = w.PaidDay, ThoughtDay = w.ThoughtDay, Mounted = w.Mounted, Escorting = w.Escorting,
            VisitedPlaces = [.. w.Visited.Keys], VisitedDays = [.. w.Visited.Values], VisitLords = [.. w.Visits.Keys], VisitDays = [.. w.Visits.Values], LootG = w.LootG, Phase = w.Phase,
        })],
        Errands = [.. b.Errands.Values.Select(e => new ErrandSave { Id = e.Id, Kind = (byte)e.Kind, Fief = e.Fief, From = e.From, To = e.To, DayMade = e.DayMade, State = (byte)e.State, DistMm = e.DistMm, Escort = e.Escort, Entry = e.Entry })],
        Bands = [.. b.Bands.Values.Select(x => new BandSave
        {
            Id = x.Id, Leader = x.Leader, Members = [.. x.Members], PrevPersons = [.. x.Prev.Keys], PrevOccupations = [.. x.Prev.Values], Lair = x.Lair, Origin = (byte)x.Origin,
            FormedDay = x.FormedDay, LastRobDay = x.LastRobDay, LootMites = x.LootMites,
        })],
        Reports = [.. b.Reports.Select(r => new NewsSave { Day = r.Day, Place = r.Place, Band = r.Band, Realm = r.Realm, Strength = r.Strength })],
        Log = [.. b.Log.Select(l => new TaskLineSave { Day = l.Day, Warband = l.Warband, Task = (byte)l.Task, Target = l.Target, Cause = l.Cause })],
        NextWarband = b.NextWarband, NextErrand = b.NextErrand, NextBand = b.NextBand,
        BarrenFiefs = [.. b.Barren.Keys], BarrenSeasons = [.. b.Barren.Values],
        Hosts = [.. b.Hosts.Values.Select(h => new HostSave
        {
            Id = h.Id, Realm = h.Realm, War = h.War, Point = h.Point, Supply = h.Supply, SummonsDay = h.SummonsDay, FormedDay = h.FormedDay, FirstArrival = h.FirstArrival, ReleasedDay = h.ReleasedDay, Expected = h.Expected,
            DecidedYear = h.DecidedYear, FundDays = h.FundDays, Commander = h.Commander, Entry = h.Entry, Worth = h.Worth, Paying = h.Paying, Raises = h.Raises, State = (byte)h.State,
            Companies = [.. h.Companies.Select(c => new LevyCompanySave
            {
                Fief = c.Fief, Seat = c.Seat, Quota = c.Quota, Heard = c.Heard, Drawn = c.Drawn, Arrives = c.Arrives, HomeDay = c.HomeDay, Evaded = c.Evaded, Asked = c.Asked, Walking = c.Walking, Captain = c.Captain, Kind = (byte)c.Kind, Men = [.. c.Men],
            })],
            JoinWarbands = [.. h.Warbands.Select(j => j.Warband)], JoinKinds = [.. h.Warbands.Select(j => (byte)j.Kind)], JoinMen = [.. h.Warbands.Select(j => j.Men)],
        })],
        NextHost = b.NextHost,
        Marches = [.. b.Marches.Values.Select(m => new HostMarchSave
        {
            Host = m.Host, Stops = [.. m.Stops], DistMm = m.DistMm, RateHourMm = m.RateHourMm, HourSec = m.HourSec, PrevDistMm = m.PrevDistMm, PrevRateHourMm = m.PrevRateHourMm, PrevHourSec = m.PrevHourSec,
            State = (byte)m.State, CampsPitched = m.CampsPitched, StartDay = m.StartDay, Hours = m.Hours, MarchHours = m.MarchHours, Cause = m.Cause, OutsiderIds = [.. m.Outsiders.Keys], OutsiderRoles = [.. m.Outsiders.Values],
            InRegion = m.InRegion, ChronEntry = m.ChronEntry, Hold = m.Hold,
            Camp = m.Camp is not { } c ? null : new HostCampSave
            {
                XMm = c.XMm, YMm = c.YMm, Near = c.Near, HeadX = c.HeadX, HeadY = c.HeadY, SinceSec = c.SinceSec, Serial = c.Serial, Men = c.Men, Nights = c.Nights, Watch = c.Watch,
                Flags = (byte)((c.Palisade ? 1 : 0) | (c.Latrine ? 2 : 0) | (c.WaterCarried ? 4 : 0) | (c.BelowTown ? 8 : 0)), Pieces = c.Pieces, Sentries = c.Sentries,
            },
        })],
    };

    public ArmyBook Restore()
    {
        var b = new ArmyBook { NextWarband = NextWarband, NextErrand = NextErrand, NextBand = NextBand, NextHost = Math.Max(1, NextHost) };
        foreach (var s in Warbands)
        {
            var w = new Warband
            {
                Id = s.Id, Lord = s.Lord, Fief = s.Fief, Realm = s.Realm, Seat = s.Seat, Captain = s.Captain, Task = (WarbandTask)s.Task, Target = s.Target, TargetId = s.TargetId, TaskDay = s.TaskDay, Cause = s.Cause,
                Here = s.Here, From = s.From, To = s.To, DistMm = s.DistMm, Dwell = s.Dwell, Morale = s.Morale, ArrearsMites = s.ArrearsMites, RaisedDay = s.RaisedDay, PaidDay = s.PaidDay,
                ThoughtDay = s.ThoughtDay, Mounted = s.Mounted, Escorting = s.Escorting, LootG = s.LootG, Phase = s.Phase,
            };
            w.Members.AddRange(s.Members); w.Stops.AddRange(s.Stops);
            for (int i = 0; i < s.PrevPersons.Length && i < s.PrevOccupations.Length; i++) w.Prev[s.PrevPersons[i]] = s.PrevOccupations[i];
            for (int i = 0; i < s.VisitedPlaces.Length && i < s.VisitedDays.Length; i++) w.Visited[s.VisitedPlaces[i]] = s.VisitedDays[i];
            for (int i = 0; i < s.VisitLords.Length && i < s.VisitDays.Length; i++) w.Visits[s.VisitLords[i]] = s.VisitDays[i];
            b.Warbands[w.Id] = w;
        }
        foreach (var s in Errands) b.Errands[s.Id] = new Errand { Id = s.Id, Kind = (ErrandKind)s.Kind, Fief = s.Fief, From = s.From, To = s.To, DayMade = s.DayMade, State = (ErrandState)s.State, DistMm = s.DistMm, Escort = s.Escort, Entry = s.Entry };
        foreach (var s in Bands)
        {
            var x = new BanditBand { Id = s.Id, Leader = s.Leader, Lair = s.Lair, Origin = (BandOrigin)s.Origin, FormedDay = s.FormedDay, LastRobDay = s.LastRobDay, LootMites = s.LootMites };
            x.Members.AddRange(s.Members);
            for (int i = 0; i < s.PrevPersons.Length && i < s.PrevOccupations.Length; i++) x.Prev[s.PrevPersons[i]] = s.PrevOccupations[i];
            b.Bands[x.Id] = x;
        }
        foreach (var r in Reports) b.Reports.Add(new News { Day = r.Day, Place = r.Place, Band = r.Band, Realm = r.Realm, Strength = r.Strength });
        foreach (var l in Log) b.Log.Add(new TaskLine { Day = l.Day, Warband = l.Warband, Task = (WarbandTask)l.Task, Target = l.Target, Cause = l.Cause });
        for (int i = 0; i < BarrenFiefs.Length && i < BarrenSeasons.Length; i++) b.Barren[BarrenFiefs[i]] = BarrenSeasons[i];
        foreach (var s in Hosts)
        {
            var h = new ArmyHost
            {
                Id = s.Id, Realm = s.Realm, War = s.War, Point = s.Point, Supply = s.Supply, SummonsDay = s.SummonsDay, FormedDay = s.FormedDay, FirstArrival = s.FirstArrival, ReleasedDay = s.ReleasedDay, Expected = s.Expected,
                DecidedYear = s.DecidedYear, FundDays = s.FundDays, Commander = s.Commander, Entry = s.Entry, Worth = s.Worth, Paying = s.Paying, Raises = s.Raises, State = (HostState)s.State,
            };
            foreach (var c in s.Companies)
            {
                var lc = new LevyCompany { Fief = c.Fief, Seat = c.Seat, Quota = c.Quota, Heard = c.Heard, Drawn = c.Drawn, Arrives = c.Arrives, HomeDay = c.HomeDay, Evaded = c.Evaded, Asked = c.Asked, Walking = c.Walking, Captain = c.Captain, Kind = (ContingentKind)c.Kind };
                lc.Men.AddRange(c.Men); h.Companies.Add(lc);
            }
            for (int i = 0; i < s.JoinWarbands.Length && i < s.JoinKinds.Length && i < s.JoinMen.Length; i++) h.Warbands.Add(new WarbandJoin(s.JoinWarbands[i], (ContingentKind)s.JoinKinds[i], s.JoinMen[i]));
            b.Hosts[h.Id] = h;
        }
        foreach (var s in Marches)
        {
            var m = new HostMarch
            {
                Host = s.Host, DistMm = s.DistMm, RateHourMm = s.RateHourMm, HourSec = s.HourSec, PrevDistMm = s.PrevDistMm, PrevRateHourMm = s.PrevRateHourMm, PrevHourSec = s.PrevHourSec, State = (MarchState)s.State,
                CampsPitched = s.CampsPitched, StartDay = s.StartDay, Hours = s.Hours, MarchHours = s.MarchHours, Cause = s.Cause, InRegion = s.InRegion, ChronEntry = s.ChronEntry, Hold = s.Hold,
            };
            m.Stops.AddRange(s.Stops);
            for (int i = 0; i < s.OutsiderIds.Length && i < s.OutsiderRoles.Length; i++) m.Outsiders[s.OutsiderIds[i]] = s.OutsiderRoles[i];
            if (s.Camp is { } c)
                m.Camp = new CampRecord
                {
                    XMm = c.XMm, YMm = c.YMm, Near = c.Near, HeadX = c.HeadX, HeadY = c.HeadY, SinceSec = c.SinceSec, Serial = c.Serial, Men = c.Men, Nights = c.Nights, Watch = c.Watch, Pieces = c.Pieces, Sentries = c.Sentries,
                    Palisade = (c.Flags & 1) != 0, Latrine = (c.Flags & 2) != 0, WaterCarried = (c.Flags & 4) != 0, BelowTown = (c.Flags & 8) != 0,
                };
            b.Marches[m.Host] = m;
        }
        foreach (var s in Rations)
        {
            var r = new HostRations { Host = s.Host };
            if (s.Fields.Length >= 6) { r.Morale = s.Fields[0]; r.HungryRun = s.Fields[1]; r.FedPm = s.Fields[2]; r.DefeatDay = s.Fields[3]; r.StripPlace = s.Fields[4]; r.StripDays = s.Fields[5]; }
            if (s.Totals.Length >= 4) { r.HungryDays = s.Totals[0]; r.ShortDays = s.Totals[1]; r.DesertedPersons = s.Totals[2]; r.DesertedCounts = s.Totals[3]; }
            for (int i = 0; i < s.WoundedPersons.Length && i < s.WoundedUntil.Length; i++) r.Wounded[s.WoundedPersons[i]] = s.WoundedUntil[i];
            b.Rations[r.Host] = r;
        }
        return b;
    }
}
