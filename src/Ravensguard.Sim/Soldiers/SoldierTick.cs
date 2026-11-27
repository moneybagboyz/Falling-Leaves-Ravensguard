using Ravensguard.Sim.Armies;
using Ravensguard.Content;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Soldiers;

/// <summary>
/// P7-SC-C: the Phase 7 seeding step "companies" (overview 7, stream 0x7B1): two to four mercenary companies a region, each made of adult men of the region's people with no soldier row (they stay in their households;
/// no purse is opened and no coin or item moves, so the economy's hash is unchanged until a company is first hired). Pure in the world seed and the people's ids.
/// </summary>
public sealed class CompanySeedStep : IPhase7SeedStep
{
    public string Name => "companies";
    public string Section => SoldierSave.SectionName;

    public bool Seed(Phase7SeedContext ctx) => ctx.Content != null && Run(ctx.State, ctx.Content, ctx.Seed);

    /// <summary>A rich land (the mean household purse at or above this, mites) and a warring one (active wars in the region) raise the count (P7-SC-D: one more for riches, one more for a war, two for three wars).</summary>
    public const long RichHouseholdMites = 5_000;

    /// <summary>The number of companies a world gets: 2 to 4 by a draw of 0x7B1, one more where the households are rich, one or two more where wars are active.</summary>
    public static int CountFor(WorldState st, ulong seed)
    {
        int n = 2 + DetRng.Range(seed, 0, 0, RngStreams.CompanySeeding, 2_000_000, 0, 3);
        var e = st.Economy;
        if (e != null && e.HouseholdCount > 0)
        {
            long sum = 0; int k = Math.Min(e.HouseholdCount, e.HouseholdPurse.Length);
            for (int h = 0; h < k; h++) if (!e.HouseholdPurse[h].IsNone) sum += e.Ledger.Balance(e.HouseholdPurse[h]).Mites;
            if (k > 0 && sum / k >= RichHouseholdMites) n++;
        }
        int wars = st.Population.Region.ActiveWars.Count;
        return n + (wars >= 3 ? 2 : wars >= 1 ? 1 : 0);
    }

    /// <summary>Makes the world's first companies (no flag is touched here); true when any was made.</summary>
    public static bool Run(WorldState st, ContentDatabase content, ulong seed)
    {
        if (st.Economy == null || st.Population.People.Count == 0 || st.Soldiers is { Companies.Count: > 0 }) return false;
        var pt = st.Population.People; int day = (int)st.Clock.Now.TotalDays; var hero = st.Party?.Hero ?? EntityId.None;
        var men = new List<ulong>();
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.Status[i] != PersonStatus.Alive || pt.Sex[i] != Sex.Male || pt.IdOf(i) == hero || pt.Household[i] < 0) continue;
            int age = pt.AgeYears(i, day); if (age < 18 || age > 45) continue;
            men.Add(pt.IdOf(i).Value);
        }
        if (men.Count < MercCompanies.MinMen * 3) return false;
        var sw = SoldierWiring.For(st, content);
        int n = CountFor(st, seed);
        int maxMen = Math.Max(MercCompanies.MinMen, men.Count / (n * 6));
        bool any = false;
        for (int k = 1; k <= n; k++)
        {
            var c = MercCompanies.Seed(sw, seed, k, 0, men, maxMen);
            if (c == null) continue;
            int row = pt.IndexOf(new EntityId(c.Captain)); c.Settlement = row >= 0 ? Math.Max(0, pt.HomeSettlement[row]) : 0;
            any = true;
        }
        return any;
    }
}

/// <summary>
/// P7-SC-C: the soldiers' daily tick on the abstract clock (hour 0, after the contracts' expiries), wiring what P7-SC-B left as plain calls: every soldier with a lord is paid his wage by ledger transfer from the lord's
/// purse (unpaid days are arrears), drills two hours with his role's weapon family, and at a season's start the lords other than the hero accept the promotions their wage budget allows; the mercenary companies take
/// their day. A world with no soldier does nothing here.
/// </summary>
public static class SoldierTick
{
    private static Skill FamilyOf(string role) => role switch
    {
        "archer" or "crossbowman" => Skill.Archery,
        "spearman" or "sergeant" => Skill.Polearms,
        "man_at_arms" or "knight" => Skill.Blades,
        _ => Skill.None,
    };

    private static HashSet<ulong>? AwayMen(ArmyBook b)
    {
        var set = new HashSet<ulong>();
        foreach (var wb in b.Warbands.Values) foreach (var m in wb.Members) set.Add(m);
        foreach (var h in b.Hosts.Values) foreach (var c in h.Companies) foreach (var m in c.Men) set.Add(m);
        return set.Count == 0 ? null : set;
    }

    public static void OnDay(Save.WorldState st, ContractWorld w, ContractBook book, int day)
    {
        if (st.Content == null || st.Economy == null) return;
        if (st.Soldiers is not { Seeded: true })          // P7-SC-D: the first companies, once, on the running world's first day (a flag in the soldiers section; the new world's layers are not touched)
        {
            CompanySeedStep.Run(st, st.Content, w.Seed);
            (st.Soldiers ??= SoldierWiring.For(st, st.Content).Book).Seeded = true;
        }
        if (st.Soldiers is not { IsEmpty: false }) return;
        var sw = SoldierWiring.For(st, st.Content);
        var chron = RuleChron.Of(st.Chronicle, st.Ids);
        var byLord = new SortedDictionary<long, List<SoldierRow>>();          // P7R-13: the lords' men grouped once, in person order
        foreach (var r in sw.Book.Rows.Values)
        {
            if (r.Lord < 0) continue;
            if (FamilyOf(r.Role) is var f && f != Skill.None) Growth.Drill(sw, r.Person, f, sw.Roles.Rules.DrillHoursDay, day);          // drill: hours of the day, no coin
            if (!byLord.TryGetValue(r.Lord, out var list)) byLord[r.Lord] = list = [];
            list.Add(r);
        }
        var away = st.Armies is { } ab ? AwayMen(ab) : null;          // P7R-1: the men of a warband or a host are paid by the armies' own day (warbands, the host's chest)
        Func<ulong, bool>? skip = away == null ? null : away.Contains;
        foreach (var (lord, rows) in byLord)
        {
            var purse = sw.PurseOf((ulong)lord);
            WageRoll.PayDay(sw, lord, purse, w.Ledger, skip, rows);
            if (lord != 0 && Promotions.IsSeasonStart(day))
            {
                Promotions.Offer(sw, lord, day);
                long budget = purse.IsNone ? 0 : w.Ledger.Balance(purse).Mites / 60;
                Promotions.AcceptWithin(sw, lord, day, chron, new EntityId((ulong)lord), budget);
            }
        }
        MercCompanies.Day(w, book, sw, chron, day);
        if (sw.Book.Prisoners.Count > 0) PrisonerPens.Day(sw, Combat.Aftermath.AftermathRules.For(st.Content), chron, w.Seed, day);
    }
}
