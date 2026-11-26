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

/// <summary>What a host is doing (armies-on-the-map 3.2). Append only. AM-B uses <c>Summoned</c> (the call is out, nobody drawn yet), <c>Mustering</c> (the levies are drawn and walking to the muster point), <c>Camped</c> (formed
/// at the point), <c>Besieging</c> (set by the siege; a host besieging stays through winter) and <c>Disbanding</c> (released: the men walk home); AM-C adds the march.</summary>
public enum HostState : byte { Summoned, Mustering, Marching, Camped, Besieging, Engaged, Retreating, Disbanding, WinterQuarters }

/// <summary>What a contingent of a host is (3.2). Append only. AM-B: <c>Levy</c> (a fief's drawn men), <c>Warband</c> (the ruler's own) and <c>Vassal</c> (a serving vassal's).</summary>
public enum ContingentKind : byte { Warband, Levy, Vassal, Mercenary, Allied }

/// <summary>
/// A fief's levy company (3.3): the men one fief gave a host, real persons drawn from their households by <c>HostSupply.Muster</c> (the village draw of overview 10d). <see cref="Heard"/> is the day the summons reached the fief, <see cref="Drawn"/>
/// the day the men were drawn and stood at the seat, <see cref="Arrives"/> the day the column reaches the muster point; <see cref="HomeDay"/> the day the released men are home (they are away from their fields until then).
/// </summary>
public sealed class LevyCompany
{
    public int Fief, Seat, Quota, Heard = -1, Drawn = -1, Arrives = -1, HomeDay = -1, Evaded, Asked;
    /// <summary>Whether the men have been released and are walking home (their labour returns on <see cref="HomeDay"/>).</summary>
    public bool Walking;
    public ulong Captain;
    public ContingentKind Kind = ContingentKind.Levy;
    /// <summary>The men by person id ascending.</summary>
    public List<ulong> Men { get; } = [];
    public bool IsDrawn => Drawn >= 0;
}

/// <summary>A warband that joined a host (a ruler's own or a serving vassal's) and the men it brought.</summary>
public sealed record WarbandJoin(int Warband, ContingentKind Kind, int Men);

/// <summary>
/// A seasonal host (3.2): the levy of a realm called to a muster point by its ruler, with the warbands that joined. <see cref="Supply"/> is its account in the army economy's <c>HostSupply</c> (0 until the first men are drawn).
/// <see cref="Worth"/> is what the ruler thinks the rest of the campaign is worth in mites, which he weighs against the harvest the levies' households would lose and the pay of the days past the owed ones; a call with no campaign behind it
/// is worth nothing, so the levy goes home for the harvest.
/// </summary>
public sealed class ArmyHost
{
    public int Id, Realm, War = -1, Point, Supply, SummonsDay, FormedDay = -1, FirstArrival = -1, ReleasedDay = -1, Expected, DecidedYear = -1, FundDays;
    public ulong Commander, Entry;
    public long Worth;
    public bool Paying, Raises;
    public HostState State;
    public List<LevyCompany> Companies { get; } = [];
    public List<WarbandJoin> Warbands { get; } = [];
    /// <summary>Levies serving (drawn and not released) and released men still walking home.</summary>
    public int Levies { get { int n = 0; foreach (var c in Companies) if (!c.Walking) n += c.Men.Count; return n; } }
    public int Walkers { get { int n = 0; foreach (var c in Companies) if (c.Walking) n += c.Men.Count; return n; } }
}

/// <summary>
/// P7-AM-D: what the military keeps of a host's belly and heart, by the host's id in the supply book (so a supply-only host of the war-load soak has one too): the morale, the run of short days, the day of the last defeat, the
/// ground the foragers have stripped and for how long, the wounded the wagons carry (person to the day he is fit) and what the desertion has cost, persons and counts. All integers, saved in the armies section's key 13.
/// </summary>
public sealed class HostRations
{
    public int Host, Morale = 60, HungryRun, FedPm = 1000, DefeatDay = -1000, StripPlace = -1, StripDays;
    public long HungryDays, DesertedPersons, DesertedCounts, ShortDays;
    public SortedDictionary<ulong, int> Wounded { get; } = [];
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
    /// <summary>P7-AM-C: the hosts on the road, by the host's id in the supply book (a host with no march is standing where its account says).</summary>
    public SortedDictionary<int, HostMarch> Marches { get; } = [];
    public List<News> Reports { get; } = [];
    public List<TaskLine> Log { get; } = [];
    public int NextWarband = 1, NextErrand = 1, NextBand = 1;
    /// <summary>P7-AM-B: the seasonal hosts by id, and the next id.</summary>
    public SortedDictionary<int, ArmyHost> Hosts { get; } = [];
    public int NextHost = 1;
    /// <summary>P7-AM-D: the hosts' bellies and hearts, by the supply book's host id.</summary>
    public SortedDictionary<int, HostRations> Rations { get; } = [];
    /// <summary>Fiefs whose lord was found to have no man to hire in a season: not asked again until the next one (a season number).</summary>
    public SortedDictionary<int, int> Barren { get; } = [];
    public const int LogKeep = 2000, ReportKeep = 64;

