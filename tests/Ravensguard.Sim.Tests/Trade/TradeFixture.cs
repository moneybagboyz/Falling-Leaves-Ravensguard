using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Tests.Trade;

/// <summary>A world for the hero's desk: the fixture region's economy and markets, a hero with a purse and a kit, a stocked market and a merchant with coin.</summary>
public sealed class TradeWorld
{
    public WorldState State { get; }
    public EconomyState Econ => State.Economy!;
    public HeroContext Ctx { get; }
    public HeroDesk Desk { get; } = new();
    public MarketBook Book { get; }
    public int Settlement { get; }
    public MarketParty Merchant { get; }
    public MarketParty Seller { get; }
    public long Sec { get; set; }

    public TradeWorld(int heroCrowns = 500, long merchantMites = 2_000_000)
    {
        var econ = EconFixture.NewEconomy();
        var st = EconFixture.WorldWith(econ);
        var origin = EconFixture.Content.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Test Hero", false, origin);
        var markets = new MarketSystem(econ, EconFixture.Pop, EconFixture.Seed);
        State = new WorldState
        {
            ContinentSettings = st.ContinentSettings, HistorySettings = st.HistorySettings, RegionSettings = st.RegionSettings, EmbarkCellX = st.EmbarkCellX, EmbarkCellY = st.EmbarkCellY,
            Continent = st.Continent, History = st.History, Population = st.Population, Clock = st.Clock, Ids = new IdAllocator(), Chunks = st.Chunks, Chronicle = new Chronicle(), Economy = econ,
            Hero = hero, Markets = markets,
        };
        econ.SeedHero(heroCrowns);
        State.Party = HeroParty.Create(econ, EconFixture.Content, State.Ids, hero);
        Ctx = new HeroContext(State, State.Party, EconFixture.Content);
        Settlement = 0;
        Book = markets.BookForSettlement(0);
        // a seller's lots on the book and a merchant who buys
        Seller = Party(PartyKind.Business, 0, ("flour", 400_000), ("wheat", 600_000), ("salt", 50_000), ("ale", 200_000));
        foreach (var g in new[] { "flour", "wheat", "salt", "ale" }) Book.List(Seller, Econ.Goods.IdOf(g), Seller.Store.Qty(Econ.Goods.IdOf(g)), 300, 0);
        Merchant = Party(PartyKind.Merchant, merchantMites);
        Sec = (long)(State.Clock.Now.TotalHours) * 3600 + 600;           // a summer afternoon, inside market hours
    }

    private int _n;
    public MarketParty Party(PartyKind kind, long mites, params (string Good, long Qty)[] goods)
    {
        var id = EntityId.Make(IdKind.Other, 0xA00_0000UL + (ulong)(++_n));
        var purse = Econ.Ledger.Open(kind == PartyKind.Household ? PurseKind.Household : PurseKind.Business, id);
        var store = Econ.OpenStore(StoreKind.House, id);
        if (mites > 0) Econ.Ledger.Mint(purse, new Coin(mites), SourceReason.Seed);
        foreach (var (g, q) in goods) store.Create(Econ.Goods.IdOf(g), q, CreateReason.Seed);
        var p = new MarketParty(id, purse, store, kind, 1000 + _n);
        if (kind == PartyKind.Merchant) Book.Merchants.Add(p);
        return p;
    }

    public ushort G(string id) => Econ.Goods.IdOf(id);
    public long Coin => Econ.Ledger.Balance(Ctx.Purse).Mites;
    public long Packed(string good) => Ctx.Pack.Qty(G(good));

    /// <summary>Issues a command at the current second and applies it at once (the barrier after this hour).</summary>
    public HeroResult Do(HeroCommand c)
    {
        var cmd = Desk.Issue(c with { IssuedSec = Sec, Settlement = c.Settlement < 0 ? Settlement : c.Settlement });
        Desk.ApplyDue(Ctx, cmd.Hour);
        return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
    }

    public long TotalCoin => Econ.Ledger.Audit().InPurses.Mites;
    public long[] GoodsTotals()
    {
        var t = new long[Econ.Goods.Count];
        for (int i = 0; i < Econ.HouseholdCount; i++) foreach (var (g, q) in Econ.HouseholdStore[i].Rows()) t[g] += q;
        foreach (var s in Econ.OtherStores.Values) foreach (var (g, q) in s.Rows()) t[g] += q;
        return t;
    }
}
