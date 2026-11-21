using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Prospecting;
using Ravensguard.Sim.Trade;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Production;

/// <summary>The Ledger's tabs (businesses spec 6), in screen order.</summary>
public enum LedgerTab : byte { Property, Businesses, Sites, Fields, Stalls, Vehicles, Shares, Stewards,
    /// <summary>P6-C3 (rule-and-income 6): the lord's books: receipts by source, arrears, grievances, officials' records, dues and loans. Appended.</summary>
    Fief,
    /// <summary>Phase 7 (overview 8.1): the industry tab (stages, tiers, output). Appended.</summary>
    Industry }

/// <summary>
/// One row of a Ledger table (spec 6): its id (for the detail), the cells as the screen shows them (already formatted, column order of the tab), sort keys per column (numbers sort as numbers,
/// text by its ordinal), and the alerts (A filters on them). Built at a barrier; the screen only sorts and filters.
/// </summary>
public sealed record LedgerRow(LedgerTab Tab, EntityId Id, string[] Cells, long[] Keys, OwnerAlert Alerts)
{
    /// <summary>P5-H (inheritance 6): the row came to the hero by inheritance (the screen marks it "Inherited").</summary>
    public bool Inherited { get; init; }
}

/// <summary>The whole Ledger as of a barrier: every row of every tab, where the coin is, the column heads (en.csv keys) per tab.</summary>
public sealed record LedgerView(int Day, IReadOnlyList<LedgerRow> Rows, long Purse, long Chest, long Boxes, int AlertCount)
{
    /// <summary>P5-H: the last estate the hero's line settled (the dead's name, its holdings, what its debts still lack until the creditors' claims at the season's start); empty when none.</summary>
    public string EstateOf { get; init; } = "";
    public int EstateLines { get; init; }
    public long EstateDebts { get; init; }
    public long EstateUnpaid { get; init; }

    public static readonly IReadOnlyDictionary<LedgerTab, string[]> Columns = new Dictionary<LedgerTab, string[]>
    {
        [LedgerTab.Property] = ["lg_col_what", "lg_col_place", "lg_col_tenure", "lg_col_dues", "lg_col_value", "lg_col_ends", "lg_col_alerts"],
        [LedgerTab.Businesses] = ["lg_col_name", "lg_col_place", "lg_col_cash", "lg_col_profit", "lg_col_staff", "lg_col_alerts", "lg_col_steward", "lg_col_value", "lg_col_sent"],
        [LedgerTab.Sites] = ["lg_col_place", "lg_col_done", "lg_col_missing", "lg_col_crew", "lg_col_escrow", "lg_col_state"],
        [LedgerTab.Fields] = ["lg_col_place", "lg_col_fields", "lg_col_rent", "lg_col_ends", "lg_col_arrears", "lg_col_alerts"],
        [LedgerTab.Stalls] = ["lg_col_place", "lg_col_sells", "lg_col_minder", "lg_col_today", "lg_col_season", "lg_col_alerts"],
        [LedgerTab.Vehicles] = ["lg_col_what", "lg_col_cargo", "lg_col_state", "lg_col_where", "lg_col_alerts", "lg_col_id"],
        [LedgerTab.Shares] = ["lg_col_name", "lg_col_place", "lg_col_share", "lg_col_value", "lg_col_dividend", "lg_col_alerts"],
        [LedgerTab.Stewards] = ["lg_col_name", "lg_col_runs", "lg_col_trading", "lg_col_lag", "lg_col_wage", "lg_col_alerts"],
        [LedgerTab.Fief] = Ravensguard.Sim.Rule.RuleLedger.Columns,
    };

    public IEnumerable<LedgerRow> Tab(LedgerTab t) => Rows.Where(r => r.Tab == t);

    /// <summary>
    /// Sorts rows by a column (BS-17): numbers by their key, descending or ascending, ties by the row's id ascending; optionally only rows with alerts, optionally those whose cells contain a text.
    /// </summary>
    public static List<LedgerRow> Sort(IEnumerable<LedgerRow> rows, int column, bool descending, bool alertsOnly = false, string? search = null)
    {
        var q = rows.Where(r => (!alertsOnly || r.Alerts != OwnerAlert.None) && (string.IsNullOrEmpty(search) || r.Cells.Any(c => c.Contains(search, StringComparison.OrdinalIgnoreCase)))).ToList();
        q.Sort((a, b) =>
        {
            long ka = column < a.Keys.Length ? a.Keys[column] : 0, kb = column < b.Keys.Length ? b.Keys[column] : 0;
            int c = descending ? kb.CompareTo(ka) : ka.CompareTo(kb);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });
        return q;
    }
}

