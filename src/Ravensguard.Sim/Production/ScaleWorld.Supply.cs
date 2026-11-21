using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production;

/// <summary>
/// Supply contracts and machine couplings (P7-ES2, economies-of-scale 4.3, 4.4, 4.5). A contract is a standing order between two businesses: goods move by store move from the seller to the buyer on the due day, the buyer pays the
/// seller the price fixed at signing (reason <c>Trade</c>; no spread, no market toll: the goods never enter a book), and the carter pays the carting (the crew and team to the household of a carter of the place, the tolls to the
/// treasury of the town the goods enter). Every coin is one ledger transfer and every good one store move.
/// </summary>
public sealed partial class ScaleWorld
{
    /// <summary>The household of a carter of a place (the first adult labourer there; -1 when there is none), the hook the world state installs (<c>HoldingsDesk.CarterOf</c>).</summary>
    public Func<int, int, int> CarterOf { get; set; } = (_, _) => -1;

    // ------------------------------------------------------------------ signing (4.3)

    /// <summary>
    /// Signs a supply contract between two open businesses (4.3): at least 500 kg a delivery (or 10 gp a season of piece goods), every <paramref name="everyDays"/> days (the default 5), 1 to 4 seasons, from the day after signing, at the seller's
    /// book mid adjusted by the owners' Trading (within +-50 permille). The carter is the seller unless <paramref name="carterIsSeller"/> is false.
    /// </summary>
    public OwnResult Offer(Business seller, Business buyer, ushort good, long qty, int everyDays, int seasons, bool carterIsSeller, int day)
    {
        var ss = Ss; var pt = Prod.Pop.People;
        if (seller == buyer) return OwnResult.No("sc_contract_same");
        if (everyDays <= 0) everyDays = ss.ContractEveryDays;
        if (seasons < 1 || seasons > ss.ContractMaxSeasons || everyDays < 1) return OwnResult.No("sc_contract_terms", ss.ContractMaxSeasons);
        if (seller.State != BusinessState.Open || buyer.State != BusinessState.Open || qty <= 0) return OwnResult.No("sc_contract_terms", ss.ContractMaxSeasons);
        var book = Prod.Markets?.BookForSettlement(seller.Settlement);
        if (book == null) return OwnResult.No("sc_contract_no_book");
        long mid = book.MidMicro(good);
        int ts = seller.Owner >= 0 ? pt.SkillLevelOf(seller.Owner, Skill.Trading) : 0, tb = buyer.Owner >= 0 ? pt.SkillLevelOf(buyer.Owner, Skill.Trading) : 0;
        long price = Contracts.PriceMicro(mid, ts, tb, ss.ContractBandPm);
        bool piece = Prod.Econ.Goods.IsPiece(good);
        if (!piece && qty < ss.ContractMinG) return OwnResult.No("sc_contract_too_small", ss.ContractMinG / 1000);
        if (piece && Contracts.SeasonMites(qty, price, everyDays, TimeConst.DaysPerSeason) < ss.ContractMinSeasonMites) return OwnResult.No("sc_contract_too_small", ss.ContractMinSeasonMites);
        var c = new SupplyContract
        {
            Id = Book.NextContractId++, Seller = seller.Id.Value, Buyer = buyer.Id.Value, Good = Prod.Econ.Goods.StringId(good), QtyPerDelivery = qty, EveryDays = everyDays,
            StartDay = day + 1, TermSeasons = seasons, PriceMicro = price, CarterIsSeller = carterIsSeller, NextDue = day + 1,
        };
        Book.Contracts.Add(c);
        return new OwnResult(true, "sc_contract_signed", [qty / (piece ? 1 : 1000), everyDays, seasons, c.Id]);
    }

    /// <summary>Ends an open contract by agreement of the party who asks (no breach, no opinion change).</summary>
    public OwnResult End(int id, int day)
    {
        if (Book.Contract(id) is not { State: SupplyState.Open } c) return OwnResult.No("sc_contract_unknown");
        c.State = SupplyState.Done; c.EndDay = day;
        return OwnResult.Yes("sc_contract_ended");
    }

    // ------------------------------------------------------------------ the days (4.3)

