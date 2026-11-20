using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Land;

/// <summary>Writes a chronicle entry and returns its id: the hero's desk writes straight to the world's chronicle, the abstract tick drafts into its outbox.</summary>
public delegate EntityId LandChronicle(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data);

/// <summary>What a land operation answers: ok or the refusal, a message key of en.csv (<c>ld_*</c>) with its parameters, the coin that moved and the deed it made or changed.</summary>
public sealed record LandResult(bool Ok, string Key, object[] Args, long Mites = 0, EntityId Deed = default, bool PriceMoved = false, bool CannotAfford = false)
{
    public static LandResult No(string key, params object[] args) => new(false, key, args);
}

/// <summary>A plot the lord offers (spec 4.2): a vacant house he holds (<see cref="Lot"/> its index) or a new plot on the plan's streets (<see cref="Lot"/> -1), its price, dues and tenure.</summary>
public sealed record Listing(int Settlement, int Lot, RectM Rect, byte Facing, int AreaM2, long PriceMites, long DuesMites, Tenure Tenure, long BuildingMites);

/// <summary>
/// The land of the home region at work (land spec 4): the lot market, resale, leases of fields, houses and stalls, the dues and the arrears ladder, claims and their improvements, and
/// founding charters (spec 4a). Every rule is an integer function of the records; coin moves only by ledger transfers between real purses (never minted), goods only by
/// <see cref="Store.Move"/>; nothing is drawn at random (stream 0x500 is reserved and never drawn, LD-14). Called at the barrier by the hero's desk and by <see cref="LandSystem"/>.
/// </summary>
public sealed partial class LandWorld
{
    public LandBook Book { get; }
    public Population Pop { get; }
    public RegionData Region => Pop.Region;
    public EconomyState Econ { get; }
    /// <summary>P6-C4 (disease-and-plague 4.5): a price of land or of a new lease as the scarcity term has it (a third of the holdings empty: about half); 1000 permille leaves it as calibrated.</summary>
    public long Scarce(long mites) => mites * Econ.LandMultPm / 1000;
    public LandRulesDef Rules { get; }
    public GrowthTuningDef GrowthTuning { get; }
    public MarketSystem? Markets { get; }
    public int RegionId { get; }
    private readonly Func<GrowthState> _growth;
    private readonly Func<IGrowthChunkSink?> _sink;
    /// <summary>Names of holders that are not households (the hero): used by refusals and the views.</summary>
    public Func<EntityId, string>? Names { get; set; }

    public LandWorld(LandBook book, Population pop, EconomyState econ, ContentDatabase? content, Func<GrowthState> growth, Func<IGrowthChunkSink?>? sink = null, MarketSystem? markets = null)
    {
        Book = book; Pop = pop; Econ = econ; Rules = LandRules.From(content); _growth = growth; _sink = sink ?? (() => null); Markets = markets;
        GrowthTuning = content?.All<GrowthTuningDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new GrowthTuningDef { Id = "growth" };
        RegionId = RegionCoord.OfCell(pop.Region.CellX, pop.Region.CellY).Id;
    }

    public LandRegistry Registry() => LandRegistry.Build(Region, Pop, Book, Rules, Names);
    private GrowthState Growth => _growth();

    // ------------------------------------------------------------------ purses

    public PurseId PurseOf(EntityId holder, PurseKind kind)
    {
        if (kind == PurseKind.Household && Pop.Households.IndexOf(holder) is int h && h >= 0 && h < Econ.HouseholdPurse.Length) return Econ.HouseholdPurse[h];
        return Econ.Ledger.Open(kind, holder);
    }

    private long Balance(EntityId holder, PurseKind kind) => Econ.Ledger.Balance(PurseOf(holder, kind)).Mites;

    private bool Pay(EntityId from, PurseKind fk, EntityId to, PurseKind tk, long mites, TransferReason why)
    {
        if (mites <= 0) return true;
        var a = PurseOf(from, fk); var b = PurseOf(to, tk);
        return a != b && Econ.Ledger.Transfer(a, b, new Coin(mites), why);
    }

    /// <summary>The treasury purse of a realm (taxes 2.1: the realm's account in this region).</summary>
    public static EntityId RealmTreasuryId(int realm) => EntityId.Make(IdKind.Faction, (1UL << 40) | (uint)realm);

    // ------------------------------------------------------------------ the lot market (spec 4.2)

    /// <summary>Whether play holds or lets a lot (a bought plot or vacant house, a leased house): growth and marriage leave such a lot alone.</summary>
    public bool HoldsLot(int settlement, int lot)
    {
        long sub = LandIds.LotSubject(settlement, lot);
        if (Book.Deeds.ContainsKey(LandIds.Of(HoldingKind.Lot, RegionId, sub))) return true;
        foreach (var d in Book.Deeds.Values) if (d.Kind == HoldingKind.Building && d.Subject == sub) return true;
        return false;
    }

    /// <summary>
    /// P6-B6 (holdings 3.1): a field parcel was let to a person (<c>begin</c>) or came back to the lord (lease ended, eviction). The wiring moves the parcel's holder household in the population
    /// table, so the farm tables credit the harvest to the lessee's barn, as they credit any holder's.
    /// </summary>
    public Action<int, EntityId, bool>? ParcelLet { get; set; }

    /// <summary>Whether a parcel is let on a lease of the book (the lord's re-let leaves it alone).</summary>
    public bool IsLeased(int parcel)
    {
        foreach (var d in Book.Deeds.Values) if (d.Kind == HoldingKind.Parcel && d.IsLease && d.Subject == parcel) return true;
        return false;
    }

    /// <summary>What a standing house is worth (spec 4.2): <c>BuildingValuePmOfCost</c> of its worker-hours at the unskilled wage and its materials at the book's reference price.</summary>
    public long BuildingValue(int settlement, DwellingType dwelling)
    {
        if (dwelling == DwellingType.None) return 0;
        var t = GrowthTuning; int pct = dwelling == DwellingType.Burgage ? t.BurgagePct : 100;
        long cost = (long)t.CottageWorkerHours * pct / 100 * RecipeBook.WagePerHour(WageClass.Unskilled);
        if (Markets != null && (uint)settlement < (uint)Markets.BookOf.Length)
        {
            var book = Markets.BookForSettlement(settlement);
            foreach (var m in t.CottageMaterials)
                if (Econ.Goods.TryIdOf(m.Good, out ushort g) && !Econ.Goods.IsPiece(g)) cost += (long)((Int128)m.Kg * pct / 100 * book.Goods[g].RefMicro * 1000 / PriceCurve.Micro);
        }
        return cost * Rules.BuildingValuePmOfCost / 1000;
    }

    /// <summary>
    /// The plots a settlement's lord offers now (spec 4.2): vacant houses he holds (lowest lot first), then new plots in <see cref="RegionGrowth.FindPlot"/>'s order (nearest the square first), up to the
    /// listing count of the place, at today's price. Growth's builds reserve their plots, so a plot a household has started on is never listed; a pure function of the records.
    /// </summary>
    /// <summary>P5-C: the blueprint id of a plot sold bare (no blueprint by that id exists, so world generation stamps nothing on it).</summary>
    public const string BarePlot = "-";

    public List<Listing> Listings(int s)
    {
        var res = new List<Listing>();
        if ((uint)s >= (uint)Region.Settlements.Count) return res;
        var st = Region.Settlements[s];
        int n = st.Class switch { SettlementClass.Town => Rules.ListingsTown, SettlementClass.Village => Rules.ListingsVillage, _ => 0 };
        if (n <= 0) return res;
        var gs = Growth;
        var claimed = new HashSet<int>(gs.Projects.Where(p => p.Settlement == s && p.Lot >= 0).Select(p => p.Lot));
        var reserved = gs.Projects.Where(p => p.Settlement == s && p.Kind != ProjectKind.Assart && p.Lot < 0).Select(p => p.Rect).ToList();
        int demand = LandRules.DemandPm(Rules, LandRules.CrowdedPct(Rules, Pop.Households, s));
        var lots = st.Plan.Lots;
        for (int l = 0; l < lots.Count && res.Count < n; l++)
        {
            var lot = lots[l];
            if (!lot.Vacant || lot.Dwelling is not (DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage) || claimed.Contains(l) || HoldsLot(s, l)) continue;
            int area = lot.Rect.W * lot.Rect.H; long bv = BuildingValue(s, lot.Dwelling);
            res.Add(new Listing(s, l, lot.Rect, lot.Facing, area, Scarce(LandRules.PlotPrice(Rules, area, st.Class, demand, bv)), LandRules.PlotDues(Rules, st.Class, area), LandRules.PlotTenure(st.Class), bv));
        }
        bool town = st.Class == SettlementClass.Town;
        int w = town ? GrowthTuning.PlotWidthTownM : GrowthTuning.PlotWidthVillageM, d = town ? GrowthTuning.PlotDepthTownM : GrowthTuning.PlotDepthVillageM;
        while (res.Count < n && RegionGrowth.FindPlot(Region, s, w, d, GrowthTuning.PlanMarginM, reserved, out var rect, out byte facing))
        {
            reserved.Add(rect);
            int area = rect.W * rect.H;
            res.Add(new Listing(s, -1, rect, facing, area, Scarce(LandRules.PlotPrice(Rules, area, st.Class, demand)), LandRules.PlotDues(Rules, st.Class, area), LandRules.PlotTenure(st.Class), 0));
        }
        return res;
    }

