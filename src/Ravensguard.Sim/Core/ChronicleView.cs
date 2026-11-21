using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Core;

/// <summary>The four views of the Chronicle (H) screen (chronicle-full 4.4).</summary>
public enum ChronicleScope : byte { MyLife, Here, Realm, Legends }

/// <summary>
/// The families the Chronicle's filter bar offers (chronicle-full 4.4): crime, war, faith, family, economy, disaster. An entry's family is read from its first cause's kind, so a type a later part adds
/// (E3 family politics, F's history v1) is filtered with no table to edit; an entry whose cause kind maps to none shows only under "all".
/// </summary>
public static class ChronicleFamilies
{
    public static readonly string[] All = ["crime", "war", "faith", "family", "economy", "disaster"];

    public static string Of(ChronicleEntry e)
    {
        foreach (var c in e.Causes)
        {
            string f = OfKind(c.Kind);
            if (f.Length > 0) return f;
        }
        return "";
    }

    public static string OfKind(CauseKind k) => k switch
    {
        CauseKind.Crime or CauseKind.Justice or CauseKind.Law or CauseKind.Slavery or CauseKind.Unrest => "crime",
        CauseKind.War or CauseKind.Battle or CauseKind.Raid or CauseKind.Diplomacy => "war",
        CauseKind.Faith => "faith",
        CauseKind.Family or CauseKind.Death or CauseKind.Office => "family",
        CauseKind.Tax or CauseKind.Trade or CauseKind.MarketDrift or CauseKind.Migration => "economy",
        CauseKind.Fire or CauseKind.Weather or CauseKind.Disease or CauseKind.Illness or CauseKind.Famine or CauseKind.MineFlood or CauseKind.Wound => "disaster",
        _ => "",
    };
}

/// <summary>
/// What the Chronicle screen reads, built at a barrier so the UI never touches live state (overview 2: no game state in Godot nodes): a snapshot of the region's entries (the game so far), the
/// history they continue (the Legends view and the far end of many a chain), the ids that name things, and who counts as "mine", "here" and "the realm's".
/// </summary>
public sealed class ChronicleView
{
    public required IReadOnlyList<ChronicleEntry> Entries { get; init; }
    public required Chronicle History { get; init; }
    /// <summary>Names by id for every id the entries mention (people, places, businesses, households, history notables and realms).</summary>
    public required IReadOnlyDictionary<ulong, string> Names { get; init; }
    /// <summary>The string ids of the goods the entries name, by catalogue index.</summary>
    public required IReadOnlyDictionary<int, string> Goods { get; init; }
    public required IReadOnlySet<ulong> Mine { get; init; }
    public required IReadOnlySet<ulong> Here { get; init; }
    public required IReadOnlySet<ulong> Realm { get; init; }
    public EntityId Hero { get; init; }
    public string HereName { get; init; } = "";
    public string RealmName { get; init; } = "";
    /// <summary>The time the snapshot was taken, seconds.</summary>
    public long NowSec { get; init; }

    /// <summary>The hero, his household, his spouse, parents and children (ids of people and of the household): whose entries are "mine".</summary>
    public static HashSet<ulong> MineOf(WorldState st)
    {
        var pt = st.Population.People; var hht = st.Population.Households;
        var mine = new HashSet<ulong>();
        int heroIdx = st.Hero != null ? st.Hero.PersonIndex : -1;
        if (heroIdx < 0) return mine;
        mine.Add(pt.IdOf(heroIdx).Value);
        int hh = pt.Household[heroIdx];
        if (hh >= 0) { mine.Add(hht.IdOf(hh).Value); foreach (int m in hht.Members(hh)) mine.Add(pt.IdOf(m).Value); }
        foreach (int k in new[] { pt.Spouse[heroIdx], pt.Father[heroIdx], pt.Mother[heroIdx] }) if (k >= 0) mine.Add(pt.IdOf(k).Value);
        for (int i = 0; i < pt.Count; i++) if (pt.Father[i] == heroIdx || pt.Mother[i] == heroIdx) mine.Add(pt.IdOf(i).Value);
        return mine;
    }

    public string NameOf(ulong id) => Names.TryGetValue(id, out var n) ? n : "";

