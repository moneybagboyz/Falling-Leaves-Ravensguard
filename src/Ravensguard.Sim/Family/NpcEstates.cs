using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Family;

/// <summary>A business a minor heir owns that his nearest adult kin runs as steward until he is sixteen (family-and-lifespan 4.7).</summary>
public sealed class Stewardship
{
    public ulong Business, Heir, Steward;
    public int Since;
}

/// <summary>
/// The estates of NPCs (family-and-lifespan 4.7, FL-13), the Phase 5 carry-overs: a head of household with property who has no kin to leave it to, or whose heir he dislikes, makes a will at the church (<see cref="NpcWills"/>, 20 percent a
/// year on his birthday, stream 0x646); a business a minor heir owns is run by his nearest adult kin as steward until he is sixteen (the heir does not work it; the steward is paid a day's wage from its cash box, and the
/// business hires a master of its own trade, as an inheritor of another trade's shop does); and a creditor who claimed a business and whose staff go unpaid for 15 days sells it to the best-off household of the place (the price
/// goes to the creditor, the shop to the buyer). Every step is in id order and keyed on ids and days.
/// </summary>
public static class NpcEstates
{
    // ------------------------------------------------------------------ wills

    /// <summary>The yearly draw (stream 0x646, key the person and the game year): does an NPC head with the means and the reason make his will this year?</summary>
    public static bool MakesWill(Ravensguard.Content.FamilySettingsDef s, ulong seed, ulong person, int year) => DetRng.Range(seed, year, person, RngStreams.NpcWills, 0, 0, 100) < s.NpcWillPct;

    /// <summary>The priest of a settlement who witnesses a will (the lowest person row of that trade, alive, with a household), -1 none.</summary>
    public static int PriestOf(FamilyWorld w, int settlement)
    {
        var pt = w.People;
        for (int i = 0; i < pt.Count; i++)
            if (pt.Status[i] == PersonStatus.Alive && pt.Occupation[i] == Occupation.Priest && pt.HomeSettlement[i] == settlement && pt.Household[i] >= 0) return i;
        return -1;
    }

    /// <summary>
    /// The day's wills (4.7): each NPC head of household alive, adult, on his birthday, with at least <c>NpcWillMinMites</c> of coin in his house and no will yet, makes one with the chance <c>NpcWillPct</c> (stream 0x646, key the person
    /// and the game year) when he has no kin the law would give it to, or his law heir's trust in him is below <c>NpcWillDislikePm</c> (the relation table; kin start at 400). He leaves everything to the next of his kin, else to the
    /// priest of the place (the church); the fee is the Phase 5 one, paid to the priest who takes it down. Returns how many were made.
    /// </summary>
    public static int Wills(FamilyWorld w, int day)
    {
        var pt = w.People; var hht = w.Households; int made = 0;
        for (int hh = 0; hh < hht.Count; hh++)
        {
            int head = hht.Head[hh];
            if (head < 0 || head >= pt.Count || head == w.HeroRow || pt.Status[head] != PersonStatus.Alive || hht.MemberCount[hh] == 0) continue;
            if (pt.AgeYears(head, day) < w.S.AdultAge || !KinTurning.Birthday(pt, head, day)) continue;
            var id = pt.IdOf(head);
            if (w.St.Wills?.Of(id) != null) continue;
            if (w.Ledger.Balance(w.HouseholdPurse(hh)).Mites < w.S.NpcWillMinMites) continue;
            if (!MakesWill(w.S, w.Seed, id.Value, day / TimeConst.DaysPerYear)) continue;
            var heirs = EstateSettler.Heirs(pt, head, day, kin: w.Pop.Kin);
            int priest = PriestOf(w, pt.HomeSettlement[head]);
            if (priest < 0 || priest == head) continue;
            EntityId to;
            if (heirs.Count == 0) to = pt.IdOf(priest);
            else
            {
                int trust = w.St.Talk?.Relations.Of(head, heirs[0].Row, kin: true).TrustPm ?? 400;
                if (trust >= w.S.NpcWillDislikePm) continue;
                to = heirs.Count > 1 ? heirs[1].Person : pt.IdOf(priest);                         // the next of kin, else the church
            }
            var will = w.St.Wills ??= new WillBook();
            var (ok, _) = will.Make(w.Ledger, id, w.HouseholdPurse(hh), w.HouseholdPurse(pt.Household[priest]), pt.IdOf(priest), [new Bequest(to, BequestWhat.All)], day);
            if (ok) made++;
        }
        return made;
    }