/// <summary>A staff member as the business panel lists him.</summary>
public sealed record StaffView(int Person, string Name, string Occupation, int Skill, int WagePerDay, long OwedMites, int Age);
/// <summary>A candidate the panel offers to hire (the idle and the employed of the place, best in the trade first) or to make steward.</summary>
public sealed record CandidateView(int Person, string Name, string Occupation, int Skill, int CurrentWage, int Trading, int Leadership);
/// <summary>A station and its batch.</summary>
public sealed record StationView(int Index, string Kind, string Recipe, int ProgressPm);
/// <summary>A recipe with its margin per worker-hour today (mites, the planner's figure).</summary>
public sealed record RecipeView(string Id, string Station, long MarginMites, int WorkerHours, long MarginPerHourMites);

/// <summary>Everything the business panel shows (spec 6 detail): staff, stations, stock, recipes with margins, directives, the steward, the season history and where the money is.</summary>
public sealed record BusinessView(EntityId Id, int Index, string Name, string Kind, int Settlement, string Place, bool Owned, bool Open, long Cash, long Capital, long Value, long ProjectedSeasonProfit,
    IReadOnlyList<StaffView> Staff, int Slots, IReadOnlyList<StationView> Stations, IReadOnlyList<(string Good, string Name, long Qty, bool Piece)> Stock, IReadOnlyList<RecipeView> Recipes,
    Directives Orders, int Steward, string StewardName, int StewardTrading, int StewardLeadership, int StewardLag, long StewardReserve, IReadOnlyList<SeasonRow> History,
    long SentToPurse, long SentToChest, long HeroPurse, long Chest, OwnerAlert Alerts, int OwnerPm, int HeroPm, IReadOnlyList<CandidateView> Hire, IReadOnlyList<CandidateView> Stewards,
    long RoyaltyPaid, bool Unlicensed, long MineRemainingG, long MineInitialG, string MineState, long StallSubject, long DailyCost, string OrderedRecipe, IReadOnlyList<string>? Inputs = null);

/// <summary>A known resource at a tile, as the overlay and the survey panel show it.</summary>
public sealed record KnownView(int Tile, int Resource, long EstG, int GradePm, int ConfidencePm, int Day, ProspectMethod Method);

/// <summary>What the prospecting panel and the resource overlay show: what the hero knows around him, his hints and pan findings, his job in progress, the reports he holds, his licences, and the
/// right holder and fee for the ore under him.</summary>
public sealed record ProspectView(int Day, int Tile, int Skill, IReadOnlyList<KnownView> Known, IReadOnlyList<ProspectHint> Hints, IReadOnlyList<PanFinding> Pans, ProspectJob? Job,
    IReadOnlyList<(EntityId Item, int Rows)> Reports, IReadOnlyList<MiningLicence> Licences, string RightHolder, long LicenceFee, bool OreKnownHere, bool Stream, IReadOnlyList<string> Tools);

public sealed partial class OwnershipWorld
{
    private string Place(int s) => (uint)s < (uint)Pop.Region.Settlements.Count ? Pop.Region.Settlements[s].Name : "";
    private static string Alerts(OwnerAlert a) => a == OwnerAlert.None ? "" : string.Join(" ", Enum.GetValues<OwnerAlert>().Where(f => f != OwnerAlert.None && (a & f) != 0).Select(f => "!" + f.ToString()));

