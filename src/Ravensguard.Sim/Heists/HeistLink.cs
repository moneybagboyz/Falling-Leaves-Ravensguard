using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Heists;

/// <summary>
/// The heist's hook on the running world's hours (heists-and-jailbreaks 4.5, 4.6): at the first hour of each day a lord whose keys were stolen has his locks changed (within two days, a locksmith's job: every key cut for the old locks stops
/// working), and a manhunt that has searched its day gives up (the bounty stays). Nothing runs while no site has been visited.
/// </summary>
public sealed class HeistLink
{
    public WorldRuntime Runtime { get; }
    private HeistLink(WorldRuntime rt) { Runtime = rt; }

    public static HeistLink Attach(WorldRuntime rt)
    {
        var l = new HeistLink(rt);
        rt.AfterHour += l.OnHour;
        return l;
    }

    private void OnHour(long hour)
    {
        var st = Runtime.State;
        if (st.Heists is not { } hb || hb.IsEmpty) return;
        if (st.Population.People.Count == 0 || st.Economy == null) return;
        var w = LawWorld.From(st, st.Content);
        Hourly(w, hb, HeistRules.From(st.Content), hour);
        if (hb.IsEmpty) st.Heists = null;
    }

    /// <summary>The hour's work: locks changed on the day due, manhunts ended when their day is spent.</summary>
    public static void Hourly(LawWorld w, HeistBook hb, HeistRules rules, long hour)
    {
        long sec = hour * 3600; int day = (int)(hour / 24);
        if (hour % 24 == 0)
            foreach (var s in hb.Sites.Values)
                if (s.ChangeDay >= 0 && day >= s.ChangeDay) ChangeLocks(w, s, sec);
        foreach (var m in hb.Manhunts)
            if (!m.Ended && !m.Active(rules, sec)) EndManhunt(w, m, sec);
        hb.LastHour = hour;
    }

    /// <summary>The lord has the locks changed: the generation goes up, and every key cut for the old one is dead iron (heists 4.3; open question 1).</summary>
    public static void ChangeLocks(LawWorld w, HSite s, long sec)
    {
        s.LockGen++; s.ChangeDay = -1;
        var ps = new List<ChronParam>(); if (w.PlaceId(s.Settlement) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(s.Settlement)));
        var cause = s.ChangeCause != 0 ? new Cause(CauseKind.Crime, new EntityId(s.ChangeCause)) : new Cause(CauseKind.Crime, null, NoteKey: "ch_note_heist_keys_taken");
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "locks_changed", [], [cause], new Dictionary<string, string> { ["settlement"] = w.PlaceName(s.Settlement), ["kind"] = s.Kind.ToString() },
            TextKey: "ch_heist_locks_changed", Params: ps));
        s.ChangeCause = 0;
    }

    /// <summary>A run is over: the guards go off and the doors lock (a broken one stays); the locks will be changed within two days if keys were stolen.</summary>
    public static void Closed(HeistBook hb, HeistRun run, long sec) => HeistSites.Close(hb, run.R, sec);

    /// <summary>The fief's guards begin a manhunt from the gaol: a radius growing 1 km an hour for a game day, the persons sought, the entry that explains it hung on the crime that was seen.</summary>
    public static Manhunt BeginManhunt(LawWorld w, HeistBook hb, int seat, long sec, IEnumerable<ulong> persons, ulong cause)
    {
        var m = new Manhunt { Id = hb.NextManhunt++, Settlement = seat, StartSec = sec, Persons = [.. persons] };
        var ps = new List<ChronParam> { ChronParam.Person("doer", new EntityId(m.Persons[0])), ChronParam.Count("hunted", m.Persons.Count) };
        if (w.PlaceId(seat) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(seat)));
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "manhunt_began", [new EntityId(m.Persons[0])], [new Cause(CauseKind.Crime, new EntityId(cause))],
            new Dictionary<string, string> { ["settlement"] = w.PlaceName(seat), ["doer"] = w.Name(new EntityId(m.Persons[0])), ["count"] = m.Persons.Count.ToString(CultureInfo.InvariantCulture) }, TextKey: "ch_heist_manhunt_began", Params: ps);
        w.Chronicle.Add(e); m.Entry = e.Id.Value;
        hb.Manhunts.Add(m);
        return m;
    }

    public static void EndManhunt(LawWorld w, Manhunt m, long sec)
    {
        m.Ended = true;
        var ps = new List<ChronParam> { ChronParam.Person("doer", new EntityId(m.Persons[0])) };
        if (w.PlaceId(m.Settlement) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(m.Settlement)));
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "manhunt_ended", [new EntityId(m.Persons[0])], [new Cause(CauseKind.Crime, new EntityId(m.Entry))],
            new Dictionary<string, string> { ["settlement"] = w.PlaceName(m.Settlement) }, TextKey: "ch_heist_manhunt_ended", Params: ps));
    }

    /// <summary>
    /// A guard on a road looks at a passer-by while a manhunt is on (crime 4.1: a described person is matched by the clothes): true when the guard is within the search radius of the gaol (<paramref name="distFromGaolM"/>) and the person is one of the
    /// hunted. The live tier calls it for every guard who sees a passer-by; the abstract tier uses the radius only.
    /// </summary>
    public static bool Seeks(HeistBook hb, HeistRules rules, ulong person, int distFromGaolM, long sec)
    {
        foreach (var m in hb.Manhunts)
            if (!m.Ended && m.Active(rules, sec) && m.Persons.Contains(person) && distFromGaolM <= m.RadiusM(rules, sec) + 1000) return true;
        return false;
    }
}
