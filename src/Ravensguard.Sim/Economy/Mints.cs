using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Economy;

/// <summary>A realm's mint in the region (derived, never saved): the town it stands in, the settlement index in the region.</summary>
public readonly record struct MintRow(int Realm, int Settlement);

/// <summary>What a delivery at the counter came to: grams taken, pieces struck now, the coin paid to the seller, the seigniorage kept, grams handed back, and why nothing happened (a text key, or "").</summary>
public readonly record struct MintOutcome(bool Ok, string Why, long Grams, long Pieces, long Paid, long Seigniorage, long BackGrams)
{
    public static MintOutcome No(string why) => new(false, why, 0, 0, 0, 0, 0);
}

/// <summary>
/// The mints of a region, wired (P7-CN1, coinage 5.1). One mint per realm with a town in the region: its largest town (ties to the lower settlement index). Anyone may hand over gold, silver or copper at the counter in market
/// hours; the mint takes the metal into its store, and the realm's moneyers (2, each 3,000 pieces a working day) strike it in the order it came. The coin is created once, from that metal, by <see cref="Ledger.Mint"/> into the seller's
/// purse (<see cref="SourceReason.Mint"/>), the seigniorage goes to the realm's treasury as a logged <see cref="TransferReason.Seigniorage"/>, the metal is destroyed as <see cref="DestroyReason.Materialised"/>, and the dust
/// (less than a piece) stays in the store. Copper is token coin: the treasury pays the seller the book bid and strikes at face, up to the cap. Draws nothing; every step is in ascending realm and queue order.
/// <para>A mint strikes only while its realm has a mint master in office (<see cref="Staffed"/>); until then deliveries wait.</para>
/// </summary>
public sealed class MintWorld
{
    public CoinageBook Book { get; }
    public EconomyState Econ { get; }
    public Region Region { get; }
    public MoneySettingsDef Cfg { get; }
    /// <summary>Whether a realm's mint has its master. A world with no rule book has none to appoint: everything strikes.</summary>
    public Func<int, bool> Staffed { get; set; } = _ => true;
    /// <summary>The seigniorage a mint kept (realm, mites): the master's hands, so the rule's honesty may skim it.</summary>
    public Action<int, long>? Handled { get; set; }
    /// <summary>The book's bid for a good at a settlement, mites a kilogram; null: the good's base price less the spread.</summary>
    public Func<int, ushort, long>? BidMitesPerKg { get; set; }
    /// <summary>Who sells metal to the mint by themselves each day: (store, cash purse, settlement) of every open business, in id order.</summary>
    public Func<IEnumerable<(Store Store, PurseId Purse, int Settlement)>>? Sellers { get; set; }
    public RuleChron Chron { get; set; } = RuleChron.Silent;

    public ushort GoldGood { get; }
    public ushort SilverGood { get; }
    public ushort CopperGood { get; }
    public const int Moneyers = 2;

    private List<MintRow>? _rows;

    public MintWorld(CoinageBook book, EconomyState econ, Region region, MoneySettingsDef cfg)
    {
        Book = book; Econ = econ; Region = region; Cfg = cfg;
        GoldGood = econ.Goods.TryIdOf("gold", out var g) ? g : ushort.MaxValue;
        SilverGood = econ.Goods.TryIdOf("silver", out var s) ? s : ushort.MaxValue;
        CopperGood = econ.Goods.TryIdOf("copper", out var c) ? c : ushort.MaxValue;
        book.Stock.TransferMinMites = cfg.GoldTransferMinMites;
        econ.Ledger.Coinage = book.Stock;
    }

    private Ledger Led => Econ.Ledger;
    public long DayCapacity => (long)Moneyers * Cfg.MoneyerPiecesPerDay;

    // ------------------------------------------------------------------ where the mints are

    /// <summary>The region's mints, ascending realm (coinage 5.1): per realm the largest town of the region, ties to the lower index. Derived once from the region.</summary>
    public IReadOnlyList<MintRow> Mints => _rows ??= Derive(Region);

    /// <summary>The mints of a region, ascending realm: per realm with a town in it, its largest town (ties to the lower settlement index). A pure function of the region.</summary>
    public static List<MintRow> Derive(Region region)
    {
        var best = new SortedDictionary<int, int>();
        for (int i = 0; i < region.Settlements.Count; i++)
        {
            var s = region.Settlements[i];
            if (s.Class != SettlementClass.Town || s.Realm < 0) continue;
            if (!best.TryGetValue(s.Realm, out int b) || s.TargetPop > region.Settlements[b].TargetPop) best[s.Realm] = i;
        }
        return [.. best.Select(kv => new MintRow(kv.Key, kv.Value))];
    }

