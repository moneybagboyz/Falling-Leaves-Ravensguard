using System.Globalization;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Bondage;

/// <summary>What an act of bondage came to: whether it was done, the answer's text key, the coin that moved and the chronicle entry written.</summary>
public sealed record SlaveResult(bool Ok, string Key, long Mites = 0, ulong Entry = 0)
{
    public static SlaveResult No(string key) => new(false, key);
}

/// <summary>
/// The acts of slavery (slavery 4.1 to 4.4): making a bond, buying and selling at a pen, binding a debtor or a convict, freeing, treatment and chains, self-purchase. Pure over a <see cref="BondWorld"/> and a
/// <see cref="BondBook"/>: coin moves only through the ledger (<see cref="TransferReason.SlaveSale"/>), people are never made or removed here (they are bonded, moved between households, and freed), and each act
/// writes its chronicle entry with its cause so "why is he a slave" always has an answer. The daily and seasonal passes are in <see cref="BondDaily"/> and <see cref="Raids"/>.
/// </summary>
public static class Slavery
{
    public static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>A chronicle entry of slavery's: <paramref name="cause"/> is the entry before it (or a note key), <paramref name="doer"/> the first person subject (the deed rows read it).</summary>
    public static ChronicleEntry Write(BondWorld bw, long sec, string type, string textKey, IReadOnlyList<EntityId> subjects, Cause cause, IReadOnlyList<ChronParam> ps, params (string K, string V)[] data)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in data) d[k] = v;
        if (!d.ContainsKey("doer") && subjects.Count > 0 && subjects[0].Kind == IdKind.Person) d["doer"] = bw.W.Name(subjects[0]);                        // the rumour names him
        foreach (var p in ps)
            if (p.Kind == ChronParamKind.Place && !d.ContainsKey("settlement")) { int at = Array.FindIndex(bw.W.SettlementIds, id => id.Value == (ulong)p.Value); if (at >= 0) d["settlement"] = bw.W.PlaceName(at); }
        var e = new ChronicleEntry(bw.W.Ids.Allocate(IdKind.Event), new GameTime(sec), type, [.. subjects], [cause], d, TextKey: textKey, Params: [.. ps]);
        bw.Chronicle.Add(e);
        return e;
    }

    public static Cause Note(string noteKey, ulong entry = 0, params ChronParam[] ps) =>
        entry != 0 ? new Cause(CauseKind.Slavery, new EntityId(entry), NoteKey: noteKey, NoteParams: ps) : new Cause(CauseKind.Slavery, null, NoteKey: noteKey, NoteParams: ps);

    public static string LawName(SlaveLaw l) => l switch { SlaveLaw.Debt => "debt", SlaveLaw.WarCaptives => "war_captives", SlaveLaw.Chattel => "chattel", _ => "none" };

    /// <summary>Whether a pen may stand in a place: the markets of chattel towns and the fairs of war-captive towns (slavery 3).</summary>
    public static bool HasMarket(SlaveLaw l) => l is SlaveLaw.Chattel or SlaveLaw.WarCaptives;

    /// <summary>"No one": the hero's own id is the value 0 (<c>EconomyState.HeroOwner</c>, which is also <c>EntityId.None</c>), so a missing person is the largest value instead.</summary>
    public static readonly EntityId Nobody = new(ulong.MaxValue);

    // ------------------------------------------------------------------ making a bond

    /// <summary>
    /// Makes a bond (the one place a <see cref="Bond"/> is created). The person must be alive and not already bonded; the caller has paid or been paid. <paramref name="cause"/> is the chronicle entry that explains it.
    /// </summary>
    public static Bond Make(BondWorld bw, BondBook book, EntityId person, EntityId owner, BondKind kind, BondOrigin origin, int day, int place, long price, int termEnd, ulong entry)
    {
        int row = bw.RowOf(person);
        var b = new Bond
        {
            Person = person.Value, Owner = owner.Value, Kind = kind, Origin = origin, Since = day, TermEndDay = termEnd, PriceMites = price, Place = place, Morale = bw.D.MoraleStart,
            HomeHousehold = row >= 0 ? bw.People.Household[row] : -1, Entry = entry,
            Chain = kind is BondKind.Debt or BondKind.Convict ? Restraint.None : Restraint.Bound,
            Work = kind is BondKind.Convict ? BondWork.Mine : BondWork.Fields,
        };
        book.Bonds[person.Value] = b;
        if (person == bw.W.HeroId) bw.W.Marks()?.SetEnslaved(person, true);
        return b;
    }

    /// <summary>A slaver band's id for a region and an index (an <c>IdKind.Other</c> id made from the region: the same region gives the same band).</summary>
    public static EntityId BandId(int region, int index) => EntityId.Make(IdKind.Other, 0x47_0000_0000_0000UL | ((ulong)(uint)region << 16) | (uint)index);

    /// <summary>A band's purse (a household account under its id), opened on first use.</summary>
    public static PurseId BandPurse(BondWorld bw, EntityId band) => bw.Ledger.Open(PurseKind.Household, band);

    // ------------------------------------------------------------------ the pens

    /// <summary>
    /// Stands a captive person in the pen of a market (4.1 rule 1): a bond owned by the captor (a band), asking the price table's price, marked Bound. The seller's purse is the captor's. Writes
    /// <c>slave_listed</c> with the capture as its cause when <paramref name="cause"/> is given.
    /// </summary>
    public static Bond List(BondWorld bw, BondBook book, EntityId person, EntityId seller, PurseKind sellerKind, int market, int day, long sec, ulong cause)
    {
        var law = bw.LawAt(market);
        var kind = law == SlaveLaw.WarCaptives ? BondKind.Thrall : BondKind.Chattel;
        long ask = bw.PriceOf(person, day);
        var b = Make(bw, book, person, seller, kind, BondOrigin.Captured, day, market, ask, 0, cause);
        b.Ask = ask; b.SellerKind = (byte)sellerKind; b.SellerOwner = seller.Value;
        var ps = new List<ChronParam> { ChronParam.Person("person", person), ChronParam.Mites("price", ask) };
        ps.AddRange(bw.PlaceParams(market));
        var e = Write(bw, sec, "slave_listed", "ch_slavery_listed", [person], Note("ch_note_slave_marched", cause), ps, ("law", LawName(law)));
        b.Entry = e.Id.Value;
        return b;
    }

    private static PurseId? SellerPurse(BondWorld bw, Bond b, int market)
    {
        if (bw.Ledger.TryFind((PurseKind)b.SellerKind, new EntityId(b.SellerOwner), out var p)) return p;
        return bw.W.TreasuryOf?.Invoke(market);
    }

    // ------------------------------------------------------------------ buying and selling

    /// <summary>
    /// Buys the person standing in a pen (4.2; Q &gt; Trade &gt; the pen's list): the asking price goes from the buyer's purse to the seller's (<see cref="TransferReason.SlaveSale"/>), the bond's owner becomes the buyer, the
    /// slave is bound and walks with (or into the household of) the buyer. The buyer is a person (the hero) or a household (an NPC). Standing: <c>slave_bought</c> carries the market's law, so the deed row
    /// that costs standing where the law forbids or limits bondage reads it; in a chattel town it costs nothing.
    /// </summary>
    public static SlaveResult Buy(BondWorld bw, BondBook book, EntityId buyer, EntityId person, int day, long sec)
    {
        if (book.Of(person) is not { InPen: true } b) return SlaveResult.No("sl_not_for_sale");
        var law = bw.LawAt(b.Place);
        if (!HasMarket(law)) return SlaveResult.No("sl_no_market");
        if (bw.PurseOf(buyer) is not { } from) return SlaveResult.No("sl_cannot_afford");
        long price = b.Ask;
        if (bw.Ledger.Balance(from).Mites < price) return SlaveResult.No("sl_cannot_afford");
        if (SellerPurse(bw, b, b.Place) is { } to && to != from && !bw.Ledger.Transfer(from, to, new Coin(price), TransferReason.SlaveSale)) return SlaveResult.No("sl_cannot_afford");
        int market = b.Place;
        var doer = bw.OwnerPerson(buyer);
        b.Owner = buyer.Value; b.Ask = 0; b.SellerKind = 0; b.SellerOwner = 0; b.PriceMites = price; b.Origin = BondOrigin.Sold; b.Since = day; b.Chain = Restraint.Bound;
        b.Work = buyer.Kind == IdKind.Person && buyer == bw.W.HeroId ? BondWork.Follow : BondWork.House; b.Treatment = Treatment.Fair; b.Morale = bw.D.MoraleStart; b.HungryDays = 0;
        if (person != bw.W.HeroId && bw.HouseholdOf(buyer) is var hh and >= 0) bw.Move(person, hh);
        if (person == bw.W.HeroId) bw.W.Marks()?.SetEnslaved(person, true);
        var ps = new List<ChronParam> { ChronParam.Person("person", person), ChronParam.Mites("price", price) };
        if (doer != Nobody) ps.Insert(0, ChronParam.Person("doer", doer));
        ps.AddRange(bw.PlaceParams(market));
        var subj = doer != Nobody ? new List<EntityId> { doer, person } : [person];
        var e = Write(bw, sec, "slave_bought", "ch_slavery_bought", subj, Note("ch_note_slave_market", b.Entry), ps, ("law", LawName(law)), ("price", N(price)));
        b.Entry = e.Id.Value;
        if (b.SeparatedDay < 0) b.SeparatedDay = day;
        return new SlaveResult(true, "sl_bought", price, e.Id.Value);
    }

    /// <summary>The household of a settlement (other than <paramref name="not"/>, the seller) that would buy a lot at a price today: coin of at least the multiple of the price and a free bed; richest first, household id on a tie (the draw 0x631 keyed on the market and the day breaks equal purses).</summary>
    public static EntityId? Buyer(BondWorld bw, int settlement, long price, int day, uint lot, EntityId? not = null)
    {
        var hht = bw.W.Households; if (hht == null) return null;
        var cand = new List<(long Coin, int Hh)>();
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] <= 0 || hht.Settlement[h] != settlement || hht.Beds[h] - hht.MemberCount[h] < 1 || not is { } seller && hht.IdOf(h) == seller) continue;      // P6-G1: a household does not buy its own man (a purse cannot pay itself)
            var pu = bw.PurseOf(hht.IdOf(h)); if (pu is not { } p) continue;
            long coin = bw.Ledger.Balance(p).Mites;
            if (coin >= (long)bw.D.BuyerCoinX * price) cand.Add((coin, h));
        }
        if (cand.Count == 0) return null;
        long best = cand.Max(c => c.Coin);
        var top = cand.Where(c => c.Coin == best).OrderBy(c => c.Hh).ToList();
        int pick = top.Count == 1 ? 0 : DetRng.Range(bw.Seed, day, bw.W.PlaceId(settlement).Value, RngStreams.SlaveMarketTies, lot, 0, top.Count);
        return hht.IdOf(top[pick].Hh);
    }

    /// <summary>
    /// Sells a bond the owner holds to a buyer of the market town at once (4.2: Q &gt; Sell bond): only where the law has a market; the best buyer pays <see cref="SlaveryRulesDef.SellPm"/> of the table's price from his purse to the
    /// owner's, the slave goes into his household. A person of the market town's own culture sold there costs the seller standing (the <c>slave_sold</c> deed, <c>own</c>); a seller in a kinder realm than the market's is not asked.
    /// </summary>
    public static SlaveResult Sell(BondWorld bw, BondBook book, EntityId owner, EntityId person, int market, int day, long sec)
    {
        if (book.Of(person) is not { InPen: false } b || b.Owner != owner.Value) return SlaveResult.No("sl_not_yours");
        var law = bw.LawAt(market);
        if (!HasMarket(law)) return SlaveResult.No("sl_no_market");
        long price = bw.PriceOf(person, day) * bw.D.SellPm / 1000;
        var buyer = Buyer(bw, market, price, day, 7, owner);
        if (buyer == null || bw.PurseOf(buyer.Value) is not { } from || bw.PurseOf(owner) is not { } to) return SlaveResult.No("sl_no_buyer");
        if (!bw.Ledger.Transfer(from, to, new Coin(price), TransferReason.SlaveSale)) return SlaveResult.No("sl_no_buyer");
        int row = bw.RowOf(person); bool own = row >= 0 && bw.W.People.Culture[row] == bw.CultureAt(market);
        var doer = bw.OwnerPerson(owner);
        var kin = row >= 0 && (bw.People.Spouse[row] >= 0 || bw.People.Father[row] >= 0 || bw.People.Mother[row] >= 0);
        b.Owner = buyer.Value.Value; b.PriceMites = price; b.Since = day; b.Origin = BondOrigin.Sold; b.Chain = Restraint.Bound; b.Place = market; b.Morale = bw.D.MoraleStart;
        if (kin) b.SeparatedDay = day;
        if (person != bw.W.HeroId && bw.HouseholdOf(buyer.Value) is var hh and >= 0) bw.Move(person, hh);
        var ps = new List<ChronParam> { ChronParam.Person("person", person), ChronParam.Mites("price", price) };
        if (doer != Nobody) ps.Insert(0, ChronParam.Person("doer", doer));
        ps.AddRange(bw.PlaceParams(market));
        var e = Write(bw, sec, "slave_sold", "ch_slavery_sold", doer != Nobody ? [doer, person] : [person], Note("ch_note_slave_market", b.Entry), ps, ("law", LawName(law)), ("own", own ? "1" : "0"), ("price", N(price)));
        b.Entry = e.Id.Value;
        return new SlaveResult(true, "sl_sold", price, e.Id.Value);
    }

    // ------------------------------------------------------------------ debt and sentence

    /// <summary>
    /// Binds a debtor to his creditor (4.1 rule 3): where the culture's law is <see cref="SlaveLaw.Debt"/> (or any law that allows bondage), for <c>debt / day wage</c> days, at least one. He works the creditor's fields,
    /// unchained, and goes back to his own household when the term ends. The debt itself is owed to the creditor and is worked off, so no coin moves here. Fails where the law is <see cref="SlaveLaw.None"/>.
    /// </summary>
    public static SlaveResult BindDebtor(BondWorld bw, BondBook book, EntityId debtor, EntityId creditor, long debtMites, int day, long sec)
    {
        int place = bw.W.HomeOf(debtor);
        var law = bw.LawAt(place);
        if (law == SlaveLaw.None) return SlaveResult.No("sl_law_forbids");
        if (book.Has(debtor)) return SlaveResult.No("sl_already_bonded");
        int days = (int)Math.Max(1, debtMites / Math.Max(1, bw.D.DebtDayWageMites));
        var kind = law == SlaveLaw.Debt ? BondKind.Debt : law == SlaveLaw.WarCaptives ? BondKind.Thrall : BondKind.Chattel;
        var ps = new List<ChronParam> { ChronParam.Person("person", debtor), ChronParam.Days("days", days), ChronParam.Mites("amount", debtMites) };
        ps.AddRange(bw.PlaceParams(place));
        var e = Write(bw, sec, "slave_bound", "ch_slavery_bound", [debtor], Note("ch_note_slave_debt"), ps, ("law", LawName(law)), ("days", N(days)));
        var b = Make(bw, book, debtor, creditor, kind == BondKind.Debt ? BondKind.Debt : kind, BondOrigin.Debt, day, place, debtMites, day + days, e.Id.Value);
        if (bw.HouseholdOf(creditor) is var hh and >= 0 && debtor != bw.W.HeroId) bw.Move(debtor, hh);
        return new SlaveResult(true, "sl_bound", days, e.Id.Value);
    }

    /// <summary>A bond_labour sentence (4.1 rule 3): a convict is bound to the fief's works for his term. The prisoner stays in the gaol's hands (the court's own record); the bond is the fief's claim on his labour and ends with the term.</summary>
    public static Bond? BindConvict(BondWorld bw, BondBook book, EntityId convict, int fief, int days, int settlement, int day, long sec, ulong crimeEntry)
    {
        if (book.Has(convict)) return null;
        var owner = fief >= 0 && fief < bw.W.FiefIds.Length ? bw.W.FiefIds[fief] : EntityId.None;
        if (owner == EntityId.None) return null;
        var ps = new List<ChronParam> { ChronParam.Person("person", convict), ChronParam.Days("days", days), ChronParam.Mites("amount", 0) };
        ps.AddRange(bw.PlaceParams(settlement));
        var e = Write(bw, sec, "slave_bound", "ch_slavery_bound", [convict], crimeEntry != 0 ? new Cause(CauseKind.Justice, new EntityId(crimeEntry), NoteKey: "ch_note_slave_sentence") : Note("ch_note_slave_sentence"), ps, ("law", LawName(bw.LawAt(settlement))), ("days", N(days)));
        var b = Make(bw, book, convict, owner, BondKind.Convict, BondOrigin.Sentenced, day, settlement, 0, day + Math.Max(1, days), e.Id.Value);
        return b;
    }

    // ------------------------------------------------------------------ freeing

    /// <summary>Why a bond ended: the owner's own act (manumission), his will, the end of a term, a purchase of his own freedom, the law (a seen slaving, a realm that does not return slaves), a revolt, a rescue.</summary>
    public enum Ending : byte { Manumission, Will, TermEnded, SelfPurchase, Law, Voided, Revolt, Rescued, Trust }

    /// <summary>
    /// Ends a bond (4.2 Free). The person is a free member of the owner's household when the owner's opinion of him is not below zero (a paid servant), else he leaves for the household he came from. Writes <c>slave_freed</c>
    /// (the deed rows give +30 standing with the settlement and +60 with the religion of the doer who freed him; freeing is lawful anywhere) or the term's own entry. Returns the entry.
    /// </summary>
    public static SlaveResult Free(BondWorld bw, BondBook book, EntityId person, Ending how, int day, long sec, ulong causeEntry = 0, CrimeBook? crimes = null)
    {
        if (book.Of(person) is not { } b) return SlaveResult.No("sl_not_bonded");
        var owner = new EntityId(b.Owner);
        var doer = how is Ending.Manumission or Ending.Will or Ending.Trust ? bw.OwnerPerson(owner) : Nobody;
        bool stays = how is Ending.Manumission or Ending.Will or Ending.Trust or Ending.TermEnded && owner.Kind != IdKind.Other && bw.W.OpinionPm(bw.OwnerPerson(owner), person) >= 0 && bw.HouseholdOf(owner) >= 0;
        int place = b.Place >= 0 ? b.Place : bw.W.HomeOf(person);
        if (b.BountyJob != 0 && bw.W.Jobs is { } jobs && bw.W.JobEnv is { } env) jobs.Cancel(env, new EntityId(b.BountyJob), sec);
        if (b.RunawayCrime != 0 && crimes?.Get(b.RunawayCrime) is { } rc && rc.State is CrimeState.Reported or CrimeState.Fled or CrimeState.Seen) rc.State = how == Ending.Voided ? CrimeState.Pardoned : CrimeState.Served;       // S5: the bond is gone, so is the runaway charge
        book.Bonds.Remove(person.Value);
        book.FreedTotal++;
        if (person == bw.W.HeroId) bw.W.Marks()?.SetEnslaved(person, false);
        if (!stays && person != bw.W.HeroId && b.HomeHousehold >= 0 && bw.HouseholdOf(person) != b.HomeHousehold && bw.W.Households is { } hht && b.HomeHousehold < hht.Count && hht.MemberCount[b.HomeHousehold] > 0) bw.Move(person, b.HomeHousehold);
        string type = how switch { Ending.TermEnded => "slave_term_ended", Ending.SelfPurchase => "slave_self_bought", Ending.Revolt => "slave_freed", _ => "slave_freed" };
        string key = how switch { Ending.TermEnded => "ch_slavery_term_ended", Ending.SelfPurchase => "ch_slavery_self_bought", _ => "ch_slavery_freed" };
        string note = how switch
        {
            Ending.Manumission => "ch_note_slave_manumitted", Ending.Will => "ch_note_slave_will", Ending.TermEnded => "ch_note_slave_term", Ending.SelfPurchase => "ch_note_slave_peculium",
            Ending.Law => "ch_note_slave_law", Ending.Voided => "ch_note_slave_voided", Ending.Revolt => "ch_note_slave_revolt", Ending.Trust => "ch_note_slave_trust", _ => "ch_note_slave_rescued",
        };
        var ps = new List<ChronParam> { ChronParam.Person("person", person) };
        if (doer != Nobody) ps.Insert(0, ChronParam.Person("doer", doer));
        if (how == Ending.SelfPurchase) ps.Add(ChronParam.Mites("price", b.PriceMites));
        ps.AddRange(bw.PlaceParams(place));
        var subj = doer != Nobody ? new List<EntityId> { doer, person } : [person];
        var cause = new Cause(CauseKind.Slavery, causeEntry != 0 ? new EntityId(causeEntry) : b.Entry != 0 ? new EntityId(b.Entry) : null, NoteKey: note);
        var e = Write(bw, sec, type, key, subj, cause, ps, ("law", LawName(bw.LawAt(place))), ("stays", stays ? "1" : "0"), ("how", how.ToString()));
        return new SlaveResult(true, "sl_freed", stays ? 1 : 0, e.Id.Value);
    }

    /// <summary>Manumission by the owner (Q &gt; Free): lawful in any realm (4.3).</summary>
    public static SlaveResult Manumit(BondWorld bw, BondBook book, EntityId owner, EntityId person, int day, long sec, CrimeBook? crimes = null)
    {
        if (book.Of(person) is not { InPen: false } b || b.Owner != owner.Value) return SlaveResult.No("sl_not_yours");
        return Free(bw, book, person, Ending.Manumission, day, sec, 0, crimes);
    }

    /// <summary>
    /// A slave who has saved his last price buys himself free (4.2): the owner must accept in a debt law and may refuse in a chattel law (a Harsh owner refuses). The coin goes from the slave's own purse to the
    /// owner's (<see cref="TransferReason.SlaveSale"/>); conserved.
    /// </summary>
    public static SlaveResult SelfPurchase(BondWorld bw, BondBook book, EntityId person, int day, long sec, CrimeBook? crimes = null)
    {
        if (book.Of(person) is not { InPen: false } b || b.PriceMites <= 0) return SlaveResult.No("sl_not_bonded");
        var owner = new EntityId(b.Owner);
        if (bw.PurseOf(owner) is not { } to) return SlaveResult.No("sl_owner_refuses");
        var law = bw.LawAt(b.Place >= 0 ? b.Place : bw.W.HomeOf(person));
        if (law is SlaveLaw.Chattel or SlaveLaw.WarCaptives && b.Treatment == Treatment.Harsh) return SlaveResult.No("sl_owner_refuses");
        var from = bw.PeculiumPurse(person);
        if (bw.Ledger.Balance(from).Mites < b.PriceMites) return SlaveResult.No("sl_too_poor");
        if (!bw.Ledger.Transfer(from, to, new Coin(b.PriceMites), TransferReason.SlaveSale)) return SlaveResult.No("sl_too_poor");
        long paid = b.PriceMites;
        var r = Free(bw, book, person, Ending.SelfPurchase, day, sec, 0, crimes);
        return r with { Mites = paid };
    }

    /// <summary>
    /// A friend or kin buys a slave free (4.4: be bought free by a companion): the payer gives his last price to the owner (<see cref="TransferReason.SlaveSale"/>) and the person is free (a rescue). The owner may refuse
    /// in a chattel law only when he is no one's debtor-keeper: here he accepts at the price he paid, so a slave can never be held for ransom beyond it.
    /// </summary>
    public static SlaveResult BuyFree(BondWorld bw, BondBook book, EntityId payer, EntityId person, int day, long sec, CrimeBook? crimes = null)
    {
        if (book.Of(person) is not { InPen: false } b || b.Owner == payer.Value) return SlaveResult.No("sl_not_bonded");
        if (bw.PurseOf(payer) is not { } from || bw.PurseOf(new EntityId(b.Owner)) is not { } to) return SlaveResult.No("sl_cannot_afford");
        long price = Math.Max(b.PriceMites, 1);
        if (bw.Ledger.Balance(from).Mites < price || !bw.Ledger.Transfer(from, to, new Coin(price), TransferReason.SlaveSale)) return SlaveResult.No("sl_cannot_afford");
        var r = Free(bw, book, person, Ending.Rescued, day, sec, 0, crimes);
        return r with { Mites = price };
    }

    /// <summary>
    /// The enslaved person picks his chains (4.4): only at night, when the overseer sleeps; the chance is the Phase 4 lock rule (stream 0x455, keyed on the second of the attempt over 20), a failed pick may be noticed
    /// (a beating, a tighter chain). On success he is out of the chains and on the run: the owner posts the runaway's bounty (<see cref="BondDaily.Flee"/>).
    /// </summary>
    public static SlaveResult Escape(BondWorld bw, BondBook book, CrimeBook crimes, AftermathRules rules, EntityId person, long sec, int stealth, int lockpicking)
    {
        if (book.Of(person) is not { InPen: false, OnRun: false } b) return SlaveResult.No("sl_not_bonded");
        if (b.Chain == Restraint.None) return SlaveResult.No("sl_not_chained");
        if (!EscapeTable.Dark(sec)) return SlaveResult.No("sl_escape_day");
        int pm = EscapeTable.LockPm(rules, lockpicking, CaptorKind.Slavers, false); long key = sec / 20; int day = (int)(sec / 86_400);
        if (EscapeTable.Roll(bw.Seed, key, person, RngStreams.LockPick, 0) < pm)
        {
            b.Chain = Restraint.None;
            BondDaily.Flee(bw, book, crimes, b, day, sec);
            return new SlaveResult(true, "sl_escaped");
        }
        if (EscapeTable.Roll(bw.Seed, key, person, RngStreams.LockPick, 1) < EscapeTable.NoticePm(rules, stealth, true))
        {
            b.Chain = Restraint.Chained; b.FlogDay = day; b.Morale = Math.Max(0, b.Morale - 10);
            return SlaveResult.No("sl_escape_caught");
        }
        return SlaveResult.No("sl_escape_failed");
    }

    // ------------------------------------------------------------------ the owner's settings

    public static SlaveResult SetTreatment(BondBook book, EntityId owner, EntityId person, Treatment t)
    {
        if (book.Of(person) is not { InPen: false } b || b.Owner != owner.Value) return SlaveResult.No("sl_not_yours");
        b.Treatment = t; return new SlaveResult(true, "sl_treatment_set");
    }

    public static SlaveResult SetChain(BondBook book, EntityId owner, EntityId person, bool chained)
    {
        if (book.Of(person) is not { InPen: false } b || b.Owner != owner.Value) return SlaveResult.No("sl_not_yours");
        b.Chain = chained ? Restraint.Chained : Restraint.None; return new SlaveResult(true, chained ? "sl_chained" : "sl_unchained");
    }

    public static SlaveResult SetWork(BondBook book, EntityId owner, EntityId person, BondWork w)
    {
        if (book.Of(person) is not { InPen: false } b || b.Owner != owner.Value) return SlaveResult.No("sl_not_yours");
        b.Work = w; return new SlaveResult(true, "sl_work_set");
    }
}