    /// <summary>
    /// Buys a listed plot from the lord (barrier command <c>LandBuy</c>, spec 4.2): the plot asked for when it is still offered, else the next one on the list (a growth build or an earlier
    /// buyer took it: the earlier command wins, LD-5). The full price is a <c>Trade</c> transfer from the buyer to the lord's purse; a new plot becomes a lot of the plan through the growth log
    /// (so a load replays it and growth never builds on it); the deed is Burgage in a town (dues 300 a season), else Freehold with a quit-rent.
    /// </summary>
    public LandResult Buy(EntityId buyer, PurseKind buyerKind, int s, int wantedLot, RectM wanted, long limitMites, int day, LandChronicle chron)
    {
        var list = Listings(s);
        if (list.Count == 0) return LandResult.No("ld_no_lots");
        var pick = list.FirstOrDefault(x => wantedLot >= 0 ? x.Lot == wantedLot : x.Lot < 0 && x.Rect == wanted) ?? list[0];
        if (limitMites > 0 && pick.PriceMites > limitMites) return new LandResult(false, "ld_price_moved", [Money(pick.PriceMites)], pick.PriceMites, PriceMoved: true);
        if (Balance(buyer, buyerKind) < pick.PriceMites) return new LandResult(false, "ld_cannot_afford", [Money(pick.PriceMites)], pick.PriceMites, CannotAfford: true);
        var st = Region.Settlements[s];
        var (lordId, lordKind) = LandRegistry.LordOf(Pop, s);
        if (!Pay(buyer, buyerKind, lordId, lordKind, pick.PriceMites, TransferReason.Trade)) return new LandResult(false, "ld_cannot_afford", [Money(pick.PriceMites)], pick.PriceMites, CannotAfford: true);
        int lot = pick.Lot;
        if (lot < 0) lot = NewLot(s, pick, day);
        var id = LandIds.Of(HoldingKind.Lot, RegionId, LandIds.LotSubject(s, lot));
        var deed = new Deed
        {
            Id = id, Kind = HoldingKind.Lot, Subject = LandIds.LotSubject(s, lot), Settlement = s, Holder = buyer, HolderPurse = buyerKind, Tenure = pick.Tenure, Lord = st.FiefIndex,
            DuesMitesPerSeason = pick.DuesMites, AcquiredDay = day, Cause = DeedCause.Bought, Rect = pick.Rect,
        };
        Put(deed, day);
        chron(Day(day), "land_bought", [buyer, id, lordId], [Why.Of(CauseKind.Trade, "ch_why_land_bought_lord", ChronParam.Person("lord", lordId))],
            ("settlement", st.Name), ("price", pick.PriceMites.ToString()), ("area_m2", pick.AreaM2.ToString()), ("tenure", pick.Tenure.ToString()));
        return new LandResult(true, "ld_bought", [st.Name, Money(pick.PriceMites), pick.Rect.W, pick.Rect.H], pick.PriceMites, id);
    }

    /// <summary>A new lot of the plan on a listed plot (through the growth log, so a load replays it and growth never builds on it): bare ground (<see cref="BarePlot"/>).</summary>
    private int NewLot(int s, Listing pick, int day)
    {
        var st = Region.Settlements[s];
        bool town = st.Class == SettlementClass.Town;
        int lot = st.Plan.Lots.Count;
        // P5-C: a bought plot is bare ground (BarePlot): with no blueprint, world generation's fallback stamped a townhouse or a cottage on it, so a house the buyer never built stood
        // in the visited chunks and refused every piece he placed there
        var rec = new LotRecord(s, lot, true, town ? LotUse.Burgage : LotUse.Cottage, DwellingType.None, BarePlot, pick.Rect, pick.Facing, (byte)(town ? 1 : 0), 0, day);
        RegionGrowth.Change(Region, _sink(), [new RectM(pick.Rect.X - 4, pick.Rect.Y - 4, pick.Rect.W + 8, pick.Rect.H + 8)], () => RegionGrowth.AddLot(Region, Pop, rec));
        Growth.Lots.Add(rec);
        return lot;
    }

    /// <summary>
    /// P5-H: a plot of the lord's let on a lease (the tanner's widow's yard where no house stands vacant): the first plot he lists, as a new lot of the plan, held Leasehold of him for the lease
    /// term at the plot's dues (a town's burgage rent). The plot stays his; the lease row stands over the derived deed.
    /// </summary>
    public LandResult LeasePlot(EntityId lessee, PurseKind lesseeKind, int s, int day, LandChronicle chron)
    {
        var pick = Listings(s).FirstOrDefault(x => x.Lot < 0);
        if (pick == null) return LandResult.No("ld_no_lots");
        int lot = NewLot(s, pick, day);
        var (lordId, lordKind) = LandRegistry.LordOf(Pop, s);
        var st = Region.Settlements[s];
        var id = LandIds.Of(HoldingKind.Lot, RegionId, LandIds.LotSubject(s, lot));
        var deed = new Deed
        {
            Id = id, Kind = HoldingKind.Lot, Subject = LandIds.LotSubject(s, lot), Settlement = s, Holder = lessee, HolderPurse = lesseeKind, Tenure = Tenure.Leasehold, Lord = st.FiefIndex,
            DuesMitesPerSeason = Math.Max(1, pick.DuesMites), LeaseEndDay = day + Rules.LeaseTermDays, Lessor = lordId, LessorPurse = lordKind, AcquiredDay = day, Cause = DeedCause.Leased, Rect = pick.Rect,
        };
        Put(deed, day);
        chron(Day(day), "plot_leased", [lessee, id, lordId], [Why.Of(CauseKind.Trade, "ch_why_plot_let", ChronParam.Person("lord", lordId))], ("settlement", st.Name), ("rent", deed.DuesMitesPerSeason.ToString()));
        return new LandResult(true, "ld_leased_house", [st.Name, Money(deed.DuesMitesPerSeason)], deed.DuesMitesPerSeason, id);
    }

    /// <summary>
    /// P5-F (businesses spec 4.1): the deed of a lot passes to a new holder with the business on it (a bought workshop's premises): a burgage in a town, else a freehold, the lot's dues as the
    /// derived deed had them. The book's deed then stands over the derived one (the household that kept the business no longer holds its ground).
    /// </summary>
    public LandResult ConveyLot(int s, int lot, EntityId to, PurseKind toKind, int day, DeedCause cause)
    {
        if ((uint)s >= (uint)Region.Settlements.Count || (uint)lot >= (uint)Region.Settlements[s].Plan.Lots.Count) return LandResult.No("ld_not_yours");
        var st = Region.Settlements[s]; var l = st.Plan.Lots[lot];
        var id = LandIds.Of(HoldingKind.Lot, RegionId, LandIds.LotSubject(s, lot));
        bool town = st.Class == SettlementClass.Town;
        var deed = new Deed
        {
            Id = id, Kind = HoldingKind.Lot, Subject = LandIds.LotSubject(s, lot), Settlement = s, Holder = to, HolderPurse = toKind, Tenure = town ? Tenure.Burgage : Tenure.Freehold, Lord = st.FiefIndex,
            DuesMitesPerSeason = town ? Rules.BurgageRentMitesPerSeason : Math.Max(1, Rules.QuitRentMitesPerAcreSeason * l.Rect.W * l.Rect.H / 4047), AcquiredDay = day, Cause = cause, Rect = l.Rect,
        };
        Put(deed, day);
        return new LandResult(true, "ld_bought", [st.Name, Money(0), l.Rect.W, l.Rect.H], 0, id);
    }

    /// <summary>Offers a deed for sale at <paramref name="askMites"/> (0 withdraws it). Only a bought plot can be offered (spec 4.2 resale).</summary>
    public LandResult Offer(EntityId holder, EntityId deedId, long askMites, int day)
    {
        if (!Book.Deeds.TryGetValue(deedId, out var d) || d.Holder != holder || d.Kind != HoldingKind.Lot || d.IsLease) return LandResult.No("ld_not_yours");
        d.AskMites = Math.Max(0, askMites);
        return new LandResult(true, askMites > 0 ? "ld_offered" : "ld_withdrawn", [Money(askMites)], askMites, deedId);
    }

