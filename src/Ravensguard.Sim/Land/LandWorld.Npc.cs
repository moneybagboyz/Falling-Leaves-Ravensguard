using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Land;

/// <summary>
/// P6-D4 (holdings-and-works-v2 3.3 and 3.4): what the NPC land market, the zoning step and the hero's offers need of the land world, kept apart from the Phase 5 file: the hooks the wiring sets, the free-lot
/// count of a place and the hero's offer for a house.
/// </summary>
public sealed partial class LandWorld
{
    /// <summary>The world's seed (the wiring sets it): the NPC market's ties are drawn from it.</summary>
    public ulong Seed { get; set; }

    /// <summary>The growth log (lots, parcels and streets built since generation): streets laid join it and are replayed on load.</summary>
    public GrowthState GrowthLog => Growth;
    /// <summary>Where visited chunks hear of a new street (null in a world with no chunks).</summary>
    public IGrowthChunkSink? GrowthSink => _sink();

    /// <summary>Whether the lord of a fief (-1: none) lets new streets be laid: zoning is a lord's policy (C3's rule book or D2's lords set it); null: every lord does.</summary>
    public Func<int, bool>? StreetsAllowed { get; set; }

    /// <summary>A household that bought a lot puts the house up on it (the wiring sets growth's <c>StartOn</c>): settlement, lot, household, day; false when it cannot. Null: none builds.</summary>
    public Func<int, int, int, int, bool>? StartBuild { get; set; }

    /// <summary>A holder's trust in the hero, permille (the wiring reads the talk book); null: 0.</summary>
    public Func<EntityId, int>? TrustOfHero { get; set; }

    /// <summary>
    /// The plots a place has free now: vacant houses nobody holds or builds on, and one more when <see cref="RegionGrowth.FindPlot"/> still finds a plot on its streets. The street planner lays a new street at 0.
    /// </summary>
    public int FreeLots(int s)
    {
        if ((uint)s >= (uint)Region.Settlements.Count) return 0;
        var st = Region.Settlements[s]; var gs = Growth;
        var claimed = new HashSet<int>(gs.Projects.Where(p => p.Settlement == s && p.Lot >= 0).Select(p => p.Lot));
        var reserved = gs.Projects.Where(p => p.Settlement == s && p.Kind != ProjectKind.Assart && p.Lot < 0).Select(p => p.Rect).ToList();
        int n = 0; var lots = st.Plan.Lots;
        for (int l = 0; l < lots.Count; l++)
            if (lots[l].Vacant && lots[l].Dwelling is DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage && !claimed.Contains(l) && !HoldsLot(s, l)) n++;
        bool town = st.Class == SettlementClass.Town;
        int w = town ? GrowthTuning.PlotWidthTownM : GrowthTuning.PlotWidthVillageM, d = town ? GrowthTuning.PlotDepthTownM : GrowthTuning.PlotDepthVillageM;
        if (RegionGrowth.FindPlot(Region, s, w, d, GrowthTuning.PlanMarginM, reserved, out _, out _)) n++;
        return n;
    }

    /// <summary>Nobody lives in the lot's house now (no household is placed in it).</summary>
    public bool LotEmpty(int s, int lot) => (uint)s < (uint)Pop.LotHousehold.Length && (uint)lot < (uint)Pop.LotHousehold[s].Length && Pop.LotHousehold[s][lot] < 0;

    /// <summary>
    /// The least a holder takes for a house (holdings 3.3): <c>price x (OfferBasePm - trust / 10) / 1000</c>, 1,100 less a tenth of his trust in the buyer in permille, so a stranger pays 1,100 and a friend
    /// the price itself. The price is <see cref="ValueOf"/> (the plot and the house, with the scarcity term).
    /// </summary>
    public long OfferFloor(Deed d, EntityId buyer)
    {
        int trust = Math.Clamp(TrustOfHero?.Invoke(d.Holder) ?? 0, -1000, 1000);
        return ValueOf(d) * (Rules.OfferBasePm - trust / 10) / 1000;
    }

    /// <summary>
    /// The hero's offer for a house or plot an NPC household holds (Q > Offer for this house; <c>ld:bid</c>): refused when it is a lease, the lord's (his plots are bought from his listings), the buyer's own, lived in
    /// (the holder lives there) or below <see cref="OfferFloor"/>; else the offer is paid to the holder as <c>Trade</c> and the deed passes to the buyer.
    /// </summary>
    public LandResult BidForHouse(EntityId buyer, PurseKind buyerKind, EntityId deedId, long offerMites, int day, LandChronicle chron)
    {
        var d = Registry().Get(deedId);
        if (d == null || d.Kind != HoldingKind.Lot || d.IsLease || d.Holder == buyer) return LandResult.No("ld_offer_not_for_sale");
        var (lordId, _) = LandRegistry.LordOf(Pop, d.Settlement);
        if (d.Holder == lordId || d.HolderPurse != PurseKind.Household) return LandResult.No("ld_offer_use_listing");
        var (_, lot) = LandIds.SplitLot(d.Subject);
        if (!LotEmpty(d.Settlement, lot)) return LandResult.No("ld_offer_lived_in");
        long floor = OfferFloor(d, buyer);
        if (offerMites < floor) return new LandResult(false, "ld_offer_low", [Money(floor)], floor);
        if (Balance(buyer, buyerKind) < offerMites || !Pay(buyer, buyerKind, d.Holder, d.HolderPurse, offerMites, TransferReason.Trade)) return new LandResult(false, "ld_cannot_afford", [Money(offerMites)], offerMites, CannotAfford: true);
        var seller = d.Holder;
        if (!Transfer(d.Id, buyer, buyerKind, day, DeedCause.Bought)) return LandResult.No("ld_offer_not_for_sale");
        var st = Region.Settlements[d.Settlement];
        chron(Day(day), "land_bought", [buyer, d.Id, seller], [new Cause(CauseKind.Trade, null, "bought from its holder")], ("settlement", st.Name), ("price", offerMites.ToString()), ("area_m2", (d.Rect.W * d.Rect.H).ToString()), ("tenure", d.Tenure.ToString()));
        return new LandResult(true, "ld_offer_taken", [st.Name, Money(offerMites)], offerMites, d.Id);
    }
}
