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

    public static ArmySave Capture(ArmyBook b) => new()
    {
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
    };

    public ArmyBook Restore()
    {
        var b = new ArmyBook { NextWarband = NextWarband, NextErrand = NextErrand, NextBand = NextBand };
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
        return b;
    }
}
