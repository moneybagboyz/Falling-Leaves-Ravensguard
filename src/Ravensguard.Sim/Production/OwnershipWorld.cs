using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Prospecting;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Production;

/// <summary>The answer of an ownership action: done or not, a message key of en.csv and its parameters, the coin it moved, the thing it made.</summary>
public sealed record OwnResult(bool Ok, string Key, object[] Args, long Mites = 0, EntityId Id = default, bool CannotAfford = false, bool PriceMoved = false)
{
    public static OwnResult No(string key, params object[] args) => new(false, key, args);
    public static OwnResult Yes(string key, params object[] args) => new(true, key, args);
}

/// <summary>
/// The hero's businesses, shares, stewards and stalls in the home region (businesses-stewards-ledger, P5-F), and prospecting and extraction businesses (prospecting-and-extraction): the one place
/// that applies the owner's and steward's orders to the Phase 2 production system (through <see cref="IBusinessSteering"/>), moves coin only by ledger transfers and goods only by store moves.
/// The book (<see cref="OwnershipBook"/>, the "ownership" section) holds only what Phase 5 adds; the businesses themselves stay in production.
/// </summary>
public sealed partial class OwnershipWorld : IBusinessSteering
{
    public OwnershipBook Book { get; }
    public ProductionSystem Prod { get; }
    public MarketSystem? Markets => Prod.Markets;
    public EconomyState Econ => Prod.Econ;
    public Population Pop => Prod.Pop;
    public ContentDatabase Content { get; }
    public OwnershipRulesDef Rules { get; }
    public ProspectRulesDef PRules { get; }
    public ulong Seed { get; }
    public int RegionId { get; }
    public IdAllocator Ids { get; }
    public ProspectTruth Truth { get; }
    public EntityId Hero { get; init; } = EconomyState.HeroOwner;
    /// <summary>The hero's row in the person table (-1 none).</summary>
    public Func<int> HeroRow { get; set; } = () => -1;
    /// <summary>The hero's skill level (the crafting book: his record plus what use has trained), and training by use.</summary>
    public Func<Skill, int>? HeroSkill { get; set; }
    public Action<Skill, long, long>? TrainHero { get; set; }
    public Ravensguard.Sim.Land.LandWorld? Land { get; set; }
    /// <summary>The building rooms (RoomDetector v2) the business stations come from.</summary>
    public Func<Ravensguard.Sim.Building.RoomWorld?>? Rooms { get; set; }
    public Ravensguard.Sim.Travel.FairBook? Fairs { get; set; }
    public Ravensguard.Sim.Land.LandChronicle? Chron { get; set; }
    /// <summary>What the desk shows the hero after each hour (steward reports, alerts, prospecting results).</summary>
    public List<HeroText> Notices { get; } = [];

    private readonly HashSet<int> _steered = [];
    private int _steeredCount = -1;

    public OwnershipWorld(OwnershipBook book, ProductionSystem prod, ContentDatabase content, ulong seed, int regionId, IdAllocator ids)
    {
        Book = book; Prod = prod; Content = content; Seed = seed; RegionId = regionId; Ids = ids;
        Rules = content.All<OwnershipRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new OwnershipRulesDef { Id = "default" };
        PRules = content.All<ProspectRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ProspectRulesDef { Id = "default" };
        Truth = new ProspectTruth(prod.Pop.Region, prod.Resources, MinedOfVein);
        prod.Steering = this;
    }

    // ------------------------------------------------------------------ lookups

    public Business? BusinessOf(EntityId id) => Prod.ById(id);
    public BusinessKind KindOf(Business b) => Prod.KindOf(b);
    public bool IsHeroOwner(Business b) => b.Owner >= 0 && b.Owner == HeroRow();
    public PurseId HeroPurse => Econ.Ledger.Open(PurseKind.Person, Hero);
    /// <summary>The hero's household (his home chest's coin, decided: the household's purse is the chest at home), -1 none.</summary>
    public int HeroHousehold { get { int r = HeroRow(); return r >= 0 && r < Pop.People.Count ? Pop.People.Household[r] : -1; } }
    public long HeroCoin => Econ.Ledger.Balance(HeroPurse).Mites;
    public long ChestCoin => HeroHousehold is int h and >= 0 && h < Econ.HouseholdCount ? Econ.Ledger.Balance(Econ.HouseholdPurse[h]).Mites : 0;
    /// <summary>Where the hero's profit goes: his purse when he is at the place, else the chest at home (coin never travels by magic).</summary>
    public PurseId PayeeFor(int settlement, out bool chest)
    {
        int hh = HeroHousehold;
        chest = Book.HeroAt != settlement && hh >= 0 && hh < Econ.HouseholdCount;
        return chest ? Econ.HouseholdPurse[hh] : HeroPurse;
    }

