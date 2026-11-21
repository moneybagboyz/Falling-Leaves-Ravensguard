using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Production;

public sealed partial class OwnershipWorld
{
    // ------------------------------------------------------------------ founding (spec 4.1)

    /// <summary>The workshop stations of a structure: the craft stations in its recognised rooms (RoomDetector v2 4.4), as the business kinds name them (forge, anvil, saw_pit...), ascending.</summary>
    public SortedDictionary<string, int> StationsOf(int structure)
    {
        var outp = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var w = Rooms?.Invoke(); if (w == null || structure <= 0) return outp;
        foreach (var room in w.Rooms)
        {
            if (room.Primary == null || room.Cells.Count == 0) continue;
            var (x, y) = room.Cells[0];
            if (w.StructureAt(x, y, room.Z) != structure) continue;
            foreach (var (st, n) in room.Stations)
            {
                string kind = Ravensguard.Sim.Crafting.RecipeUnifier.KindOf(Content, st);
                if (Array.IndexOf(RecipeBook.StationKinds, kind) < 0) continue;
                outp[kind] = outp.GetValueOrDefault(kind) + n;
            }
        }
        return outp;
    }

    /// <summary>The business kinds a set of stations offers (spec 4.1): a workshop kind (not a merchant, not founded on a resource) with a recipe whose station stands there.</summary>
    public List<BusinessKind> KindsFor(IReadOnlyDictionary<string, int> stations)
        => [.. Prod.Content.Kinds.Where(k => !k.Trader && k.Extractions.Length == 0 && k.Id is not ("manor" or "mine") && k.Recipes.Any(r => stations.ContainsKey(r.Station))).OrderBy(k => k.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Opens a business in a building the hero may use (spec 4.1, BS-1): the kind must be one its recognised workshop rooms offer; its stations are the room's stations the kind uses (more forges, more
    /// batches; never more output per input). The capital moves from the hero's purse into the new cash box (<c>Gift</c>; <c>Capital</c> records it, as Phase 2).
    /// </summary>
    public OwnResult Found(string kindId, int settlement, int lot, int structure, long capital, int day, Func<int, int, bool>? mayUse = null, IReadOnlyDictionary<string, int>? stationsOverride = null)
    {
        var kind = Prod.Content.Kinds.FirstOrDefault(k => k.Id == kindId);
        if (kind == null || kind.Trader) return OwnResult.No("bz_no_such_kind");
        if ((uint)settlement >= (uint)Pop.Region.Settlements.Count) return OwnResult.No("bz_no_place");
        int row = HeroRow(); if (row < 0) return OwnResult.No("bz_no_hero");
        // the building must be one the hero may use (land 4.1): his deed, a lease, a claim
        if (mayUse != null && Rooms?.Invoke() is { } rw && rw.StructureCells(structure).FirstOrDefault() is long key && key != 0)
        {
            var (cx, cy, _) = Ravensguard.Sim.Building.RoomWorld.Unpack(key);
            if (!mayUse(cx, cy)) return OwnResult.No("bz_not_your_building");
        }
        IReadOnlyDictionary<string, int> stations = stationsOverride ?? StationsOf(structure);
        if (!KindsFor(stations).Contains(kind)) return OwnResult.No("bz_no_stations", kindId);
        if (capital < 0 || HeroCoin < capital) return new OwnResult(false, "bz_cannot_afford", [M(capital)], capital, CannotAfford: true);
        var b = Prod.NewBusiness(kind, settlement, lot, row);
        b.OwnerWorks = false;                                                                       // the hero's hands are his own (crafting, the desk's work), never the abstract tier's
        foreach (var (st, n) in stations)
            if (kind.Stations.Any(s => s.Kind == st)) for (int i = 0; i < n; i++) b.Stations.Add(new Station { Kind = st });
        if (capital > 0) Econ.Ledger.Transfer(HeroPurse, b.Purse, new Coin(capital), TransferReason.Gift);
        b.Capital = capital; b.CashStart = capital;
        var h = Hold(b); h.FoundedDay = day; h.HandsOn = true; if (structure > 0) h.Premises.Add(structure);
        Event(day, "business_founded", [Hero, b.Id], [Why.Of(CauseKind.Trade, "ch_why_hero_opened", ChronParam.Person("who", Hero))], ("settlement", Pop.Region.Settlements[settlement].Name), ("kind", kind.Id), ("capital", capital.ToString()), ("stations", b.Stations.Count.ToString()));
        return new OwnResult(true, "bz_founded", [Name(b), b.Stations.Count, M(capital)], capital, b.Id);
    }

    /// <summary>Puts coin into a business's cash box (<c>Gift</c>, counted as capital) or takes it out (<c>Dividend</c>, to the hero's purse).</summary>
    public OwnResult Cash(EntityId biz, long mites)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        if (mites > 0)
        {
            if (HeroCoin < mites || !Econ.Ledger.Transfer(HeroPurse, b.Purse, new Coin(mites), TransferReason.Gift)) return new OwnResult(false, "bz_cannot_afford", [M(mites)], mites, CannotAfford: true);
            b.Capital += mites; b.CashStart += mites;
            return new OwnResult(true, "bz_cash_in", [M(mites), Name(b)], mites);
        }
        long take = Math.Min(-mites, Econ.Ledger.Balance(b.Purse).Mites);
        if (take <= 0 || !Econ.Ledger.Transfer(b.Purse, HeroPurse, new Coin(take), TransferReason.Dividend)) return OwnResult.No("bz_box_empty");
        b.DividendsSeason += take; b.Dividends += take;
        return new OwnResult(true, "bz_cash_out", [M(take), Name(b)], take);
    }

    // ------------------------------------------------------------------ buying, shares (spec 4.1)

    /// <summary>Whether an NPC owner would sell (spec 4.1): the owner is dead, old over 60 with no heir in the trade, or the business lost money two seasons running; a closed business is for sale.</summary>
    public bool ForSale(Business b, int day)
    {
        if (IsHeroOwner(b) || KindOf(b).Id is "manor") return false;
        if (b.State == BusinessState.Closed) return true;
        var pt = Pop.People;
        if (b.Owner < 0 || pt.Status[b.Owner] != PersonStatus.Alive) return true;
        if (b.LossSeasons >= Rules.SellLossSeasons) return true;
        if (pt.AgeYears(b.Owner, day) > Rules.SellOwnerAge)
        {
            var occ = KindOf(b).OwnerOccupation; int hh = pt.Household[b.Owner];
            bool heir = hh >= 0 && Pop.Households.Members(hh).Any(m => m != b.Owner && pt.Status[m] == PersonStatus.Alive && pt.AgeYears(m, day) is >= 16 and < 60 && pt.Occupation[m] == occ);
            if (!heir) return true;
        }
        return false;
    }

    public long PriceOf(Business b) => b.State == BusinessState.Closed ? ValueOf(b) * Rules.ClosedSalePm / 1000 : ValueOf(b);

    /// <summary>Buys a whole business that is for sale at its value (a closed one at 700 pm), paid to the seller household (<c>Trade</c>); the deed of its premises goes with it (land 4.2).</summary>
    public OwnResult Buy(EntityId biz, long limit, int day)
    {
        if (BusinessOf(biz) is not { } b) return OwnResult.No("bz_no_such_business");
        if (!ForSale(b, day)) return OwnResult.No("bz_not_for_sale", Name(b));
        long price = PriceOf(b);
        if (limit > 0 && price > limit) return new OwnResult(false, "bz_price_moved", [M(price)], price, PriceMoved: true);
        if (HeroCoin < price) return new OwnResult(false, "bz_cannot_afford", [M(price)], price, CannotAfford: true);
        int sellerHh = b.OwnerHousehold;
        var to = sellerHh >= 0 && sellerHh < Econ.HouseholdCount && Pop.Households.MemberCount[sellerHh] > 0 ? Econ.HouseholdPurse[sellerHh] : Ravensguard.Sim.Land.LandRegistry.LordOf(Pop, b.Settlement) is var (lid, lk) ? Econ.Ledger.Open(lk, lid) : HeroPurse;
        if (price > 0 && !Econ.Ledger.Transfer(HeroPurse, to, new Coin(price), TransferReason.Trade)) return new OwnResult(false, "bz_cannot_afford", [M(price)], price, CannotAfford: true);
        int row = HeroRow(); int old = b.Owner;
        if (b.State == BusinessState.Closed) { b.State = BusinessState.Open; b.LossSeasons = 0; b.IdleDays = 0; b.WagesSeason = b.InputsSeason = b.DividendsSeason = 0; b.CashStart = Econ.Ledger.Balance(b.Purse).Mites; }
        b.Owner = row; b.OwnerHousehold = HeroHousehold; b.OwnerWorks = false;
        foreach (int s in b.Staff) if (Pop.People.Employer[s] == old || Pop.People.Employer[s] < 0) Pop.People.Employer[s] = row;
        if (old >= 0 && old != row && Pop.People.Status[old] == PersonStatus.Alive && Pop.People.Occupation[old] == KindOf(b).OwnerOccupation && Pop.People.WorkSettlement[old] == b.Settlement) { Pop.People.Occupation[old] = Occupation.None; Pop.People.WorkSettlement[old] = -1; Pop.People.WorkLot[old] = -1; }
        var h = Hold(b); h.HandsOn = true; h.FoundedDay = day;
        h.Shares.RemoveAll(s => s.Holder == Hero);
        if (b.Lot >= 0) Land?.ConveyLot(b.Settlement, b.Lot, Hero, PurseKind.Person, day, Ravensguard.Sim.Land.DeedCause.Bought);
        Event(day, "business_bought", [Hero, b.Id], [Why.Of(CauseKind.Trade, "ch_why_bought_value", ChronParam.Person("who", Hero))], ("settlement", Pop.Region.Settlements[b.Settlement].Name), ("price", price.ToString()));
        return new OwnResult(true, "bz_bought", [Name(b), M(price)], price, b.Id);
    }

    /// <summary>
    /// Buys a share of an NPC's business (spec 4.1, BS-3): at most 490 pm in all (the owner keeps control above 500, BS-4), at <c>value x share</c> to the owner's household (<c>ShareSale</c>).
    /// </summary>
    public OwnResult BuyShare(EntityId biz, int pm, long limit, int day)
    {
        if (BusinessOf(biz) is not { } b || b.State != BusinessState.Open) return OwnResult.No("bz_no_such_business");
        if (IsHeroOwner(b)) return OwnResult.No("bz_already_yours");
        if (pm <= 0) return OwnResult.No("bz_bad_share");
        var h = H(b);
        int held = h?.Shares.Sum(s => s.Pm) ?? 0;
        if (1000 - held - pm <= 500 || held + pm > Rules.ShareCapPm) return OwnResult.No("bz_share_control", Rules.ShareCapPm / 10);
        int hh = b.OwnerHousehold; if (hh < 0 || hh >= Econ.HouseholdCount) return OwnResult.No("bz_no_owner");
        long price = Valuation.SharePrice(ValueOf(b), pm);
        if (limit > 0 && price > limit) return new OwnResult(false, "bz_price_moved", [M(price)], price, PriceMoved: true);
        if (HeroCoin < price || !Econ.Ledger.Transfer(HeroPurse, Econ.HouseholdPurse[hh], new Coin(price), TransferReason.ShareSale)) return new OwnResult(false, "bz_cannot_afford", [M(price)], price, CannotAfford: true);
        h ??= Hold(b);
        h.HandsOn = false;
        int i = h.Shares.FindIndex(s => s.Holder == Hero);
        if (i >= 0) h.Shares[i] = h.Shares[i] with { Pm = h.Shares[i].Pm + pm }; else { h.Shares.Add(new ShareRow(Hero, pm)); h.Shares.Sort((x, y) => x.Holder.CompareTo(y.Holder)); }
        Event(day, "share_bought", [Hero, b.Id], [Why.Of(CauseKind.Trade, "ch_why_share_bought", ChronParam.Person("who", Hero))], ("pm", pm.ToString()), ("price", price.ToString()));
        return new OwnResult(true, "bz_share_bought", [pm / 10, Name(b), M(price)], price, b.Id);
    }

    /// <summary>Sells the hero's share back (spec 4.1): the owner buys it at value when his household holds the coin; else it stays listed (refused here).</summary>
    public OwnResult SellShare(EntityId biz, int day)
    {
        if (BusinessOf(biz) is not { } b || H(b) is not { } h) return OwnResult.No("bz_no_such_business");
        int i = h.Shares.FindIndex(s => s.Holder == Hero); if (i < 0) return OwnResult.No("bz_no_share");
        int pm = h.Shares[i].Pm; long price = Valuation.SharePrice(ValueOf(b), pm);
        int hh = b.OwnerHousehold;
        if (hh < 0 || hh >= Econ.HouseholdCount || Econ.Ledger.Balance(Econ.HouseholdPurse[hh]).Mites < price) return OwnResult.No("bz_share_no_buyer", M(price));
        if (!Econ.Ledger.Transfer(Econ.HouseholdPurse[hh], HeroPurse, new Coin(price), TransferReason.ShareSale)) return OwnResult.No("bz_share_no_buyer", M(price));
        h.Shares.RemoveAt(i);
        if (h.Shares.Count == 0 && !IsHeroOwner(b)) { Book.Holdings.Remove(b.Id); _steeredCount = -1; }
        Event(day, "share_sold", [Hero, b.Id], [Why.Of(CauseKind.Trade, "ch_why_share_sold", ChronParam.Person("who", Hero))], ("pm", pm.ToString()), ("price", price.ToString()));
        return new OwnResult(true, "bz_share_sold", [pm / 10, Name(b), M(price)], price, b.Id);
    }

    // ------------------------------------------------------------------ staff (spec 4.2, 4.3)

    /// <summary>A person's daily wage now: his agreed wage at a hero's business, else his slot's class wage (0 when he has no post).</summary>
    public int CurrentWage(int person, out Business? employer)
    {
        employer = null;
        foreach (var b in Prod.Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            int i = b.Staff.IndexOf(person); if (i < 0) continue;
            employer = b;
            if (H(b) is { } h && h.Wages.TryGetValue(person, out int w)) return w;
            var occ = Pop.People.Occupation[person];
            foreach (var (o, _, wc) in KindOf(b).Slots) if (o == occ) return RecipeBook.WagePerDay(wc);
            return RecipeBook.WagePerDay(KindOf(b).Slots.Length > 0 ? KindOf(b).Slots[0].Wage : WageClass.Unskilled);
        }
        return 0;
    }

    /// <summary>
    /// Hires a person for a business at a day wage (spec 4.2, BS-6): accepted by the labour market's rule, a better wage than now or idle. A person on another business's staff quits that post
    /// at the barrier (his old employer's vacancy is then posted by the Phase 2 counters); an owner, a lord, a priest or the hero never takes a post.
    /// </summary>
    public OwnResult Hire(EntityId biz, int person, int wagePerDay, int day)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b) || b.State != BusinessState.Open) return OwnResult.No("bz_not_yours");
        var pt = Pop.People;
        if ((uint)person >= (uint)pt.Count || pt.Status[person] != PersonStatus.Alive || pt.AgeYears(person, day) < LabourMarket.ApprenticeMinAge) return OwnResult.No("bz_cannot_hire");
        if (pt.Occupation[person] is Occupation.Lord or Occupation.Priest or Occupation.Mayor or Occupation.GuildMaster || Prod.Businesses.Any(x => x.State == BusinessState.Open && x.Owner == person)) return OwnResult.No("bz_will_not_work", pt.FullName(person));
        if (b.Staff.Contains(person)) return OwnResult.No("bz_already_staff", pt.FullName(person));
        if (wagePerDay <= 0) return OwnResult.No("bz_bad_wage");
        int now = CurrentWage(person, out var old);
        bool idle = old == null && pt.Occupation[person] is Occupation.None or Occupation.Labourer or Occupation.Servant;
        if (!idle && wagePerDay <= now) return OwnResult.No("bz_wage_too_low", pt.FullName(person), M(now));
        var kind = KindOf(b);
        var occ = kind.Slots.Select(s => s.Occupation).Contains(pt.Occupation[person]) ? pt.Occupation[person] : kind.Slots.Length > 0 ? kind.Slots[0].Occupation : Occupation.Labourer;
        if (old != null) Prod.DropStaff(old, old.Staff.IndexOf(person), alive: true, keepJob: true);
        Prod.Employ(b, person, occ);
        pt.Employer[person] = b.Owner;
        Hold(b).Wages[person] = wagePerDay;
        Event(day, "hired", [Hero, pt.IdOf(person), b.Id], [Why.Of(CauseKind.Trade, old != null ? "ch_why_hired_better" : "ch_why_hired_work", ChronParam.Person("who", pt.IdOf(person)))], ("wage", wagePerDay.ToString()), ("occupation", occ.ToString()));
        return new OwnResult(true, old != null ? "bz_hired_away" : "bz_hired", [pt.FullName(person), Name(b), M(wagePerDay)], wagePerDay, b.Id);
    }

    /// <summary>Lets a staff member go (spec 4.2): his wages owed are paid first if the box can; he is idle again.</summary>
    public OwnResult Fire(EntityId biz, int person)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        int i = b.Staff.IndexOf(person); if (i < 0) return OwnResult.No("bz_not_staff");
        Prod.PayWages(b, GameTime.FromGameDays(Today));
        i = b.Staff.IndexOf(person); if (i < 0) return OwnResult.Yes("bz_fired", Pop.People.FullName(person));
        Prod.DropStaff(b, i, alive: true);
        H(b)?.Wages.Remove(person);
        return OwnResult.Yes("bz_fired", Pop.People.FullName(person));
    }

    /// <summary>The properties a steward already runs (businesses of the hero's he stewards); P7-ES1 (economies-of-scale 5.2): a works counts as <c>ceil(crew / 20)</c> properties, at least one.</summary>
    public int StewardLoad(int person) => Book.Holdings.Values.Where(h => h.Steward == person).Sum(h => Prod.ById(h.Business) is { } b ? Math.Max(1, (b.Staff.Count + 19) / 20) : 1);

    /// <summary>
    /// Hires a steward for a business (spec 4.4): Trading 30 and Leadership 20 at least; 350 a day (skilled), from the cash box (else the hero's purse); he runs up to <c>3 + Leadership / 20</c>
    /// properties (at most 6). He is honest (Phase 5); his skill decides how old his prices are and how much he keeps back.
    /// </summary>
    public OwnResult HireSteward(EntityId biz, int person, int day)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        var pt = Pop.People;
        if ((uint)person >= (uint)pt.Count || pt.Status[person] != PersonStatus.Alive || pt.AgeYears(person, day) < LabourMarket.ManagementMinAge) return OwnResult.No("bz_cannot_hire");
        int tr = pt.SkillLevelOf(person, Skill.Trading), ld = pt.SkillLevelOf(person, Skill.Leadership);
        if (!StewardBrain.Qualifies(tr, ld, Rules)) return OwnResult.No("bz_steward_unfit", pt.FullName(person), tr, ld);
        if (StewardLoad(person) >= StewardBrain.Properties(ld, Rules)) return OwnResult.No("bz_steward_full", pt.FullName(person), StewardBrain.Properties(ld, Rules));
        if (Prod.Businesses.Any(x => x.State == BusinessState.Open && x.Owner == person)) return OwnResult.No("bz_will_not_work", pt.FullName(person));
        var h = Hold(b);
        if (StewardLoad(person) == 0)
        {
            int now = CurrentWage(person, out var old);
            if (old != null) { if (Rules.StewardWageMitesPerDay <= now) return OwnResult.No("bz_wage_too_low", pt.FullName(person), M(now)); Prod.DropStaff(old, old.Staff.IndexOf(person), alive: true, keepJob: true); }
            pt.Occupation[person] = Occupation.Steward; pt.Employer[person] = HeroRow(); pt.WorkSettlement[person] = b.Settlement; pt.WorkLot[person] = (short)Math.Max(-1, b.Lot);
        }
        h.Steward = person; h.StewardWage = Rules.StewardWageMitesPerDay; h.StewardShortDays = 0; h.HandsOn = false;
        Event(day, "steward_hired", [Hero, pt.IdOf(person), b.Id], [Why.Of(CauseKind.Trade, "ch_why_steward", ChronParam.Person("who", pt.IdOf(person)))], ("trading", tr.ToString()), ("leadership", ld.ToString()));
        return new OwnResult(true, "bz_steward_hired", [pt.FullName(person), Name(b), StewardBrain.LagDays(tr, Rules)], h.StewardWage, b.Id);
    }

    public OwnResult DismissSteward(EntityId biz)
    {
        if (BusinessOf(biz) is not { } b || H(b) is not { Steward: >= 0 } h) return OwnResult.No("bz_no_steward");
        int p = h.Steward; h.Steward = -1; h.HandsOn = true;
        PaySteward(b, h, final: true);
        if (StewardLoad(p) == 0 && Pop.People.Occupation[p] == Occupation.Steward) { Pop.People.Occupation[p] = Occupation.None; Pop.People.Employer[p] = -1; Pop.People.WorkSettlement[p] = -1; Pop.People.WorkLot[p] = -1; }
        return OwnResult.Yes("bz_steward_gone", Pop.People.FullName(p));
    }

    // ------------------------------------------------------------------ directives (spec 4.4)

    /// <summary>Sets the owner's standing orders (spec 4.4): "key=value" pairs (recipe, perday, keep_good, keep_qty, floor_good, floor_mites, budget, send_days, keep_mites, hire, close).</summary>
    public OwnResult Orders(EntityId biz, string spec)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        var o = Hold(b).Orders.Clone();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2); if (kv.Length != 2) return OwnResult.No("bz_bad_orders");
            string k = kv[0], v = kv[1];
            long N() => long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long x) ? x : long.MinValue;
            int G() => v.Length == 0 || v == "-" ? -1 : Econ.Goods.TryIdOf(v, out ushort g) ? g : int.MinValue;
            switch (k)
            {
                case "recipe": o.Recipe = v is "" or "-" ? -1 : Prod.Content.Recipes.TryGet(v, out var r) && KindOf(b).Recipes.Contains(r) ? r.Index : int.MinValue; break;
                case "perday": o.PerDay = (int)Math.Clamp(N(), 0, 100); break;
                case "keep_good": o.KeepGood = G(); break;
                case "keep_qty": o.KeepQty = Math.Max(0, N()); break;
                case "floor_good": o.FloorGood = G(); break;
                case "floor_mites": o.FloorMites = Math.Max(0, N()); break;
                case "budget": o.SeasonBudget = Math.Max(0, N()); break;
                case "send_days": o.SendEveryDays = (int)Math.Clamp(N(), 1, 60); break;
                case "keep_mites": o.KeepMites = Math.Max(0, N()); break;
                case "hire": o.HireUpTo = (int)Math.Clamp(N(), -1, 200); break;
                case "close": o.CloseAfterTwoLosses = v is "1" or "true"; break;
                default: return OwnResult.No("bz_bad_orders");
            }
            if (o.Recipe == int.MinValue || o.KeepGood == int.MinValue || o.FloorGood == int.MinValue) return OwnResult.No("bz_bad_orders");
        }
        Hold(b).Orders = o;
        return OwnResult.Yes("bz_orders_set", Name(b));
    }

    // ------------------------------------------------------------------ goods (spec 4.2: supplying inputs is moving goods into the business store)

    /// <summary>Moves goods into the business's store from another store (the hero's pack, a cart's load): no coin moves (BS-7).</summary>
    public OwnResult Supply(EntityId biz, Store from, ushort good, long qty)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        long q = Math.Min(qty, from.Qty(good));
        if (q <= 0) return OwnResult.No("bz_none_to_supply");
        if (!Store.Move(from, b.Store, good, q)) return OwnResult.No("bz_none_to_supply");
        return new OwnResult(true, "bz_supplied", [Econ.Goods.IsPiece(good) ? q.ToString() : Quotes.Weight(q), Econ.Goods.Def(good).Name.ToLowerInvariant(), Name(b)], 0, b.Id);
    }

    public OwnResult Take(EntityId biz, Store to, ushort good, long qty)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        long q = Math.Min(qty, b.Store.Qty(good));
        if (q <= 0 || !Store.Move(b.Store, to, good, q)) return OwnResult.No("bz_none_to_take");
        return new OwnResult(true, "bz_taken", [Econ.Goods.IsPiece(good) ? q.ToString() : Quotes.Weight(q), Econ.Goods.Def(good).Name.ToLowerInvariant()], 0, b.Id);
    }

    // ------------------------------------------------------------------ hands-on (spec 4.2, BS-5)

    /// <summary>
    /// The hero works a station of his business by his own hands from <paramref name="fromSec"/> to <paramref name="untilSec"/> (crafting-full 4.4's bridge for business batches): a batch of the
    /// recipe is started from the business's store if the station is idle, his time is credited at the one worker-hour rule and his skill, the finished batch goes into the business's store and
    /// counts in its season books. A second is never credited twice.
    /// </summary>
    public OwnResult Work(EntityId biz, int station, string recipeId, long fromSec, long untilSec)
    {
        if (BusinessOf(biz) is not { } b || !IsHeroOwner(b) || b.State != BusinessState.Open) return OwnResult.No("bz_not_yours");
        if ((uint)station >= (uint)b.Stations.Count) return OwnResult.No("bz_no_station");
        var h = Hold(b);
        long from = Math.Max(fromSec, h.HandsToSec); if (untilSec <= from) return OwnResult.No("bz_nothing_done");
        var st = b.Stations[station];
        Recipe? r = st.Busy ? Prod.Content.Recipes.Recipes[st.Recipe] : Prod.Content.Recipes.TryGet(recipeId, out var rr) && KindOf(b).Recipes.Contains(rr) && rr.Station == st.Kind ? rr : null;
        if (r == null) return OwnResult.No("bz_no_recipe");
        int skill = r.Skill == Skill.None ? 40 : HeroSkill?.Invoke(r.Skill) ?? 30;
        long batches0 = b.Batches;
        var res = Prod.CreditHands(b, station, r, skill, untilSec - from, new GameTime(untilSec));
        if (!res) return OwnResult.No("bz_no_inputs", r.Id);
        h.HandsToSec = untilSec;
        if (r.Skill != Skill.None) TrainHero?.Invoke(r.Skill, from, untilSec);
        return new OwnResult(true, b.Batches > batches0 ? "bz_batch_done" : "bz_worked", [r.Id, (untilSec - from) / 60, Name(b)], 0, b.Id);
    }
}