    public MintRow? MintOf(int realm) { foreach (var m in Mints) if (m.Realm == realm) return m; return null; }
    public MintRow? MintAt(int settlement) { foreach (var m in Mints) if (m.Settlement == settlement) return m; return null; }

    public static EntityId StoreOwner(int realm) => EntityId.Make(IdKind.Faction, (5UL << 40) | (uint)realm);
    public Store StoreOf(int realm) => Econ.OpenStore(StoreKind.Chest, StoreOwner(realm), 0, 0, false);
    public bool TryTreasury(int realm, out PurseId p) => Led.TryFind(PurseKind.Treasury, Ravensguard.Sim.Land.LandWorld.RealmTreasuryId(realm), out p);

    private ushort GoodOf(MintMetal m) => m switch { MintMetal.Gold => GoldGood, MintMetal.Silver => SilverGood, _ => CopperGood };
    private int PieceMg(MintMetal m) => MintMath.PieceMg(m, Cfg.GoldPieceMg, Cfg.CrownSilverMg, Cfg.CopperPieceMg);
    private int Bp(MintMetal m) => m == MintMetal.Gold ? Cfg.GoldSeigniorageBp : Cfg.MintSeigniorageBp;

    private void NewDay(int day)
    {
        if (Book.UsedDay == day) return;
        Book.UsedDay = day; Book.Used.Clear();
    }

    // ------------------------------------------------------------------ the counter

    /// <summary>
    /// Hands gold or silver over: the grams move from <paramref name="from"/> into the mint's store and wait in the queue for the moneyers; whatever the day's capacity allows is struck at once and the seller paid. Fails with a
    /// text key when the metal is not gold or silver, the store lacks it, or the realm has no mint here. Copper goes through <see cref="SellCopper"/>.
    /// </summary>
    public MintOutcome Deliver(int realm, MintMetal metal, long grams, Store from, PurseId seller, int day)
    {
        if (metal == MintMetal.Copper) return MintOutcome.No("co_use_copper_counter");
        if (grams <= 0 || MintOf(realm) == null) return MintOutcome.No("co_no_mint");
        if (!Staffed(realm) || !TryTreasury(realm, out _)) return MintOutcome.No("co_mint_closed");
        ushort good = GoodOf(metal); if (good == ushort.MaxValue) return MintOutcome.No("co_no_metal");
        if (from.Qty(good) < grams) return MintOutcome.No("co_not_enough_metal");
        var store = StoreOf(realm);
        if (!Store.Move(from, store, good, grams)) return MintOutcome.No("co_not_enough_metal");
        Book.Queue.Insert(0, new MintDelivery { Realm = realm, Metal = metal, Mg = grams * 1000, Seller = seller.Index });      // the counter serves walk-in sellers before the businesses' standing deliveries
        var o = StrikeRealm(realm, day, seller.Index);
        return new MintOutcome(true, "", grams, o.Pieces, o.Paid, o.Seigniorage, 0);
    }

    /// <summary>Copper at the counter (coinage 5.1, token coin): the treasury pays the seller the book's bid for the metal it takes, strikes at face (10 mites a piece) up to the cap and hands back the rest.</summary>
    public MintOutcome SellCopper(int realm, long grams, Store from, PurseId seller, int day)
    {
        var row = MintOf(realm);
        if (row == null || grams <= 0) return MintOutcome.No("co_no_mint");
        if (CopperGood == ushort.MaxValue) return MintOutcome.No("co_no_metal");
        if (from.Qty(CopperGood) < grams) return MintOutcome.No("co_not_enough_metal");
        if (!Staffed(realm) || !TryTreasury(realm, out var treasury)) return MintOutcome.No("co_mint_closed");
        long bid = BidMitesPerKg?.Invoke(row.Value.Settlement, CopperGood) ?? (long)(Econ.Goods.Def(CopperGood).BasePriceMitesPerKg * (1000 - 80) / 1000);
        long supply = Led.Audit().Circulating.Mites;
        var r = MintMath.StrikeCopper(grams * 1000, bid, supply, Book.Stock.CopperFace, Cfg.CopperCapPermille, Cfg.CopperPieceMg);
        long usedG = r.UsedMg / 1000, back = grams - usedG;
        var sr = Book.SeasonOf(realm);
        if (r.Pieces == 0)
        {
            NoteCapped(realm, row.Value, back, day, sr);
            return MintOutcome.No("co_copper_capped");
        }
        var store = StoreOf(realm);
        if (!Store.Move(from, store, CopperGood, usedG) || !store.Destroy(CopperGood, usedG, DestroyReason.Materialised)) return MintOutcome.No("co_not_enough_metal");
        Led.Mint(treasury, r.Face, SourceReason.Mint);
        Book.Stock.CopperStruck += r.Pieces;
        if (r.ToSeller.Mites > 0 && seller != treasury) Led.Transfer(treasury, seller, r.ToSeller, TransferReason.Trade);
        long gain = r.Face.Mites - r.ToSeller.Mites;
        sr.Copper += r.Pieces; sr.Seigniorage += gain;
        if (back > 0) NoteCapped(realm, row.Value, back, day, sr);
        Handled?.Invoke(realm, gain);
        return new MintOutcome(true, "", usedG, r.Pieces, r.ToSeller.Mites, gain, back);
    }