    // ------------------------------------------------------------------ stewards for minors

    /// <summary>
    /// The minors' businesses (4.7): a business open and owned by an NPC under sixteen is run by his nearest adult kin (<see cref="EstateSettler.Guardian"/>) as steward: the heir does not work it, the steward is paid
    /// <c>StewardWageMitesPerDay</c> from its cash box when it holds that much, and when the heir turns sixteen he takes it up himself. Returns how many stewardships were named or ended.
    /// </summary>
    public static int Stewards(FamilyWorld w, int day)
    {
        var prod = w.St.Production; if (prod == null) return 0;
        var pt = w.People; var book = w.Book; int changed = 0;
        // ended: the heir has come of age (or died, or lost the business): the business is his to work again
        foreach (var s in book.Stewards.Values.OrderBy(x => x.Business).ToList())
        {
            var b = prod.ById(new EntityId(s.Business)); int heir = pt.IndexOf(new EntityId(s.Heir));
            bool over = b == null || heir < 0 || pt.Status[heir] != PersonStatus.Alive || b.Owner != heir || pt.AgeYears(heir, day) >= w.S.AdultAge;
            if (!over) continue;
            book.Stewards.Remove(s.Business); changed++;
            if (b != null && heir >= 0 && b.Owner == heir && pt.Status[heir] == PersonStatus.Alive) { b.OwnerWorks = prod.KindOf(b).Stations.Length > 0 && !prod.KindOf(b).Trader; Chron(w, day, "minor_of_age", "ch_family_minor_of_age", [new EntityId(s.Heir), new EntityId(s.Steward)], [ChronParam.Person("heir", new EntityId(s.Heir)), ChronParam.Person("steward", new EntityId(s.Steward)), ChronParam.Place("place", w.PlaceId(b.Settlement))], "ch_note_minor_grown"); }
        }
        foreach (var b in prod.Businesses)
        {
            if (b.State != Ravensguard.Sim.Production.BusinessState.Open || b.Owner < 0 || b.Owner >= pt.Count || pt.Status[b.Owner] != PersonStatus.Alive || b.Owner == w.HeroRow) continue;
            if (pt.AgeYears(b.Owner, day) >= w.S.AdultAge) continue;
            if (!book.Stewards.ContainsKey(b.Id.Value))
            {
                int g = EstateSettler.Guardian(pt, b.Owner, day); if (g < 0) continue;
                book.Stewards[b.Id.Value] = new Stewardship { Business = b.Id.Value, Heir = pt.IdOf(b.Owner).Value, Steward = pt.IdOf(g).Value, Since = day };
                b.OwnerWorks = false; changed++;
                Chron(w, day, "minor_steward", "ch_family_minor_steward", [pt.IdOf(g), pt.IdOf(b.Owner)], [ChronParam.Person("steward", pt.IdOf(g)), ChronParam.Person("heir", pt.IdOf(b.Owner)), ChronParam.Place("place", w.PlaceId(b.Settlement))], "ch_note_minor_heir");
            }
            if (book.Stewards.TryGetValue(b.Id.Value, out var st))
            {
                int g = pt.IndexOf(new EntityId(st.Steward));
                if (g >= 0 && pt.Status[g] == PersonStatus.Alive && pt.Household[g] >= 0)
                {
                    long wage = Math.Min(w.S.StewardWageMitesPerDay, w.Ledger.Balance(b.Purse).Mites);
                    if (wage > 0) w.Ledger.Transfer(b.Purse, w.HouseholdPurse(pt.Household[g]), Coin.FromMites(wage), TransferReason.Wage);
                }
            }
        }
        return changed;
    }

    // ------------------------------------------------------------------ a creditor sells a claimed business

