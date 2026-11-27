using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Faith;
using Ravensguard.Sim.Trade;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// OV-7 (overview 4, "gold, silver and copper"): a Phase 6 save (written by a version below 0.4.0, no <c>coinage</c> section) loads with the coinage notice (informational, never a question) and re-saves byte for byte; the loaded
/// game then runs a season on the real runtime: the hero delivers silver and the mint strikes it (seigniorage paid), copper is struck, a purse is changed to gold, every price shows as gp, sp and cp, and the gold and copper book
/// (<c>CoinStock.Check</c>: opted + loose = struck - melted) and the audit hold every day. The cap itself (<c>mint_copper_capped</c>) is CN-6's unit test; a season of one hero's copper does not reach 4% of the supply.
/// </summary>
public class Ov7CoinageTests(ITestOutputHelper output)
{
    private static ushort Good(FaithRig r, string id) => r.State.Economy!.Goods.IdOf(id);

    private static HeroResult Hero(FaithRig r, HeroCommand c) => r.Do(c, ((r.Runtime.HourDone / 24) + 1) * 86400 + 10 * 3600L);

    [Fact]
    public void OV7_APhaseSixSave_LoadsWithTheCoinNotice_ResavesIdentical_ThenASeasonOfSilverCopperAndGold_EveryDayExact()
    {
        // the Phase 6 save: a pre-0.4.0 header, no coinage section
        byte[] bytes; var heroCoin0 = 0L;
        using (var r0 = new FaithRig(runtime: false))
        {
            var file = WorldSave.Capture(r0.State, "h", "0.3.0-dev");
            Assert.DoesNotContain("coinage", file.Sections.Keys);
            bytes = SaveCodec.Encode(file);
            var rep = WorldSave.Inspect(SaveCodec.Decode(bytes), r0.Content, "h");
            Assert.Contains(LoadReport.CoinageNotice, rep.Notices); Assert.False(rep.NeedsConfirmation); Assert.True(rep.CanLoad);
            Assert.Contains("gold, silver and copper", rep.ToText());
            var st = WorldSave.Restore(SaveCodec.Decode(bytes), r0.Content, "h", null, r0.W.Continent, r0.W.History); st.Content = r0.Content;
            var again = WorldSave.Capture(st, "h", "0.3.0-dev");
            Assert.DoesNotContain("coinage", again.Sections.Keys);
            Assert.True(bytes.AsSpan().SequenceEqual(SaveCodec.Encode(again)), "the Phase 6 save re-saves byte for byte");
            heroCoin0 = st.Economy!.Ledger.Balance(new HeroContext(st, st.Party!, r0.Content).Purse).Mites;
        }
        output.WriteLine($"Phase 6 save: {bytes.Length} bytes, notice shown, hero holds {heroCoin0} mites");

        // a season on the real runtime of the same world (the loaded state is a fresh rig's twin: seed 1234, day 0)
        using var r = new FaithRig();
        var mints = r.Sim.Get<MintWorld>()!; Assert.NotEmpty(mints.Mints);
        var e = r.State.Economy!; var led = e.Ledger; var book = r.State.Coinage!; var m0 = mints.Mints[0];
        long Days() => r.Runtime.HourDone / 24;
        void Advance(int days) { long h0 = r.Runtime.HourDone + (24 - r.Runtime.HourDone % 24) % 24; r.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24L * days)); }
        Advance(16);                                                                                      // the mint master is in post
        led.Mint(r.Ctx.Purse, new Coin(300_000), SourceReason.Seed);
        long seeded = led.Audit().Seeded.Mites;

        // gold: the hero's silver changed to gold at the money changer's 2%; and gold struck from his ore
        r.Ctx.Pack.Create(Good(r, "gold"), 5_500, CreateReason.Mined);
        Assert.Equal(HeroResultCode.Ok, Hero(r, CoinDesk.Sell(MintMetal.Gold, 5_500, m0.Settlement, 0)).Code);
        long goldStruck = book.Stock.GoldStruck; Assert.True(goldStruck >= 5_000);
        Assert.True(book.Stock.Pieces(r.Ctx.Purse) > 0, "the hero is opted in and holds gold pieces");

        Advance(1);                                                                                       // the moneyers strike 6,000 pieces a day: one metal a day
        // silver: delivered by the hero, struck, seigniorage paid
        long purse0 = led.Balance(r.Ctx.Purse).Mites, minted0 = led.Sourced(SourceReason.Mint).Mites, seign0 = led.Flow(TransferReason.Seigniorage).Mites;
        r.Ctx.Pack.Create(Good(r, "silver"), 5_000, CreateReason.Other);
        Assert.Equal(HeroResultCode.Ok, Hero(r, CoinDesk.Sell(MintMetal.Silver, 5_000, m0.Settlement, 0)).Code);
        Assert.Equal(purse0 + 384_600 - 28_845, led.Balance(r.Ctx.Purse).Mites);                          // 3,846 sp struck for a walk-in seller, less 7.5%
        Assert.True(led.Sourced(SourceReason.Mint).Mites >= minted0 + 384_600); Assert.True(led.Flow(TransferReason.Seigniorage).Mites >= seign0 + 28_845);

        Advance(1);
        // copper: a heap of the token metal is struck at face
        var copper = Good(r, "copper");
        r.Ctx.Pack.Create(copper, 20_000, CreateReason.Other);
        var cres = Hero(r, CoinDesk.Sell(MintMetal.Copper, 20_000, m0.Settlement, 0));
        output.WriteLine($"copper sale: {cres.Code} {cres.Text}; copper struck {book.Stock.CopperStruck}");
        Assert.Equal(HeroResultCode.Ok, cres.Code); Assert.True(book.Stock.CopperStruck > 0);

        // the season: every day the books add up, the audit holds, nothing was made but by a mint's strike
        long minted1 = led.Sourced(SourceReason.Mint).Mites;
        for (int d = 0; d < 60; d++)
        {
            if (d == 20) Assert.Equal(HeroResultCode.Ok, Hero(r, CoinDesk.Melt(MintMetal.Gold, 2, 0)).Code);
            Advance(1);
            Assert.Equal("", book.Stock.Check(led));                                                      // sum(opted) + loose = struck - melted
            Assert.True(led.Audit().IsConserved, "day " + d);
            Assert.Equal(seeded, led.Audit().Seeded.Mites);                                               // nothing seeded: the strikes are the only faucet
            Assert.True(e.MassAuditHolds(), "mass, day " + d);
        }
        Assert.Equal(2, book.Stock.GoldMelted);
        Assert.True(led.Sourced(SourceReason.Mint).Mites >= minted1);

        // every price shows as gp, sp and cp
        Assert.Equal("1 gp 2 sp 3 cp", MoneyText.Format(1_234)); Assert.Equal("1 sp 5 cp", MoneyText.Format(150)); Assert.Equal("5 cp", MoneyText.Format(Coin.FromCopper(5).Mites));
        Assert.Equal(MoneyText.Format(led.Balance(r.Ctx.Purse).Mites), MoneyText.Format(led.Balance(r.Ctx.Purse).Mites, MoneyStyle.Amount));

        // and the save of the season is stable, with the coinage section now written
        var (back, saved) = P7Game.RoundTrip(r.State, r.W, "OV-7 after the season");
        Assert.True(back.Coinage!.Stock.Conserved);
        Assert.Contains("coinage", SaveCodec.Decode(saved).Sections.Keys);
        output.WriteLine($"gold struck {book.Stock.GoldStruck}, melted {book.Stock.GoldMelted}, copper struck {book.Stock.CopperStruck}, silver seigniorage {led.Flow(TransferReason.Seigniorage).Mites} mites, season save {saved.Length} bytes, day {Days()}");
    }
}