    /// <summary>P5-H: pays what a deed of <paramref name="holder"/> owes now (as much of its arrears as the purse holds) to its payee as <c>Rent</c>; cleared arrears end the ladder.</summary>
    public LandResult PayArrears(EntityId holder, PurseKind kind, EntityId deedId, int day, LandChronicle chron)
    {
        if (!Book.Deeds.TryGetValue(deedId, out var d) || d.Holder != holder) return LandResult.No("ld_not_yours");
        if (d.OwedMites <= 0) return LandResult.No("ld_nothing_owed");
        var (to, tk) = Payee(d);
        long pay = Math.Min(d.OwedMites, Balance(holder, kind));
        if (to == EntityId.None || pay <= 0 || !Pay(holder, kind, to, tk, pay, TransferReason.Rent)) return new LandResult(false, "ld_cannot_afford", [Money(d.OwedMites)], d.OwedMites, CannotAfford: true);
        d.OwedMites -= pay;
        if (d.OwedMites == 0) { d.ArrearsSeasons = 0; d.InDebt = false; d.NoticeDay = -1; }
        chron(Day(day), "arrears_paid", [holder, d.Id, to], [Why.Of(CauseKind.Trade, "ch_why_arrears_paid", ChronParam.Person("holder", holder))], ("paid", pay.ToString()), ("owed", d.OwedMites.ToString()));
        return new LandResult(true, d.OwedMites == 0 ? "ld_arrears_cleared" : "ld_arrears_part", [Money(pay), Money(d.OwedMites)], pay, d.Id);
    }

    /// <summary>
    /// P5-I: what a holder's deed is worth to him today: a freehold or burgage the plot's price; a leasehold or tenancy only the rights that are left in it, its season's dues for each season still
    /// to run (an open-ended tenure counts a year), never more than the plot's price; customary tenure the same as a tenancy.
    /// </summary>
    public long HoldingValue(Deed d, int day)
    {
        long price = ValueOf(d);
        if (d.Tenure is Tenure.Freehold or Tenure.Burgage) return price;
        int seasons = d.LeaseEndDay >= 0 ? Math.Max(0, (d.LeaseEndDay - day + TimeConst.DaysPerSeason - 1) / TimeConst.DaysPerSeason) : 4;
        return Math.Min(price, Math.Max(0, d.DuesMitesPerSeason) * seasons);
    }

    /// <summary>The value of a deed the resale compares the ask with: what the lord would ask today for the same ground.</summary>
    public long ValueOf(Deed d)
    {
        if (d.Kind != HoldingKind.Lot || (uint)d.Settlement >= (uint)Region.Settlements.Count) return 0;
        var st = Region.Settlements[d.Settlement]; var (_, l) = LandIds.SplitLot(d.Subject);
        int area = d.Rect.W * d.Rect.H;
        var dw = l < st.Plan.Lots.Count ? st.Plan.Lots[l].Dwelling : DwellingType.None;
        return Scarce(LandRules.PlotPrice(Rules, area, st.Class, LandRules.DemandPm(Rules, LandRules.CrowdedPct(Rules, Pop.Households, d.Settlement)), BuildingValue(d.Settlement, dw)));
    }

    /// <summary>
    /// The season's resale (spec 4.2): a deed offered at no more than <c>ResaleBuyerPm</c> of its value finds a buyer, a household of the place that wants room (crowded, or with a daughter
    /// waiting to marry) and can pay and keep its reserve; the richest first, ties by index. The buyer pays the ask (<c>Trade</c>) and holds the deed.
    /// </summary>
    public void Resale(int day, LandChronicle chron)
    {
        var hht = Pop.Households; var pt = Pop.People;
        long reserve = (long)Rules.ResaleReserveDays * RecipeBook.WagePerHour(WageClass.Unskilled) * 10;
        foreach (var d in Book.Deeds.Values.Where(x => x.AskMites > 0).ToList())
        {
            if (d.AskMites > ValueOf(d) * Rules.ResaleBuyerPm / 1000) continue;
            int best = -1; long bestCoin = -1;
            for (int h = 0; h < hht.Count; h++)
            {
                if (hht.Settlement[h] != d.Settlement || hht.MemberCount[h] == 0 || hht.Class[h] == SocialClass.Gentry || hht.IdOf(h) == d.Holder) continue;
                bool wants = hht.MemberCount[h] * 100 > hht.Beds[h] * Rules.CrowdedOverPct;
                if (!wants) foreach (int m in hht.Members(h)) if (pt.Status[m] == PersonStatus.Alive && pt.Sex[m] == Sex.Female && pt.Spouse[m] < 0 && pt.MarriageWait[m] > 0) { wants = true; break; }
                if (!wants) continue;
                long coin = Econ.Ledger.Balance(Econ.HouseholdPurse[h]).Mites;
                if (coin < d.AskMites + reserve || coin <= bestCoin) continue;
                best = h; bestCoin = coin;
            }
            if (best < 0) continue;
            var buyerId = hht.IdOf(best); long price = d.AskMites; var seller = d.Holder;
            if (!Pay(buyerId, PurseKind.Household, d.Holder, d.HolderPurse, price, TransferReason.Trade)) continue;
            d.Holder = buyerId; d.HolderPurse = PurseKind.Household; d.AskMites = 0; d.AcquiredDay = day; d.Cause = DeedCause.Sold;
            Book.Changes.Add(new DeedChange(day, d.Id, d.Holder, d.Tenure, d.DuesMitesPerSeason, d.LeaseEndDay, DeedCause.Sold));
            chron(Day(day), "land_sold", [seller, buyerId, d.Id], [Why.Of(CauseKind.Trade, "ch_why_land_sold", ChronParam.Person("seller", seller))], ("price", price.ToString()), ("household", hht.Name[best]));
        }
    }

    // ------------------------------------------------------------------ leases (spec 4.3)

    /// <summary>A parcel the lord's re-let would let (the holding fell back to him, not his demesne or the glebe) and nobody leases.</summary>
    public bool Leasable(int p)
    {
        if ((uint)p >= (uint)Region.Parcels.Count) return false;
        var pc = Region.Parcels[p];
        if (pc.Owner.Kind is OwnerKind.Demesne or OwnerKind.Glebe || IsLeased(p)) return false;
        int lord = LandRegistry.LordHousehold(Pop, pc.Settlement);
        return lord >= 0 && p < Pop.ParcelHolder.Length && Pop.ParcelHolder[p] == lord;
    }

    /// <summary>The parcels of a settlement the hero could lease now (ascending).</summary>
    public List<int> LeasableParcels(int s) { var l = new List<int>(); if ((uint)s < (uint)Region.Settlements.Count) foreach (int p in Region.Settlements[s].Parcels) if (Leasable(p)) l.Add(p); l.Sort(); return l; }

    /// <summary>Leases a field parcel from its lord for a year (<c>LeaseTermDays</c>), rent <c>LeaseRentMitesPerFieldYear</c> a field paid by the season through the dues step (LD-7).</summary>
    public LandResult LeaseParcel(EntityId lessee, PurseKind lesseeKind, int p, int day, LandChronicle chron)
    {
        if (!Leasable(p)) return LandResult.No("ld_not_leasable");
        var pc = Region.Parcels[p]; var (lordId, lordKind) = LandRegistry.LordOf(Pop, pc.Settlement);
        var id = LandIds.Serial(RegionId, Book.NextSerial++);
        var deed = new Deed
        {
            Id = id, Kind = HoldingKind.Parcel, Subject = p, Settlement = pc.Settlement, Holder = lessee, HolderPurse = lesseeKind, Tenure = Tenure.Leasehold, Lord = Region.Settlements[pc.Settlement].FiefIndex,
            DuesMitesPerSeason = Scarce(LandRules.FieldLeasePerSeason(Rules, pc.FieldCount)), LeaseEndDay = day + Rules.LeaseTermDays, Lessor = lordId, LessorPurse = lordKind, AcquiredDay = day, Cause = DeedCause.Leased,
        };
        Put(deed, day);
        ParcelLet?.Invoke(p, lessee, true);
        chron(Day(day), "land_leased", [lessee, id, lordId], [Why.Of(CauseKind.Trade, "ch_why_field_let", ChronParam.Person("lord", lordId))], ("parcel", p.ToString()), ("fields", pc.FieldCount.ToString()), ("rent", deed.DuesMitesPerSeason.ToString()));
        return new LandResult(true, "ld_leased_field", [pc.FieldCount, Money(deed.DuesMitesPerSeason)], deed.DuesMitesPerSeason, id);
    }