    private void NoteCapped(int realm, MintRow row, long backGrams, int day, MintSeasonRow sr)
    {
        if (backGrams <= 0) return;
        bool first = sr.CopperCapped == 0;
        sr.CopperCapped += backGrams;
        if (first)
            Chron.Chron(RuleCtx.At(day), "mint_copper_capped", "ch_coin_copper_capped", [ChronParam.Place("place", Region.Settlements[row.Settlement].Id), ChronParam.Count("kg", Math.Max(1, backGrams / 1000))],
                [Region.Settlements[row.Settlement].Id], [new Cause(CauseKind.Trade, null, NoteKey: "ch_note_copper_cap")]);
    }

    // ------------------------------------------------------------------ the moneyers' day

    /// <summary>The day's work (hour 0 of the daily globals): the open businesses of each realm sell the gold and silver they hold, then the moneyers strike what waits. Returns the pieces struck.</summary>
    public long Daily(int day)
    {
        NewDay(day);
        long n = 0;
        foreach (var m in Mints)
        {
            if (!Staffed(m.Realm) || !TryTreasury(m.Realm, out _)) continue;
            if (Sellers != null)
                foreach (var (store, purse, settlement) in Sellers())
                {
                    if ((uint)settlement >= (uint)Region.Settlements.Count || Region.Settlements[settlement].Realm != m.Realm) continue;
                    foreach (var metal in new[] { MintMetal.Gold, MintMetal.Silver })
                    {
                        ushort good = GoodOf(metal); if (good == ushort.MaxValue) continue;
                        long have = store.Qty(good);
                        if (have > 0) { var moved = Store.Move(store, StoreOf(m.Realm), good, have); if (moved) Book.Queue.Add(new MintDelivery { Realm = m.Realm, Metal = metal, Mg = have * 1000, Seller = purse.Index }); }
                    }
                }
            n += StrikeRealm(m.Realm, day, -1).Pieces;
        }
        return n;
    }

