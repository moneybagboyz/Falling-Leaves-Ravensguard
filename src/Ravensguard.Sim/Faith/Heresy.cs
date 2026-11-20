using System.Numerics;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Heresy (faith-and-church 4.5, P6-E2): a priest of Theology 50 and piety 80 who has a grievance with his bishop, or a settlement the famine struck, may start one (2% a season, stream 0x652 key settlement and season):
/// it has a preacher and changes one doctrine. Adherents grow by hearings: each adherent is heard by a person of the settlement a day, who converts with P = 10% + 2% per grievance of his household - piety / 5 (0x652, key person,
/// draw 1). The bishop answers by his own piety: he preaches against it (adherents -20% a season), has the preacher tried (the crime <c>heresy</c>), or lays an interdict (no services, two grievances a household).
/// </summary>
public static class Heresy
{
    /// <summary>The doctrines a heresy may change (the text key is <c>fa_heresy_</c> + the id).</summary>
    public static readonly string[] Changes = ["tithe_poor", "slavery_forbid", "usury_forbid", "fasting_strict"];

    private const int DrawStart = 0, DrawHear = 0, DrawConvert = 1, DrawChange = 2, DrawResponse = 3;
    private const int AdultAge = 16;

    private static ulong Key(int a, int b) => ((ulong)(uint)a << 32) | (uint)b;

    /// <summary>The permille a hearing converts the hearer (4.5): base + per grievance x his household's grievances - piety x 10 / divisor, kept 0..1000.</summary>
    public static int ConvertPm(FaithSettingsDef s, int grievances, int piety) => Math.Clamp(s.HeresyBasePm + s.HeresyPerGrievancePm * grievances - piety * 10 / Math.Max(1, s.HeresyPietyDivisor), 0, 1000);

    /// <summary>The grievance flags a household bears (hunger, tax, crowding, crime, lord, cold, a missed meal).</summary>
    public static int GrievancesOf(WorldState st, int person)
    {
        var e = st.Economy; if (e == null) return 0;
        int hh = st.Population.People.Household[person]; if (hh < 0 || hh >= e.HouseholdCount) return 0;
        return BitOperations.PopCount((uint)(ushort)e.Grievances[hh]);
    }

    /// <summary>Begins a heresy in a settlement (4.5): the row, the preacher as its first adherent, the chronicle entry. Returns null when the settlement already has one.</summary>
    public static HeresyRow? Start(WorldState st, ContentDatabase db, int settlement, EntityId preacher, string change, int day, string noteKey = "ch_note_heresy_grievance")
    {
        var p = FaithOps.Of(st, db); if (p == null || p.Pol.Heresies.ContainsKey(settlement)) return null;
        int ri = p.Religions.OfSettlement(p.Region, settlement); var rel = p.Religions.Get(ri); if (rel == null) return null;
        var row = new HeresyRow { Settlement = settlement, Religion = rel.Id, Change = change, Preacher = (long)preacher.Value, StartDay = day };
        row.Adherents.Add(preacher.Value);
        var ps = new List<ChronParam> { ChronParam.Person("preacher", preacher), ChronParam.Place("place", FaithOps.PlaceOf(st, settlement)), ChronParam.TextKey("doctrine", "fa_heresy_" + change) };
        row.Entry = FaithOps.Chron(st, FaithOps.SecOf(day), "heresy_begun", "ch_faith_heresy_begun", ps, [preacher, FaithOps.PlaceOf(st, settlement)], null, noteKey, null, ("change", change)).Value;
        p.Pol.Heresies[settlement] = row;
        return row;
    }

    // ============================================================ the season

    /// <summary>
    /// The season's pass: new heresies where the conditions hold (stream 0x652, key settlement and season), then the bishops' answers and the preaching cut. Returns the heresies begun.
    /// </summary>
    public static int Season(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return 0;
        int season = day / TimeConst.DaysPerSeason, begun = 0;
        var pc = PoliticsOf(st, db, day);
        foreach (var par in p.Book.Parishes.Values)
        {
            if (p.Pol.Heresies.ContainsKey(par.Settlement) || par.Priest < 0 || !FaithEngine.IsLivingPriest(p.People, par.Priest, out int prow)) continue;
            var priest = p.People.IdOf(prow);
            if (!(HasGrievance(p, pc, par, prow, priest) || Famine(st, p, par.Settlement))) continue;
            if (DetRng.Range(st.Seed, season, Key(par.Settlement, season), RngStreams.HeresyRoll, DrawStart, 0, 1000) >= p.S.HeresyStartPm) continue;
            string change = Changes[DetRng.Range(st.Seed, season, Key(par.Settlement, season), RngStreams.HeresyRoll, DrawChange, 0, Changes.Length)];
            bool famine = !HasGrievance(p, pc, par, prow, priest);
            if (Start(st, db, par.Settlement, priest, change, day, famine ? "ch_note_heresy_famine" : "ch_note_heresy_grievance") != null) begun++;
        }
        foreach (var h in p.Pol.Heresies.Values.ToList()) Answer(st, db, p, pc, h, day, season);
        return begun;
    }