    /// <summary>The Ledger (spec 6) as of now: every property, business, site, field, stall, vehicle, share and steward of the hero. Pure reads; the build for 200 rows stays under 15 ms (BS-17).</summary>
    public LedgerView Ledger(int day, Ravensguard.Sim.Construction.ConstructionWorld? cw = null, Ravensguard.Sim.Animals.RideBook? rides = null)
    {
        var rows = new List<LedgerRow>();
        long boxes = 0;
        // property and fields: the land book's deeds of the hero
        if (Land != null)
            foreach (var d in Land.Book.Deeds.Values)
            {
                if (d.Holder != Hero) continue;
                var a = OwnerAlert.None; if (d.ArrearsSeasons > 0) a |= OwnerAlert.WagesOwed;
                string place = d.Settlement >= 0 ? Place(d.Settlement) : d.Kind == HoldingKind.Parcel && (uint)d.Subject < (uint)Pop.Region.Parcels.Count ? Place(Pop.Region.Parcels[(int)d.Subject].Settlement) : "";
                if (d.Kind == HoldingKind.Parcel)
                {
                    int fields = (uint)d.Subject < (uint)Pop.Region.Parcels.Count ? Pop.Region.Parcels[(int)d.Subject].FieldCount : 0;
                    rows.Add(new LedgerRow(LedgerTab.Fields, d.Id, [place, fields.ToString(), M(d.DuesMitesPerSeason), d.LeaseEndDay >= 0 ? $"d{d.LeaseEndDay}" : "-", M(d.OwedMites), Alerts(a)], [0, fields, d.DuesMitesPerSeason, d.LeaseEndDay, d.OwedMites, (long)a], a) { Inherited = d.Cause == DeedCause.Inherited });
                }
                else if (d.Kind != HoldingKind.Stall)
                {
                    long value = Land.HoldingValue(d, day);
                    rows.Add(new LedgerRow(LedgerTab.Property, d.Id, [d.Kind.ToString(), place, d.Tenure.ToString(), M(d.DuesMitesPerSeason), M(value), d.LeaseEndDay >= 0 ? $"d{d.LeaseEndDay}" : "-", Alerts(a)], [(long)d.Kind, 0, (long)d.Tenure, d.DuesMitesPerSeason, value, d.LeaseEndDay, (long)a], a) { Inherited = d.Cause == DeedCause.Inherited });
                }
            }
        // businesses and shares
        foreach (var h in Book.Holdings.Values)
        {
            if (Prod.ById(h.Business) is not { } b) continue;
            long cash = Econ.Ledger.Balance(b.Purse).Mites; boxes += IsHeroOwner(b) ? cash : 0;
            long profit = h.History.Count > 0 ? h.History[^1].Profit : 0;
            long value = ValueOf(b);
            if (IsHeroOwner(b))
            {
                int slots = KindOf(b).StaffCapacity;
                string stew = h.Steward >= 0 ? Pop.People.FullName(h.Steward) : "-";
                rows.Add(new LedgerRow(LedgerTab.Businesses, b.Id, [Name(b), Place(b.Settlement), M(cash), M(profit), $"{b.Staff.Count}/{slots}", Alerts(h.Alerts), stew, M(value), M(h.SentToPurse + h.SentToChest)],
                    [0, b.Settlement, cash, profit, b.Staff.Count, (long)h.Alerts, h.Steward, value, h.SentToPurse + h.SentToChest], h.Alerts) { Inherited = h.InheritedDay >= 0 });
            }
            int pm = h.Shares.Where(s => s.Holder == Hero).Sum(s => s.Pm);
            if (pm > 0)
            {
                long div = h.History.Count > 0 ? h.History[^1].Paid * pm / 1000 : 0;
                rows.Add(new LedgerRow(LedgerTab.Shares, b.Id, [Name(b), Place(b.Settlement), $"{pm / 10}%", M(value * pm / 1000), M(div), Alerts(h.Alerts)], [0, b.Settlement, pm, value * pm / 1000, div, (long)h.Alerts], h.Alerts) { Inherited = h.InheritedDay >= 0 });
            }
        }
        // stalls
        foreach (var s in Book.Stalls.Values)
        {
            var a = s.Minder < 0 ? OwnerAlert.StallUnminded : OwnerAlert.None;
            string sells = s.Business >= 0 && s.Business < Prod.Businesses.Count ? Name(Prod.Businesses[s.Business]) : "-";
            string minder = s.Minder >= 0 ? Pop.People.FullName(s.Minder) : "-";
            rows.Add(new LedgerRow(LedgerTab.Stalls, EntityId.Make(IdKind.Other, (0x5FUL << 40) | (ulong)s.Subject), [Place(s.Settlement), sells, minder, M(s.TakingsToday), M(s.TakingsSeason), Alerts(a)], [s.Settlement, s.Business, s.Minder, s.TakingsToday, s.TakingsSeason, (long)a], a));
        }
        // stewards
        foreach (var g in Book.Holdings.Values.Where(h => h.Steward >= 0).GroupBy(h => h.Steward).OrderBy(g => g.Key))
        {
            int p = g.Key; int tr = Pop.People.SkillLevelOf(p, Skill.Trading), ld = Pop.People.SkillLevelOf(p, Skill.Leadership);
            var a = g.Aggregate(OwnerAlert.None, (x, h) => x | (h.StewardOwed > 0 ? OwnerAlert.WagesOwed : OwnerAlert.None));
            rows.Add(new LedgerRow(LedgerTab.Stewards, Pop.People.IdOf(p), [Pop.People.FullName(p), $"{g.Count()}/{StewardBrain.Properties(ld, Rules)}", tr.ToString(), $"{StewardBrain.LagDays(tr, Rules)} d", M(g.First().StewardWage), Alerts(a)], [0, g.Count(), tr, StewardBrain.LagDays(tr, Rules), g.First().StewardWage, (long)a], a));
        }
        // sites (construction)
        if (cw != null)
            foreach (var site in cw.Book.Sites.Values)
            {
                if (site.Owner != Hero || !site.Open) continue;
                long done = 0, total = 0;
                foreach (var g in cw.GhostsOf(site.Id)) { total += g.WorkNeed; done += g.WorkDone; }
                long pct = total > 0 ? done * 100 / total : 0, missing = 0;
                if (Econ.OtherStores.TryGetValue(site.Pile, out var pile)) foreach (var (good, grams) in cw.Needed(site)) missing += Math.Max(0, grams - pile.Qty(good));
                long escrow = Econ.Ledger.TryFind(PurseKind.Chest, site.Escrow, out var ep) ? Econ.Ledger.Balance(ep).Mites : 0;
                long dayWages = site.Crew.Sum(m => (long)m.WageMitesPerDay);
                long escrowDays = dayWages > 0 ? escrow / dayWages : 0;
                var a = site.State == Ravensguard.Sim.Construction.SiteState.Paused ? OwnerAlert.NoInput : OwnerAlert.None;
                if (missing > 0) a |= OwnerAlert.NoInput;
                rows.Add(new LedgerRow(LedgerTab.Sites, site.Id, [site.Blueprint.Length > 0 ? site.Blueprint : "site", $"{pct}%", missing > 0 ? Quotes.Weight(missing) : "-", site.Crew.Count.ToString(), escrowDays.ToString(), site.State == Ravensguard.Sim.Construction.SiteState.Paused ? site.Pause.ToString() : site.State.ToString()],
                    [0, pct, missing, site.Crew.Count, escrowDays, (long)site.State], a));
            }
        // vehicles and animals
        if (rides != null)
        lock (rides.Gate)                                            // P5-G: the vehicle rows share one lock with the tick worker's vehicle pass
        {
            foreach (var veh in rides.Vehicles.Rows)
            {
                if (veh.Owner != Hero) continue;
                // every vehicle the hero owns (P5-G: his own designs, a commissioned or half-built one, a boat afloat, aground or sunk)
                (string state, int code) = veh.Water == Ravensguard.Sim.Carts.WaterState.Sunk ? ("sunk", 5) : veh.Water == Ravensguard.Sim.Carts.WaterState.Aground ? ("aground", 4)
                    : !veh.Finished ? ("building", 3) : veh.Broken ? ("broken", 2) : veh.Stuck ? ("stuck", 1) : veh.Water == Ravensguard.Sim.Carts.WaterState.Afloat ? ("afloat", 0) : ("ok", 0);
                var a = code is 1 or 2 or 4 or 5 ? OwnerAlert.NoInput : OwnerAlert.None;
                string what = veh.Design.Length > 0 ? veh.Design : "vehicle";
                rows.Add(new LedgerRow(LedgerTab.Vehicles, veh.Id, [what, Quotes.Weight(veh.Cargo.TotalMassG()), state, $"{veh.XMm / 1000},{veh.YMm / 1000}", Alerts(a), veh.Id.Value.ToString()], [0, veh.Cargo.TotalMassG(), code, 0, (long)a, (long)veh.Id.Value], a));
            }
            foreach (var an in rides.Animals.Rows)
            {
                if (an.Owner != Hero) continue;
                string breed = an.Breed >= 0 ? rides.Breeds.At(an.Breed).Id : "animal";
                rows.Add(new LedgerRow(LedgerTab.Vehicles, an.Id, [breed, $"{an.MassKg} kg", "ok", "", "", an.Id.Value.ToString()], [1, an.MassKg, 0, 0, 0, (long)an.Id.Value], OwnerAlert.None));
            }
        }
        int alerts = rows.Count(r => r.Alerts != OwnerAlert.None);
        return new LedgerView(day, rows, HeroCoin, ChestCoin, boxes, alerts);
    }