    public bool IsEmpty => Warbands.Count == 0 && Errands.Count == 0 && Bands.Count == 0 && Reports.Count == 0 && Log.Count == 0 && Barren.Count == 0 && NextWarband == 1 && NextErrand == 1 && NextBand == 1 && Hosts.Count == 0 && Marches.Count == 0 && NextHost == 1 && Rations.Count == 0;

    public Warband? WarbandOfFief(int fief) { foreach (var w in Warbands.Values) if (w.Fief == fief) return w; return null; }
    public Warband? WarbandOfLord(ulong lord) { foreach (var w in Warbands.Values) if (w.Lord == lord) return w; return null; }
    public Warband? WarbandWith(ulong person) { foreach (var w in Warbands.Values) if (w.Members.Contains(person)) return w; return null; }
    public ArmyHost? HostOfWarband(int warband) { foreach (var h in Hosts.Values) foreach (var j in h.Warbands) if (j.Warband == warband) return h; return null; }
    public ArmyHost? HostOfRealm(int realm) { foreach (var h in Hosts.Values) if (h.Realm == realm && h.State != HostState.Disbanding) return h; return null; }
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
            foreach (var h in Hosts.Values) foreach (var c in h.Companies) if (c.Walking) foreach (var m in c.Men) _away.Add(m);        // a released levy is off his fields until he is home
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
        if (Hosts.Count > 0 || NextHost != 1)
        {
            M(-6); M(NextHost);
            foreach (var (id, x) in Hosts)
            {
                M(id); M(x.Realm); M(x.War); M(x.Point); M(x.Supply); M(x.SummonsDay); M(x.FormedDay); M(x.FirstArrival); M(x.Expected); M(x.DecidedYear); M(x.FundDays); M((long)x.Commander); M((long)x.Entry); M(x.Worth);
                M(x.Paying ? 1 : 0); M(x.Raises ? 1 : 0); M((long)x.State); M(x.ReleasedDay);
                M(x.Companies.Count);
                foreach (var c in x.Companies) { M(c.Fief); M(c.Seat); M(c.Quota); M(c.Heard); M(c.Drawn); M(c.Arrives); M(c.HomeDay); M(c.Evaded); M(c.Asked); M(c.Walking ? 1 : 0); M((long)c.Captain); M((long)c.Kind); M(c.Men.Count); foreach (var p in c.Men) M((long)p); }
                M(x.Warbands.Count); foreach (var j in x.Warbands) { M(j.Warband); M((long)j.Kind); M(j.Men); }
            }
        }
        if (Marches.Count > 0)                                                            // P7-AM-C: absent while no host marches, so the trail of a world without one keeps its bytes
        {
            M(-7);
            foreach (var (id, m) in Marches)
            {
                M(id); M((long)m.State); M(m.DistMm); M(m.RateHourMm); M(m.HourSec); M(m.PrevDistMm); M(m.PrevRateHourMm); M(m.PrevHourSec); M(m.CampsPitched); M(m.StartDay); M(m.Hours); M(m.MarchHours); S(m.Cause); M(m.InRegion ? 1 : 0); M(m.Hold ? 1 : 0); M((long)m.ChronEntry);
                M(m.Stops.Count); foreach (int s in m.Stops) M(s);
                M(m.Outsiders.Count); foreach (var (p, r) in m.Outsiders) { M((long)p); M(r); }
                if (m.Camp is { } c) { M(1); M(c.XMm); M(c.YMm); M(c.Near); M(c.HeadX); M(c.HeadY); M(c.SinceSec); M(c.Serial); M(c.Men); M(c.Nights); M(c.Watch); M((c.Palisade ? 1 : 0) | (c.Latrine ? 2 : 0) | (c.WaterCarried ? 4 : 0) | (c.BelowTown ? 8 : 0)); M(c.Pieces); M(c.Sentries); } else M(0);
            }
        }
        if (Rations.Count > 0)                                                            // P7-AM-D: absent while no host has been fed and rolled
        {
            M(-8);
            foreach (var (id, r) in Rations)
            {
                M(id); M(r.Morale); M(r.HungryRun); M(r.FedPm); M(r.DefeatDay); M(r.StripPlace); M(r.StripDays); M(r.HungryDays); M(r.DesertedPersons); M(r.DesertedCounts); M(r.ShortDays);
                M(r.Wounded.Count); foreach (var (p, d) in r.Wounded) { M((long)p); M(d); }
            }
        }
        return h;
    }
}
