using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Growth;

/// <summary>
/// Background growth (plan 4A.2, docs/specs/phase3/background-growth.md): once a season the abstract world builds. Households and lords put up cottages on vacant parcels
/// where couples wait for a home or households are crowded; they clear new fields from woodland where a settlement has too few arable acres a head; and a person opens a
/// new business where a good is wanted and unsupplied. Materials are bought at the market (or poles cut in the woodland), labour is the household's own and hired idle hands
/// paid as real transfers, and every build takes at least a season. A finished cottage is a lot with beds in the plan, which marriage then uses; a clearing is a parcel of
/// fields farming then works. Deterministic: ascending loops, counter RNG only, integers only.
/// </summary>
public sealed class GrowthSystem
{
    private readonly EconomyWorld _ew;
    private SimWorld W => _ew.World;
    private RegionData Region => W.Population.Region;
    private GrowthTuningDef T => _ew.Content.Growth;
    public GrowthState State { get; private set; } = new();
    /// <summary>Where visited chunks hear of new buildings and fields; null in a world with no chunks (the headless soak).</summary>
    public IGrowthChunkSink? ChunkSink { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>P7-EA3e: new crews of charcoal burners when charcoal is dear (EA_F19). Tests whose rig keeps the books' charcoal up with its own faucet switch it off.</summary>
    public bool CharcoalCrews { get; set; } = true;
    /// <summary>P5-A2: lots play holds or lets (a bought vacant house, a leased one): growth does not refit them. Set by the land wiring; null: none.</summary>
    public Func<int, int, bool>? LandHolds { get; set; }
    /// <summary>P5-D: growth made visible: a project on a plot the player has seen is a real building site (build-mode-and-construction 4.9). Set by the construction's wiring; null: Phase 3.</summary>
    public IGrowthSites? Sites { get; set; }
    // P5-D: what the visible sites share with the seasonal step (the same rules for buying, digging and paying)
    internal EconomyWorld Economy => _ew;
    public GrowthTuningDef Tuning => T;
    internal long ReserveMites => Reserve;
    internal void TopUp(int hh, int settlement, long need) => EnsureCoin(hh, settlement, need);
    internal List<int> IdleHands(int settlement, int day) { _hands = null; return HandsOf(settlement, day); }
    internal long DigClay(int settlement, long g, int day) => DrawClay(settlement, g, day);
    public int LordOf(int settlement) => LordHousehold(settlement);
    internal void Complete(BuildProject p, GameTime now, RegionOutbox ob) => CompleteCottage(p, now, ob);

    /// <summary>P5-D: premises built (a visible site finished, or one that could not be laid out): the lot is added as Phase 3 adds it and the business moves in.</summary>
    internal void CompletePremises(BuildProject p, GameTime now)
    {
        var rec = new LotRecord(p.Settlement, Region.Settlements[p.Settlement].Plan.Lots.Count, true, p.Use, DwellingType.None, p.Blueprint, p.Rect, p.Facing, 1, 0, (int)now.TotalDays);
        RegionGrowth.Change(Region, ChunkSink, [Pad(p.Rect)], () => RegionGrowth.AddLot(Region, W.Population, rec));
        State.Lots.Add(rec); State.Year(now.Year).PlotsGiven++;
        foreach (var b in _ew.Production.Businesses)
            if (b.Kind == p.BusinessKind && b.Settlement == p.Settlement && b.Owner == p.Owner && b.State == BusinessState.Open) { b.Lot = rec.Lot; break; }
    }
    private readonly GoodCatalog _cat;

    public GrowthSystem(EconomyWorld ew) { _ew = ew; _cat = ew.World.Economy.Goods; }

    /// <summary>Adopts a state read from a save (the region has already been replayed): marks the cleared rows of the resource layer again.</summary>
    public void Attach(GrowthState state)
    {
        State = state;
        foreach (int t in state.ClearedTiles) { int row = _ew.Resources.Find(t, ResourceKind.Wood); if (row >= 0) _ew.Resources.MarkCleared(row); }
    }

    private static readonly int[] None = [];

    // ---------------------------------------------------------------- the season

    /// <summary>The seasonal step, one part an hour of the season's first day so no hour carries it all (decision G17): hours 0 to 3 advance the projects (a quarter each, by serial), 4 starts cottages, 5 clearings, 6 businesses, 7 moves to town; <paramref name="step"/> -1 runs them all (tests).</summary>
    public void Seasonal(GameTime now, RegionOutbox ob, int step = -1)
    {
        if (!Enabled) return;
        var pt = W.People; var hht = W.Households;
        _hands = null;
        _popBy = new int[Region.Settlements.Count];
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0 && (uint)hht.Settlement[h] < (uint)_popBy.Length) _popBy[hht.Settlement[h]] += hht.MemberCount[h];
        if (step is -1 or (>= 0 and < 4))
            for (int i = 0; i < State.Projects.Count; i++)
            {
                var p = State.Projects[i];
                if (step >= 0 && p.Serial % 4 != step) continue;
                if (Advance(p, now, ob)) { State.Projects.RemoveAt(i); i--; }
            }
        if (step is -1 or 4) StartCottages(now, ob);
        if (step is -1 or 5) StartClearings(now, ob);
        if (step is -1 or 6) OpenBusinesses(now, ob);
        if (step is -1 or 7) MoveToTowns(now, ob);
    }

    private int[] _popBy = [];
    private bool[] _coalDear = [];

    private int ClusterOf(int s) { int f = Region.Settlements[s].FiefIndex; return f >= 0 && f < Region.Fiefs.Count ? f : Region.Fiefs.Count; }

    // ---------------------------------------------------------------- people and coin

    private List<int>[]? _hands;
    /// <summary>Idle adults (16 to 59, no post, no employer, not on a business's books, not gentry) of each settlement, ascending.</summary>
    private List<int> HandsOf(int s, int day)
    {
        if (_hands == null)
        {
            var pt = W.People; var hht = W.Households; _hands = new List<int>[Region.Settlements.Count];
            for (int i = 0; i < _hands.Length; i++) _hands[i] = [];
            for (int i = 0; i < pt.Count; i++)
            {
                if (pt.Status[i] != PersonStatus.Alive || pt.Employer[i] >= 0 || pt.Occupation[i] is not (Occupation.None or Occupation.Labourer)) continue;
                int hh = pt.Household[i]; if (hh < 0 || hht.Class[hh] == SocialClass.Gentry || _ew.Labour.IsStaff(i)) continue;
                int age = pt.AgeYears(i, day); if (age < 16 || age >= 60) continue;
                int hs = pt.HomeSettlement[i]; if ((uint)hs < (uint)_hands.Length) _hands[hs].Add(i);
            }
        }
        return _hands[s];
    }

