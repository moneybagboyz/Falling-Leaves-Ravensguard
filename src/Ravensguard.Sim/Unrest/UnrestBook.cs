using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Unrest;

/// <summary>What a crowd goes for (unrest-and-revolt 4.2): the lord's barn, the reeve and the chest, the manor, the gaol. Append only.</summary>
public enum RiotTarget : byte { Barn, Reeve, Manor, Gaol }

/// <summary>Where a riot stands: gathering until the gather hour, acting, over. Append only.</summary>
public enum RiotState : byte { Gathering, Active, Ended }

/// <summary>How a riot ended. Append only.</summary>
public enum RiotEnd : byte { None, Dispersed, Conceded, BurnedOut }

/// <summary>Where a revolt stands: raised and waiting for its day, won by the rebels, lost. Append only.</summary>
public enum RevoltState : byte { Pending, Won, Lost }

/// <summary>An event's grievance on a household (unrest-and-revolt 3): its kind (a <c>grievance_kind</c> id), the points, the day it lapses and the chronicle entry of the event that raised it (0 none).</summary>
public sealed class GrievanceRow
{
    public string Kind = "";
    public int Points, UntilDay;
    public ulong Entry;
}

/// <summary>A settlement's place on the ladder (unrest-and-revolt 3, 4.1): the unrest in permille at the last daily pass, the rung, the days it has stood at the riot rung and at the revolt rung, a trigger waiting, the cooldown after a riot and the fear after a lost revolt.</summary>
public sealed class SettlementUnrest
{
    public int Unrest, Rung, HighDays, TopDays;
    /// <summary>The day a trigger (an eviction, a hanging of a local, a tallage) was struck (-1 none) and its kind key.</summary>
    public int TriggerDay = -1;
    public string TriggerKind = "";
    public int CooldownUntil, FearUntil;
    /// <summary>The last season index the grumbling cost the lord his standing in.</summary>
    public int GrumbleSeason = -1;
    public bool IsDefault => Unrest == 0 && Rung == 0 && HighDays == 0 && TopDays == 0 && TriggerDay < 0 && CooldownUntil == 0 && FearUntil == 0 && GrumbleSeason < 0;
}

/// <summary>A riot of real people (unrest-and-revolt 4.2): its leader, its crowd, what it goes for and why, and how it ended.</summary>
public sealed class Riot
{
    public int Id, Settlement, Fief;
    public ulong Leader;
    public int StartDay;
    public RiotTarget Target;
    public RiotState State;
    public RiotEnd End;
    /// <summary>The crowd's people in ascending id, the leader among them.</summary>
    public List<ulong> Crowd = [];
    /// <summary>The grievance counts among the aggrieved at the start, largest first (ties by kind id): the chronicle lists them.</summary>
    public List<(string Kind, int Count)> Counts = [];
    /// <summary>The grievance kind that sent the crowd.</summary>
    public string TopKind = "";
    public ulong RiseEntry, EndEntry;
    public int Deaths, UnrestPm;
    /// <summary>The hour (total game hours) it began to act (-1 not yet) and the hour it ended.</summary>
    public long ActedHour = -1, EndedHour = -1;
    public bool Conceded;
    /// <summary>The hero's side: 0 none, 1 joined, 2 leads, 3 stands with the guards, 4 mediated.</summary>
    public byte HeroSide;
    public long LootMites, LootGrams;
    /// <summary>A slave rising (the bonded are the rebels; no claimant) when a bond holder is the cause.</summary>
    public bool Bonded;
}

/// <summary>A revolt (unrest-and-revolt 4.3): raised against a fief's lord with a claimant, to be fought at the seat on its day.</summary>
public sealed class Revolt
{
    public int Id, Fief, Settlement, StartDay, ResolveDay, RiotId;
    public RevoltState State;
    /// <summary>The claimant (a rival lord's or the riot leader's person id; 0 none for a slave rising) and what he is: 0 none, 1 a rival lord, 2 the riot's leader.</summary>
    public ulong Claimant;
    public byte ClaimantKind;
    public ulong OldLord, Leader;
    /// <summary>The rebels' people, ascending (the aggrieved men the levy rule picks, or the bonded).</summary>
    public List<ulong> Rebels = [];
    public int Defenders;
    public bool Bonded;
    public ulong RiseEntry, EndEntry;
    public int Rebels0, Dead;
}

/// <summary>
/// Unrest and revolt of the home region (unrest-and-revolt 3, the optional "unrest" save section, no format bump): the grievances events left on households, each settlement's place on the ladder, the riots and the revolts. The
/// grievances of condition (hunger, the tax level, crowding, crime, the lord's standing) are read live each day and never stored: only what an event raised is a row. No random draw lives here; the engine draws with the
/// registered streams (0x680 to 0x682) and writes the results in.
/// </summary>
public sealed class UnrestBook
{
    /// <summary>Event grievances by household id value, each household's rows in the order added.</summary>
    public readonly SortedDictionary<ulong, List<GrievanceRow>> Rows = [];
    public readonly SortedDictionary<int, SettlementUnrest> Settlements = [];
    public readonly List<Riot> Riots = [];
    public readonly List<Revolt> Revolts = [];
    /// <summary>Fief -> the day a grain dole holds its hunger points down until (the barn feeds the hungry).</summary>
    public readonly SortedDictionary<int, int> DoleUntil = [];
    /// <summary>People pardoned after a riot (their kin's points cleared and their crime dropped).</summary>
    public readonly SortedSet<ulong> Pardoned = [];
    public int NextRiot = 1, NextRevolt = 1, LastDay = -1;

