using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Superstition per settlement, 0 to 100 (faith-and-church 4.7, P6-E2): it starts from what the region holds (a monster lair within 10 km +30, a barrow +15) and rises by rumours of monsters (+10 for each heard by half the
/// settlement), falling 5 a season otherwise back toward that base. From 40 villagers avoid the night roads and the barrow and buy charms; from 70 a bad harvest or a death can start a witch hunt (a scapegoat accused;
/// the bishop may stop it, stream 0x654). Showing a slain monster's head takes 40 off.
/// </summary>
public static class Superstition
{
    private const int AdultAge = 16;

    /// <summary>What a settlement starts from: the lairs and barrows within the setting's reach of its centre.</summary>
    public static int BaseOf(FaithOps.Pass p, int settlement)
    {
        if (settlement < 0 || settlement >= p.Region.Settlements.Count) return 0;
        var s = p.Region.Settlements[settlement]; long reach = p.S.LairKm * 1000L, r2 = reach * reach;
        bool lair = false, barrow = false;
        foreach (var site in p.Region.Sites)
        {
            long dx = site.X - s.CenterX, dy = site.Y - s.CenterY; if (dx * dx + dy * dy > r2) continue;
            if (site.MonsterLair) lair = true;
            if (site.Kind == SiteKind.Barrow) barrow = true;
        }
        return Math.Clamp((lair ? p.S.LairPoints : 0) + (barrow ? p.S.BarrowPoints : 0), 0, 100);
    }

    public static int Of(FaithOps.Pass p, int settlement) => p.Pol.Superstition.TryGetValue(settlement, out byte v) ? v : BaseOf(p, settlement);

    /// <summary>Sets the level (0..100); a value at its base is not kept.</summary>
    public static int Set(FaithOps.Pass p, int settlement, int value)
    {
        value = Math.Clamp(value, 0, 100);
        if (value == BaseOf(p, settlement)) p.Pol.Superstition.Remove(settlement); else p.Pol.Superstition[settlement] = (byte)value;
        return value;
    }

    public static int Add(FaithOps.Pass p, int settlement, int delta) => Set(p, settlement, Of(p, settlement) + delta);

    /// <summary>Whether villagers keep off the night roads and the barrow at this level (schedules read it).</summary>
    public static bool AvoidsNight(FaithSettingsDef s, int level) => level >= s.SuperstitionNight;

    // ============================================================ the day: rumours of monsters

    /// <summary>A rumour counts as a monster rumour when it is an omen of a lair's legend or a sighting (kind <c>Omen</c>, the entry types <c>lair_legend</c> and <c>monster_sighted</c>).</summary>
    public static bool IsMonsterRumour(Rumour r) => r.Kind == RumourKind.Omen && (r.TypeId is "lair_legend" or "monster_sighted" or "");

    /// <summary>
    /// The day's rumours (4.7): a monster rumour that half of a settlement's people have heard raises its superstition by 10, once for that rumour. Returns the points added in all.
    /// </summary>
    public static int Daily(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); var talk = st.Talk; if (p == null || talk == null) return 0;
        var pt = p.People; talk.Rumours.EnsureCounts(i => i >= 0 && i < pt.Count ? pt.HomeSettlement[i] : -1);
        int added = 0; var live = new HashSet<ulong>();
        var members = new Dictionary<int, int>();
        foreach (var r in talk.Rumours.Rumours.Values)
        {
            live.Add(r.Id.Value);
            if (!IsMonsterRumour(r) || r.Settlement < 0 || p.Pol.CountedRumours.Contains(r.Id.Value)) continue;
            if (!members.TryGetValue(r.Settlement, out int n)) { n = 0; for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.HomeSettlement[i] == r.Settlement) n++; members[r.Settlement] = n; }
            int heard = talk.Rumours.HeardBySettlement(r.Id).GetValueOrDefault(r.Settlement);
            if (n == 0 || heard * 100 < p.S.RumourSharePct * n) continue;
            p.Pol.CountedRumours.Add(r.Id.Value);
            int before = Of(p, r.Settlement); added += Add(p, r.Settlement, p.S.RumourPoints) - before;
        }
        p.Pol.CountedRumours.RemoveWhere(id => !live.Contains(id));
        return added;
    }

    // ============================================================ the season

    /// <summary>The season: superstition falls 5 toward its base, then a witch hunt may start where it stands at 70 or more after a famine or a death (0x654), and the hunts under way are settled.</summary>
    public static int Season(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return 0;
        int season = day / TimeConst.DaysPerSeason, started = 0;
        foreach (var (s, v) in p.Pol.Superstition.ToList())
        {
            int b = BaseOf(p, s); if (v > b) Set(p, s, Math.Max(b, v - p.S.SuperstitionFall));
        }
        foreach (var h in p.Pol.Hunts.Where(x => x.State == 0).ToList()) Settle(st, db, p, h, day, season);
        var pt = p.People;
        foreach (var par in p.Book.Parishes.Values)
        {
            int s = par.Settlement; if (Of(p, s) < p.S.WitchHuntAt || p.Pol.Hunts.Any(h => h.Settlement == s && h.State == 0)) continue;
            if (!(Hungry(st, p, s) || DeathsSince(pt, s, day - TimeConst.DaysPerSeason) > 0)) continue;
            if (DetRng.Range(st.Seed, season, (ulong)(uint)s, RngStreams.WitchHunts, 0, 0, 1000) >= p.S.WitchHuntPm) continue;
            var pool = new List<int>();
            for (int i = 0; i < pt.Count; i++)
                if (pt.IsAlive(i) && pt.HomeSettlement[i] == s && pt.AgeYears(i, day) >= AdultAge && pt.Occupation[i] != Occupation.Priest && pt.Status[i] != PersonStatus.Hero) pool.Add(i);
            if (pool.Count == 0) continue;
            var accused = pt.IdOf(pool[DetRng.Range(st.Seed, season, (ulong)(uint)s, RngStreams.WitchHunts, 1, 0, pool.Count)]);
            var e = FaithOps.Chron(st, FaithOps.SecOf(day), "witch_hunt", "ch_faith_witch_hunt", [ChronParam.Place("place", FaithOps.PlaceOf(st, s)), ChronParam.Person("accused", accused)], [accused, FaithOps.PlaceOf(st, s)], null, "ch_note_superstition_high");
            p.Pol.Hunts.Add(new HuntRow { Settlement = s, Day = day, Accused = accused.Value, Entry = e.Value }); started++;
        }
        return started;
    }

    private static bool Hungry(WorldState st, FaithOps.Pass p, int settlement)
    {
        var e = st.Economy; var hht = st.Population.Households; if (e == null) return false;
        int n = 0, hungry = 0;
        for (int h = 0; h < hht.Count && h < e.HouseholdCount; h++) { if (hht.MemberCount[h] == 0 || hht.Settlement[h] != settlement) continue; n++; if ((e.Grievances[h] & Grievance.Hunger) != 0) hungry++; }
        return n > 0 && hungry * 100 >= p.S.FamineSharePct * n;
    }

    private static int DeathsSince(PersonTable pt, int settlement, int sinceDay)
    {
        int n = 0; for (int i = 0; i < pt.Count; i++) if (pt.Status[i] == PersonStatus.Dead && pt.HomeSettlement[i] == settlement && pt.DeathDay[i] >= sinceDay) n++; return n;
    }

    /// <summary>A hunt of an earlier season ends: the bishop (piety at least BishopTryPiety - 10) stops it in 6 of 10 draws, else the accused is handed to the lord's court (the crime <c>heresy</c>, a neighbour its witness).</summary>
    private static void Settle(WorldState st, ContentDatabase db, FaithOps.Pass p, HuntRow h, int day, int season)
    {
        if (h.Day / TimeConst.DaysPerSeason >= season) return;
        var accused = new EntityId(h.Accused); var pt = p.People; var place = FaithOps.PlaceOf(st, h.Settlement);
        int ri = p.Religions.OfSettlement(p.Region, h.Settlement); var rel = p.Religions.Get(ri);
        var see = rel == null ? null : p.Book.SeeOf(rel.Id, ChurchSeeding.RealmOf(p.Region, h.Settlement));
        bool merciful = see != null && see.Bishop >= 0 && FaithEngine.IsLivingPriest(pt, see.Bishop, out int brow) && Piety.Of(p.Book, pt, pt.IdOf(brow)) >= p.S.BishopTryPiety - 10
            && DetRng.Range(st.Seed, season, (ulong)(uint)h.Settlement, RngStreams.WitchHunts, 3, 0, 10) < 6;
        if (merciful)
        {
            h.State = 1; Add(p, h.Settlement, -10);
            var bishop = pt.IdOf(pt.IndexOf(new EntityId((ulong)see!.Bishop)));
            FaithOps.Chron(st, FaithOps.SecOf(day), "witch_hunt_stopped", "ch_faith_witch_hunt_stopped", [ChronParam.Person("bishop", bishop), ChronParam.Person("accused", accused), ChronParam.Place("place", place)], [bishop, accused], new EntityId(h.Entry), "ch_note_bishop_merciful");
            return;
        }
        h.State = 2;
        FaithOps.Chron(st, FaithOps.SecOf(day), "witch_hunt_tried", "ch_faith_witch_hunt_tried", [ChronParam.Person("accused", accused), ChronParam.Place("place", place)], [accused, place], new EntityId(h.Entry), "ch_note_bishop_silent");
        int arow = pt.IndexOf(accused); if (arow < 0) return;
        var neigh = new List<int>(); for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && i != arow && pt.HomeSettlement[i] == h.Settlement && pt.AgeYears(i, day) >= AdultAge && pt.Occupation[i] != Occupation.Priest) neigh.Add(i);
        if (neigh.Count == 0) return;
        var witness = pt.IdOf(neigh[DetRng.Range(st.Seed, season, (ulong)(uint)h.Settlement, RngStreams.WitchHunts, 4, 0, neigh.Count)]);
        var book = st.Law ??= new CrimeBook(); var w = LawWorld.From(st, db);
        CrimeSystem.Commit(w, book, new ActSpec("heresy", accused, EntityId.None, h.Settlement, FaithOps.SecOf(day), 0, 0, "ch_note_crime_taboo"), [new WitnessRow(witness.Value, WitnessRow.KnewIt)], ActorLook.None);
    }

    // ============================================================ the hero: a monster's head, a charm

    /// <summary>The hero's monster kills in the chronicle whose heads he has not yet shown.</summary>
    public static List<ChronicleEntry> UnshownKills(WorldState st, FaithOps.Pass p, EntityId hero)
        => [.. st.Chronicle.All.Where(e => e.TypeId is "monster_slain" or "site_monster_slain" && !p.Pol.HeadsShown.Contains(e.Id.Value) && (e.Subjects.Contains(hero) || e.Data.GetValueOrDefault("attacker") == (st.Hero?.Name ?? "\u0001")))];

    /// <summary>Showing a slain monster's head at a settlement (4.7): superstition -40, the kill counts once, the deed raises standing with the settlement and the faith. Returns "" or a refusal key.</summary>
    public static string ShowHead(WorldState st, ContentDatabase db, EntityId hero, int settlement, int day, out int level)
    {
        level = 0;
        var p = FaithOps.Of(st, db); if (p == null) return "fa_no_church";
        if (settlement < 0 || settlement >= p.Region.Settlements.Count) return "fa_no_church";
        var kill = UnshownKills(st, p, hero).FirstOrDefault(); if (kill == null) return "fa_no_head";
        p.Pol.HeadsShown.Add(kill.Id.Value);
        level = Add(p, settlement, -p.S.HeadShownDrop);
        var e = FaithOps.Chron(st, FaithOps.SecOf(day), "monster_head_shown", "ch_faith_head_shown", [ChronParam.Person("doer", hero), ChronParam.Place("place", FaithOps.PlaceOf(st, settlement))], [hero, FaithOps.PlaceOf(st, settlement)], kill.Id, "ch_note_monster_slain");
        FaithStanding.Record(st, db, hero, "monster_head_shown", e, settlement, 1, -1, true, day);
        return "";
    }
}