    /// <summary>
    /// The creditors' sales (4.7, Phase 5's open item): for each business an estate's creditor claimed (its owner's house is the claimant's), the days its staff have gone unpaid (wages owed) are counted; at
    /// <c>CreditorSellDays</c> the creditor sells it at its value to the best-off household of the place that can pay (a head alive and grown, not the claimant's): the price goes to the claimant's purse through the ledger, the
    /// buyer's head owns the shop and its premises' deed goes with it. Returns how many were sold.
    /// </summary>
    public static int Creditors(FamilyWorld w, int day)
    {
        var prod = w.St.Production; var sb = w.St.Succession; if (prod == null || sb == null || w.Content == null) return 0;
        var pt = w.People; var hht = w.Households; var book = w.Book; int sold = 0;
        var claimed = new SortedDictionary<ulong, ulong>();
        foreach (var est in sb.Estates)
            foreach (var e in est.Holdings)
                if (e.Kind == EstateThing.Business && e.ClaimedBy != EntityId.None) claimed[e.Id.Value] = e.ClaimedBy.Value;
        foreach (var (bid, claimant) in claimed)
        {
            var b = prod.ById(new EntityId(bid));
            if (b == null || b.State != Ravensguard.Sim.Production.BusinessState.Open || b.OwnerHousehold < 0 || b.OwnerHousehold >= hht.Count) { book.Distress.Remove(bid); continue; }
            if (hht.IdOf(b.OwnerHousehold).Value != claimant && (b.Owner < 0 || b.Owner >= pt.Count || pt.IdOf(b.Owner).Value != claimant)) { book.Distress.Remove(bid); continue; }     // sold on already, or never his
            if (b.ArrearsMites <= 0) { book.Distress.Remove(bid); continue; }
            int days = book.Distress.GetValueOrDefault(bid) + 1;
            if (days < w.S.CreditorSellDays) { book.Distress[bid] = days; continue; }
            var ow = OwnershipWiring.For(w.St, w.Content);
            long price = ow.ValueOf(b); int seller = b.OwnerHousehold, buyerHh = -1, buyerHead = -1; long best = -1;
            for (int hh = 0; hh < hht.Count; hh++)
            {
                if (hh == seller || hht.MemberCount[hh] == 0 || hht.Settlement[hh] != b.Settlement) continue;
                int head = hht.Head[hh]; if (head < 0 || head == w.HeroRow || pt.Status[head] != PersonStatus.Alive || pt.AgeYears(head, day) < w.S.AdultAge) continue;
                long coin = w.Ledger.Balance(w.HouseholdPurse(hh)).Mites;
                if (coin >= price && coin > best) { best = coin; buyerHh = hh; buyerHead = head; }
            }
            if (buyerHh < 0) continue;                                                              // nobody can pay yet: the count keeps
            if (price > 0 && !w.Ledger.Transfer(w.HouseholdPurse(buyerHh), w.HouseholdPurse(seller), Coin.FromMites(price), TransferReason.Trade)) continue;
            int sellerHead = hht.Head[seller];
            prod.Inherit(b, buyerHead);
            if (b.Lot >= 0 && ow.Land is { } land) land.ConveyLot(b.Settlement, b.Lot, hht.IdOf(buyerHh), PurseKind.Household, day, DeedCause.Bought);
            book.Distress.Remove(bid); sold++;
            var subj = new List<EntityId> { pt.IdOf(buyerHead) }; if (sellerHead >= 0) subj.Add(pt.IdOf(sellerHead));
            Chron(w, day, "business_sold_unpaid", "ch_family_business_sold", subj,
                [ChronParam.Person("seller", sellerHead >= 0 ? pt.IdOf(sellerHead) : EntityId.None), ChronParam.Person("buyer", pt.IdOf(buyerHead)), ChronParam.Place("place", w.PlaceId(b.Settlement)), ChronParam.Mites("price", price)], "ch_note_staff_unpaid");
        }
        return sold;
    }

    // ------------------------------------------------------------------

    private static void Chron(FamilyWorld w, int day, string type, string key, IReadOnlyList<EntityId> subjects, IReadOnlyList<ChronParam> ps, string note) =>
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L + 4 * 3600), type, subjects, [new Cause(CauseKind.Family, null, NoteKey: note)],
            new Dictionary<string, string> { ["day"] = day.ToString(CultureInfo.InvariantCulture) }, TextKey: key, Params: ps));
}