    /// <summary>The lessee gives up a lease at its end (it ends then when nothing is owed and nothing of his is inside), or takes the renewal back.</summary>
    public LandResult GiveUp(EntityId lessee, EntityId deedId, bool renew)
    {
        if (!Book.Deeds.TryGetValue(deedId, out var d) || d.Holder != lessee || !d.IsLease) return LandResult.No("ld_not_yours");
        d.Renew = renew;
        return new LandResult(true, renew ? "ld_renews" : "ld_gives_up", [], 0, deedId);
    }

    public int StallCount(int s) => (uint)s >= (uint)Region.Settlements.Count ? 0 : Region.Settlements[s].Class switch { SettlementClass.Town => Rules.StallsTown, SettlementClass.Village => Rules.StallsVillage, _ => 0 };

    /// <summary>The first day of the next season after <paramref name="day"/>.</summary>
    public static int NextSeasonStart(int day) => day - (day % TimeConst.DaysPerSeason) + TimeConst.DaysPerSeason;

    /// <summary>Rents a market stall for the season (<c>StallRentMitesPerSeason</c>, to its end) or a fair day (<c>StallFairDayMites</c>), paid now as <c>Rent</c> to the town's treasury (LD-8).</summary>
    public LandResult LeaseStall(EntityId lessee, PurseKind lesseeKind, int s, int stall, bool fairDay, int day, LandChronicle chron)
    {
        if (stall < 0 || stall >= StallCount(s)) return LandResult.No("ld_no_stall");
        var id = LandIds.Of(HoldingKind.Stall, RegionId, LandIds.StallSubject(s, stall));
        if (Book.Deeds.TryGetValue(id, out var cur) && cur.LeaseEndDay > day) return LandResult.No("ld_stall_taken");
        long rent = fairDay ? Rules.StallFairDayMites : Rules.StallRentMitesPerSeason;
        var (townId, townKind) = LandRegistry.TownOf(Region, s);
        if (Balance(lessee, lesseeKind) < rent || !Pay(lessee, lesseeKind, townId, townKind, rent, TransferReason.Rent)) return new LandResult(false, "ld_cannot_afford", [Money(rent)], rent, CannotAfford: true);
        var deed = new Deed
        {
            Id = id, Kind = HoldingKind.Stall, Subject = LandIds.StallSubject(s, stall), Settlement = s, Holder = lessee, HolderPurse = lesseeKind, Tenure = Tenure.Leasehold, Lord = Region.Settlements[s].FiefIndex,
            LeaseEndDay = fairDay ? day + 1 : NextSeasonStart(day), Lessor = townId, LessorPurse = townKind, AcquiredDay = day, Cause = DeedCause.Leased, Renew = false,
        };
        Put(deed, day);
        chron(Day(day), "stall_rented", [lessee, id, townId], [Why.Of(CauseKind.Trade, fairDay ? "ch_why_stall_day" : "ch_why_stall_season", ChronParam.Place("place", townId))], ("settlement", Region.Settlements[s].Name), ("rent", rent.ToString()));
        return new LandResult(true, fairDay ? "ld_stall_day" : "ld_stall_season", [Region.Settlements[s].Name, Money(rent)], rent, id);
    }

    /// <summary>Rents a vacant house from the lord at the dwelling rent table (spec 4.3 buildings); its store is the house's, keyed by the deed.</summary>
    public LandResult LeaseHouse(EntityId lessee, PurseKind lesseeKind, int s, int lot, int day, LandChronicle chron)
    {
        if ((uint)s >= (uint)Region.Settlements.Count || (uint)lot >= (uint)Region.Settlements[s].Plan.Lots.Count) return LandResult.No("ld_not_leasable");
        var l = Region.Settlements[s].Plan.Lots[lot];
        if (!l.Vacant || l.Dwelling == DwellingType.None || HoldsLot(s, lot) || Growth.Projects.Any(p => p.Settlement == s && p.Lot == lot)) return LandResult.No("ld_not_leasable");
        var (lordId, lordKind) = LandRegistry.LordOf(Pop, s);
        var id = LandIds.Serial(RegionId, Book.NextSerial++);
        var deed = new Deed
        {
            Id = id, Kind = HoldingKind.Building, Subject = LandIds.LotSubject(s, lot), Settlement = s, Holder = lessee, HolderPurse = lesseeKind, Tenure = Tenure.Leasehold, Lord = Region.Settlements[s].FiefIndex,
            DuesMitesPerSeason = Scarce(LandRules.HouseRent(Rules, l.Dwelling)), LeaseEndDay = day + Rules.LeaseTermDays, Lessor = lordId, LessorPurse = lordKind, AcquiredDay = day, Cause = DeedCause.Leased, Rect = l.Rect,
        };
        Put(deed, day);
        HouseStore(deed);
        chron(Day(day), "house_leased", [lessee, id, lordId], [Why.Of(CauseKind.Trade, "ch_why_house_let", ChronParam.Person("lord", lordId))], ("settlement", Region.Settlements[s].Name), ("rent", deed.DuesMitesPerSeason.ToString()));
        return new LandResult(true, "ld_leased_house", [Region.Settlements[s].Name, Money(deed.DuesMitesPerSeason)], deed.DuesMitesPerSeason, id);
    }

    /// <summary>The store of a leased house (an economy store owned by the deed id, saved with the economy).</summary>
    public Store HouseStore(Deed d)
    {
        var (s, l) = LandIds.SplitLot(d.Subject);
        var dw = (uint)s < (uint)Region.Settlements.Count && (uint)l < (uint)Region.Settlements[s].Plan.Lots.Count ? Region.Settlements[s].Plan.Lots[l].Dwelling : DwellingType.Cottage;
        long litres = dw switch { DwellingType.Longhouse => 6000, DwellingType.Burgage => 8000, _ => 3000 };
        return Econ.OpenStore(StoreKind.House, d.Id, 0, litres * 1000, enforce: false);
    }

    // ------------------------------------------------------------------ the season's dues and the arrears ladder (spec 4.3, S5)

    /// <summary>Who receives a deed's dues: the lessor of a lease, the grantor's treasury of a charter, else the lord of the settlement or of the claim's fief.</summary>
    public (EntityId Id, PurseKind Kind) Payee(Deed d)
    {
        if (d.IsLease) return (d.Lessor, d.LessorPurse);
        if (d.Kind == HoldingKind.FoundingCharter && Book.CharterOf((int)d.Subject) is { } c)
            return c.GrantorKind == GrantorKind.Realm ? (RealmTreasuryId(c.Grantor), PurseKind.Treasury) : (Region.Fiefs[c.Grantor].Id, PurseKind.Treasury);
        if (d.Settlement >= 0) return LandRegistry.LordOf(Pop, d.Settlement);
        if (d.Lord >= 0 && d.Lord < Region.Fiefs.Count)
        {
            int seat = Region.Fiefs[d.Lord].SeatSettlement;
            return LandRegistry.LordOf(Pop, seat);
        }
        return (EntityId.None, PurseKind.Person);
    }

    /// <summary>
    /// The dues of every deed of the book at the season's start (spec 4.6, S5): paid as <c>Rent</c> to the payee when the holder can, with what was owed before; else the season is owed.
    /// After <c>ArrearsDebtSeasons</c> the sum owed is a debt; after <c>ArrearsEvictSeasons</c> the lessor gives <c>EvictionNoticeDays</c> of notice (a charter's arrears are only recorded: revocation is Phase 6).
    /// </summary>
    public void CollectDues(int day, LandChronicle chron)
    {
        foreach (var d in Book.Deeds.Values.ToList())
        {
            if (d.DuesMitesPerSeason <= 0) continue;
            var (to, tk) = Payee(d);
            if (to == EntityId.None || (to == d.Holder && tk == d.HolderPurse)) continue;
            long due = d.DuesMitesPerSeason;
            if (Balance(d.Holder, d.HolderPurse) >= due && Pay(d.Holder, d.HolderPurse, to, tk, due, TransferReason.Rent))
            {
                if (d.OwedMites > 0)
                {
                    long back = Math.Min(d.OwedMites, Balance(d.Holder, d.HolderPurse));
                    if (back > 0 && Pay(d.Holder, d.HolderPurse, to, tk, back, TransferReason.Rent)) d.OwedMites -= back;
                }
                if (d.OwedMites == 0) { d.ArrearsSeasons = 0; d.InDebt = false; }
                continue;
            }
            d.OwedMites += due;
            if (d.ArrearsSeasons < byte.MaxValue) d.ArrearsSeasons++;
            if (d.ArrearsSeasons >= Rules.ArrearsDebtSeasons && !d.InDebt)
            {
                d.InDebt = true;
                chron(Day(day), "land_debt", [d.Holder, d.Id, to], [Why.Of(CauseKind.Tax, "ch_why_dues_unpaid", ChronParam.Person("holder", d.Holder), ChronParam.Person("lord", to))], ("owed", d.OwedMites.ToString()), ("seasons", d.ArrearsSeasons.ToString()));
            }
            if (d.ArrearsSeasons >= Rules.ArrearsEvictSeasons && d.NoticeDay < 0 && d.Kind != HoldingKind.FoundingCharter)
            {
                d.NoticeDay = day + Rules.EvictionNoticeDays;
                chron(Day(day), "land_notice", [d.Holder, d.Id, to], [Why.Of(CauseKind.Law, "ch_why_arrears_notice", ChronParam.Person("holder", d.Holder), ChronParam.Person("lord", to))], ("owed", d.OwedMites.ToString()), ("until", d.NoticeDay.ToString()));
            }
        }
    }

