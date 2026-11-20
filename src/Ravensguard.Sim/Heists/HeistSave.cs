using MessagePack;
using Ravensguard.Sim.Heists;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "heists" section (P6-D3, heists-and-jailbreaks 5; no format bump, absent while no site has been visited, so a world that never used it keeps its bytes): the visited sites (lock generation, the day the locks are to be changed,
/// the chest's day, and while a run is on, the doors, sconces, captives and guards), the hero's keys, the run in play and the manhunts. Maps are never saved: a site names its template.
/// </summary>
[MessagePackObject]
public sealed class HeistSave
{
    public const string SectionName = "heists";

    [Key(0)] public List<HeistSiteSave> Sites { get; set; } = [];
    [Key(1)] public long[][] Keys { get; set; } = [];
    [Key(2)] public HeistRunSave? Run { get; set; }
    [Key(3)] public List<HeistManhuntSave> Manhunts { get; set; } = [];
    [Key(4)] public int NextManhunt { get; set; } = 1;
    [Key(5)] public long LastHour { get; set; } = -1;

    public static HeistSave Capture(HeistBook b) => new()
    {
        Sites = [.. b.Sites.Values.Select(HeistSiteSave.Of)],
        Keys = [.. b.Keys.Select(k => new long[] { (long)k.Site, k.Door, k.Generation })],
        Run = b.Run is { } r ? HeistRunSave.Of(r) : null,
        Manhunts = [.. b.Manhunts.Select(m => new HeistManhuntSave { Id = m.Id, Settlement = m.Settlement, StartSec = m.StartSec, Persons = [.. m.Persons], Entry = m.Entry, Ended = m.Ended })],
        NextManhunt = b.NextManhunt, LastHour = b.LastHour,
    };

    /// <summary>The book again; <paramref name="rules"/> gives each site its map back (a template the content no longer has drops its site).</summary>
    public HeistBook Restore(HeistRules rules, ulong seed)
    {
        var b = new HeistBook { NextManhunt = NextManhunt, LastHour = LastHour };
        foreach (var s in Sites)
        {
            var site = s.Restore(rules);
            if (site != null) b.Sites[site.Id] = site;
        }
        foreach (var k in Keys) { if (k.Length < 3) throw new SaveException("Corrupt key in 'heists' section."); b.Keys.Add(new KeyRef((ulong)k[0], (int)k[1], (int)k[2])); }
        foreach (var m in Manhunts) b.Manhunts.Add(new Manhunt { Id = m.Id, Settlement = m.Settlement, StartSec = m.StartSec, Persons = [.. m.Persons], Entry = m.Entry, Ended = m.Ended });
        if (Run is { } r && b.Sites.ContainsKey(r.Cur)) b.Run = r.Restore(rules, seed, b);
        return b;
    }
}

[MessagePackObject]
public sealed class HeistSiteSave
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public int Settlement { get; set; }
    [Key(2)] public byte Kind { get; set; }
    [Key(3)] public string Template { get; set; } = "";
    [Key(4)] public int LockGen { get; set; }
    [Key(5)] public int ChangeDay { get; set; } = -1;
    [Key(6)] public int ChestDay { get; set; } = -1;
    [Key(7)] public int Visits { get; set; }
    [Key(8)] public string[] Extras { get; set; } = [];
    [Key(9)] public byte[] Door { get; set; } = [];
    [Key(10)] public bool[] SconceOut { get; set; } = [];
    [Key(11)] public ulong[] Slot { get; set; } = [];
    [Key(12)] public bool[] Freed { get; set; } = [];
    [Key(13)] public List<HeistGuardSave> Guards { get; set; } = [];
    [Key(14)] public bool Alarm { get; set; }
    [Key(15)] public bool OuterLocked { get; set; }
    [Key(16)] public int FireX { get; set; } = -1;
    [Key(17)] public int FireY { get; set; } = -1;
    [Key(18)] public long FireUntil { get; set; }
    [Key(19)] public long[][] Noises { get; set; } = [];
    [Key(20)] public long NoiseSeq { get; set; }
    [Key(21)] public ulong ChangeCause { get; set; }

    public static HeistSiteSave Of(HSite s) => new()
    {
        Id = s.Id, Settlement = s.Settlement, Kind = (byte)s.Kind, Template = s.Template, LockGen = s.LockGen, ChangeDay = s.ChangeDay, ChestDay = s.ChestDay, Visits = s.Visits, Extras = [.. s.Extras], Door = s.Door, SconceOut = s.SconceOut,
        Slot = s.Slot, Freed = s.Freed, Guards = [.. s.Guards.Select(HeistGuardSave.Of)], Alarm = s.Alarm, OuterLocked = s.OuterLocked, FireX = s.FireX, FireY = s.FireY, FireUntil = s.FireUntil,
        Noises = [.. s.Noises.Select(n => (long[])n.Clone())], NoiseSeq = s.NoiseSeq, ChangeCause = s.ChangeCause,
    };

    public HSite? Restore(HeistRules rules)
    {
        var map = rules.Map(Template); if (map == null) return null;
        var s = new HSite
        {
            Id = Id, Settlement = Settlement, Kind = (HeistKind)Kind, Template = Template, Map = map, LockGen = LockGen, ChangeDay = ChangeDay, ChestDay = ChestDay, Visits = Visits, Extras = [.. Extras], Door = [.. Door], SconceOut = [.. SconceOut],
            Slot = [.. Slot], Freed = [.. Freed], Guards = [.. Guards.Select(g => g.Restore())], Alarm = Alarm, OuterLocked = OuterLocked, FireX = FireX, FireY = FireY, FireUntil = FireUntil,
            Noises = [.. Noises.Select(n => (long[])n.Clone())], NoiseSeq = NoiseSeq, ChangeCause = ChangeCause,
        };
        // a template that changed shape since the save: the run state of the old shape is dropped (the persistent part stays)
        if (s.Door.Length != 0 && s.Door.Length != map.Doors.Count + 1) { s.Door = []; s.SconceOut = []; s.Slot = []; s.Freed = []; s.Guards = []; s.Noises = []; }
        return s;
    }
}