    /// <summary>Every open contract falling due: delivered, or waited for up to the late days, then missed (the buyer's cover at the market charged as <c>Restitution</c> to a seller at fault; two misses in a season break it). Returns the deliveries made.</summary>
    public int Daily(int day)
    {
        if (Book.Contracts.Count == 0) return 0;
        int made = 0; var ss = Ss;
        for (int i = 0; i < Book.Contracts.Count; i++)
        {
            var c = Book.Contracts[i];
            if (c.State != SupplyState.Open) continue;
            if (c.NextDue >= c.EndsOn) { c.State = SupplyState.Done; c.EndDay = day; continue; }
            if (day < c.NextDue) continue;
            if (Deliver(c, day, out bool sellerAtFault)) { c.Delivered++; c.NextDue += c.EveryDays; made++; continue; }
            if (day - c.NextDue <= ss.ContractLateDays) continue;
            Miss(c, day, sellerAtFault);
        }
        Book.Contracts.RemoveAll(c => c.State != SupplyState.Open && day - c.EndDay > ss.FineKeepDays * 2);
        return made;
    }

    private void Miss(SupplyContract c, int day, bool sellerAtFault)
    {
        var ss = Ss; int season = day / TimeConst.DaysPerSeason;
        c.Missed++;
        if (c.SeasonOfMiss != season) { c.SeasonOfMiss = season; c.SeasonMisses = 0; }
        c.SeasonMisses++; c.NextDue += c.EveryDays;
        var seller = Prod.ById(new EntityId(c.Seller)); var buyer = Prod.ById(new EntityId(c.Buyer));
        if (sellerAtFault && seller != null && buyer != null && Prod.Econ.Goods.TryIdOf(c.Good, out var g) && Prod.Markets?.BookForSettlement(buyer.Settlement) is { } book)
        {
            // the buyer bought the goods at the market instead: the seller makes good what they cost above the contract price
            long diff = Math.Max(0, book.AskMicro(g) - c.PriceMicro);
            long due = (c.QtyPerDelivery * diff + 500_000) / 1_000_000, pay = Math.Min(due, Led.Balance(seller.Purse).Mites);
            if (pay > 0) Led.Transfer(seller.Purse, buyer.Purse, new Coin(pay), TransferReason.Restitution);
        }
        if (c.SeasonMisses < ss.ContractMissesToBreak) return;
        c.State = SupplyState.Broken; c.EndDay = day;
        var pt = Prod.Pop.People; var subjects = new List<EntityId>(); EntityId sp = EntityId.None, bp = EntityId.None;
        if (seller is { Owner: >= 0 }) { sp = pt.IdOf(seller.Owner); subjects.Add(sp); }
        if (buyer is { Owner: >= 0 }) { bp = pt.IdOf(buyer.Owner); subjects.Add(bp); }
        if (sp != EntityId.None && bp != EntityId.None && sp != bp) { AddOpinion?.Invoke(bp.Value, sp.Value, ss.ContractBrokenOpinion); AddOpinion?.Invoke(sp.Value, bp.Value, ss.ContractBrokenOpinion); }
        int gi = Prod.Econ.Goods.TryIdOf(c.Good, out var gg) ? gg : 0;
        Chron.Chron(GameTime.FromGameHours((long)day * 24 + 9), "supply_contract_broken", "ch_scale_contract_broken",
            [ChronParam.Person("buyer", bp), ChronParam.Person("seller", sp), ChronParam.Good("good", gi), ChronParam.Count("missed", c.Missed)],
            subjects, [new Cause(CauseKind.Trade, null, NoteKey: "ch_note_contract_missed")]);
    }