    private long Balance(int hh) => W.Economy.Ledger.Balance(W.Economy.HouseholdPurse[hh]).Mites;

    private int LordHousehold(int settlement)
    {
        int f = Region.Settlements[settlement].FiefIndex;
        return f >= 0 && f < W.Population.FiefLordHousehold.Length ? W.Population.FiefLordHousehold[f] : -1;
    }

    /// <summary>Tops a lord's purse up from his fief's treasury when it falls short of <paramref name="need"/> (the treasury advances the works of the fief, reason Stipend).</summary>
    private void EnsureCoin(int hh, int settlement, long need)
    {
        long bal = Balance(hh); if (bal >= need || LordHousehold(settlement) != hh) return;
        int f = Region.Settlements[settlement].FiefIndex;
        var e = W.Economy;
        if (f >= 0 && e.Ledger.TryFind(PurseKind.Treasury, Region.Fiefs[f].Id, out var t))
        {
            long adv = Math.Min(need - bal, e.Ledger.Balance(t).Mites / 4);
            if (adv > 0) e.Ledger.Transfer(t, e.HouseholdPurse[hh], new Coin(adv), TransferReason.Stipend);
        }
    }

    private long Reserve => (long)T.BuilderReserveDays * 150;
    private static int Wobble(ulong seed, long hour, int serial, int pct) => DetRng.Range(seed, hour, (ulong)serial, RngStreams.GrowthWork, 0, 100 - pct, 100 + pct + 1);

    // ---------------------------------------------------------------- cottages: deciding

