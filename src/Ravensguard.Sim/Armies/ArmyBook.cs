using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Armies;

/// <summary>What a warband is doing (armies-on-the-map 3.1). Append only: the save and the lords' log store the number.</summary>
public enum WarbandTask : byte { Idle, Patrol, Escort, Visit, ChaseBandits, Raid, Garrison, JoinHost, Return, Flee }

/// <summary>Where a band of bandits came from (3.4). Append only.</summary>
public enum BandOrigin : byte { Deserters, Outlaws, Mercenaries }

/// <summary>What a lord sends out of his seat that his warband may escort (4.1). Append only; AM-A sends the season's tax wagon.</summary>
public enum ErrandKind : byte { TaxWagon, Envoy, Bride }

/// <summary>Where an errand is. <c>Waiting</c> at the seat for an escort, <c>Moving</c> on the road, <c>Done</c> when it has arrived.</summary>
public enum ErrandState : byte { Waiting, Moving, Done }

/// <summary>
/// A standing warband (3.1): the men a lord keeps all year, real people, and where they are. <see cref="Here"/> is the settlement they stand in (-1 on the road, then <see cref="From"/> to <see cref="To"/> and
/// <see cref="DistMm"/> along the way, the same road path a caravan walks). Every number is an integer; the record holds no object reference, so it saves as it stands.
/// </summary>
public sealed class Warband
{
    public int Id;
    /// <summary>The lord: a person id value (the hero's is 0, a real lord, so 0 never means nobody here).</summary>
    public ulong Lord;
    public int Fief, Realm, Seat;
    /// <summary>The men, by person id ascending; <see cref="Captain"/> is the best fighter of them (0 none).</summary>
    public List<ulong> Members { get; } = [];
    /// <summary>What a man did before he was hired (an <c>Occupation</c> value), by person id: given back when he is let go. A household guard of a manor keeps his post and is not listed.</summary>
    public SortedDictionary<ulong, byte> Prev { get; } = [];
    public ulong Captain;
    public WarbandTask Task;
    /// <summary>The target of the task: a settlement index (patrol: the loop's head; visit: the lord's seat; chase: the place of the news; raid: the village) or -1; <see cref="TargetId"/> a band, errand or lord id.</summary>
    public int Target = -1;
    public ulong TargetId;
    public int TaskDay = -1000;
    /// <summary>The text key of why the task was taken (a <c>ch_note_am_*</c> key): every task has one.</summary>
    public string Cause = "";
    public int Here = -1, From = -1, To = -1;
    public long DistMm;
    /// <summary>Hours still to spend where it stands (a visit, a search, a raid).</summary>
    public int Dwell;
    /// <summary>The settlements still to reach, in order (a patrol's loop, a way home).</summary>
    public List<int> Stops { get; } = [];
    public int Morale = 70;
    public long ArrearsMites;
    public int RaisedDay, PaidDay = -1, ThoughtDay = -1;
    /// <summary>Whether the men ride (the trot) or walk (the column's pace).</summary>
    public bool Mounted = true;
    /// <summary>The errand being escorted (0 none).</summary>
    public int Escorting;
    /// <summary>Where the task stands: a chase or a raid is 0 on the way and 1 once the search or the raid is done.</summary>
    public int Phase;
    /// <summary>The day each settlement was last ridden through on a patrol, and the day each lord was last visited (by his person id).</summary>
    public SortedDictionary<int, int> Visited { get; } = [];
    public SortedDictionary<ulong, int> Visits { get; } = [];
    /// <summary>The grain (grams) the warband carries home from a raid: in the real baggage store; this is its running count for the chronicle.</summary>
    public long LootG;

    public bool OnRoad => Here < 0;
}