    /// <summary>One delivery: checked whole before anything moves, then goods, payment, the carting. False names who is at fault (the seller short of goods or a carter, else the buyer short of coin or room).</summary>
    private bool Deliver(SupplyContract c, int day, out bool sellerAtFault)
    {
        sellerAtFault = true;
        var seller = Prod.ById(new EntityId(c.Seller)); var buyer = Prod.ById(new EntityId(c.Buyer));
        if (seller == null || seller.State != BusinessState.Open) return false;
        if (buyer == null || buyer.State != BusinessState.Open) { sellerAtFault = false; return false; }
        if (!Prod.Econ.Goods.TryIdOf(c.Good, out var g)) return false;
        long qty = c.QtyPerDelivery;
        if (seller.Store.Qty(g) < qty) return false;
        long pay = Contracts.PayMites(qty, c.PriceMicro);
        var reg = Prod.Pop.Region;
        long dist = 0; bool gate = false;
        if ((uint)seller.Settlement < (uint)reg.Settlements.Count && (uint)buyer.Settlement < (uint)reg.Settlements.Count && seller.Settlement != buyer.Settlement)
        {
            long dx = reg.Settlements[seller.Settlement].CenterX - reg.Settlements[buyer.Settlement].CenterX, dy = reg.Settlements[seller.Settlement].CenterY - reg.Settlements[buyer.Settlement].CenterY;
            dist = IntMath.Isqrt(dx * dx + dy * dy); gate = true;
        }
        var book = Prod.Markets?.BookForSettlement(seller.Settlement);
        long hay = FeedPrice(book, "hay", Ss.HayMitesPerKg), oats = FeedPrice(book, "oats", Ss.OatsMitesPerKg);
        var trip = Carting.Plan(Ss, dist, Prod.Econ.Goods.MassG(g, qty), hay, oats, Prod.Content.Tax.GateTollMitesPerCart, gate);
        var carter = c.CarterIsSeller ? seller : buyer;
        int driver = trip.CrewAndTeamMites > 0 ? CarterOf(seller.Settlement, day) : -1;
        if (trip.CrewAndTeamMites > 0 && (driver < 0 || driver >= Prod.Econ.HouseholdCount)) { sellerAtFault = c.CarterIsSeller; return false; }
        PurseId treasury = default; long toll = trip.TollMites;
        if (toll > 0 && !Treasury(buyer.Settlement, out treasury, out _)) toll = 0;
        long bill = trip.CrewAndTeamMites + toll;
        long carterBal = Led.Balance(carter.Purse).Mites + (c.CarterIsSeller ? pay : 0);
        if (Led.Balance(buyer.Purse).Mites < pay + (c.CarterIsSeller ? 0 : bill)) { sellerAtFault = false; return false; }
        if (carterBal < bill) { sellerAtFault = c.CarterIsSeller; return false; }
        if (!Store.Move(seller.Store, buyer.Store, g, qty)) { sellerAtFault = false; return false; }
        if (pay > 0 && !Led.Transfer(buyer.Purse, seller.Purse, new Coin(pay), TransferReason.Trade)) throw new InvalidOperationException("contract payment vanished");
        if (trip.CrewAndTeamMites > 0 && !Led.Transfer(carter.Purse, Prod.Econ.HouseholdPurse[driver], new Coin(trip.CrewAndTeamMites), TransferReason.Wage)) throw new InvalidOperationException("carting bill vanished");
        if (toll > 0 && !Led.Transfer(carter.Purse, treasury, new Coin(toll), TransferReason.Toll)) throw new InvalidOperationException("carting toll vanished");
        return true;
    }

    private long FeedPrice(Ravensguard.Sim.Economy.Markets.MarketBook? book, string good, long fallback)
    {
        if (book == null || !Prod.Econ.Goods.TryIdOf(good, out var g)) return fallback;
        long mid = book.MidMicro(g);      // micro-mites a gram is 1,000 x mites a kilogram / 1,000,000: mites a kg = micro / 1,000
        return mid <= 0 ? fallback : Math.Max(1, (mid + 500) / 1000);
    }

    // ------------------------------------------------------------------ machines (4.5)

    /// <summary>Couples a station of a business to the machine's station piece at a cell, so the premises' power net sets the station's labour (the hook the business-to-premises link will fill).</summary>
    public OwnResult Couple(Business b, int station, int x, int y, int z)
    {
        if ((uint)station >= (uint)b.Stations.Count) return OwnResult.No("sc_couple_no_station");
        Book.Couplings.RemoveAll(k => k.Business == b.Id.Value && k.Station == station);
        Book.Couplings.Add(new MachineCoupling { Business = b.Id.Value, Station = station, X = x, Y = y, Z = z });
        Book.Couplings.Sort((a, c2) => a.Business != c2.Business ? a.Business.CompareTo(c2.Business) : a.Station.CompareTo(c2.Station));
        return OwnResult.Yes("sc_coupled");
    }

    public OwnResult Uncouple(Business b, int station)
        => Book.Couplings.RemoveAll(k => k.Business == b.Id.Value && k.Station == station) > 0 ? OwnResult.Yes("sc_uncoupled") : OwnResult.No("sc_couple_no_station");
}