    /// <summary>
    /// The notice ran out (spec 4.3): the deed returns to the lessor or the lord; goods left in a leased house go to the lessor's store as distraint, valued at the market's bid and
    /// set against what was owed (no coin moves; <c>MassLedger</c> stays exact because the goods are moved, LD-13). A forfeited claim lapses.
    /// </summary>
    public long Evict(Deed d, int day, LandChronicle chron)
    {
        var (to, tk) = Payee(d);
        long value = 0;
        if (d.Kind == HoldingKind.Building && Econ.OtherStores.TryGetValue(d.Id, out var store))
        {
            var dest = StoreOf(to, tk);
            MarketBook? book = Markets != null && (uint)d.Settlement < (uint)Markets.BookOf.Length ? Markets.BookForSettlement(d.Settlement) : null;
            foreach (var (g, q) in store.Rows())
            {
                long moved = Store.MoveUpTo(store, dest, g, q);
                if (book != null && moved > 0) value += (long)((Int128)book.BidMicro(g) * moved / PriceCurve.Micro);
            }
            d.OwedMites = Math.Max(0, d.OwedMites - value);
            Book.DistrainedMites += value;
        }
        Book.Deeds.Remove(d.Id);
        if (d.Kind == HoldingKind.Parcel && d.IsLease) ParcelLet?.Invoke((int)d.Subject, d.Holder, false);
        Book.Changes.Add(new DeedChange(day, d.Id, to, Tenure.Freehold, 0, -1, DeedCause.Evicted));
        foreach (var c in Book.Claims) if (c.DeedId == d.Id && c.State != ClaimState.Lapsed) { c.State = ClaimState.Lapsed; c.EndDay = day; }
        chron(Day(day), "land_evicted", [d.Holder, d.Id, to], [Why.Of(CauseKind.Law, "ch_why_arrears_evicted", ChronParam.Person("holder", d.Holder), ChronParam.Person("lord", to))], ("owed", d.OwedMites.ToString()), ("distraint", value.ToString()));
        return value;
    }

    private Store StoreOf(EntityId holder, PurseKind kind)
    {
        if (kind == PurseKind.Household && Pop.Households.IndexOf(holder) is int h && h >= 0 && h < Econ.HouseholdStore.Length) return Econ.HouseholdStore[h];
        return Econ.OpenStore(StoreKind.Barn, holder);
    }

    // ------------------------------------------------------------------ claims (spec 4.4; overview 10a)

    /// <summary>Whether a tile is inside a live claim or charter site of the book.</summary>
    public bool Taken(int t)
    {
        foreach (var c in Book.Claims) if (c.State is ClaimState.Open or ClaimState.Deeded && Array.BinarySearch(c.Tiles, t) >= 0) return true;
        foreach (var c in Book.Charters) if (c.State != CharterState.Lapsed && Array.BinarySearch(c.Tiles, t) >= 0) return true;
        return false;
    }

    /// <summary>
    /// Connected tiles for a claim or a site: grown from <paramref name="have"/> (or the start tile) over tiles <paramref name="ok"/> allows, level and dry first, then nearest the banner, ties by
    /// index; up to <paramref name="count"/> in all, ascending. Fewer when the open ground runs out.
    /// </summary>
    public int[] Grow(int start, int[] have, int count, int bx, int by, Func<int, bool> ok)
    {
        int N = RegionData.Size;
        var chosen = new HashSet<int>(have);
        int Pen(int t) => Region.SlopePermille[t] <= Rules.ClaimLevelSlopePermille && (Region.Drainage.Length <= t || Region.Drainage[t] >= Rules.ClaimDryDrainage) ? 0 : 1;
        long D2(int t) { long dx = (t % N) * (long)RegionData.TileM + RegionData.TileM / 2 - bx, dy = (t / N) * (long)RegionData.TileM + RegionData.TileM / 2 - by; return dx * dx + dy * dy; }
        var frontier = new SortedSet<(int Pen, long D2, int T)>();
        void Around(int t)
        {
            int x = t % N, y = t / N;
            foreach (var (dx, dy) in N4)
            {
                int nx = x + dx, ny = y + dy; if ((uint)nx >= (uint)N || (uint)ny >= (uint)N) continue;
                int nt = ny * N + nx; if (!chosen.Contains(nt) && ok(nt)) frontier.Add((Pen(nt), D2(nt), nt));
            }
        }
        if (chosen.Count == 0) { if (start < 0 || !ok(start)) return []; chosen.Add(start); }
        foreach (int t in chosen.ToList()) Around(t);
        while (chosen.Count < count && frontier.Count > 0)
        {
            var f = frontier.Min; frontier.Remove(f);
            if (!chosen.Add(f.T)) continue;
            Around(f.T);
        }
        var res = chosen.ToArray(); Array.Sort(res);
        return res;
    }
    private static readonly (int Dx, int Dy)[] N4 = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    /// <summary>Where a claim at a point would stand: the fief whose waste it is (-1: wild land outside every lord's land), whether the ground may be claimed at all, and why not.</summary>
    public (bool Ok, int Fief, string Why) ClaimSite(int xM, int yM)
    {
        int t = LandRules.TileAt(xM, yM);
        if (t < 0) return (false, -1, "ld_claim_off_map");
        if (!LandRules.OpenGround(Region, t)) return (false, -1, "ld_claim_not_open");
        if (Taken(t)) return (false, -1, "ld_claim_taken");
        int fief = LandRules.LandFief(Region, Rules, t);
        if (fief < -1) return (false, -1, "ld_claim_too_near");
        return (true, fief, "");
    }

    /// <summary>A tile a claim of <paramref name="fief"/>'s land (or wild land, -1) may take: open, of the same land, not in another claim or a charter's site (<paramref name="own"/> are the claim's own).</summary>
    public bool ClaimTileOk(int t, int fief, int[]? own)
    {
        if (!LandRules.OpenGround(Region, t)) return false;
        if (Taken(t) && (own == null || Array.BinarySearch(own, t) < 0)) return false;
        return LandRules.LandFief(Region, Rules, t) == fief;
    }

    /// <summary>
    /// Stakes a claim (spec 4.4, decided with the user): the <c>ClaimStartTiles</c> tiles nearest the banner. Outside every fief it is free and unprotected; on a fief's waste a licence
    /// (<c>ClaimLicenceMitesPerAcre</c> an acre, paid to the lord as <c>Rent</c>) makes it protected, and without one it is allowed but marked <see cref="Claim.Squatting"/> (the lord's eviction is Phase 6).
    /// </summary>
    public LandResult Stake(EntityId claimant, PurseKind kind, int xM, int yM, bool licence, int day, LandChronicle chron)
    {
        var (ok, fief, why) = ClaimSite(xM, yM);
        if (!ok) return LandResult.No(why);
        int t0 = LandRules.TileAt(xM, yM);
        var tiles = Grow(t0, [], Rules.ClaimStartTiles, xM, yM, t => ClaimTileOk(t, fief, null));
        if (tiles.Length < Rules.ClaimStartTiles) return LandResult.No("ld_claim_small");
        long fee = 0;
        EntityId lordId = EntityId.None; PurseKind lordKind = PurseKind.Treasury;
        if (fief >= 0 && licence)
        {
            (lordId, lordKind) = LandRegistry.LordOf(Pop, Region.Fiefs[fief].SeatSettlement);
            fee = LandRules.Licence(Rules, tiles.Length);
            if (Balance(claimant, kind) < fee || !Pay(claimant, kind, lordId, lordKind, fee, TransferReason.Rent)) return new LandResult(false, "ld_cannot_afford", [Money(fee)], fee, CannotAfford: true);
        }
        var c = new Claim
        {
            Serial = Book.NextSerial++, Claimant = claimant, ClaimantPurse = kind, Tiles = tiles, BannerX = xM, BannerY = yM, StakeDay = day, Fief = fief,
            Licensed = fief >= 0 && licence, Squatting = fief >= 0 && !licence, LicenceMites = fee, State = ClaimState.Open,
        };
        Book.Claims.Add(c);
        var subj = new List<EntityId> { claimant }; if (lordId != EntityId.None) subj.Add(lordId);
        chron(Day(day), "claim_staked", subj, [Why.Of(CauseKind.Law, c.Squatting ? "ch_why_claim_squat" : c.Licensed ? "ch_why_claim_licensed" : "ch_why_claim_wild", ChronParam.Person("claimant", c.Claimant))],
            ("claim", c.Serial.ToString()), ("tiles", tiles.Length.ToString()), ("fief", fief >= 0 ? Region.Fiefs[fief].Name : ""), ("licence", fee.ToString()), ("squatting", c.Squatting ? "1" : "0"));
        string key = c.Squatting ? "ld_claim_squatting" : c.Licensed ? "ld_claim_licensed" : "ld_claim_free";
        return new LandResult(true, key, [tiles.Length, Money(fee)], fee, LandIds.Serial(RegionId, c.Serial));
    }