/// <summary>A tax wagon (or envoy, or bride) waiting at a lord's seat or on its way (4.1): the trip the escort is for. It carries no coin or goods of its own: the dues it stands for move in the tax and rule books.</summary>
public sealed class Errand
{
    public int Id;
    public ErrandKind Kind;
    public int Fief, From, To;
    public int DayMade;
    public ErrandState State;
    public long DistMm;
    /// <summary>The warband escorting it (0 none).</summary>
    public int Escort;
    public ulong Entry;
}

/// <summary>
/// A band of bandits (3.4): real people who live off the roads. Deserters far from home, outlaws, a mercenary company gone bad. <see cref="Lair"/> is the settlement it lurks near. The strength is its living members.
/// </summary>
public sealed class BanditBand
{
    public int Id;
    public ulong Leader;
    public List<ulong> Members { get; } = [];
    public SortedDictionary<ulong, byte> Prev { get; } = [];
    public int Lair = -1;
    public BandOrigin Origin;
    public int FormedDay, LastRobDay = -1000;
    /// <summary>The coin (mites) it holds from robberies (loot: hoard purse of the band, found again by owner; 0 when nothing was taken).</summary>
    public long LootMites;
    public int Strength => Members.Count;
}

/// <summary>News of a robbery or a raid, as the lords heard it (4.1, 4.10): where (a settlement), when, which band (0 none) or which realm raided (-1 none) and how strong the news made it. Lords act on these, never on the band's own place.</summary>
public sealed class News
{
    public int Day, Place, Band = 0, Realm = -1, Strength;
}

/// <summary>A line of the warbands' log: who took which task, why (the text key) and where it was on (the lords' log of 4.1: every task has a cause).</summary>
public sealed class TaskLine
{
    public int Day, Warband;
    public WarbandTask Task;
    public int Target;
    public string Cause = "";
}

/// <summary>
/// The armies' state (armies-on-the-map 3), the optional <c>armies</c> section: the standing warbands, the errands they escort, the bandit bands, the news of robberies and raids, and the log of tasks taken. Hosts, levy
/// companies, camps and intel join in later parts as appended keys. Empty (so absent from a save) while no warband, band or errand exists. Kept sorted by id: equal states give equal bytes and equal hashes.
/// </summary>
public sealed class ArmyBook
{
    public SortedDictionary<int, Warband> Warbands { get; } = [];
    public SortedDictionary<int, Errand> Errands { get; } = [];
    public SortedDictionary<int, BanditBand> Bands { get; } = [];
    public List<News> Reports { get; } = [];
    public List<TaskLine> Log { get; } = [];
    public int NextWarband = 1, NextErrand = 1, NextBand = 1;
    /// <summary>Fiefs whose lord was found to have no man to hire in a season: not asked again until the next one (a season number).</summary>
    public SortedDictionary<int, int> Barren { get; } = [];
    public const int LogKeep = 2000, ReportKeep = 64;

    public bool IsEmpty => Warbands.Count == 0 && Errands.Count == 0 && Bands.Count == 0 && Reports.Count == 0 && Log.Count == 0 && Barren.Count == 0 && NextWarband == 1 && NextErrand == 1 && NextBand == 1;

    public Warband? WarbandOfFief(int fief) { foreach (var w in Warbands.Values) if (w.Fief == fief) return w; return null; }
    public Warband? WarbandOfLord(ulong lord) { foreach (var w in Warbands.Values) if (w.Lord == lord) return w; return null; }
    public Warband? WarbandWith(ulong person) { foreach (var w in Warbands.Values) if (w.Members.Contains(person)) return w; return null; }
    public BanditBand? BandWith(ulong person) { foreach (var b in Bands.Values) if (b.Members.Contains(person)) return b; return null; }

    public void Note(int day, int warband, WarbandTask task, int target, string cause)
    {
        Log.Add(new TaskLine { Day = day, Warband = warband, Task = task, Target = target, Cause = cause });
        if (Log.Count > LogKeep) Log.RemoveRange(0, Log.Count - LogKeep);
    }