    private void Reindex()
    {
        _steered.Clear();
        foreach (var id in Book.Holdings.Keys) if (Prod.ById(id) is { } b) _steered.Add(b.Index);
        _steeredCount = Book.Holdings.Count;
    }

    public BusinessHolding Hold(Business b)
    {
        if (!Book.Holdings.TryGetValue(b.Id, out var h)) { Book.Holdings[b.Id] = h = new BusinessHolding { Business = b.Id, StewardWage = Rules.StewardWageMitesPerDay }; h.Orders.SendEveryDays = Rules.SendEveryDays; h.Orders.KeepMites = Rules.KeepMites; }
        _steeredCount = -1;
        return h;
    }

    private BusinessHolding? H(Business b) => Book.Holdings.TryGetValue(b.Id, out var h) ? h : null;

    private EntityId Event(int day, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string, string)[] data)
        => Chron?.Invoke(GameTime.FromGameHours((long)day * 24), type, subjects, causes, data) ?? default;

    private static string M(long mites) => Quotes.Money(mites);
    private string Name(Business b) => (Content.TryGet<BusinessDef>(KindOf(b).Id, out var d) ? d.Name : KindOf(b).Id) + ", " + Pop.Region.Settlements[Math.Clamp(b.Settlement, 0, Pop.Region.Settlements.Count - 1)].Name;
    public string NameOf(Business b) => Name(b);

    // ------------------------------------------------------------------ IBusinessSteering

    public bool Steered(Business b)
    {
        if (_steeredCount != Book.Holdings.Count) Reindex();
        return _steered.Contains(b.Index);
    }

    public bool Owned(Business b) => IsHeroOwner(b);

    public int WagePerHour(Business b, int person, int classWagePerHour)
        => H(b) is { } h && h.Wages.TryGetValue(person, out int w) ? Math.Max(0, w / Prod_WorkdayHours) : classWagePerHour;
    private const int Prod_WorkdayHours = ProductionSystem.WorkdayHours;

    public long? MarginMicro(Business b, Recipe r)
    {
        if (H(b) is not { Steward: >= 0 } h || !IsHeroOwner(b)) return null;
        int lag = StewardBrain.LagDays(Pop.People.SkillLevelOf(h.Steward, Skill.Trading), Rules);
        if (lag == 0 || h.PriceDays.Count == 0) return null;
        var row = h.PriceDays[Math.Max(0, h.PriceDays.Count - 1 - lag)];
        var book = Markets?.BookForSettlement(b.Settlement);
        long Mid(ushort g) { int i = Array.IndexOf(h.PriceGoods, g); return i >= 0 && i < row.Length ? row[i] : book?.MidMicro(g) ?? Econ.Goods.BasePriceMilliMites(g) * (Econ.Goods.IsPiece(g) ? 1000 : 1); }
        return StewardBrain.MarginMicro(r, Mid, Econ.Money.SpreadHalfPermille, Econ.Money.MarketTollPermille);
    }

    public Recipe? Ordered(Business b, BusinessKind kind, string station)
    {
        if (H(b) is not { } h || !IsHeroOwner(b) || h.Orders.Recipe < 0 || h.Orders.Recipe >= Prod.Content.Recipes.Recipes.Count) return null;
        var r = Prod.Content.Recipes.Recipes[h.Orders.Recipe];
        if (r.Station != station || !kind.Recipes.Contains(r)) return null;
        // "make N a day": once the day's N batches have started the station rests
        if (h.Orders.PerDay > 0 && h.MadeToday >= h.Orders.PerDay) return null;
        return r;
    }

    public bool MayBuyInputs(Business b) => H(b) is not { } h || !IsHeroOwner(b) || h.Orders.SeasonBudget <= 0 || b.InputsSeason < h.Orders.SeasonBudget;

    public (int FloorPm, long Keep)? Sale(Business b, ushort good)
    {
        if (H(b) is not { } h || !IsHeroOwner(b)) return null;
        if (HasStall(b)) return (900, long.MaxValue / 4);                                     // everything is kept for the stall, which sells it minded
        int floor = 900; long keep = 0;
        if (h.Orders.FloorGood == good && Markets != null)
        {
            var gb = Markets.BookForSettlement(b.Settlement).Goods[good];
            long unitMicro = h.Orders.FloorMites * PriceCurve.Micro;
            floor = gb.RefMicro > 0 ? (int)Math.Clamp(unitMicro * 1000 / gb.RefMicro, 1, 4000) : 900;
        }
        if (h.Orders.KeepGood == good) keep = h.Orders.KeepQty;
        return (floor, keep);
    }

    public int TileFor(Business b, ExtractionKind k, int fallback)
    {
        if (H(b) is not { } h || h.Tiles.Length == 0 || Prod.Resources == null || k.Resource is not { } res) return fallback;
        int best = -1; long bs = 0;
        foreach (int t in h.Tiles) { int row = Prod.Resources.Find(t, res); if (row >= 0 && Prod.Resources.StockG(row) > bs) { bs = Prod.Resources.StockG(row); best = row; } }
        return best;
    }

    public bool PayDividend(Business b, long dividend)
    {
        if (H(b) is not { } h) return false;
        if (h.Shares.Count == 0 && !IsHeroOwner(b)) return false;
        var ownerId = IsHeroOwner(b) ? Hero : b.OwnerHousehold >= 0 ? Pop.Households.IdOf(b.OwnerHousehold) : default;
        foreach (var (holder, mites) in Valuation.Split(dividend, ownerId, h.Shares))
        {
            if (mites <= 0) continue;
            PurseId to;
            if (holder == Hero) { to = PayeeFor(b.Settlement, out bool chest); if (chest) h.SentToChest += mites; else h.SentToPurse += mites; }
            else if (Pop.Households.IndexOf(holder) is int hh and >= 0 && hh < Econ.HouseholdCount) to = Econ.HouseholdPurse[hh];
            else continue;
            if (!Econ.Ledger.Transfer(b.Purse, to, new Coin(mites), TransferReason.Dividend)) return false;
        }
        return true;
    }

    public void SeasonClosed(Business b, long net, long cash)
    {
        if (H(b) is not { } h) return;
        int season = (int)(Pop.NowDay / TimeConst.DaysPerSeason);
        long revenue = Math.Max(0, net + b.WagesSeason + b.InputsSeason + h.StewardPaidSeason);
        h.History.Add(new SeasonRow(season, revenue, b.WagesSeason + h.StewardPaidSeason, b.InputsSeason, net, b.DividendsSeason));
        while (h.History.Count > 4) h.History.RemoveAt(0);
        h.RevenueSeason = 0; h.SentSeason = 0; h.StewardPaidSeason = 0;
        if (IsHeroOwner(b) && h.Orders.CloseAfterTwoLosses && h.History.Count >= 2 && h.History[^1].Profit < 0 && h.History[^2].Profit < 0)
        {
            Prod.Close(b);
            Notices.Add(HeroText.Of("bz_closed_losses", Name(b)));
        }
    }

    public int HireCap(Business b)
    {
        if (H(b) is not { } h || !IsHeroOwner(b)) return int.MaxValue;
        if (h.Steward < 0) return b.Staff.Count;                                                  // hands-on: the hero hires himself
        return h.Orders.HireUpTo < 0 ? int.MaxValue : h.Orders.HireUpTo;
    }

    // ------------------------------------------------------------------ valuation

    /// <summary>The business's premises at the land's price (the plot and its building), or 0 when it stands on nobody's lot.</summary>
    public long PremisesValue(Business b)
    {
        if (Land == null || b.Lot < 0 || (uint)b.Settlement >= (uint)Pop.Region.Settlements.Count) return 0;
        var st = Pop.Region.Settlements[b.Settlement]; if ((uint)b.Lot >= (uint)st.Plan.Lots.Count) return 0;
        var lot = st.Plan.Lots[b.Lot];
        var r = Land.Rules;
        return Ravensguard.Sim.Land.LandRules.PlotPrice(r, lot.Rect.W * lot.Rect.H, st.Class, Ravensguard.Sim.Land.LandRules.DemandPm(r, Ravensguard.Sim.Land.LandRules.CrowdedPct(r, Pop.Households, b.Settlement)), Land.BuildingValue(b.Settlement, lot.Dwelling));
    }

    /// <summary>Micro-mites a unit (a gram, or a piece) of a good at a book's bid (or mid), else at its base price.</summary>
    public long UnitMicro(MarketBook? book, ushort g, bool bid = true)
        => book == null ? Econ.Goods.BasePriceMilliMites(g) * (Econ.Goods.IsPiece(g) ? 1000 : 1) : bid ? book.BidMicro(g) : book.MidMicro(g);

    /// <summary>The store at the book's bid (mites).</summary>
    public long StockBid(Business b)
    {
        long s = 0; var book = Markets?.BookForSettlement(b.Settlement);
        foreach (var (g, q) in b.Store.Rows()) s += (long)((Int128)q * UnitMicro(book, g) / PriceCurve.Micro);
        return s;
    }

    /// <summary>Station items and tools at bid: each station is valued at the bid of a piece of tools (the abstract tier keeps no tool rows for a business).</summary>
    public long ToolsBid(Business b)
    {
        var book = Markets?.BookForSettlement(b.Settlement);
        if (!Econ.Goods.TryIdOf("tools", out ushort t)) return 0;
        return UnitMicro(book, t) / PriceCurve.Micro * b.Stations.Count;
    }

    public long ValueOf(Business b)
    {
        var profits = H(b)?.History.Select(x => x.Profit).ToList() ?? [];
        return Valuation.Value(PremisesValue(b), StockBid(b), ToolsBid(b), Econ.Ledger.Balance(b.Purse).Mites, profits, Rules.GoodwillSeasons);
    }

    // ------------------------------------------------------------------ royalties and exhaustion

    private long MinedOfVein(int vein)
    {
        long g = 0;
        foreach (var x in Book.Holdings.Values) if (x.Vein == vein && Prod.ById(x.Business) is { Mine: >= 0 } b) g += Prod.Mines[b.Mine].MinedG;
        // a mine world generation placed on the vein: its site lies in the vein's footprint
        var v = Pop.Region.Veins[vein];
        foreach (var m in Prod.Mines)
        {
            if (m.SiteIndex < 0 || m.SiteIndex >= Pop.Region.Sites.Count || m.Mineral != v.Type) continue;
            var s = Pop.Region.Sites[m.SiteIndex]; int dx = s.TileX - v.TileX, dy = s.TileY - v.TileY;
            if (dx * dx + dy * dy <= v.RadiusTiles * v.RadiusTiles) g += m.MinedG;
        }
        return g;
    }

    public void Extracted(Business b, ushort good, long grams, GameTime now)
    {
        if (H(b) is not { } h) return;
        h.LastMinedG += grams;
        if (b.Mine < 0) return;
        var lic = h.Licence >= 0 ? Book.Licences.FirstOrDefault(l => l.Serial == h.Licence && !l.Revoked) : null;
        if (lic == null) { h.Alerts |= OwnerAlert.Unlicensed; return; }
        // the royalty: a share of the value of what was mined, at the book's mid, to the right holder (spec 5)
        var book = Markets?.BookForSettlement(b.Settlement);
        long value = (long)((Int128)grams * UnitMicro(book, good, bid: false) / PriceCurve.Micro);
        long due = value * lic.RoyaltyPm / 1000 + h.RoyaltyOwed;
        if (due <= 0) return;
        var to = GrantorPurse(lic.Grantor, lic.GrantorIndex);
        long pay = Math.Min(due, Econ.Ledger.Balance(b.Purse).Mites);
        if (pay > 0 && Econ.Ledger.Transfer(b.Purse, to, new Coin(pay), TransferReason.Royalty)) { h.RoyaltyPaid += pay; b.InputsSeason += pay; }
        else pay = 0;
        h.RoyaltyOwed = due - pay;
    }

    /// <summary>The purse of a right holder: a fief's lord's household, else its treasury; a realm's treasury (the taxes' realm purse).</summary>
    public PurseId GrantorPurse(Ravensguard.Sim.Land.GrantorKind k, int index)
    {
        if (k == Ravensguard.Sim.Land.GrantorKind.Fief && index >= 0 && index < Pop.Region.Fiefs.Count)
        {
            int hh = index < Pop.FiefLordHousehold.Length ? Pop.FiefLordHousehold[index] : -1;
            if (hh >= 0 && hh < Econ.HouseholdCount && Pop.Households.MemberCount[hh] > 0) return Econ.HouseholdPurse[hh];
            return Econ.Ledger.Open(PurseKind.Treasury, Pop.Region.Fiefs[index].Id);
        }
        return Econ.Ledger.Open(PurseKind.Treasury, Ravensguard.Sim.Land.LandWorld.RealmTreasuryId(index));
    }
}
