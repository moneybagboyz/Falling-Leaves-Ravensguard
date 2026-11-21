using Ravensguard.Content;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Economy;

/// <summary>
/// P7-CN2: what the mint counter and the money changer panels show, built at a barrier (coinage 9). A read: nothing here changes a store, a purse, a stream or the book.
/// The panels send <c>cn:</c> commands through the hero's desk (<see cref="CoinDesk"/>); this is only their window.
/// </summary>
public sealed record CoinView(int Settlement, int Hour, bool HasMint, bool MintOpenNow, bool MintStaffed, bool HasChanger, int FeePermille,
    long Mites, long GoldPieces, long GoldG, long SilverG, long CopperG,
    long GoldKgPaidMites, long SilverKgPaidMites, long QueueGoldG, long QueueSilverG, long GoldAvailable)
{
    public static CoinView Build(WorldState st, ContentDatabase content, HeroParty party, int settlement, int hour)
    {
        var econ = st.Economy ?? throw new InvalidOperationException("coinage needs the world's economy");
        var mw = CoinageWiring.For(st, content); var book = st.Coinage ??= new CoinageBook();
        var cfg = mw.Cfg; var led = econ.Ledger;
        var purse = led.Open(PurseKind.Person, party.Hero); var pack = party.Carried.Pack(econ);
        long G(ushort good) => good == ushort.MaxValue ? 0 : pack.Qty(good);
        var row = mw.MintAt(settlement);
        long kgPaid(MintMetal m)
        {
            int pieceMg = MintMath.PieceMg(m, cfg.GoldPieceMg, cfg.CrownSilverMg, cfg.CopperPieceMg);
            var r = m == MintMetal.Gold ? MintMath.StrikeGold(1_000_000, cfg.GoldSeigniorageBp, pieceMg) : MintMath.Strike(1_000_000, cfg.MintSeigniorageBp, pieceMg);
            return r.ToSeller.Mites;
        }
        bool staffed = row is { } rw && mw.Staffed(rw.Realm) && mw.TryTreasury(rw.Realm, out _);
        var changer = st.Production is { } prod ? CoinDesk.ChangerAt(prod, settlement) : null;
        var fee = content.All<MoneySettingsDef>().OrderBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault() ?? econ.Money;
        long qg = 0, qs = 0;
        if (row is { } r0) { qg = book.WaitingMg(r0.Realm, MintMetal.Gold) / 1000; qs = book.WaitingMg(r0.Realm, MintMetal.Silver) / 1000; }
        long avail = changer != null ? book.Stock.Pieces(changer.Purse) + book.Stock.Loose : 0;
        return new CoinView(settlement, hour, row != null, hour >= econ.Money.MarketOpenHour && hour < econ.Money.MarketCloseHour, staffed, changer != null, fee.ChangerFeePermille,
            led.Balance(purse).Mites, book.Stock.Pieces(purse), G(mw.GoldGood), G(mw.SilverGood), G(mw.CopperGood),
            kgPaid(MintMetal.Gold), kgPaid(MintMetal.Silver), qg, qs, avail);
    }
}