    private static PoliticsCtx? PoliticsOf(WorldState st, ContentDatabase db, int day)
    {
        var sim = st.HomeRecords?.Sim; var rule = RuleWiring.Bind(st, db);
        if (sim == null || rule == null) return null;
        var pb = st.Politics ??= new PoliticsBook(); PoliticsWiring.Bind(pb, db, st.Seed);
        return PoliticsEngine.Context(sim, pb, rule, RuleChron.Of(st.Chronicle, st.Ids), day);
    }

    /// <summary>A priest with Theology and piety enough who thinks ill of his bishop (opinion below zero).</summary>
    private static bool HasGrievance(FaithOps.Pass p, PoliticsCtx? pc, ParishRow par, int prow, EntityId priest)
    {
        if (p.People.SkillLevelOf(prow, Skill.Theology) < p.S.HeresyTheology || Piety.Of(p.Book, p.People, priest) < p.S.HeresyPiety) return false;
        int r = p.Religions.OfSettlement(p.Region, par.Settlement); var rel = p.Religions.Get(r); if (rel == null || pc == null) return false;
        var see = p.Book.SeeOf(rel.Id, ChurchSeeding.RealmOf(p.Region, par.Settlement));
        if (see == null || see.Bishop < 0 || see.Bishop == par.Priest) return false;
        return Opinions.Value(pc.RB, priest.Value, (ulong)see.Bishop) < 0;
    }