    /// <summary>Buys the lord's licence for a squatter's claim afterwards (its acres now): the claim is protected from then on.</summary>
    public LandResult BuyLicence(EntityId claimant, int serial, int day, LandChronicle chron)
    {
        var c = Book.ClaimOf(serial);
        if (c == null || c.Claimant != claimant || c.State is not (ClaimState.Open or ClaimState.Deeded)) return LandResult.No("ld_not_yours");
        if (!c.Squatting) return LandResult.No("ld_claim_no_licence_needed");
        var (lordId, lordKind) = LandRegistry.LordOf(Pop, Region.Fiefs[c.Fief].SeatSettlement);
        long fee = LandRules.Licence(Rules, c.Tiles.Length);
        if (Balance(claimant, c.ClaimantPurse) < fee || !Pay(claimant, c.ClaimantPurse, lordId, lordKind, fee, TransferReason.Rent)) return new LandResult(false, "ld_cannot_afford", [Money(fee)], fee, CannotAfford: true);
        c.Squatting = false; c.Licensed = true; c.LicenceMites += fee;
        if (c.DwellingDay >= 0 && c.State == ClaimState.Open) MakeClaimDeed(c, day, chron);
        chron(Day(day), "claim_licensed", [claimant, lordId], [Why.Of(CauseKind.Law, "ch_why_licence_bought", ChronParam.Person("claimant", claimant))], ("claim", serial.ToString()), ("licence", fee.ToString()));
        return new LandResult(true, "ld_claim_licensed", [c.Tiles.Length, Money(fee)], fee, LandIds.Serial(RegionId, serial));
    }

    /// <summary>
    /// An improvement on a claim (spec 4.4): a dwelling RoomDetector v2 recognises (construction calls this at the barrier), or an acre sown. Each adds <c>ClaimTilesPerImprovement</c> connected
    /// tiles up to <c>ClaimMaxTiles</c>; a dwelling makes the claim a deed: Freehold outside fiefs, Customary with a quit-rent when licensed (a squatter's stays a claim).
    /// </summary>
    public LandResult Improve(int serial, ImprovementKind kind, int day, LandChronicle chron)
    {
        var c = Book.ClaimOf(serial);
        if (c == null || c.State is not (ClaimState.Open or ClaimState.Deeded)) return LandResult.No("ld_not_yours");
        c.Improvements++;
        if (kind == ImprovementKind.Dwelling && c.DwellingDay < 0) c.DwellingDay = day;
        if (kind == ImprovementKind.SownAcre) c.SownAcres++;
        int want = Math.Min(Rules.ClaimMaxTiles, c.Tiles.Length + Rules.ClaimTilesPerImprovement);
        if (want > c.Tiles.Length) c.Tiles = Grow(-1, c.Tiles, want, c.BannerX, c.BannerY, t => ClaimTileOk(t, c.Fief, c.Tiles));
        chron(Day(day), "claim_improved", [c.Claimant], [Why.Of(CauseKind.Law, kind == ImprovementKind.Dwelling ? "ch_why_claim_dwelling" : "ch_why_claim_acre", ChronParam.Person("claimant", c.Claimant))], ("claim", serial.ToString()), ("tiles", c.Tiles.Length.ToString()));
        if (c.State == ClaimState.Open && c.DwellingDay >= 0 && !c.Squatting) MakeClaimDeed(c, day, chron);
        return new LandResult(true, "ld_claim_improved", [c.Tiles.Length], 0, LandIds.Serial(RegionId, serial));
    }

    /// <summary>The acres a claim's licence paid for: the quit-rent of a licensed claim is on these (8 acres: 200 mites a season, LD-11); acres an improvement adds are assarts within the licence.</summary>
    public int LicensedAcres(Claim c) => Rules.ClaimLicenceMitesPerAcre <= 0 ? c.Tiles.Length : (int)(c.LicenceMites / Rules.ClaimLicenceMitesPerAcre);

    private void MakeClaimDeed(Claim c, int day, LandChronicle chron)
    {
        var id = LandIds.Serial(RegionId, c.Serial);
        var d = new Deed
        {
            Id = id, Kind = HoldingKind.Claim, Subject = c.Serial, Settlement = -1, Holder = c.Claimant, HolderPurse = c.ClaimantPurse, Lord = c.Fief,
            Tenure = c.Licensed ? Tenure.Customary : Tenure.Freehold, DuesMitesPerSeason = c.Licensed ? LandRules.QuitRent(Rules, LicensedAcres(c)) : 0, AcquiredDay = day, Cause = DeedCause.Claimed,
        };
        Put(d, day);
        c.State = ClaimState.Deeded; c.DeedId = id;
        chron(Day(day), "claim_deeded", [c.Claimant, id], [Why.Of(CauseKind.Law, "ch_why_claim_deeded", ChronParam.Person("claimant", c.Claimant))], ("claim", c.Serial.ToString()), ("tenure", d.Tenure.ToString()), ("tiles", c.Tiles.Length.ToString()));
    }

    /// <summary>Pulls up the banner: an open claim ends (a deeded one stands; the deed no longer needs it).</summary>
    public LandResult Pull(EntityId claimant, int serial, int day, LandChronicle chron)
    {
        var c = Book.ClaimOf(serial);
        if (c == null || c.Claimant != claimant || c.State != ClaimState.Open) return LandResult.No(c?.State == ClaimState.Deeded ? "ld_claim_deeded_stays" : "ld_not_yours");
        c.State = ClaimState.Pulled; c.EndDay = day;
        chron(Day(day), "claim_pulled", [claimant], [Why.Of(CauseKind.Law, "ch_why_claim_pulled", ChronParam.Person("claimant", claimant))], ("claim", serial.ToString()));
        return new LandResult(true, "ld_claim_pulled", [], 0, LandIds.Serial(RegionId, serial));
    }

    // ------------------------------------------------------------------ founding charters (spec 4a)