[MessagePackObject]
public sealed class HeistGuardSave
{
    [Key(0)] public ulong Person { get; set; }
    [Key(1)] public bool Synthetic { get; set; }
    [Key(2)] public int Slot { get; set; }
    [Key(3)] public bool Keeper { get; set; }
    [Key(4)] public int X { get; set; }
    [Key(5)] public int Y { get; set; }
    [Key(6)] public int HomeX { get; set; }
    [Key(7)] public int HomeY { get; set; }
    [Key(8)] public byte State { get; set; }
    [Key(9)] public int Route { get; set; }
    [Key(10)] public int Timer { get; set; }
    [Key(11)] public int TargetX { get; set; }
    [Key(12)] public int TargetY { get; set; }
    [Key(13)] public int Familiarity { get; set; }
    [Key(14)] public int Honesty { get; set; } = 45;
    [Key(15)] public int[] Keys { get; set; } = [];
    [Key(16)] public bool KeysTaken { get; set; }
    [Key(17)] public int Searches { get; set; }
    [Key(18)] public long Listened { get; set; }
    [Key(19)] public bool Aware { get; set; }

    public static HeistGuardSave Of(HGuard g) => new()
    {
        Person = g.Person, Synthetic = g.Synthetic, Slot = g.Slot, Keeper = g.Keeper, X = g.X, Y = g.Y, HomeX = g.HomeX, HomeY = g.HomeY, State = (byte)g.State, Route = g.Route, Timer = g.Timer, TargetX = g.TargetX, TargetY = g.TargetY,
        Familiarity = g.Familiarity, Honesty = g.Honesty, Keys = [.. g.Keys], KeysTaken = g.KeysTaken, Searches = g.Searches, Listened = g.Listened, Aware = g.Aware,
    };

    public HGuard Restore() => new()
    {
        Person = Person, Synthetic = Synthetic, Slot = Slot, Keeper = Keeper, X = X, Y = Y, HomeX = HomeX, HomeY = HomeY, State = (GState)State, Route = Route, Timer = Timer, TargetX = TargetX, TargetY = TargetY,
        Familiarity = Familiarity, Honesty = Honesty, Keys = [.. Keys], KeysTaken = KeysTaken, Searches = Searches, Listened = Listened, Aware = Aware,
    };
}

