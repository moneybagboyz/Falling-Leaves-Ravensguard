using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Markets;

/// <summary>Small hand-built markets for the golden tests: an economy with no households, one book, parties made on demand.</summary>
public sealed class MarketKit
{
    public EconomyState Econ { get; }
    public MarketBook Book { get; }
    public PurseId Toll { get; }
    private int _next = 1;
    public ushort Grain { get; }

    /// <summary>The spec's test good: Ref 45 mites/kg, T = 16 days, d = 100 kg/day. Uses wheat's slot with those numbers.</summary>
    public MarketKit(ulong seed = 7, int settlementIndex = 0)
    {
        Econ = new EconomyState(EconFixture.Catalog(), EconFixture.Content.All<Ravensguard.Content.MoneySettingsDef>().First(), EconFixture.Content.All<Ravensguard.Content.NeedsBasketDef>().OrderBy(b => b.Id, StringComparer.Ordinal).ToList(), 0, 0);
        Toll = Econ.Ledger.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 99));
        Book = new MarketBook(Econ, settlementIndex, EntityId.Make(IdKind.Settlement, (ulong)settlementIndex + 1), "Testtown", Toll, 0, seed);
        Grain = Econ.Goods.IdOf("wheat");
        var gb = Book.Goods[Grain]; gb.RefMicro = gb.BaseRefMicro = 45_000; gb.TargetCoverDays = 16; gb.DemandEma = 100_000;
    }

    public MarketParty Party(PartyKind kind, long mites = 0, params (string Good, long Qty)[] goods)
    {
        var id = EntityId.Make(IdKind.Other, 0x900_0000UL + (ulong)_next++);
        var purse = Econ.Ledger.Open(kind == PartyKind.Household ? PurseKind.Household : PurseKind.Business, id);
        var store = Econ.OpenStore(StoreKind.House, id);
        if (mites > 0) Econ.Ledger.Mint(purse, new Coin(mites), SourceReason.Seed);
        foreach (var (g, q) in goods) store.Create(Econ.Goods.IdOf(g), q, CreateReason.Seed);
        var p = new MarketParty(id, purse, store, kind, _next);
        if (kind == PartyKind.Merchant) Book.Merchants.Add(p);
        return p;
    }

    public long Coin(MarketParty p) => Econ.Ledger.Balance(p.Purse).Mites;
    public long Qty(MarketParty p, ushort g) => p.Store.Qty(g);
    public long TotalCoin => Econ.Ledger.Audit().InPurses.Mites;

    /// <summary>Stocks the book with a merchant-owned lot of <paramref name="grams"/> of grain (floor 0.8 by default).</summary>
    public MarketParty Stock(long grams, int floor = 800, PartyKind kind = PartyKind.Business)
    {
        var seller = Party(kind, 0, ("wheat", grams));
        Assert.Equal(grams, Book.List(seller, Grain, grams, floor, 0, kind == PartyKind.Merchant));
        return seller;
    }
}