    /// <summary>The business panel's detail (spec 6): staff, stations, stock, recipes with margin per hour, directives, the steward, four seasons back, where the money is, candidates to hire.</summary>
    public BusinessView? Detail(EntityId biz, int day)
    {
        if (Prod.ById(biz) is not { } b) return null;
        var h = H(b); var kind = KindOf(b); var pt = Pop.People; bool owned = IsHeroOwner(b);
        var book = Markets?.BookForSettlement(b.Settlement);
        var staff = new List<StaffView>();
        for (int i = 0; i < b.Staff.Count; i++)
        {
            int p = b.Staff[i]; var sk = OccupationInfo.Skills(pt.Occupation[p]).A;
            staff.Add(new StaffView(p, pt.FullName(p), pt.Occupation[p].ToString(), sk == Skill.None ? 0 : pt.SkillLevelOf(p, sk), h != null && h.Wages.TryGetValue(p, out int w) ? w : CurrentWage(p, out _), b.Owed[i], pt.AgeYears(p, day)));
        }
        var stations = b.Stations.Select((s, i) => new StationView(i, s.Kind, s.Busy ? Prod.Content.Recipes.Recipes[s.Recipe].Id : "", s.Busy ? s.ProgressPpm / 1000 : 0)).ToList();
        var stock = b.Store.Rows().Select(r => (Econ.Goods.StringId(r.Good), Econ.Goods.Def(r.Good).Name, r.Qty, Econ.Goods.IsPiece(r.Good))).ToList();
        var recipes = kind.Recipes.Select(r => { long m = Prod.ExpectedMargin(b, r); return new RecipeView(r.Id, r.Station, m, r.WorkerHours, m / Math.Max(1, r.WorkerHours)); }).ToList();
        int skillTrade = OccupationInfo.Skills(kind.OwnerOccupation).A is var tsk && tsk != Skill.None ? 0 : 0;
        // the projection at the solver's prices (BS-9): its staff at their mean skill on the planner's first recipe
        var r0 = kind.Recipes.FirstOrDefault(r => r.Id == "forge_nails") ?? kind.Recipes.FirstOrDefault();
        int meanSkill = staff.Count == 0 ? 50 : (int)staff.Average(s => s.Skill);
        long proj = r0 == null ? 0 : Valuation.ProjectedSeasonProfit(Math.Max(1, staff.Count), meanSkill, r0.WorkerHours, RecipeBook.WagePerHour(r0.Wage));
        int stew = h?.Steward ?? -1;
        int tr = stew >= 0 ? pt.SkillLevelOf(stew, Skill.Trading) : 0, ld = stew >= 0 ? pt.SkillLevelOf(stew, Skill.Leadership) : 0;
        long daily = h != null ? DailyCost(b, h) : 0;
        var hire = new List<CandidateView>(); var stewards = new List<CandidateView>();
        if (owned)
        {
            var sk = kind.Slots.Length > 0 ? OccupationInfo.Skills(kind.Slots[0].Occupation).A : Skill.None;
            var cands = new List<(int P, int S)>(); var stc = new List<(int P, int S)>();
            for (int i = 0; i < pt.Count; i++)
            {
                if (pt.Status[i] != PersonStatus.Alive || pt.HomeSettlement[i] != b.Settlement || b.Staff.Contains(i)) continue;
                int age = pt.AgeYears(i, day); if (age < 16 || age >= 60) continue;
                if (pt.Occupation[i] is Occupation.Lord or Occupation.Priest or Occupation.Mayor) continue;
                cands.Add((i, sk == Skill.None ? 0 : pt.SkillLevelOf(i, sk)));
                int t = pt.SkillLevelOf(i, Skill.Trading), l = pt.SkillLevelOf(i, Skill.Leadership);
                if (StewardBrain.Qualifies(t, l, Rules) && i != stew) stc.Add((i, t + l));
            }
            foreach (var (p, s) in cands.OrderByDescending(x => x.S).ThenBy(x => x.P).Take(6)) hire.Add(new CandidateView(p, pt.FullName(p), pt.Occupation[p].ToString(), s, CurrentWage(p, out _), pt.SkillLevelOf(p, Skill.Trading), pt.SkillLevelOf(p, Skill.Leadership)));
            foreach (var (p, s) in stc.OrderByDescending(x => x.S).ThenBy(x => x.P).Take(4)) stewards.Add(new CandidateView(p, pt.FullName(p), pt.Occupation[p].ToString(), s, CurrentWage(p, out _), pt.SkillLevelOf(p, Skill.Trading), pt.SkillLevelOf(p, Skill.Leadership)));
        }
        var mine = b.Mine >= 0 && b.Mine < Prod.Mines.Count ? Prod.Mines[b.Mine] : null;
        _ = skillTrade;
        return new BusinessView(b.Id, b.Index, Name(b), kind.Id, b.Settlement, Place(b.Settlement), owned, b.State == BusinessState.Open, Econ.Ledger.Balance(b.Purse).Mites, b.Capital, ValueOf(b), proj,
            staff, kind.StaffCapacity, stations, stock, recipes, h?.Orders.Clone() ?? new Directives(), stew, stew >= 0 ? pt.FullName(stew) : "", tr, ld, stew >= 0 ? StewardBrain.LagDays(tr, Rules) : 0,
            stew >= 0 ? StewardBrain.Reserve(daily, ld, Rules) : 0, h?.History.ToList() ?? [], h?.SentToPurse ?? 0, h?.SentToChest ?? 0, HeroCoin, ChestCoin, h?.Alerts ?? OwnerAlert.None,
            h?.OwnerPm ?? 1000, h?.Shares.Where(s => s.Holder == Hero).Sum(s => s.Pm) ?? 0, hire, stewards, h?.RoyaltyPaid ?? 0, h?.Unlicensed ?? false, mine?.RemainingG ?? 0, mine?.InitialG ?? 0,
            mine?.State.ToString() ?? "", StallOf(b)?.Subject ?? -1, daily, h != null && h.Orders.Recipe >= 0 && h.Orders.Recipe < Prod.Content.Recipes.Recipes.Count ? Prod.Content.Recipes.Recipes[h.Orders.Recipe].Id : "",
            kind.Recipes.SelectMany(r => r.Inputs.Select(i => Econ.Goods.StringId(i.Good))).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    /// <summary>The prospecting view where the hero stands (the overlay reads <paramref name="radiusTiles"/> around him).</summary>
    public ProspectView Prospect(int tile, int day, IReadOnlyList<string> tools, int radiusTiles = 12)
    {
        int tx = tile % RegionData.Size, ty = tile / RegionData.Size;
        var known = new List<KnownView>();
        foreach (var r in Book.Prospects.Of(Hero))
        {
            int x = r.Tile % RegionData.Size, y = r.Tile / RegionData.Size;
            if (Math.Abs(x - tx) > radiusTiles || Math.Abs(y - ty) > radiusTiles) continue;
            known.Add(new KnownView(r.Tile, r.Resource, r.EstG, r.GradePm, r.ConfidencePm, r.Day, r.Method));
        }
        var reports = new List<(EntityId, int)>();
        foreach (var (id, rows) in Book.Prospects.Reports) if (Econ.Items.IsLive(id) && Econ.Items.Get(id).Owner == Hero) reports.Add((id, rows.Length));
        var ore = Truth.OreAt(tile);
        bool oreKnown = ore is { } o && Book.Prospects.Rows.ContainsKey((Hero.Value, tile, ResourceCode.Ore(o.Mineral)));
        string holder = ""; long fee = 0;
        if (oreKnown && RightHolder(tile, ore!.Value.Mineral) is { } gh) { holder = Land?.GrantorName(gh.Kind, gh.Index) ?? ""; fee = LicenceFee(ore.Value.Mineral); }
        return new ProspectView(day, tile, HeroProspecting(), known, [.. Book.Prospects.Hints.Where(h => h.Person == Hero)], [.. Book.Prospects.Pans.Where(p => p.Person == Hero)],
            Book.Prospects.Jobs.FirstOrDefault(j => j.Person == Hero && !j.Done), reports, [.. Book.Licences.Where(l => l.Holder == Hero)], holder, fee, oreKnown, Truth.IsStream(tile), tools);
    }
}