    // ---- not saved: bound when the book is attached
    public UnrestSettingsDef Settings { get; set; } = new() { Id = "unrest" };
    public IReadOnlyDictionary<string, GrievanceKindDef> Kinds { get; set; } = new Dictionary<string, GrievanceKindDef>();
    public IReadOnlyList<GrievanceKindDef> KindList { get; set; } = [];
    public ulong Seed { get; set; }

    public bool IsEmpty => Rows.Count == 0 && Settlements.Count == 0 && Riots.Count == 0 && Revolts.Count == 0 && DoleUntil.Count == 0 && Pardoned.Count == 0 && NextRiot == 1 && NextRevolt == 1 && LastDay < 0;

    public SettlementUnrest Of(int settlement)
    {
        if (!Settlements.TryGetValue(settlement, out var s)) Settlements[settlement] = s = new SettlementUnrest();
        return s;
    }
    public SettlementUnrest? Peek(int settlement) => Settlements.GetValueOrDefault(settlement);
    public int RungOf(int settlement) => Settlements.TryGetValue(settlement, out var s) ? s.Rung : 0;
    public int UnrestOf(int settlement) => Settlements.TryGetValue(settlement, out var s) ? s.Unrest : 0;
    public Riot? RiotById(int id) { foreach (var r in Riots) if (r.Id == id) return r; return null; }
    public Revolt? RevoltById(int id) { foreach (var r in Revolts) if (r.Id == id) return r; return null; }
    public Riot? ActiveRiot(int settlement) { foreach (var r in Riots) if (r.Settlement == settlement && r.State != RiotState.Ended) return r; return null; }
    public IEnumerable<Riot> ActiveRiots() { foreach (var r in Riots) if (r.State != RiotState.Ended) yield return r; }

    /// <summary>Adds an event's grievance to a household (kind from the content; points and days default to the kind's). A repeat of the same kind refreshes the row rather than stacking.</summary>
    public void Add(ulong household, string kind, int day, ulong entry = 0, int points = -1, int days = -1)
    {
        if (household == 0 || !Kinds.TryGetValue(kind, out var def)) return;
        int pts = points >= 0 ? points : def.Points, d = days >= 0 ? days : def.Days; if (pts <= 0) return;
        if (!Rows.TryGetValue(household, out var list)) Rows[household] = list = [];
        foreach (var r in list) if (r.Kind == kind) { r.Points = Math.Max(r.Points, pts); r.UntilDay = Math.Max(r.UntilDay, day + Math.Max(1, d)); if (entry != 0) r.Entry = entry; return; }
        list.Add(new GrievanceRow { Kind = kind, Points = pts, UntilDay = day + Math.Max(1, d), Entry = entry });
    }

    /// <summary>The live rows of a household on a day.</summary>
    public IEnumerable<GrievanceRow> Live(ulong household, int day)
    {
        if (!Rows.TryGetValue(household, out var list)) yield break;
        foreach (var r in list) if (r.UntilDay > day) yield return r;
    }

    /// <summary>Drops rows that have lapsed (the daily pass), and households left with none.</summary>
    public void Prune(int day)
    {
        foreach (var k in Rows.Keys.ToList())
        {
            var l = Rows[k]; l.RemoveAll(r => r.UntilDay <= day);
            if (l.Count == 0) Rows.Remove(k);
        }
        foreach (var k in Settlements.Keys.ToList()) if (Settlements[k].IsDefault) Settlements.Remove(k);
    }

    /// <summary>The <c>UnrestHash</c> of the daily trail (unrest-and-revolt 5): grievances, rungs, riots and revolts in ascending order.</summary>
    public ulong Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(long v) => h = DetRng.Mix(h ^ ((ulong)v + 0x9E3779B97F4A7C15UL));
        void S(string s) { foreach (char c in s) M(c); M(-1); }
        foreach (var (k, l) in Rows) { M((long)k); foreach (var r in l) { S(r.Kind); M(r.Points); M(r.UntilDay); M((long)r.Entry); } }
        foreach (var (k, s) in Settlements) { M(k); M(s.Unrest); M(s.Rung); M(s.HighDays); M(s.TopDays); M(s.TriggerDay); S(s.TriggerKind); M(s.CooldownUntil); M(s.FearUntil); M(s.GrumbleSeason); }
        foreach (var r in Riots)
        {
            M(r.Id); M(r.Settlement); M(r.Fief); M((long)r.Leader); M(r.StartDay); M((long)r.Target); M((long)r.State); M((long)r.End); foreach (var p in r.Crowd) M((long)p);
            foreach (var (k, n) in r.Counts) { S(k); M(n); }
            S(r.TopKind); M((long)r.RiseEntry); M((long)r.EndEntry); M(r.Deaths); M(r.UnrestPm); M(r.ActedHour); M(r.EndedHour); M(r.Conceded ? 1 : 0); M(r.HeroSide); M(r.LootMites); M(r.LootGrams); M(r.Bonded ? 1 : 0);
        }
        foreach (var v in Revolts)
        {
            M(v.Id); M(v.Fief); M(v.Settlement); M(v.StartDay); M(v.ResolveDay); M(v.RiotId); M((long)v.State); M((long)v.Claimant); M(v.ClaimantKind); M((long)v.OldLord); M((long)v.Leader);
            foreach (var p in v.Rebels) M((long)p);
            M(v.Defenders); M(v.Bonded ? 1 : 0); M((long)v.RiseEntry); M((long)v.EndEntry); M(v.Rebels0); M(v.Dead);
        }
        foreach (var (k, d) in DoleUntil) { M(k); M(d); }
        foreach (var p in Pardoned) M((long)p);
        M(NextRiot); M(NextRevolt); M(LastDay);
        return h;
    }
}