    public void Hear(int day, int place, int band, int realm, int strength)
    {
        Reports.Add(new News { Day = day, Place = place, Band = band, Realm = realm, Strength = strength });
        if (Reports.Count > ReportKeep) Reports.RemoveRange(0, Reports.Count - ReportKeep);
    }

    private readonly HashSet<ulong> _away = [];
    private bool _dirty = true;

    /// <summary>Says a warband's or band's men changed (every change of a member list calls it): the set <see cref="IsAway"/> reads is rebuilt on its next call.</summary>
    public void Touch() => _dirty = true;

    /// <summary>Whether the person is in a warband or a band of bandits (the farming planner's hook: a man of a retinue or a band is not one of his household's reapers).</summary>
    public bool IsAway(ulong person)
    {
        if (_dirty)
        {
            _away.Clear(); foreach (var w in Warbands.Values) foreach (var m in w.Members) _away.Add(m); foreach (var b in Bands.Values) foreach (var m in b.Members) _away.Add(m);
            _dirty = false;
        }
        return _away.Contains(person);
    }

    public bool InWarband(ulong person) { foreach (var w in Warbands.Values) if (w.Members.BinarySearch(person) >= 0) return true; return false; }

    /// <summary>The <c>ArmyHash</c> of the trail (overview 7): warbands with their men and tasks, errands, bands, news and the log, fixed order, integers only. Zero while empty.</summary>
    public ulong Hash()
    {
        if (IsEmpty) return 0;
        ulong h = 14695981039346656037UL;
        void M(long v) { unchecked { h = (h ^ (ulong)v) * 1099511628211UL; } }
        void S(string s) { foreach (char c in s) M(c); M(-9); }
        M(NextWarband); M(NextErrand); M(NextBand);
        foreach (var (id, w) in Warbands)
        {
            M(id); M((long)w.Lord); M(w.Fief); M(w.Realm); M(w.Seat); M((long)w.Captain); M((long)w.Task); M(w.Target); M((long)w.TargetId); M(w.TaskDay); S(w.Cause);
            M(w.Here); M(w.From); M(w.To); M(w.DistMm); M(w.Dwell); M(w.Morale); M(w.ArrearsMites); M(w.RaisedDay); M(w.PaidDay); M(w.ThoughtDay); M(w.Mounted ? 1 : 0); M(w.Escorting); M(w.LootG); M(w.Phase);
            M(w.Members.Count); foreach (var p in w.Members) M((long)p);
            M(w.Prev.Count); foreach (var (p, o) in w.Prev) { M((long)p); M(o); }
            M(w.Stops.Count); foreach (int s in w.Stops) M(s);
            M(w.Visited.Count); foreach (var (s, d) in w.Visited) { M(s); M(d); }
            M(w.Visits.Count); foreach (var (l, d) in w.Visits) { M((long)l); M(d); }
        }
        M(-1);
        foreach (var (id, e) in Errands) { M(id); M((long)e.Kind); M(e.Fief); M(e.From); M(e.To); M(e.DayMade); M((long)e.State); M(e.DistMm); M(e.Escort); M((long)e.Entry); }
        M(-2);
        foreach (var (id, b) in Bands)
        {
            M(id); M((long)b.Leader); M(b.Lair); M((long)b.Origin); M(b.FormedDay); M(b.LastRobDay); M(b.LootMites);
            M(b.Members.Count); foreach (var p in b.Members) M((long)p);
            M(b.Prev.Count); foreach (var (p, o) in b.Prev) { M((long)p); M(o); }
        }
        M(-3);
        foreach (var r in Reports) { M(r.Day); M(r.Place); M(r.Band); M(r.Realm); M(r.Strength); }
        M(-4);
        foreach (var l in Log) { M(l.Day); M(l.Warband); M((long)l.Task); M(l.Target); S(l.Cause); }
        M(-5);
        foreach (var (f, s) in Barren) { M(f); M(s); }
        return h;
    }
}