    /// <summary>
    /// The snapshot at a barrier: <paramref name="heroSettlement"/> is the settlement index the hero stands in (-1 for none). Mine is the hero, his household, spouse, parents and children;
    /// Here is the settlement and its fief; the realm's is the realm, its fiefs and their settlements.
    /// </summary>
    public static ChronicleView Of(WorldState st, int heroSettlement)
    {
        var all = st.Chronicle.All.ToArray();
        var pop = st.Population; var pt = pop.People; var region = pop.Region; var hht = pop.Households;
        var names = new Dictionary<ulong, string>(); var goods = new Dictionary<int, string>();
        var mine = MineOf(st);
        int heroIdx = st.Hero != null ? st.Hero.PersonIndex : -1;
        var heroId = heroIdx >= 0 ? pt.IdOf(heroIdx) : EntityId.None;
        // here and the realm
        var here = new HashSet<ulong>(); var realm = new HashSet<ulong>(); string hereName = "", realmName = "";
        int fief = heroSettlement >= 0 && heroSettlement < region.Settlements.Count ? region.Settlements[heroSettlement].FiefIndex : -1;
        if (heroSettlement >= 0 && heroSettlement < region.Settlements.Count) { here.Add(region.Settlements[heroSettlement].Id.Value); hereName = region.Settlements[heroSettlement].Name; }
        if (fief >= 0 && fief < region.Fiefs.Count)
        {
            here.Add(region.Fiefs[fief].Id.Value);
            int r = region.Fiefs[fief].Realm;
            if (r >= 0 && r < st.History.Realms.Count) { realm.Add(st.History.Realms[r].Id.Value); realmName = st.History.Realms[r].Name; }
            foreach (var f in region.Fiefs.Where(f => f.Realm == r)) { realm.Add(f.Id.Value); foreach (int s in f.Settlements) realm.Add(region.Settlements[s].Id.Value); }
        }
        // names for every id the entries mention
        void Name(ulong v)
        {
            if (v == 0 || names.ContainsKey(v)) return;
            var id = new EntityId(v); string n = "";
            switch (id.Kind)
            {
                case IdKind.Person: { int i = pt.IndexOf(id); n = i >= 0 ? pt.FullName(i) : st.History.GetNotable(id)?.Full ?? ""; break; }
                case IdKind.Household: { int i = hht.IndexOf(id); n = i >= 0 ? hht.Name[i] : ""; break; }
                case IdKind.Settlement:
                    n = st.History.GetSettlement(id)?.Name ?? "";
                    if (n.Length == 0) foreach (var s in region.Settlements) if (s.Id == id) { n = s.Name; break; }
                    if (n.Length == 0) foreach (var f in region.Fiefs) if (f.Id == id) { n = f.Name; break; }
                    if (n.Length == 0) n = st.History.Realms.FirstOrDefault(r => r.Id == id)?.Name ?? "";
                    break;
                default:
                    n = st.History.GetDynasty(id)?.Name ?? st.History.GetArtefact(id)?.Name ?? "";
                    break;
            }
            if (n.Length > 0) names[v] = n;
        }
        void Ps(IReadOnlyList<ChronParam>? ps)
        {
            if (ps == null) return;
            foreach (var p in ps)
            {
                if (p.Kind is ChronParamKind.Person or ChronParamKind.Place or ChronParamKind.Item or ChronParamKind.Entry) Name((ulong)p.Value);
                else if (p.Kind == ChronParamKind.Good && st.Economy?.Goods is { } gc && p.Value >= 0 && p.Value < gc.Count) goods[(int)p.Value] = gc.Def((ushort)p.Value).Name;
            }
        }
        // the hero's person id is 0 (EntityId.None by value), which Name() skips as "nobody": name him here so his own deeds read with his name, not "someone" (P6 close-out G1)
        if (st.Hero != null)
        {
            string hn = heroIdx >= 0 ? pt.FullName(heroIdx) : "";
            if (hn.Length == 0) hn = st.Hero!.Name;
            if (hn.Length > 0) names[heroId.Value] = hn;
        }
        foreach (var e in all)
        {
            foreach (var s in e.Subjects) Name(s.Value);
            Ps(e.Params);
            foreach (var c in e.Causes) Ps(c.NoteParams);
        }
        return new ChronicleView
        {
            Entries = all, History = st.History.Chronicle, Names = names, Goods = goods, Mine = mine, Here = here, Realm = realm, Hero = heroId,
            HereName = hereName, RealmName = realmName, NowSec = st.Clock.Now.Seconds,
        };
    }
}