    private (int Waiting, int Crowded, int Free, int Projects)[] ClusterCounts(int day)
    {
        var pop = W.Population; var pt = W.People; var hht = W.Households; var rates = W.Rates;
        int nc = Region.Fiefs.Count + 1; var c = new (int Waiting, int Crowded, int Free, int Projects)[nc];
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.Sex[p] != Sex.Female || pt.Spouse[p] >= 0 || pt.MarriageWait[p] == 0) continue;
            int hh = pt.Household[p]; if (hh < 0) continue;
            c[ClusterOf(hht.Settlement[hh])].Waiting++;
        }
        for (int h = 0; h < hht.Count; h++)
            if (hht.MemberCount[h] > T.AdultsPerHouseholdMax && AdultsIn(h, day) > T.AdultsPerHouseholdMax) c[ClusterOf(hht.Settlement[h])].Crowded++;      // P8-BL-1: more than six adults, not a bed count
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            var lots = Region.Settlements[s].Plan.Lots; int cl = ClusterOf(s);
            for (int l = 0; l < lots.Count; l++)
                if (pop.LotBeds[s][l] >= 2 && pop.LotHousehold[s][l] < 0 && lots[l].IsDwelling && lots[l].Dwelling != DwellingType.Manor) c[cl].Free++;
        }
        foreach (var pr in State.Projects) if (pr.Kind == ProjectKind.Cottage) c[ClusterOf(pr.Settlement)].Projects++;
        return c;
    }

    /// <summary>The living members of a household aged 16 and over.</summary>
    private int AdultsIn(int h, int day)
    {
        var pt = W.People; int n = 0;
        foreach (int m in W.Households.Members(h)) if (pt.Status[m] == PersonStatus.Alive && pt.AgeYears(m, day) >= 16) n++;
        return n;
    }

    private int BaseDwellings()
    {
        int dw = 0; foreach (var s in Region.Settlements) dw += s.Plan.Lots.Count(l => l.IsDwelling);
        return dw - State.Lots.Count(l => l.Dwelling != DwellingType.None);
    }

    private void StartCottages(GameTime now, RegionOutbox ob)
    {
        int day = (int)now.TotalDays; var yr = State.Year(now.Year);
        long alive = W.Vital.Alive;
        int cap = (int)Math.Max(1, alive * T.MaxStartsPerYearPerTenThousand / 10_000);
        int landCap = Math.Max(1, BaseDwellings() * T.MaxNewDwellingsPctOfBase / 100);
        var counts = ClusterCounts(day);
        for (int cl = 0; cl < counts.Length; cl++)
        {
            var (waiting, crowded, free, inProg) = counts[cl];
            int need = waiting / Math.Max(1, T.WaitingPerCottage) + crowded / Math.Max(1, T.CrowdedPerCottage) - free - inProg;
            int starts = 0;
            while (need > 0 && starts < T.MaxStartsPerClusterSeason && yr.CottagesStarted < cap
                   && State.Lots.Count(l => l.Dwelling != DwellingType.None) + State.Projects.Count(p => p.Kind == ProjectKind.Cottage) < landCap)
            {
                if (!TryStartCottage(cl, now, ob)) break;
                starts++; need--; yr.CottagesStarted++;
            }
        }
    }

    private bool TryStartCottage(int cluster, GameTime now, RegionOutbox ob)
    {
        var pt = W.People; var hht = W.Households; int day = (int)now.TotalDays;
        // the settlements of the cluster, those with the most waiting women first
        var waitBy = new Dictionary<int, int>();
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.Sex[p] != Sex.Female || pt.Spouse[p] >= 0 || pt.MarriageWait[p] == 0) continue;
            int hh = pt.Household[p]; if (hh < 0) continue; int s = hht.Settlement[hh];
            if (ClusterOf(s) == cluster) waitBy[s] = waitBy.GetValueOrDefault(s) + 1;
        }
        var order = new List<int>();
        for (int s = 0; s < Region.Settlements.Count; s++) if (ClusterOf(s) == cluster && Region.Settlements[s].Class != SettlementClass.Farmstead) order.Add(s);
        order.Sort((a, b) => { int c = waitBy.GetValueOrDefault(b).CompareTo(waitBy.GetValueOrDefault(a)); return c != 0 ? c : a.CompareTo(b); });
        foreach (int s in order)
        {
            var st = Region.Settlements[s]; bool town = st.Class == SettlementClass.Town;
            var claimed = new HashSet<int>(State.Projects.Where(p => p.Settlement == s && p.Lot >= 0).Select(p => p.Lot));
            if (LandHolds != null) for (int l = 0; l < st.Plan.Lots.Count; l++) if (st.Plan.Lots[l].Vacant && LandHolds(s, l)) claimed.Add(l);     // P5-A2: a vacant house bought or let
            var reserved = State.Projects.Where(p => p.Settlement == s && p.Kind != ProjectKind.Assart && p.Lot < 0).Select(p => p.Rect).ToList();
            int lot = RegionGrowth.FindVacantLot(Region, s, claimed);
            var pr = new BuildProject { Serial = State.NextSerial, Kind = ProjectKind.Cottage, Settlement = s, StartDay = day, Lot = lot };
            if (lot >= 0)
            {
                var l = st.Plan.Lots[lot]; pr.Use = l.Use; pr.Dwelling = l.Dwelling; pr.Blueprint = l.BlueprintId; pr.Rect = l.Rect; pr.Facing = l.Facing; pr.Wealth = l.Wealth;
            }
            else
            {
                int w = town ? T.PlotWidthTownM : T.PlotWidthVillageM, d = town ? T.PlotDepthTownM : T.PlotDepthVillageM;
                if (!RegionGrowth.FindPlot(Region, s, w, d, T.PlanMarginM, reserved, out var rect, out byte facing)) continue;
                pr.Rect = rect; pr.Facing = facing;
                pr.Use = town ? LotUse.Burgage : LotUse.Cottage; pr.Dwelling = town ? DwellingType.Burgage : DwellingType.Cottage;
                pr.Blueprint = town ? "burgage_townhouse" : "peasant_cottage"; pr.Wealth = (byte)(town ? 1 : 0);
            }
            pr.Beds = pr.Dwelling switch { DwellingType.Burgage => T.BurgageBeds, DwellingType.Longhouse => T.LonghouseBeds, _ => T.CottageBeds };
            int pct = pr.Dwelling == DwellingType.Burgage ? T.BurgagePct : 100; if (lot >= 0) pct = pct * T.VacantRepairPct / 100;
            pr.HoursNeeded = (long)T.CottageWorkerHours * pct / 100 * Wobble(W.Seed, now.TotalHours, pr.Serial, 10) / 100;
            var lines = T.CottageMaterials; pr.MaterialG = new long[lines.Count];
            long cost = pr.HoursNeeded * RecipeBook.WagePerHour(WageClass.Unskilled) * 7 / 10;
            var book = _ew.Markets.BookForSettlement(s);
            for (int i = 0; i < lines.Count; i++) cost += (long)(lines[i].Kg * (long)pct / 100 * book.RefMitesPerKg(_cat.IdOf(lines[i].Good)));
            int payer = ChoosePayer(s, cluster, cost, waitBy.Count > 0);
            if (payer < 0) continue;
            pr.Payer = payer; State.NextSerial++; State.Projects.Add(pr);
            Sites?.Started(pr, now);                                                                // P5-D: a plot the player has seen becomes a site at once
            return true;
        }
        return false;
    }

    /// <summary>The household that pays for a build in settlement <paramref name="s"/>: a waiting woman's household that can afford it (lowest woman first), else the fief's lord, helped by the treasury; -1 when nobody can.</summary>
    private int ChoosePayer(int s, int cluster, long cost, bool anyWaiting)
    {
        var pt = W.People; var hht = W.Households; long need = cost + Reserve;
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.Sex[p] != Sex.Female || pt.Spouse[p] >= 0 || pt.MarriageWait[p] == 0) continue;
            int hh = pt.Household[p]; if (hh < 0 || hht.Settlement[hh] != s || hht.Class[hh] == SocialClass.Gentry) continue;
            if (Balance(hh) >= need) return hh;
        }
        int lord = LordHousehold(s);
        if (lord >= 0 && hht.MemberCount[lord] > 0)
        {
            EnsureCoin(lord, s, need);
            if (Balance(lord) >= need) return lord;
        }
        return -1;
    }

    // ---------------------------------------------------------------- advancing a project

    /// <summary>One season of a project; true when it is finished or given up.</summary>
    private bool Advance(BuildProject p, GameTime now, RegionOutbox ob)
    {
        var hht = W.Households; int day = (int)now.TotalDays;
        if (Sites != null && Sites.Advance(p, now, ob, out bool siteDone)) return siteDone;      // P5-D: a project that is a building site
        if (p.Kind == ProjectKind.Premises) return true;
        if (p.Payer < 0 || p.Payer >= hht.Count || hht.MemberCount[p.Payer] == 0) { State.Year(now.Year); return true; }     // the payer is gone: given up
        var led = W.Economy.Ledger; var e = W.Economy;
        long wage = RecipeBook.WagePerHour(WageClass.Unskilled);
        int seasonsOld = (day - p.StartDay) / TimeConst.DaysPerSeason;
        long allowed = p.HoursNeeded;
        if (p.Kind == ProjectKind.Cottage)
        {
            BuyMaterials(p, now);
            long frac = 1000; for (int i = 0; i < p.MaterialG.Length; i++) { long need = MaterialNeedG(p, i); frac = Math.Min(frac, need == 0 ? 1000 : p.MaterialG[i] * 1000 / need); }
            allowed = p.HoursNeeded * frac / 1000;
        }
        int crewMax = p.Kind == ProjectKind.Cottage ? T.CrewMax : T.AssartCrewMax;
        int handHours = (p.Kind == ProjectKind.Cottage ? T.HiredHoursPerHandDay : T.AssartHoursPerHandDay) * TimeConst.DaysPerSeason;
        long own = 0;
        if (p.Kind == ProjectKind.Cottage && hht.Class[p.Payer] != SocialClass.Gentry)
            foreach (int m in hht.Members(p.Payer)) { if (W.People.Status[m] == PersonStatus.Alive) { int a = W.People.AgeYears(m, day); if (a >= 14 && a < 60) own += T.OwnHoursPerMemberDay * TimeConst.DaysPerSeason; } }
        long room = Math.Max(0, allowed - p.HoursDone);
        long ownUsed = Math.Min(own, room); room -= ownUsed;
        long hired = 0;
        if (room > 0)
        {
            EnsureCoin(p.Payer, p.Settlement, room * wage + Reserve);
            var hands = HandsOf(p.Settlement, day);
            int used = 0;
            for (int k = 0; k < hands.Count && used < crewMax && room > 0; k++)
            {
                int person = hands[k]; int hh = W.People.Household[person]; if (hh == p.Payer) continue;
                long hrs = Math.Min(handHours, room);
                long pay = hrs * wage;
                long spare = Balance(p.Payer) - Reserve / 2;
                if (spare < wage) break;
                if (pay > spare) { hrs = spare / wage; pay = hrs * wage; }
                if (hrs <= 0 || !led.Transfer(e.HouseholdPurse[p.Payer], e.HouseholdPurse[hh], new Coin(pay), TransferReason.Wage)) continue;
                hired += hrs; room -= hrs; used++; p.PaidMites += pay; State.Year(now.Year).WagesMites += pay;
            }
        }
        long did = ownUsed + hired;
        if (did == 0) p.StalledSeasons++; else p.StalledSeasons = 0;
        p.HoursDone += did;
        if (p.StalledSeasons >= T.GiveUpSeasons) return true;
        bool matsOk = p.Kind != ProjectKind.Cottage || MaterialsComplete(p);
        if (p.HoursDone < p.HoursNeeded || !matsOk || seasonsOld < T.MinSeasons) return false;
        if (p.Kind == ProjectKind.Cottage) CompleteCottage(p, now, ob); else return CompleteClearing(p, now, ob);
        return true;
    }

    private long MaterialNeedG(BuildProject p, int i)
    {
        int pct = p.Dwelling == DwellingType.Burgage ? T.BurgagePct : 100; if (IsRefit(p)) pct = pct * T.VacantRepairPct / 100;
        return (long)T.CottageMaterials[i].Kg * 1000 * pct / 100;
    }
    /// <summary>A vacant house taken up again (cheaper): a project on a lot that already has its house. A bare plot a household bought (blueprint "-") is built in full.</summary>
    private bool IsRefit(BuildProject p) => p.Lot >= 0 && Region.Settlements[p.Settlement].Plan.Lots[p.Lot].BlueprintId != Ravensguard.Sim.Land.LandWorld.BarePlot;

    /// <summary>
    /// P6-D4 (holdings 3.3): a household that bought a lot (a bare plot of the lord's or a vacant house) puts up the house on it: the project is the cottage's, on that lot, paid by the buyer. False when a project
    /// already stands on the lot or the lot is not there. The buyer's reserve and hands are Advance's affair, as for every cottage.
    /// </summary>
    public bool StartOn(int s, int lot, int payer, GameTime now)
    {
        if ((uint)s >= (uint)Region.Settlements.Count || (uint)lot >= (uint)Region.Settlements[s].Plan.Lots.Count || payer < 0) return false;
        if (State.Projects.Any(p => p.Settlement == s && p.Lot == lot)) return false;
        var st = Region.Settlements[s]; var l = st.Plan.Lots[lot]; bool town = st.Class == SettlementClass.Town; bool bare = l.BlueprintId == Ravensguard.Sim.Land.LandWorld.BarePlot;
        if (!bare && !(l.Vacant && l.Dwelling is DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage)) return false;
        var pr = new BuildProject { Serial = State.NextSerial, Kind = ProjectKind.Cottage, Settlement = s, StartDay = (int)now.TotalDays, Lot = lot, Rect = l.Rect, Facing = l.Facing, Payer = payer };
        if (bare) { pr.Use = town ? LotUse.Burgage : LotUse.Cottage; pr.Dwelling = town ? DwellingType.Burgage : DwellingType.Cottage; pr.Blueprint = town ? "burgage_townhouse" : "peasant_cottage"; pr.Wealth = (byte)(town ? 1 : 0); }
        else { pr.Use = l.Use; pr.Dwelling = l.Dwelling; pr.Blueprint = l.BlueprintId; pr.Wealth = l.Wealth; }
        pr.Beds = pr.Dwelling switch { DwellingType.Burgage => T.BurgageBeds, DwellingType.Longhouse => T.LonghouseBeds, _ => T.CottageBeds };
        int pct = pr.Dwelling == DwellingType.Burgage ? T.BurgagePct : 100; if (!bare) pct = pct * T.VacantRepairPct / 100;
        pr.HoursNeeded = (long)T.CottageWorkerHours * pct / 100 * Wobble(W.Seed, now.TotalHours, pr.Serial, 10) / 100;
        pr.MaterialG = new long[T.CottageMaterials.Count];
        State.NextSerial++; State.Projects.Add(pr);
        return true;
    }

    private bool MaterialsComplete(BuildProject p) { for (int i = 0; i < p.MaterialG.Length; i++) if (p.MaterialG[i] < MaterialNeedG(p, i)) return false; return true; }

    /// <summary>
    /// Gets what is still missing and builds it in (consumed as it comes). Poles are cut in the settlement's woodland, daub is dug from the clay pits near it and thatch
    /// is bought from the commons' straw (the reeve sells it for the fief's treasury at the book's reference price); what these cannot give the payer buys at the market.
    /// Gathering costs hours (<see cref="GrowthTuningDef.GatherKgPerHour"/>), added to the build.
    /// </summary>
    private void BuyMaterials(BuildProject p, GameTime now)
    {
        var e = W.Economy; int day = (int)now.TotalDays; var book = _ew.Markets.BookForSettlement(p.Settlement);
        var party = _ew.Markets.HouseholdParty(p.Payer); var lines = T.CottageMaterials; int fief = Region.Settlements[p.Settlement].FiefIndex;
        for (int i = 0; i < lines.Count; i++)
        {
            long need = MaterialNeedG(p, i) - p.MaterialG[i]; if (need <= 0) continue;
            ushort good = _cat.IdOf(lines[i].Good); long got = 0;
            if (lines[i].Good == "logs" && p.MaterialG[i] == 0) { got = _ew.Woodland.Draw(p.Settlement, need * T.PolesFromWoodlandPct / 100, day); State.PolesGatheredG += got; }
            else if (lines[i].Good == "clay") got = DrawClay(p.Settlement, need, day);
            else if (lines[i].Good is "thatch" or "straw" && e.OtherStores.TryGetValue(Ravensguard.Sim.Farming.FarmIds.Fodder(p.Settlement), out var fodder))
            {
                ushort straw = _cat.IdOf("straw"); long take = Math.Min(need, fodder.Qty(straw) / 7);
                if (take > 0)
                {
                    long price = (long)(take / 1000.0 * book.RefMitesPerKg(straw)); price = Math.Min(price, Math.Max(0, Balance(p.Payer) - Reserve / 2));
                    take = price <= 0 ? 0 : Math.Min(take, take);
                    if (take > 0 && fodder.Destroy(straw, take, DestroyReason.Consumed))
                    {
                        got = take;
                        if (price > 0 && fief >= 0 && e.Ledger.TryFind(PurseKind.Treasury, Region.Fiefs[fief].Id, out var tr)) e.Ledger.Transfer(e.HouseholdPurse[p.Payer], tr, new Coin(price), TransferReason.Rent);
                        State.Year(now.Year).MaterialsMites += price;
                    }
                }
            }
            if (got > 0) { p.MaterialG[i] += got; need -= got; p.HoursNeeded += got / 1000 / Math.Max(1, T.GatherKgPerHour); }
            if (need <= 0) continue;
            long limit = Math.Max(0, Balance(p.Payer) - Reserve / 2);
            if (limit <= 0) continue;
            var res = book.Buy(party, good, need, 3000, day, limit);
            if (res.Qty <= 0) continue;
            if (!e.HouseholdStore[p.Payer].Destroy(good, res.Qty, DestroyReason.Consumed)) throw new InvalidOperationException("bought materials vanished");
            p.MaterialG[i] += res.Qty; State.Year(now.Year).MaterialsMites += res.PaidMites;
        }
    }

    private readonly Dictionary<int, int[]> _clayRows = [];
    /// <summary>Digs up to <paramref name="g"/> grams of clay from the pits (clay rows) within 3 km of the settlement, nearest first.</summary>
    private long DrawClay(int s, long g, int day)
    {
        if (!_clayRows.TryGetValue(s, out var rows))
        {
            var st = Region.Settlements[s]; int N = RegionData.Size; int tx = st.CenterX / RegionData.TileM, ty = st.CenterY / RegionData.TileM, rad = 3000 / RegionData.TileM;
            var list = new List<(long D2, int Row)>();
            for (int y = Math.Max(0, ty - rad); y <= Math.Min(N - 1, ty + rad); y++)
                for (int x = Math.Max(0, tx - rad); x <= Math.Min(N - 1, tx + rad); x++)
                {
                    int row = _ew.Resources.Find(y * N + x, ResourceKind.Clay);
                    if (row >= 0) list.Add(((long)(x - tx) * (x - tx) + (long)(y - ty) * (y - ty), row));
                }
            _clayRows[s] = rows = [.. list.OrderBy(v => v.D2).ThenBy(v => v.Row).Select(v => v.Row)];
        }
        long got = 0;
        foreach (int row in rows) { if (got >= g) break; got += _ew.Resources.Take(row, g - got, day); }
        return got;
    }

    // ---------------------------------------------------------------- cottages: finishing

    private void CompleteCottage(BuildProject p, GameTime now, RegionOutbox ob)
    {
        var pop = W.Population; var yr = State.Year(now.Year);
        bool vacant = p.Lot >= 0;
        int lot = vacant ? p.Lot : Region.Settlements[p.Settlement].Plan.Lots.Count;
        var rec = new LotRecord(p.Settlement, lot, !vacant, p.Use, p.Dwelling, p.Blueprint, p.Rect, p.Facing, p.Wealth, p.Beds, (int)now.TotalDays);
        RegionGrowth.Change(Region, ChunkSink, [Pad(p.Rect)], () => RegionGrowth.AddLot(Region, pop, rec));
        State.Lots.Add(rec);
        if (vacant) yr.VacantRefitted++; else yr.PlotsGiven++;
        yr.CottagesBuilt++;
        ob.Count("cottages_built");
        var hht = W.Households;
        ob.Chronicle(now, "cottage_built", [hht.IdOf(p.Payer)], [Why.Of(CauseKind.Migration, "ch_why_cottage", ChronParam.Person("owner", hht.IdOf(p.Payer)), ChronParam.Place("place", Region.Settlements[p.Settlement].Id))],
            ("settlement", Region.Settlements[p.Settlement].Name), ("kind", p.Dwelling.ToString()), ("hours", p.HoursNeeded.ToString()), ("paid", p.PaidMites.ToString()));
    }

    private static RectM Pad(RectM q) => new(q.X - 6, q.Y - 6, q.W + 12, q.H + 12);

    // ---------------------------------------------------------------- assarting

    private void StartClearings(GameTime now, RegionOutbox ob)
    {
        int day = (int)now.TotalDays; var yr = State.Year(now.Year);
        var acres = new int[Region.Settlements.Count];
        for (int s = 0; s < acres.Length; s++) foreach (int p in Region.Settlements[s].Parcels) acres[s] += Region.Parcels[p].FieldCount;
        var todo = new List<(long Deficit, int S)>();
        for (int s = 0; s < acres.Length; s++)
        {
            if (acres[s] == 0 && Region.Settlements[s].Class == SettlementClass.Farmstead) { }
            long want = (long)_popBy[s] * T.TargetAcresPerPersonX10, have = (long)acres[s] * 10;
            if (have < want) todo.Add((want - have, s));
        }
        todo.Sort((a, b) => a.Deficit != b.Deficit ? b.Deficit.CompareTo(a.Deficit) : a.S.CompareTo(b.S));
        int started = 0; long wage = RecipeBook.WagePerHour(WageClass.Unskilled);
        var claimed = new HashSet<int>(); foreach (var pr in State.Projects) if (pr.Kind == ProjectKind.Assart) foreach (int t in pr.Tiles) claimed.Add(t);
        foreach (var (_, s) in todo)
        {
            if (started >= T.AssartMaxStartsPerSeason) break;
            if (State.Projects.Any(p => p.Kind == ProjectKind.Assart && p.Settlement == s)) continue;
            int lord = LordHousehold(s); if (lord < 0 || W.Households.MemberCount[lord] == 0) continue;
            var tiles = RegionGrowth.FindClearing(Region, s, T.AssartTilesPerProject, T.AssartMinFertility, T.AssartMaxSlopePermille, T.AssartMaxRadiusM, claimed);
            if (tiles.Length == 0) continue;
            var pr = new BuildProject { Serial = State.NextSerial, Kind = ProjectKind.Assart, Settlement = s, StartDay = day, Payer = lord, Tiles = tiles };
            pr.HoursNeeded = (long)tiles.Length * T.AssartHoursPerAcre * Wobble(W.Seed, now.TotalHours, pr.Serial, 10) / 100;
            long cost = pr.HoursNeeded * wage;
            EnsureCoin(lord, s, cost + Reserve);
            if (Balance(lord) < cost + Reserve) continue;
            State.NextSerial++; State.Projects.Add(pr); foreach (int t in tiles) claimed.Add(t); started++; yr.ClearingsStarted++;
        }
    }

    private bool CompleteClearing(BuildProject p, GameTime now, RegionOutbox ob)
    {
        var r = Region; int N = RegionData.Size; int s = p.Settlement; var yr = State.Year(now.Year);
        var tiles = p.Tiles.Where(t => r.ParcelMap[t] == 0 && r.Built[t] == 0).ToArray();
        if (tiles.Length == 0) return true;
        var farming = _ew.Farming; var st = r.Settlements[s]; int fief = st.FiefIndex;
        if (State.FertilityScalePermille == 0)
        {
            long a = 0, b = 0; var F = farming.State.Fields;
            for (int i = 0; i < F.Count; i++) { a += F.Fertility[i]; b += r.Fertility[F.Tile[i]]; }
            State.FertilityScalePermille = b == 0 ? 1000 : (int)Math.Clamp(a * 1000 / b, 500, 4000);
        }
        var fert = new byte[tiles.Length];
        for (int i = 0; i < tiles.Length; i++) fert[i] = (byte)Math.Clamp(r.Fertility[tiles[i]] * (long)State.FertilityScalePermille / 1000, 0, 100);
        // the holder lot of the new holding: the settlement's first dwelling lot
        int holderLot = 0; for (int l = 0; l < st.Plan.Lots.Count; l++) if (st.Plan.Lots[l].IsDwelling && st.Plan.Lots[l].Dwelling != DwellingType.Manor) { holderLot = l; break; }
        var owner = new ParcelOwner(st.Class == SettlementClass.Town ? OwnerKind.Burgher : OwnerKind.Tenant, fief, holderLot);
        var rec = new ParcelRecord(s, owner, 2, tiles, fert, (int)now.TotalDays);
        var rects = tiles.Select(t => new RectM(t % N * RegionData.TileM, t / N * RegionData.TileM, RegionData.TileM, RegionData.TileM)).ToList();
        // the wood comes off the woodland rows and is taken to market as firewood and logs
        long wood = 0; int day = (int)now.TotalDays;
        int pidx = RegionGrowth.Change(r, ChunkSink, rects.Select(q => new RectM(q.X - RegionData.TileM, q.Y - RegionData.TileM, q.W + 2 * RegionData.TileM, q.H + 2 * RegionData.TileM)).ToList(), () =>
        {
            foreach (int t in tiles)
            {
                int row = _ew.Resources.Find(t, ResourceKind.Wood);
                if (row >= 0) { wood += _ew.Resources.Clear(row, day); State.ClearedTiles.Add(t); }
            }
            State.ClearedTiles.Sort();
            int pi = RegionGrowth.ApplyParcel(r, rec);
            farming.AddParcels([pi], p.Payer);
            return pi;
        });
        State.Parcels.Add(rec);
        if (wood > 0)
        {
            var store = W.Economy.HouseholdStore[p.Payer]; var food = W.Economy.Food;
            long fire = wood * T.AssartFirewoodPct / 100, logs = wood - fire;
            store.Create(food.Firewood, fire, CreateReason.Cut); store.Create(_cat.IdOf("logs"), logs, CreateReason.Cut);
            State.WoodDrawnG += wood; State.FirewoodMadeG += fire; State.LogsMadeG += logs;
        }
        yr.AcresCleared += tiles.Length;
        ob.Count("acres_assarted", tiles.Length);
        ob.Chronicle(now, "land_cleared", [W.Households.IdOf(p.Payer)], [Why.Of(CauseKind.Famine, "ch_why_assart", ChronParam.Place("place", st.Id))],
            ("settlement", st.Name), ("acres", tiles.Length.ToString()), ("wood_kg", (wood / 1000).ToString()));
        return true;
    }

    // ---------------------------------------------------------------- new businesses

    private void OpenBusinesses(GameTime now, RegionOutbox ob)
    {
        var prod = _ew.Production; var pt = W.People; var hht = W.Households; int day = (int)now.TotalDays; var yr = State.Year(now.Year);
        var kinds = _ew.Content.Kinds; int opened = 0;
        // P7-EA3e: where charcoal is dear is a town (it burns most and has no woods), where the burners can go is a village by the woods: so the signal is the fief's, the dearest book of the fief
        var coalDear = new bool[Region.Fiefs.Count + 1]; var dearAt = new int[Region.Fiefs.Count + 1]; var dearBy = new int[Region.Fiefs.Count + 1];
        if (CharcoalCrews && _cat.TryIdOf("charcoal", out var chId))
            foreach (var bk in _ew.Markets.AllBooks())
            {
                int ratio = bk.MidRatioPermille(chId), cl = ClusterOf(bk.SettlementIndex);
                if (bk.Goods[chId].Demand > 0 && ratio >= T.UnmetPricePermille && (!coalDear[cl] || ratio > dearBy[cl])) { coalDear[cl] = true; dearBy[cl] = ratio; dearAt[cl] = bk.SettlementIndex; }
            }
        _coalDear = coalDear;
        var wanted = new List<(int Seasons, long Near, int S, int K)>();
        foreach (var book in _ew.Markets.AllBooks())
        {
            int s = book.SettlementIndex;
            foreach (var kind in kinds)
            {
                if (kind.Trader || kind.FoundedOnly || kind.Stations.Length == 0 || kind.Extractions.Length > 0 || kind.Id is "mine" or "manor" or "mill" or "woodlot" or "fishery" || kind.Recipes.Length == 0) continue;
                long key = (long)s * 4096 + kind.Index; bool want = false;
                if (kind.Id == "charcoal_crew") want = CharcoalCrews && CharcoalWanted(book, s, day);                // P7-EA3e: supply follows the price (EA_F19)
                else foreach (var r in kind.Recipes)
                {
                    var gb = book.Goods[r.Outputs[0].Good];
                    if (gb.Demand > 0 && book.MidRatioPermille(r.Outputs[0].Good) >= T.UnmetPricePermille && gb.StockUnits < gb.Demand) { want = true; break; }
                }
                if (!want) { State.Unmet.Remove(key); continue; }
                int n = State.Unmet.GetValueOrDefault(key) + 1; State.Unmet[key] = n;
                if (n < T.UnmetSeasons) continue;
                long near = 0;                                                                         // a new crew goes to the woods nearest the dearest book of its fief: its charcoal has the shortest way to carry
                if (kind.Id == "charcoal_crew") { var a = Region.Settlements[s]; var d = Region.Settlements[dearAt[ClusterOf(s)]]; long dx = a.CenterX - d.CenterX, dy = a.CenterY - d.CenterY; near = dx * dx + dy * dy; }
                wanted.Add((n, near, s, kind.Index));
            }
        }
        wanted.Sort((a, b) => a.Seasons != b.Seasons ? b.Seasons.CompareTo(a.Seasons) : a.Near != b.Near ? a.Near.CompareTo(b.Near) : a.S != b.S ? a.S.CompareTo(b.S) : a.K.CompareTo(b.K));
        int coalOpened = 0, crews = 0;
        foreach (var b in prod.Businesses) if (b.State == BusinessState.Open && kinds[b.Kind].Id == "charcoal_crew") crews++;
        foreach (var (_, _, s, k) in wanted)
        {
            var kind = kinds[k]; bool coal = kind.Id == "charcoal_crew";
            if (coal ? coalOpened >= T.CharcoalCrewsPerSeason || crews >= T.CharcoalCrewsMax : opened >= T.BusinessMaxPerSeason) continue;
            int open = 0; bool closedHere = false;
            foreach (var b in prod.Businesses) if (b.Kind == k && b.Settlement == s) { if (b.State == BusinessState.Open) open++; else closedHere = true; }
            if (closedHere || open >= Math.Max(1, _popBy[s] / Math.Max(1, T.PeoplePerBusiness))) continue;
            int owner = ChooseOwner(s, kind, day);
            if (owner < 0) continue;
            int ohh = pt.Household[owner];
            int lot = -1; var claimed = new HashSet<int>(); BuildProject? premises = null;
            if (kind.Lot is { } use)
            {
                bool town = Region.Settlements[s].Class == SettlementClass.Town;
                if (!RegionGrowth.FindPlot(Region, s, town ? T.BusinessPlotWidthTownM : T.BusinessPlotWidthVillageM, town ? T.BusinessPlotDepthTownM : T.BusinessPlotDepthVillageM, T.PlanMarginM, State.Projects.Where(p => p.Settlement == s && p.Lot < 0).Select(p => p.Rect).ToList(), out var rect, out byte facing)) continue;
                var hands = HandsOf(s, day); long pay = T.PremisesMites;
                if (Balance(ohh) < pay + T.BusinessMinOwnerMites / 2) continue;
                string bp = use switch { LotUse.Smithy => "smithy", LotUse.Tavern => "tavern", _ => "" };
                if (Sites != null && Sites.Shows(rect))
                {
                    // P5-D: premises on a plot the player has seen are built in front of him: the business opens at the owner's home and moves in when the house stands
                    premises = new BuildProject { Serial = State.NextSerial++, Kind = ProjectKind.Premises, Settlement = s, StartDay = day, Payer = ohh, Rect = rect, Facing = facing, Use = use,
                        Dwelling = DwellingType.None, Blueprint = bp, Wealth = 1, BusinessKind = k, Owner = owner, HoursNeeded = 1 };
                    State.Projects.Add(premises);
                    lot = hht.Lot[ohh];
                }
                else
                {
                var rec = new LotRecord(s, Region.Settlements[s].Plan.Lots.Count, true, use, DwellingType.None, bp, rect, facing, 1, 0, day);
                RegionGrowth.Change(Region, ChunkSink, [Pad(rect)], () => RegionGrowth.AddLot(Region, W.Population, rec));
                State.Lots.Add(rec); lot = rec.Lot; yr.PlotsGiven++;
                PayHands(ohh, hands, pay, now);
                }
            }
            else lot = coal ? -1 : hht.Lot[ohh];                                                   // a crew works the woods: no lot, as the generated crews
            var biz = prod.NewBusiness(kind, s, lot, owner);
            prod.SetOwner(biz, owner); biz.OwnerWorks = !coal;
            foreach (var (sk, count) in kind.Stations) for (int c = 0; c < count; c++) biz.Stations.Add(new Station { Kind = sk });
            var skill = OccupationInfo.Skills(kind.OwnerOccupation).A;
            _ew.Labour.EnsureSkill(owner, skill, LabourMarket.StartSkill);
            var e = W.Economy;
            long capital = Balance(ohh) / 2;
            if (capital > 0) e.Ledger.Transfer(e.HouseholdPurse[ohh], biz.Purse, new Coin(capital), TransferReason.Gift);
            int fief = Region.Settlements[s].FiefIndex;
            if (capital < T.BusinessTreasuryAdvanceMites && fief >= 0 && fief < Region.Fiefs.Count && e.Ledger.TryFind(PurseKind.Treasury, Region.Fiefs[fief].Id, out var tr))
            {
                long adv = Math.Min(T.BusinessTreasuryAdvanceMites, e.Ledger.Balance(tr).Mites / 50);
                if (adv > 0 && e.Ledger.Transfer(tr, biz.Purse, new Coin(adv), TransferReason.Stipend)) { prod.TreasuryPaidMites += adv; prod.AdvancesMites += adv; }
            }
            biz.CashStart = e.Ledger.Balance(biz.Purse).Mites; biz.Capital = Math.Max(biz.CashStart, 1000);
            State.Unmet.Remove((long)s * 4096 + k);
            if (premises != null) Sites!.Started(premises, now);                                    // P5-D
            yr.BusinessesOpened++; if (coal) { coalOpened++; crews++; ob.Count("charcoal_crews_opened"); } else opened++;
            ob.Count("businesses_opened");
            ob.Chronicle(now, "business_opened", [pt.IdOf(owner)], [Why.Of(CauseKind.MarketDrift, "ch_why_business_drift", ChronParam.Place("place", Region.Settlements[s].Id))],
                ("settlement", Region.Settlements[s].Name), ("kind", kind.Id));
        }
        if (opened + coalOpened > 0) _ew.Labour.Rebuild();
    }

    /// <summary>
    /// P7-EA3e (EA_F19): whether a settlement wants a new crew of charcoal burners: charcoal is dear in some book of its fief (the burners sit by the woods, the price is highest in the towns) and its own logs are not (the wood is there to buy), woodland lies in reach with at least <see cref="GrowthTuningDef.CharcoalMinWoodG"/> above the cutting floor,
    /// and the settlement has idle hands to crew it. The crew then hires them through the labour market and buys its logs from the books: the woodcutters cut the woodland, which regrows by its season.
    /// </summary>
    private bool CharcoalWanted(MarketBook book, int s, int day)
    {
        var logs = _cat.IdOf("logs");
        if (!_coalDear[ClusterOf(s)] || book.MidRatioPermille(logs) > 1000) return false;
        if (_ew.Woodland.AvailableG(s) < T.CharcoalMinWoodG) return false;
        return HandsOf(s, day).Count >= T.CharcoalMinHands;
    }

    private void PayHands(int payer, List<int> hands, long pay, GameTime now)
    {
        var e = W.Economy; long wage = RecipeBook.WagePerHour(WageClass.Unskilled); long left = pay;
        for (int i = 0; i < hands.Count && left >= wage; i++)
        {
            int hh = W.People.Household[hands[i]]; if (hh == payer) continue;
            long amt = Math.Min(left, wage * T.HiredHoursPerHandDay * TimeConst.DaysPerSeason / 4);
            if (e.Ledger.Transfer(e.HouseholdPurse[payer], e.HouseholdPurse[hh], new Coin(amt), TransferReason.Wage)) { left -= amt; State.Year(now.Year).WagesMites += amt; }
        }
    }

    /// <summary>The owner of a new business: an idle adult of the settlement whose household holds the minimum, the right trade first, then a merchant, then the best skilled; ties to the lower index.</summary>
    private int ChooseOwner(int s, BusinessKind kind, int day)
    {
        var pt = W.People; var hht = W.Households; var owners = new HashSet<int>();
        foreach (var b in _ew.Production.Businesses) if (b.State == BusinessState.Open && b.Owner >= 0) owners.Add(b.Owner);
        var skill = OccupationInfo.Skills(kind.OwnerOccupation).A;
        int best = -1; long bestKey = long.MinValue;
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.Status[i] != PersonStatus.Alive || pt.HomeSettlement[i] != s || pt.Employer[i] >= 0 || owners.Contains(i) || _ew.Labour.IsStaff(i)) continue;
            var occ = pt.Occupation[i];
            if (occ != kind.OwnerOccupation && occ is not (Occupation.None or Occupation.Labourer or Occupation.Merchant)) continue;
            int age = pt.AgeYears(i, day); if (age < LabourMarket.ManagementMinAge || age >= LabourMarket.RetireAge) continue;
            int hh = pt.Household[i]; if (hh < 0 || hht.Class[hh] == SocialClass.Gentry || Balance(hh) < T.BusinessMinOwnerMites) continue;
            long key = (occ == kind.OwnerOccupation ? 3_000_000L : occ == Occupation.Merchant ? 2_000_000L : 1_000_000L) + pt.SkillLevelOf(i, skill) * 1000L - i * 0L;
            if (key > bestKey) { bestKey = key; best = i; }
        }
        return best;
    }

    // ---------------------------------------------------------------- rural to town

    /// <summary>
    /// Internal migration (decision G12): towns lose people to disease, so historically they grew by drawing them from the countryside. A town with free dwellings and
    /// posts its businesses cannot fill (the wage on offer is the draw) takes up to <see cref="GrowthTuningDef.TownMovesPerTownSeason"/> landless peasant households of the
    /// villages a season, those with an idle adult first, into its free burgage and other houses. The household keeps its coin and goods; only its home changes.
    /// </summary>
    private void MoveToTowns(GameTime now, RegionOutbox ob)
    {
        var pop = W.Population; var pt = W.People; var hht = W.Households; int day = (int)now.TotalDays;
        List<int>? villagers = null;
        for (int t = 0; t < Region.Settlements.Count; t++)
        {
            if (Region.Settlements[t].Class != SettlementClass.Town) continue;
            var lots = Region.Settlements[t].Plan.Lots; var free = new List<int>();
            for (int l = 0; l < lots.Count; l++)
                if (pop.LotBeds[t][l] >= 3 && pop.LotHousehold[t][l] < 0 && lots[l].IsDwelling && lots[l].Dwelling is DwellingType.Burgage or DwellingType.Cottage) free.Add(l);
            if (free.Count == 0) continue;
            int openings = 0;
            foreach (var b in _ew.Production.Businesses)
            {
                if (b.Settlement != t || b.State != BusinessState.Open) continue;
                var k = _ew.Production.KindOf(b); if (k.Trader || k.Id == "manor") continue;
                openings += Math.Max(0, k.StaffCapacity - b.Staff.Count);
            }
            int quota = Math.Min(Math.Min(free.Count, openings / 2 + (openings > 0 ? 1 : 0)), T.TownMovesPerTownSeason);
            if (quota <= 0) continue;
            if (villagers == null)
            {
                villagers = [];
                for (int h = 0; h < hht.Count; h++)
                {
                    if (hht.MemberCount[h] == 0 || hht.ParcelCount[h] > 0 || hht.Class[h] != SocialClass.Peasant || hht.Settlement[h] >= Region.Settlements.Count) continue;
                    if (Region.Settlements[hht.Settlement[h]].Class != SettlementClass.Village) continue;
                    bool idle = false, tied = false;
                    foreach (int m in hht.Members(h))
                    {
                        if (pt.Status[m] != PersonStatus.Alive) continue;
                        if (_ew.Labour.IsStaff(m) || pt.Employer[m] >= 0 || pt.Occupation[m] is not (Occupation.None or Occupation.Labourer)) { if (pt.Occupation[m] != Occupation.None && pt.Occupation[m] != Occupation.Labourer) tied = true; continue; }
                        int a = pt.AgeYears(m, day); if (a >= 16 && a < 60) idle = true;
                    }
                    if (idle && !tied) villagers.Add(h);
                }
            }
            int moved = 0;
            for (int i = 0; i < villagers.Count && moved < quota; i++)
            {
                int hh = villagers[i]; if (hh < 0 || hht.MemberCount[hh] == 0) continue;
                int slot = free.FindIndex(l => pop.LotBeds[t][l] >= hht.MemberCount[hh]); if (slot < 0) continue;
                int lot = free[slot]; free.RemoveAt(slot); villagers[i] = -1;
                Demography.VacateDwelling(W, hh);
                hht.Settlement[hh] = t; hht.Lot[hh] = (short)lot; hht.Beds[hh] = (short)pop.LotBeds[t][lot]; hht.Dwelling[hh] = lots[lot].Dwelling; hht.Deed[hh] = DeedKind.Rented; pop.LotHousehold[t][lot] = hh;
                foreach (int m in hht.Members(hh)) { pt.HomeSettlement[m] = t; pt.HomeLot[m] = (short)lot; pt.Bed[m] = -1; }
                moved++; State.Year(now.Year).MovedToTown++;
            }
            if (moved > 0) ob.Count("moved_to_town", moved);
        }
    }
}