    /// <summary>Strikes what waits at one realm's mint within the day's capacity, in queue order. <paramref name="reportFor"/> is the seller purse whose result the caller wants (a hero's delivery), else -1.</summary>
    private (long Pieces, long Paid, long Seigniorage) StrikeRealm(int realm, int day, int reportFor)
    {
        NewDay(day);
        long pieces = 0, paid = 0, seigTotal = 0;
        if (!Staffed(realm) || !TryTreasury(realm, out var treasury)) return (0, 0, 0);
        long used = Book.Used.GetValueOrDefault(realm), capacity = DayCapacity;
        long myPieces = 0, myPaid = 0, mySeign = 0;
        for (int i = 0; i < Book.Queue.Count; i++)
        {
            var d = Book.Queue[i]; if (d.Realm != realm || d.Metal == MintMetal.Copper) continue;
            int pmg = PieceMg(d.Metal);
            long can = Math.Min(d.Mg / pmg, capacity - used);
            if (can <= 0) continue;
            var sr = Book.SeasonOf(realm);
            long struck = can * MintMath.PieceMites(d.Metal), seign = struck * Bp(d.Metal) / 10000;
            var seller = d.Seller >= 0 ? new PurseId(d.Seller) : treasury;
            Led.Mint(seller, new Coin(struck), SourceReason.Mint);
            if (d.Metal == MintMetal.Gold) Book.Stock.AddStruck(Led, seller, can);
            bool kept = seller != treasury;                                                  // the mint's own dust is struck into the treasury and keeps all of it
            if (kept && seign > 0) Led.Transfer(seller, treasury, new Coin(seign), TransferReason.Seigniorage);
            d.Mg -= can * pmg; used += can; pieces += can;
            if (kept) { paid += struck - seign; seigTotal += seign; sr.Seigniorage += seign; }
            if (d.Metal == MintMetal.Gold) sr.Gold += can; else sr.Silver += can;
            if (d.Seller == reportFor && kept) { myPieces += can; myPaid += struck - seign; mySeign += seign; }
        }
        if (used > 0) Book.Used[realm] = used;
        // what is left of a delivery that no longer fills a piece is the mint's dust: it stays in the store, in one unowned row per metal
        for (int i = 0; i < Book.Queue.Count; i++)
        {
            var d = Book.Queue[i];
            if (d.Realm != realm || d.Seller < 0 || d.Mg == 0 || d.Mg >= PieceMg(d.Metal)) continue;
            var dust = Book.Queue.FirstOrDefault(x => x.Realm == realm && x.Metal == d.Metal && x.Seller < 0 && !ReferenceEquals(x, d));
            if (dust != null) { dust.Mg += d.Mg; d.Mg = 0; } else d.Seller = -1;
        }
        Book.Queue.RemoveAll(x => x.Mg == 0);
        SyncStore(realm);
        if (seigTotal > 0) Handled?.Invoke(realm, seigTotal);
        return reportFor >= 0 ? (myPieces, myPaid, mySeign) : (pieces, paid, seigTotal);
    }

    /// <summary>The mint's store holds exactly the metal still waiting or kept as dust (whole grams, rounded up); the rest was struck into coin and is destroyed as <see cref="DestroyReason.Materialised"/>.</summary>
    private void SyncStore(int realm)
    {
        var store = StoreOf(realm);
        foreach (var metal in new[] { MintMetal.Gold, MintMetal.Silver })
        {
            ushort good = GoodOf(metal); if (good == ushort.MaxValue) continue;
            long need = (Book.WaitingMg(realm, metal) + 999) / 1000;
            long have = store.Qty(good);
            if (have > need) store.Destroy(good, have - need, DestroyReason.Materialised);
        }
    }

    // ------------------------------------------------------------------ the season

    /// <summary>A season's first day (coinage 8): each mint that struck anything last season writes one <c>mint_struck</c> entry, and the rows start again.</summary>
    public int Seasonal(int day)
    {
        int season = day / TimeConst.DaysPerSeason, n = 0;
        if (Book.SeasonNo == season) return 0;
        foreach (var r in Book.Season)
        {
            if (r.Gold + r.Silver + r.Copper == 0 || MintOf(r.Realm) is not { } m) continue;
            var place = Region.Settlements[m.Settlement].Id;
            Chron.Chron(RuleCtx.At(day), "mint_struck", "ch_coin_mint_struck",
                [ChronParam.Place("place", place), ChronParam.Count("gold", r.Gold), ChronParam.Count("silver", r.Silver), ChronParam.Count("copper", r.Copper), ChronParam.Mites("seigniorage", r.Seigniorage)],
                [place], [new Cause(CauseKind.Trade, null, NoteKey: "ch_note_mint_season")]);
            n++;
        }
        Book.Season.Clear(); Book.SeasonNo = season;
        return n;
    }
}

/// <summary>The mints' place in the abstract tick (P7-CN1, overview 8.4): the moneyers' day at hour 0 of the daily globals, after taxes; a season's first day writes the summaries first. A no-op in a world without a <see cref="MintWorld"/> (only the home region has one).</summary>
[Abstract.AbstractSystem]
public sealed class MintSystem : Abstract.AbstractSystem
{
    public MintSystem() : base("mint", Abstract.TickPhase.H5DailyGlobals, Abstract.SystemOrder.MintDaily) { }

    public override void Step(Abstract.TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var mw = ctx.World.Get<MintWorld>(); if (mw == null) return;
        var prev = mw.Chron;
        mw.Chron = RuleChron.Of(ctx.Outbox);
        try
        {
            if (ctx.IsSeasonStartDay) mw.Seasonal((int)ctx.Day);
            mw.Daily((int)ctx.Day);
        }
        finally { mw.Chron = prev; }
    }
}
