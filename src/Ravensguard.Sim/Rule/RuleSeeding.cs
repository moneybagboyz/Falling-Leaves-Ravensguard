using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// Derives the rule book of a region from the Phase 1 lords and the people (rule-and-income 3, the Phase 6 seeding pass): every fief gets a deed row with its holder (the lord the population names), its overlord's realm,
/// all four rights and the service it owes; each fief gets a reeve and a bailiff from the adults living in it, each realm a customs collector, and the honesty of those office holders is rolled (stream 0x540, key the person id).
/// A pure function of the world seed, the content and the people's ids, never of the day it runs, so a new game and the first load of a save from before Phase 6 derive the same book.
/// </summary>
public static class RuleSeeding
{
    /// <summary>Binds the content's numbers to a book (settings, offices, customs rows) and the world's seed.</summary>
    public static RuleBook Bind(RuleBook b, ContentDatabase? db, ulong seed)
    {
        b.Seed = seed;
        if (db == null) return b;
        b.Settings = db.All<RuleSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? b.Settings;
        b.OfficeDefs = db.All<OfficeDef>().ToDictionary(o => o.Id, StringComparer.Ordinal);
        b.CustomsRates = [.. db.All<CustomsRateDef>().OrderBy(r => r.Id, StringComparer.Ordinal)];
        return b;
    }

    /// <summary>Builds the book of the home region as the people stand (fiefs, offices, honesty; no dues, loans or councils yet).</summary>
    public static RuleBook Build(WorldState st, ContentDatabase? db)
    {
        var b = Bind(new RuleBook(), db, st.Seed);
        var pop = st.Population; var region = pop.Region; var pt = pop.People; int day = pop.NowDay;
        for (int f = 0; f < region.Fiefs.Count; f++)
        {
            var fi = region.Fiefs[f];
            int lord = f < pop.FiefLord.Length ? pop.FiefLord[f] : -1;
            var (men, days) = RuleMath.ServiceOf(b.Settings, manor: false, fi.Households);
            b.Fiefs[f] = new FiefRow { Fief = f, Holder = lord >= 0 && pt.IsAlive(lord) ? (long)pt.IdOf(lord).Value : -1, Realm = fi.Realm, Rights = FiefRights.All, ServiceMen = men, ServiceDays = days, TookDay = -1 };
        }
        int lordHouse(int f) => f < pop.FiefLordHousehold.Length ? pop.FiefLordHousehold[f] : -1;
        for (int f = 0; f < region.Fiefs.Count; f++)
        {
            int reeve = Pick(pop, f, seatOnly: true, Skill.Leadership, day, -1, lordHouse(f));
            if (reeve >= 0) AppointAtSeed(b, st.Seed, OfficeKind.Reeve, f, region.Fiefs[f].Realm, pt.IdOf(reeve), day);
            int bailiff = Pick(pop, f, seatOnly: false, Skill.Law, day, reeve, lordHouse(f));
            if (bailiff >= 0) AppointAtSeed(b, st.Seed, OfficeKind.Bailiff, f, region.Fiefs[f].Realm, pt.IdOf(bailiff), day);
        }
        foreach (int realm in region.Fiefs.Select(x => x.Realm).Where(r => r >= 0).Distinct().OrderBy(r => r))
        {
            int seat = -1;
            for (int f = 0; f < region.Fiefs.Count; f++) if (region.Fiefs[f].Realm == realm && (seat < 0 || region.Settlements[region.Fiefs[f].SeatSettlement].TargetPop > region.Settlements[region.Fiefs[seat].SeatSettlement].TargetPop)) seat = f;
            if (seat < 0) continue;
            int coll = PickInSettlement(pop, region.Fiefs[seat].SeatSettlement, Skill.Trading, day, -1, lordHouse(seat));
            if (coll >= 0) AppointAtSeed(b, st.Seed, OfficeKind.CustomsCollector, -1, realm, pt.IdOf(coll), day);
        }
        return b;
    }