[MessagePackObject]
public sealed class HeistRunSave
{
    [Key(0)] public ulong Cur { get; set; }
    [Key(1)] public ulong Partner { get; set; }
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public long Sec { get; set; }
    [Key(5)] public int Weather { get; set; }
    /// <summary>Bits: 1 sneak, 2 torch, 4 running, 8 livery, 16 carrying, 32 moved, 64 picks, 128 pry, 256 flame.</summary>
    [Key(6)] public int Flags { get; set; }
    [Key(7)] public ulong LiveryLord { get; set; }
    [Key(8)] public int Stealth { get; set; }
    [Key(9)] public int Lockpicking { get; set; }
    [Key(10)] public byte Outcome { get; set; }
    [Key(11)] public int[] Busy { get; set; } = [];
    [Key(12)] public ulong[] Seen { get; set; } = [];
    [Key(13)] public bool[] SeenKnew { get; set; } = [];
    [Key(14)] public ulong[] Followers { get; set; } = [];
    [Key(15)] public string[] Taken { get; set; } = [];
    [Key(16)] public long StartSec { get; set; }
    [Key(17)] public long Stolen { get; set; }
    [Key(18)] public long[][] Events { get; set; } = [];

    public static HeistRunSave Of(HeistRun r) => new()
    {
        Cur = r.Cur, Partner = r.Partner, X = r.X, Y = r.Y, Sec = r.Sec, Weather = r.Weather,
        Flags = (r.Sneak ? 1 : 0) | (r.Torch ? 2 : 0) | (r.Running ? 4 : 0) | (r.Livery ? 8 : 0) | (r.Carrying ? 16 : 0) | (r.Moved ? 32 : 0) | (r.HasPicks ? 64 : 0) | (r.HasPry ? 128 : 0) | (r.HasFlame ? 256 : 0),
        LiveryLord = r.LiveryLord, Stealth = r.Stealth, Lockpicking = r.Lockpicking, Outcome = (byte)r.Outcome,
        Busy = [r.BusyKind, r.BusyTarget, r.BusyLeft, r.BusyTotal, r.BusyAttempt],
        Seen = [.. r.Seen.Select(x => x.Guard)], SeenKnew = [.. r.Seen.Select(x => x.Knew)], Followers = [.. r.Followers], Taken = [.. r.Taken], StartSec = r.StartSec, Stolen = r.Stolen,
        Events = [.. r.Events.Select(e => new long[] { (long)e.Kind, e.Sec, (long)e.A, (long)e.B, e.Value })],
    };

    public HeistRun Restore(HeistRules rules, ulong seed, HeistBook b)
    {
        if (Busy.Length < 5 || Seen.Length != SeenKnew.Length) throw new SaveException("Corrupt run in 'heists' section.");
        var r = new HeistRun(rules, seed, b.Sites, b.Keys, Cur, StartSec, Weather) { Partner = Partner, Sec = Sec, X = X, Y = Y, LiveryLord = LiveryLord, Stealth = Stealth, Lockpicking = Lockpicking, Outcome = (RunOutcome)Outcome, Stolen = Stolen };
        r.Events.Clear();
        r.Sneak = (Flags & 1) != 0; r.Torch = (Flags & 2) != 0; r.Running = (Flags & 4) != 0; r.Livery = (Flags & 8) != 0; r.Carrying = (Flags & 16) != 0; r.Moved = (Flags & 32) != 0;
        r.HasPicks = (Flags & 64) != 0; r.HasPry = (Flags & 128) != 0; r.HasFlame = (Flags & 256) != 0;
        r.BusyKind = Busy[0]; r.BusyTarget = Busy[1]; r.BusyLeft = Busy[2]; r.BusyTotal = Busy[3]; r.BusyAttempt = Busy[4];
        for (int i = 0; i < Seen.Length; i++) r.Seen.Add((Seen[i], SeenKnew[i]));
        r.Followers.AddRange(Followers); r.Taken.AddRange(Taken);
        foreach (var e in Events) r.Events.Add(new HeistEvent((HeistEventKind)e[0], e[1], (ulong)e[2], (ulong)e[3], e[4]));
        return r;
    }
}

[MessagePackObject]
public sealed class HeistManhuntSave
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public int Settlement { get; set; }
    [Key(2)] public long StartSec { get; set; }
    [Key(3)] public List<ulong> Persons { get; set; } = [];
    [Key(4)] public ulong Entry { get; set; }
    [Key(5)] public bool Ended { get; set; }
}