    /// <summary>Hunger struck the settlement: this share of its households carries the hunger flag.</summary>
    private static bool Famine(WorldState st, FaithOps.Pass p, int settlement)
    {
        var e = st.Economy; var hht = st.Population.Households; if (e == null) return false;
        int n = 0, hungry = 0;
        for (int h = 0; h < hht.Count && h < e.HouseholdCount; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Settlement[h] != settlement) continue;
            n++; if ((e.Grievances[h] & Grievance.Hunger) != 0) hungry++;
        }
        return n > 0 && hungry * 100 >= p.S.FamineSharePct * n;
    }

    /// <summary>The bishop's answer to a heresy he has heard of (4.5), by his own piety; a heresy that loses all its adherents but the dead is over.</summary>
    private static void Answer(WorldState st, ContentDatabase db, FaithOps.Pass p, PoliticsCtx? pc, HeresyRow h, int day, int season)
    {
        var pt = p.People; var s = p.S;
        if (!FaithEngine.IsLivingPriest(pt, h.Preacher, out _) && h.Adherents.Count <= 1) { End(st, p, h, day); return; }
        var rel = p.Religions.All.FirstOrDefault(r => r.Id == h.Religion); if (rel == null) return;
        var see = p.Book.SeeOf(rel.Id, ChurchSeeding.RealmOf(p.Region, h.Settlement));
        if (see == null || see.Bishop < 0 || !FaithEngine.IsLivingPriest(pt, see.Bishop, out int brow)) return;
        var bishop = pt.IdOf(brow); int pb = Piety.Of(p.Book, pt, bishop);
        if (h.Response == 0)
        {
            int share = h.Adults > 0 ? h.Adherents.Count * 100 / h.Adults : 0;
            if (share >= s.InterdictSharePct && pb >= s.BishopTryPiety - 10) Interdict(st, p, pc, h, bishop, day);
            else if (pb >= s.BishopTryPiety) Try(st, db, p, h, bishop, day);
            else if (pb >= s.BishopLaxPiety) { h.Response = 1; h.ResponseDay = day; Chronicle(st, "heresy_preached", "ch_faith_heresy_preached", h, bishop, day, "ch_note_bishop_pious"); }
            else { h.Response = 4; h.ResponseDay = day; }
        }
        if (h.Response == 1) { Cut(h, s.PreachCutPct); if (h.Adherents.Count <= 1) End(st, p, h, day); }
    }

    /// <summary>Preaching takes back a share of the adherents (every nth in id order, never the preacher): integer, no draw.</summary>
    public static int Cut(HeresyRow h, int pct)
    {
        int want = h.Adherents.Count * pct / 100, cut = 0; if (want <= 0) return 0;
        var list = h.Adherents.Where(a => (long)a != h.Preacher).ToList(); if (list.Count == 0) return 0;
        int step = Math.Max(1, list.Count / want);
        for (int i = 0; i < list.Count && cut < want; i += step) { h.Adherents.Remove(list[i]); cut++; }
        return cut;
    }

    private static void Interdict(WorldState st, FaithOps.Pass p, PoliticsCtx? pc, HeresyRow h, EntityId bishop, int day)
    {
        h.Response = 3; h.ResponseDay = day; h.Interdict = true;
        var e = Chronicle(st, "interdict_laid", "ch_faith_interdict", h, bishop, day, "ch_note_heresy_widespread");
        if (pc == null) return;
        var hht = st.Population.Households;
        for (int hh = 0; hh < hht.Count; hh++)
            if (hht.MemberCount[hh] > 0 && hht.Settlement[hh] == h.Settlement) RuleDues.AddGrievance(pc.R, hh, p.S.InterdictGrievances, p.S.InterdictGrievanceSeasons, day, e);
    }

    /// <summary>The bishop asks the lord to try the preacher: the crime <c>heresy</c> with the bishop for its witness (the court's sentence follows the culture's table).</summary>
    private static void Try(WorldState st, ContentDatabase db, FaithOps.Pass p, HeresyRow h, EntityId bishop, int day)
    {
        h.Response = 2; h.ResponseDay = day;
        var preacher = new EntityId((ulong)h.Preacher);
        Chronicle(st, "heresy_charged", "ch_faith_heresy_charged", h, bishop, day, "ch_note_bishop_zeal");
        var book = st.Law ??= new CrimeBook(); var w = LawWorld.From(st, db);
        CrimeSystem.Commit(w, book, new ActSpec("heresy", preacher, EntityId.None, h.Settlement, FaithOps.SecOf(day), 0, 0, "ch_note_crime_taboo"), [new WitnessRow(bishop.Value, WitnessRow.KnewIt)], ActorLook.None);
        if (st.Politics is { } pb && st.Rule is { } rb) Opinions.Add(pb, rb, bishop.Value, preacher.Value, "heresy", day, h.Entry);
    }

    private static EntityId Chronicle(WorldState st, string type, string key, HeresyRow h, EntityId bishop, int day, string note)
    {
        var preacher = new EntityId((ulong)h.Preacher);
        var ps = new List<ChronParam> { ChronParam.Person("bishop", bishop), ChronParam.Person("preacher", preacher), ChronParam.Place("place", FaithOps.PlaceOf(st, h.Settlement)) };
        return FaithOps.Chron(st, FaithOps.SecOf(day), type, key, ps, [bishop, preacher], new EntityId(h.Entry), note);
    }

    private static void End(WorldState st, FaithOps.Pass p, HeresyRow h, int day)
    {
        var preacher = new EntityId((ulong)h.Preacher);
        FaithOps.Chron(st, FaithOps.SecOf(day), "heresy_ended", "ch_faith_heresy_ended", [ChronParam.Place("place", FaithOps.PlaceOf(st, h.Settlement)), ChronParam.TextKey("doctrine", "fa_heresy_" + h.Change)], [FaithOps.PlaceOf(st, h.Settlement)], new EntityId(h.Entry), "ch_note_heresy_faded");
        p.Pol.Heresies.Remove(h.Settlement);
    }

    // ============================================================ the day

    /// <summary>
    /// The day's hearings (4.5): each adherent is heard by one person of the settlement (so as many hearers as adherents, chosen with P = hearings / non-adherents, stream 0x652 draw 0 key person); a hearer
    /// converts with <see cref="ConvertPm"/> (draw 1). Returns the conversions. People are taken in id order.
    /// </summary>
    public static int Daily(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null || p.Pol.Heresies.Count == 0) return 0;
        int total = 0; var pt = p.People; var s = p.S;
        foreach (var h in p.Pol.Heresies.Values)
        {
            var candidates = new List<int>(); int adults = 0;
            for (int i = 0; i < pt.Count; i++)
            {
                if (!pt.IsAlive(i) || pt.HomeSettlement[i] != h.Settlement || pt.AgeYears(i, day) < AdultAge) continue;
                adults++; if (!h.Adherents.Contains(pt.IdOf(i).Value)) candidates.Add(i);
            }
            h.Adults = adults;
            if (candidates.Count == 0) continue;
            int hearings = h.Adherents.Count * s.HearingsPerAdherent;
            int hearPm = (int)Math.Min(1000L, hearings * 1000L / candidates.Count);
            var won = new List<ulong>();
            foreach (int i in candidates)
            {
                var id = pt.IdOf(i);
                if (DetRng.Range(st.Seed, day, id.Value, RngStreams.HeresyRoll, DrawHear, 0, 1000) >= hearPm) continue;
                int pm = ConvertPm(s, GrievancesOf(st, i), Piety.Of(p.Book, pt, id));
                if (DetRng.Range(st.Seed, day, id.Value, RngStreams.HeresyRoll, DrawConvert, 0, 1000) < pm) won.Add(id.Value);
            }
            foreach (var w in won) h.Adherents.Add(w);
            total += won.Count;
        }
        return total;
    }
}