    /// <summary>The seat of a grantor in this region: the fief's seat, or the realm's largest town here (its royal officer), ties to the lower index; -1 when the realm has no place here.</summary>
    public int SeatOf(GrantorKind k, int grantor)
    {
        if (k == GrantorKind.Fief) return (uint)grantor < (uint)Region.Fiefs.Count ? Region.Fiefs[grantor].SeatSettlement : -1;
        int best = -1;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            var st = Region.Settlements[s];
            int realm = st.FiefIndex >= 0 && st.FiefIndex < Region.Fiefs.Count ? Region.Fiefs[st.FiefIndex].Realm : st.Realm;
            if (realm != grantor) continue;
            if (best < 0) { best = s; continue; }
            var b = Region.Settlements[best];
            int rank(RegionSettlement x) => x.Class == SettlementClass.Town ? 0 : x.Class == SettlementClass.Village ? 1 : 2;
            if (rank(st) < rank(b) || rank(st) == rank(b) && st.TargetPop > b.TargetPop) best = s;
        }
        return best;
    }

    private bool SiteTileOk(int t, GrantorKind k, int grantor)
    {
        if (!LandRules.OpenGround(Region, t) || LandRules.LandFief(Region, Rules, t) != -1) return false;           // wild land, beyond every lord's waste
        if (k == GrantorKind.Fief) return LandRules.FiefOfTile(Region, t) == grantor;                                // in the lord's wider domain
        return Region.RealmMap.Length <= t || Region.RealmMap[t] == grantor || Region.RealmMap[t] == 255;          // the realm's or nobody's
    }

    /// <summary>The site a charter at a point would cover (ascending tiles; empty when the ground there cannot carry one).</summary>
    public int[] CharterSite(GrantorKind k, int grantor, int xM, int yM)
    {
        int t0 = LandRules.TileAt(xM, yM);
        if (t0 < 0) return [];
        return Grow(t0, [], Rules.CharterSiteTiles, xM, yM, t => SiteTileOk(t, k, grantor));
    }

    /// <summary>
    /// A petition for a founding charter (spec 4a, Phase 5's simple grant rule): made in person at the grantor's seat; granted when the petitioner's standing with the grantor is at least
    /// <c>CharterMinStanding</c>, the fee is paid (to the grantor's treasury, <c>Rent</c>) and the site is neither claimed nor contested (an active war near it). The charter is a deed of kind
    /// <see cref="HoldingKind.FoundingCharter"/> with its conditions: <c>CharterMinHouseholds</c> households settled within <c>CharterSettleYears</c>, dues a year, service owed (LD-C1, LD-C3).
    /// </summary>
    public LandResult Petition(EntityId grantee, PurseKind kind, GrantorKind gk, int grantor, int atSettlement, int xM, int yM, int day, LandChronicle chron)
    {
        int seat = SeatOf(gk, grantor);
        if (seat < 0) return LandResult.No("ld_charter_no_grantor");
        if (atSettlement != seat) return LandResult.No("ld_charter_at_seat", Region.Settlements[seat].Name);
        if (CharterJudge == null && Book.StandingWith(gk, grantor) < Rules.CharterMinStanding) return LandResult.No("ld_charter_standing");
        var tiles = CharterSite(gk, grantor, xM, yM);
        if (tiles.Length < Rules.CharterSiteTiles) return LandResult.No("ld_charter_no_site");
        bool contested = tiles.Any(Taken) || Region.NearestActiveWarM(xM, yM) < Rules.CharterWarM;
        if (CharterJudge != null)
        {
            // P6-C3 (rule-and-income 4.9): charters are politics: the ruler's opinion, the petitioner's standing, the fee and whether the land is contested, and a refusal names its largest negative term
            var (granted, refusalKey) = CharterJudge(grantee, gk, grantor, Rules.CharterFeeMites, contested);
            if (!granted) return LandResult.No(refusalKey);
        }
        else if (contested) return LandResult.No("ld_charter_contested");
        var (gId, gKind) = gk == GrantorKind.Realm ? (RealmTreasuryId(grantor), PurseKind.Treasury) : (Region.Fiefs[grantor].Id, PurseKind.Treasury);
        long fee = Rules.CharterFeeMites;
        if (Balance(grantee, kind) < fee || !Pay(grantee, kind, gId, gKind, fee, TransferReason.Rent)) return new LandResult(false, "ld_cannot_afford", [Money(fee)], fee, CannotAfford: true);
        int anchor = -1; long bd = long.MaxValue;
        for (int s = 0; s < Region.Settlements.Count; s++) { long d2 = RegionData.Dist2(Region.Settlements[s].CenterX - xM, Region.Settlements[s].CenterY - yM); if (d2 < bd) { bd = d2; anchor = s; } }
        var c = new Charter
        {
            Serial = Book.NextSerial++, GrantorKind = gk, Grantor = grantor, Grantee = grantee, GranteePurse = kind, Tiles = tiles, CenterX = xM, CenterY = yM, Anchor = anchor,
            GrantDay = day, SettleByDay = day + Rules.CharterSettleYears * TimeConst.DaysPerYear, MinHouseholds = Rules.CharterMinHouseholds,
            DuesMitesPerYear = Rules.CharterDuesMitesPerYear, FeeMites = fee, ServiceMen = Rules.CharterServiceMen, ServiceDays = Rules.CharterServiceDays, State = CharterState.Granted,
        };
        var id = LandIds.Serial(RegionId, c.Serial);
        c.DeedId = id;
        Book.Charters.Add(c);
        var deed = new Deed
        {
            Id = id, Kind = HoldingKind.FoundingCharter, Subject = c.Serial, Settlement = -1, Holder = grantee, HolderPurse = kind, Tenure = Tenure.Freehold, Lord = gk == GrantorKind.Fief ? grantor : -1,
            DuesMitesPerSeason = c.DuesMitesPerYear / 4, AcquiredDay = day, Cause = DeedCause.Granted,
        };
        Put(deed, day);
        string grantorName = GrantorName(gk, grantor);
        c.GrantEvent = chron(Day(day), "charter_granted", [grantee, gId, id], [Why.Of(CauseKind.Law, "ch_why_charter_petition", ChronParam.Person("grantee", grantee), ChronParam.Place("seat", Region.Settlements[seat].Id))],
            ("grantor", grantorName), ("fee", fee.ToString()), ("tiles", tiles.Length.ToString()), ("settle_by", c.SettleByDay.ToString()), ("households", c.MinHouseholds.ToString()),
            ("dues_year", c.DuesMitesPerYear.ToString()), ("service", $"{c.ServiceMen}x{c.ServiceDays}"));
        return new LandResult(true, "ld_charter_granted", [grantorName, Money(fee), c.MinHouseholds, c.SettleByDay], fee, id);
    }

    /// <summary>P6-C3: the politics of a charter petition (grantee, grantor, the fee, whether the land is contested): granted or the refusal's text key. Null leaves Phase 5's simple standing rule in force. Not saved.</summary>
    public Func<EntityId, GrantorKind, int, long, bool, (bool Granted, string RefusalKey)>? CharterJudge { get; set; }

    /// <summary>
    /// P6-C3 (rule-and-income 4.6, 4.9): a deed passes to <paramref name="to"/> by a cause of the book (a mortgage forfeited, a charter revoked): the registry's deed is copied with its new holder and the change recorded.
    /// Returns false when no such deed exists.
    /// </summary>
    public bool Transfer(EntityId deedId, EntityId to, PurseKind toKind, int day, DeedCause cause)
    {
        var d = Registry().Get(deedId); if (d == null) return false;
        var n = d.Clone(); n.Holder = to; n.HolderPurse = toKind; n.AcquiredDay = day; n.Cause = cause; n.AskMites = 0; n.NoticeDay = -1; n.OwedMites = 0; n.ArrearsSeasons = 0; n.InDebt = false;
        Put(n, day);
        return true;
    }

    /// <summary>P6-C3 (rule-and-income 4.9): a charter's deed returns to its grantor (<see cref="DeedCause.Forfeited"/>); the settlers stay as the grantor's tenants. The charter keeps its <see cref="CharterState.Lapsed"/> state.</summary>
    public bool RevokeCharter(Charter c, int day, LandChronicle chron)
    {
        if (!Book.Deeds.TryGetValue(c.DeedId, out var d)) return false;
        var (gId, gKind) = c.GrantorKind == GrantorKind.Realm ? (RealmTreasuryId(c.Grantor), PurseKind.Treasury) : ((uint)c.Grantor < (uint)Region.Fiefs.Count ? Region.Fiefs[c.Grantor].Id : EntityId.None, PurseKind.Treasury);
        Book.Deeds.Remove(c.DeedId);
        Book.Changes.Add(new DeedChange(day, c.DeedId, gId, Tenure.Freehold, 0, -1, DeedCause.Forfeited));
        c.State = CharterState.Lapsed; if (c.LapseDay < 0) c.LapseDay = day;
        _ = d; _ = gKind; _ = chron;
        return true;
    }

    /// <summary>P6-C3 (rule-and-income 4.7): the lord's eviction of an unlicensed claim: it is <see cref="ClaimState.Pulled"/>, and what the squatter built stays with the lord (a change of the book with <see cref="DeedCause.Evicted"/>).</summary>
    public bool EvictClaim(Claim c, int day, LandChronicle chron)
    {
        if (c.State != ClaimState.Open) return false;
        c.State = ClaimState.Pulled; c.EndDay = day;
        EntityId lordId = EntityId.None;
        if (c.Fief >= 0 && c.Fief < Region.Fiefs.Count) (lordId, _) = LandRegistry.LordOf(Pop, Region.Fiefs[c.Fief].SeatSettlement);
        Book.Changes.Add(new DeedChange(day, LandIds.Serial(RegionId, c.Serial), lordId, Tenure.Freehold, 0, -1, DeedCause.Evicted));
        chron(Day(day), "claim_evicted", [c.Claimant, lordId], [new Cause(CauseKind.Law, null, NoteKey: "ch_note_squatter_evicted")], ("claim", c.Serial.ToString()), ("tiles", c.Tiles.Length.ToString()));
        return true;
    }

    public string GrantorName(GrantorKind k, int g) => k == GrantorKind.Fief ? (uint)g < (uint)Region.Fiefs.Count ? Region.Fiefs[g].Name : "" : Region.Realms.FirstOrDefault(r => r.HistoryIndex == g)?.Name ?? $"realm {g}";

    /// <summary>A household of the region that would come to the site: alive, landless peasants, not already a charter's.</summary>
    public bool CanSettle(int hh)
    {
        var hht = Pop.Households;
        if ((uint)hh >= (uint)hht.Count || hht.MemberCount[hh] == 0 || hht.Class[hh] != SocialClass.Peasant || hht.ParcelCount[hh] > 0) return false;
        var id = hht.IdOf(hh);
        foreach (var c in Book.Charters) if (c.Households.Contains(id)) return false;
        foreach (int f in Pop.FiefLordHousehold) if (f == hh) return false;
        return true;
    }

    /// <summary>
    /// A household the grantee recruited comes to the site (spec 4a: they walk there with their own goods; coin and stores stay theirs): it leaves its dwelling and camps on the site, counted
    /// at the anchor settlement, until build mode lets it raise a house. When enough have come before the deadline the charter is fulfilled (LD-C2).
    /// </summary>
    public LandResult Settle(EntityId grantee, int serial, int hh, int day, LandChronicle chron)
    {
        var c = Book.CharterOf(serial);
        if (c == null || c.Grantee != grantee || c.State == CharterState.Lapsed) return LandResult.No("ld_not_yours");
        if (!CanSettle(hh)) return LandResult.No("ld_settle_refused");
        var hht = Pop.Households; var pt = Pop.People; var lh = Pop.LotHousehold;
        int s0 = hht.Settlement[hh], l0 = hht.Lot[hh];
        if (s0 >= 0 && s0 < lh.Length && l0 >= 0 && l0 < lh[s0].Length && lh[s0][l0] == hh) lh[s0][l0] = -1;
        hht.Settlement[hh] = c.Anchor; hht.Lot[hh] = -1; hht.Beds[hh] = 0; hht.Dwelling[hh] = DwellingType.None; hht.Deed[hh] = DeedKind.None;
        foreach (int m in hht.Members(hh)) { pt.HomeSettlement[m] = c.Anchor; pt.HomeLot[m] = -1; pt.Bed[m] = -1; if (pt.Status[m] == PersonStatus.Alive) pt.Homeless[m] = HomelessReason.NoRoom; }
        var id = hht.IdOf(hh);
        c.Households.Add(id);
        chron(Day(day), "charter_settled", [id, grantee, c.DeedId], [Why.After(CauseKind.Migration, c.GrantEvent, "ch_why_charter_recruited")], ("household", hht.Name[hh]), ("settled", Settled(c).ToString()));
        CheckCharter(c, day, chron);
        return new LandResult(true, "ld_settled", [hht.Name[hh], Settled(c), c.MinHouseholds], 0, c.DeedId);
    }

    /// <summary>Settled households that are still there: alive, counted at the anchor, camping or housed on the site.</summary>
    public int Settled(Charter c)
    {
        var hht = Pop.Households; int n = 0;
        foreach (var id in c.Households)
        {
            int h = hht.IndexOf(id);
            if (h < 0 || hht.MemberCount[h] == 0 || hht.Settlement[h] != c.Anchor) continue;
            int l = hht.Lot[h];
            if (l < 0) { n++; continue; }
            var q = Region.Settlements[c.Anchor].Plan.Lots[l].Rect;
            if (Array.BinarySearch(c.Tiles, LandRules.TileAt(q.CenterX, q.CenterY)) >= 0) n++;
        }
        return n;
    }

    /// <summary>
    /// Fulfils a charter with enough households before its deadline, records a lapse after it (Phase 5 only records it; revocation is Phase 6), and its growth into a village by the existing
    /// rule: <c>CharterVillagePeople</c> people with a chapel or a mill on the site (recorded; a new settlement of its own waits for Phase 6 zoning).
    /// </summary>
    public void CheckCharter(Charter c, int day, LandChronicle chron)
    {
        if (c.State == CharterState.Granted)
        {
            int n = Settled(c);
            if (n >= c.MinHouseholds && day <= c.SettleByDay)
            {
                c.State = CharterState.Fulfilled; c.FulfilDay = day;
                chron(Day(day), "charter_fulfilled", [c.Grantee, c.DeedId], [Why.After(CauseKind.Law, c.GrantEvent, "ch_why_charter_in_time")], ("settled", n.ToString()), ("deadline", c.SettleByDay.ToString()));
            }
            else if (day > c.SettleByDay)
            {
                c.State = CharterState.Lapsed; c.LapseDay = day;
                chron(Day(day), "charter_lapsed", [c.Grantee, c.DeedId], [Why.After(CauseKind.Law, c.GrantEvent, "ch_why_charter_too_few")], ("settled", n.ToString()), ("needed", c.MinHouseholds.ToString()));
            }
        }
        if (c.State == CharterState.Fulfilled && c.VillageDay < 0)
        {
            var hht = Pop.Households; int people = 0;
            foreach (var id in c.Households) { int h = hht.IndexOf(id); if (h >= 0) people += hht.MemberCount[h]; }
            if (people < Rules.CharterVillagePeople || c.Anchor < 0) return;
            bool church = Region.Settlements[c.Anchor].Plan.Lots.Any(l => l.Use is LotUse.Chapel or LotUse.Church or LotUse.Mill && Array.BinarySearch(c.Tiles, LandRules.TileAt(l.Rect.CenterX, l.Rect.CenterY)) >= 0);
            if (!church) return;
            c.VillageDay = day;
            chron(Day(day), "charter_village", [c.Grantee, c.DeedId], [Why.After(CauseKind.Migration, c.GrantEvent, "ch_why_charter_village")], ("people", people.ToString()));
        }
    }

    // ------------------------------------------------------------------ the daily and seasonal steps

    /// <summary>
    /// The day's land work (hour 6): notices that ran out end their leases (eviction), leases at their end renew or end (a lease in arrears or with goods inside holds over), claims without an
    /// improvement lapse on their 60th day (LD-9), charters are fulfilled or lapse.
    /// </summary>
    public void Daily(int day, LandChronicle chron)
    {
        foreach (var d in Book.Deeds.Values.Where(x => x.NoticeDay >= 0 && day >= x.NoticeDay).ToList()) Evict(d, day, chron);
        foreach (var d in Book.Deeds.Values.Where(x => x.IsLease && x.LeaseEndDay >= 0 && day >= x.LeaseEndDay).ToList())
        {
            bool goods = d.Kind == HoldingKind.Building && Econ.OtherStores.TryGetValue(d.Id, out var st) && st.RowCount > 0 && st.Rows().Any(r => r.Qty > 0);
            if (d.Kind != HoldingKind.Stall && (d.Renew || d.OwedMites > 0 || goods))
            {
                d.LeaseEndDay += Rules.LeaseTermDays;                 // renewed, or held over while something is owed or left inside
                Book.Changes.Add(new DeedChange(day, d.Id, d.Holder, d.Tenure, d.DuesMitesPerSeason, d.LeaseEndDay, DeedCause.Leased));
                continue;
            }
            Book.Deeds.Remove(d.Id);
            if (d.Kind == HoldingKind.Parcel) ParcelLet?.Invoke((int)d.Subject, d.Holder, false);
            Book.Changes.Add(new DeedChange(day, d.Id, d.Lessor, Tenure.Freehold, 0, -1, DeedCause.LeaseEnded));
            chron(Day(day), "lease_ended", [d.Holder, d.Id, d.Lessor], [Why.Of(CauseKind.Trade, "ch_why_lease_ran_out", ChronParam.Person("holder", d.Holder))], ("kind", d.Kind.ToString()));
        }
        foreach (var c in Book.Claims)
        {
            if (c.State != ClaimState.Open || c.Improvements > 0 || day < c.StakeDay + Rules.ClaimImproveWithinDays) continue;
            c.State = ClaimState.Lapsed; c.EndDay = day;
            chron(Day(day), "claim_lapsed", [c.Claimant], [Why.Of(CauseKind.Law, "ch_why_claim_lapsed", ChronParam.Person("claimant", c.Claimant))], ("claim", c.Serial.ToString()), ("tiles", c.Tiles.Length.ToString()));
        }
        foreach (var c in Book.Charters) CheckCharter(c, day, chron);
    }

    /// <summary>The season's land work (its first day): dues and the arrears ladder, then the resale of offered deeds.</summary>
    public void Seasonal(int day, LandChronicle chron)
    {
        CollectDues(day, chron);
        Resale(day, chron);
    }

    // ------------------------------------------------------------------ helpers

    private void Put(Deed d, int day)
    {
        Book.Deeds[d.Id] = d;
        Book.Changes.Add(new DeedChange(day, d.Id, d.Holder, d.Tenure, d.DuesMitesPerSeason, d.LeaseEndDay, d.Cause));
    }

    private static GameTime Day(int day) => GameTime.FromGameHours((long)day * 24);
    public static string Money(long mites) => Ravensguard.Sim.Trade.Quotes.Money(mites);
}