    /// <summary>The lord fills the posts that fell vacant (a holder died or was dismissed): a reeve at the seat, a bailiff in the fief, a collector for a realm; each by the same choice the seeding made.</summary>
    public static int FillVacancies(RuleCtx c, int day)
    {
        int n = 0; var pop = c.W.Population; var region = c.Region;
        foreach (var f in c.B.Fiefs.Values)
        {
            if (f.Forfeited) continue;
            int lordHouse = f.Fief < pop.FiefLordHousehold.Length ? pop.FiefLordHousehold[f.Fief] : -1;
            int reeveRow = -1;
            if (c.B.OfficeOf(OfficeKind.Reeve, f.Fief) == null) { reeveRow = Pick(pop, f.Fief, true, Skill.Leadership, day, -1, lordHouse); if (reeveRow >= 0 && RuleOfficials.Appoint(c, OfficeKind.Reeve, f.Fief, f.Realm, (long)pop.People.IdOf(reeveRow).Value, day) != null) n++; }
            if (c.B.OfficeOf(OfficeKind.Bailiff, f.Fief) == null) { int br = Pick(pop, f.Fief, false, Skill.Law, day, reeveRow, lordHouse); if (br >= 0 && RuleOfficials.Appoint(c, OfficeKind.Bailiff, f.Fief, f.Realm, (long)pop.People.IdOf(br).Value, day) != null) n++; }
        }
        _ = region;
        return n;
    }

    private static void AppointAtSeed(RuleBook b, ulong seed, OfficeKind kind, int fief, int realm, EntityId person, int day)
    {
        b.Offices.Add(new Office { Serial = b.NextOffice++, Kind = kind, Holder = (long)person.Value, Fief = fief, Realm = realm, WageMitesPerDay = RuleOfficials.WageOf(b, kind), Honesty = RuleOfficials.HonestyOf(b, seed, person), AppointedDay = day });
    }

    private static bool Fit(Population pop, int i, int day, int notRow, int lordHouse)
    {
        var pt = pop.People;
        if (!pt.IsAlive(i) || i == notRow || pt.Household[i] == lordHouse) return false;
        int age = pt.AgeYears(i, day); if (age < 25 || age >= 65) return false;
        return pt.Occupation[i] is not (Occupation.Lord or Occupation.Priest or Occupation.Guard);
    }

    /// <summary>The adult of a fief with the most of a skill (ties to the lower row): at the seat only, or anywhere in the fief.</summary>
    internal static int Pick(Population pop, int fief, bool seatOnly, Skill skill, int day, int notRow, int lordHouse)
    {
        var region = pop.Region; int seat = region.Fiefs[fief].SeatSettlement; int best = -1, bestSkill = -1; var pt = pop.People;
        for (int i = 0; i < pt.Count; i++)
        {
            int hs = pt.HomeSettlement[i]; if (hs < 0 || hs >= region.Settlements.Count) continue;
            if (seatOnly ? hs != seat : region.Settlements[hs].FiefIndex != fief) continue;
            if (!Fit(pop, i, day, notRow, lordHouse)) continue;
            int sk = pt.SkillLevelOf(i, skill);
            if (sk > bestSkill) { best = i; bestSkill = sk; }
        }
        if (best < 0 && seatOnly) return Pick(pop, fief, false, skill, day, notRow, lordHouse);
        return best;
    }

    internal static int PickInSettlement(Population pop, int settlement, Skill skill, int day, int notRow, int lordHouse)
    {
        int best = -1, bestSkill = -1; var pt = pop.People;
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.HomeSettlement[i] != settlement || !Fit(pop, i, day, notRow, lordHouse)) continue;
            int sk = pt.SkillLevelOf(i, skill);
            if (sk > bestSkill) { best = i; bestSkill = sk; }
        }
        return best;
    }
}

/// <summary>The fiefs step of the Phase 6 seeding pass (overview 7): fief deeds, offices and honesty. A bare world with no content or no fiefs keeps no section; a loaded older save is marked pending and derived when the runtime starts.</summary>
public sealed class RuleSeedStep : IPhase6SeedStep
{
    public string Name => "fiefs";
    public string Section => RuleSave.SectionName;

    public bool Seed(Phase6SeedContext ctx)
    {
        var st = ctx.State;
        if (ctx.Content == null || st.Population.Region.Fiefs.Count == 0) return false;
        if (ctx.FromSave) { st.RuleSeedPending = true; return false; }                       // a loaded save is seeded when the runtime starts (RuleWiring.Attach): read and written back, it keeps its bytes
        st.Rule = RuleSeeding.Build(st, ctx.Content);
        return true;
    }
}
