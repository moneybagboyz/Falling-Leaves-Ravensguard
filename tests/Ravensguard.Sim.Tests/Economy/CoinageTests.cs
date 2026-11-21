using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Faith;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Tests.Economy;

/// <summary>P7-CN1 (coinage 10): CN-2 to CN-10 and CN-13 to CN-16, and the pieces they stand on (the three coins, the gold book's hook, the changer, the mass, the save section, the load report's notice).</summary>
public class CoinageTests
{
    // ---------------------------------------------------------------- the coins

    [Fact]
    public void Coin_Views_AndGreedyParts()
    {
        Assert.Equal(new Coin(10), Coin.FromCopper(1));
        Assert.Equal(new Coin(100), Coin.FromSilver(1));
        Assert.Equal(new Coin(1000), Coin.FromGold(1));
        Assert.Equal(Coin.FromCrowns(7), Coin.FromSilver(7));
        new Coin(1234).Parts(out long gp, out long sp, out long cp, out long m);
        Assert.Equal((1L, 2L, 3L, 4L), (gp, sp, cp, m));
        new Coin(250_000).Parts(out gp, out sp, out cp, out m);
        Assert.Equal((250L, 0L, 0L, 0L), (gp, sp, cp, m));
    }

    [Fact]
    public void CN3_SilverStrike_KeepsTheOldNumbers() => Assert.Equal(new StrikeResult(new Coin(769_200), new Coin(57_690), new Coin(711_510), 400), MintMath.Strike(10_000_000, 750));

    [Fact]
    public void CN4_GoldStrike()
    {
        var r = MintMath.StrikeGold(1_000_000, 500);
        Assert.Equal(new StrikeResult(new Coin(909_000), new Coin(45_450), new Coin(863_550), 100), r);
    }

    [Fact]
    public void CN5_CopperToken_PaysTheBid_StrikesAtFace()
    {
        var r = MintMath.StrikeCopper(50_000_000, 160, 125_000_000, 0);
        Assert.Equal(10_000, r.Pieces); Assert.Equal(100_000, r.Face.Mites); Assert.Equal(8_000, r.ToSeller.Mites); Assert.Equal(0, r.HandedBackMg);
    }

    [Fact]
    public void CN6_CopperCap_StrikesUpToIt_HandsTheRestBack()
    {
        var r = MintMath.StrikeCopper(10_000_000, 160, 125_000_000, 4_990_000);
        Assert.Equal(1_000, r.Pieces); Assert.Equal(5_000_000, r.HandedBackMg); Assert.Equal(5_000_000, r.UsedMg);
        Assert.Equal(0, MintMath.StrikeCopper(10_000_000, 160, 125_000_000, 5_000_000).Pieces);
    }

    // ---------------------------------------------------------------- the gold book

    private static (Ledger L, CoinStock S) Book() { var l = new Ledger(); var s = new CoinStock(); l.Coinage = s; return (l, s); }

    /// <summary>A purse of an opted-in kind holding value and gold pieces: the value is minted, the pieces entered as struck.</summary>
    private static PurseId Fund(Ledger l, CoinStock s, PurseKind kind, ulong id, long mites, long gold)
    {
        var p = l.Open(kind, new EntityId(id));
        l.Mint(p, new Coin(mites), SourceReason.Seed);
        if (gold > 0) s.AddStruck(l, p, gold);
        return p;
    }

    [Fact]
    public void CN7_LargePayment_MovesGold_SmallDoesNot()
    {
        var (l, s) = Book();
        var treasury = Fund(l, s, PurseKind.Treasury, 1, 80_000, 50);
        var captain = l.Open(PurseKind.Person, new EntityId(2)); s.OptIn(captain);
        var house = l.Open(PurseKind.Household, new EntityId(3));
        Assert.True(l.Transfer(treasury, captain, new Coin(25_000), TransferReason.Wage));
        Assert.Equal(25, s.Pieces(captain)); Assert.Equal(25, s.Pieces(treasury));
        Assert.True(l.Transfer(treasury, house, new Coin(300), TransferReason.Wage));
        Assert.Equal(25, s.Pieces(treasury)); Assert.Equal(0, s.Loose);
        Assert.Equal("", s.Check(l));
    }

    [Fact]
    public void CN8_Clamp_MakingChange_ASpareGoldPieceGoesLoose()
    {
        var (l, s) = Book();
        var chest = Fund(l, s, PurseKind.Chest, 1, 5_000, 5);
        var house = l.Open(PurseKind.Household, new EntityId(3));
        Assert.True(l.Transfer(chest, house, new Coin(300), TransferReason.Gift));
        Assert.Equal(4, s.Pieces(chest)); Assert.Equal(4_700, l.Balance(chest).Mites); Assert.Equal(1, s.Loose);
        Assert.Equal(5, s.GoldInWorld); Assert.Equal("", s.Check(l));
    }

    [Fact]
    public void LargePayment_ToANonOptedPayee_PutsTheGoldInCirculation()
    {
        var (l, s) = Book();
        var treasury = Fund(l, s, PurseKind.Treasury, 1, 80_000, 50);
        var house = l.Open(PurseKind.Household, new EntityId(3));
        Assert.True(l.Transfer(treasury, house, new Coin(12_500), TransferReason.Wage));
        Assert.Equal(12, s.Loose); Assert.Equal(38, s.Pieces(treasury)); Assert.Equal("", s.Check(l));
    }

    [Fact]
    public void CN9_Exchange_TwoPercent_PiecesFromThePool()
    {
        var (l, s) = Book();
        var hero = l.Open(PurseKind.Person, new EntityId(1)); l.Mint(hero, new Coin(50_000), SourceReason.Seed);
        var changer = l.Open(PurseKind.Business, new EntityId(2)); l.Mint(changer, new Coin(5_000), SourceReason.Seed);
        var mint = Fund(l, s, PurseKind.Treasury, 9, 30_000, 20);
        Assert.True(l.Transfer(mint, l.Open(PurseKind.Household, new EntityId(7)), new Coin(30_000), TransferReason.Wage));         // 20 gold pieces go loose
        Assert.Equal(20, s.Loose);
        var r = MoneyChanger.ToGold(l, s, hero, changer, 10_000, 20);
        Assert.True(r.Ok); Assert.Equal(200, r.Fee); Assert.Equal(9, r.Pieces);
        Assert.Equal(9, s.Pieces(hero)); Assert.Equal(11, s.Loose);
        Assert.Equal(49_800, l.Balance(hero).Mites); Assert.Equal(5_200, l.Balance(changer).Mites);
        Assert.Equal(200, l.Flow(TransferReason.Exchange).Mites);
        Assert.Equal("", s.Check(l));
        var back = MoneyChanger.ToSilver(l, s, hero, changer, 4, 20);
        Assert.True(back.Ok); Assert.Equal(80, back.Fee); Assert.Equal(5, s.Pieces(hero)); Assert.Equal(4, s.Pieces(changer));
        Assert.Equal("", s.Check(l));
    }

    [Fact]
    public void Exchange_NoGoldToBeHad_IsRefused()
    {
        var (l, s) = Book();
        var hero = l.Open(PurseKind.Person, new EntityId(1)); l.Mint(hero, new Coin(50_000), SourceReason.Seed);
        var changer = l.Open(PurseKind.Business, new EntityId(2));
        var r = MoneyChanger.ToGold(l, s, hero, changer, 10_000, 20);
        Assert.False(r.Ok); Assert.Equal("co_no_gold", r.Why); Assert.Equal(50_000, l.Balance(hero).Mites);
    }

    [Fact]
    public void CN10_Mass_GoldPiecesWeighLessThanTheirSilver()
    {
        var (l, s) = Book();
        var hero = l.Open(PurseKind.Person, new EntityId(1)); s.OptIn(hero);
        l.Mint(hero, new Coin(10_000), SourceReason.Seed); s.AddStruck(l, hero, 9);
        Assert.Equal(22_900, Encumbrance.CoinMassMg(l, hero)); Assert.Equal(22, Encumbrance.CoinMassG(l, hero));
        var plain = l.Open(PurseKind.Person, new EntityId(2)); l.Mint(plain, new Coin(10_000), SourceReason.Seed);
        Assert.Equal(130, Encumbrance.CoinMassG(l, plain)); Assert.Equal(Encumbrance.CoinMassG(10_000), Encumbrance.CoinMassG(l, plain));
    }

    [Fact]
    public void CN15_Melt_ThreeGoldPieces()
    {
        var (l, s) = Book();
        var cat = Rig.Goods;
        var chest = Fund(l, s, PurseKind.Chest, 1, 10_000, 5);
        var store = new Store(cat, new MassLedger(cat.Count), StoreKind.Chest, new EntityId(1));
        ushort gold = cat.IdOf("gold");
        Assert.True(s.MeltGold(l, chest, 3, store, gold));
        Assert.Equal(3, store.Qty(gold)); Assert.Equal(300, s.GoldMeltDustMg);                          // 3,300 mg: three whole grams in the store, 300 mg carried
        Assert.Equal(3_000, l.Sunk(SinkReason.Melt).Mites); Assert.Equal(7_000, l.Balance(chest).Mites);
        Assert.Equal(2, s.Pieces(chest)); Assert.Equal(3, s.GoldMelted); Assert.Equal("", s.Check(l));
        Assert.False(s.MeltGold(l, chest, 3, store, gold));
    }

    private static class Rig { public static readonly GoodCatalog Goods = EconFixture.Catalog(); }

    // ---------------------------------------------------------------- the save section

    [Fact]
    public void CoinageSave_RoundTrips_AndEmptyBooksAreNotWritten()
    {
        Assert.True(new CoinageBook().IsEmpty);
        var b = new CoinageBook();
        b.Stock.GoldStruck = 12; b.Stock.GoldMelted = 2; b.Stock.Loose = 4; b.Stock.CopperStruck = 90; b.Stock.CopperMelted = 10; b.Stock.GoldMeltDustMg = 300;
        b.Stock.Opted[5] = 6; b.Stock.Extra.Add(9); b.Stock.Extra.Add(5);
        b.Queue.Add(new MintDelivery { Realm = 2, Metal = MintMetal.Silver, Mg = 91_000, Seller = 17 });
        b.Queue.Add(new MintDelivery { Realm = 2, Metal = MintMetal.Gold, Mg = 400, Seller = -1 });
        b.SeasonOf(2).Silver = 70; b.SeasonOf(2).Seigniorage = 5_250; b.SeasonNo = 3; b.UsedDay = 44; b.Used[2] = 70;
        Assert.False(b.IsEmpty);
        var bytes = SaveCodec.SerializeCompressed(CoinageSave.Capture(b));
        var back = SaveCodec.DeserializeCompressed<CoinageSave>(bytes).Restore();
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(CoinageSave.Capture(back)));
        Assert.Equal(90 - 10, back.Stock.CopperPieces); Assert.Equal(6, back.Stock.OptedTotal + 0 - 0); Assert.Equal(2, back.Queue.Count);
    }

    // ---------------------------------------------------------------- the notice

    [Theory]
    [InlineData("0.1", true)] [InlineData("0.1.0", true)] [InlineData("0.3.0-dev", true)] [InlineData("", true)] [InlineData("0.4.0-dev", false)] [InlineData("0.5.1", false)] [InlineData("1.0", false)]
    public void LoadReport_CoinageNotice_ForSavesFromBeforePhase7(string version, bool notice)
    {
        Assert.Equal(notice, LoadReport.IsBeforePhase7(version));
        var r = new LoadReport { Notices = notice ? [LoadReport.CoinageNotice] : [] };
        Assert.False(r.NeedsConfirmation);                                                              // informational: never asks
        Assert.Equal(notice, r.ToText().Contains("gold, silver and copper"));
    }

    // ---------------------------------------------------------------- the running world

    private static ushort Good(FaithRig r, string id) => r.State.Economy!.Goods.IdOf(id);

    private static MintWorld Mints(FaithRig r) => r.Sim.Get<MintWorld>()!;

    private static void Advance(FaithRig r, int days)
    {
        long h0 = r.Runtime.HourDone + (24 - r.Runtime.HourDone % 24) % 24;
        r.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24L * days));
    }

    /// <summary>A hero command at a game hour of the day after the world's current hour, run through the desk as the barrier would.</summary>
    private static HeroResult Hero(FaithRig r, HeroCommand c, int dayOffset = 1, int hour = 10)
    {
        long day = (r.Runtime.HourDone / 24) + dayOffset;
        return r.Do(c, day * 86400 + hour * 3600L);
    }

    [Fact]
    public void CN16_PhaseSixWorld_RunOneSeason_MintMasterAppointed_NothingWrittenUntilTheFirstStrike()
    {
        using var r = new FaithRig();
        var mw = Mints(r);
        Assert.NotEmpty(mw.Mints);
        Assert.DoesNotContain(r.State.Rule!.Offices, o => o.Kind == OfficeKind.MintMaster);
        Assert.True(r.State.Coinage!.IsEmpty);
        Advance(r, 16);
        foreach (var m in mw.Mints) Assert.NotNull(r.State.Rule!.OfficeOfRealm(OfficeKind.MintMaster, m.Realm));
        // the section is written exactly while the book holds something: silver the region's hearths delivered may already have been struck
        Assert.Equal(!r.State.Coinage!.IsEmpty, Capture(r).Sections.ContainsKey("coinage"));
        var m0 = mw.Mints[0];
        var e = r.State.Economy!; long minted0 = e.Ledger.Sourced(SourceReason.Mint).Mites, purse0 = e.Ledger.Balance(r.Ctx.Purse).Mites;
        var silver = Good(r, "silver");
        r.Ctx.Pack.Create(silver, 5_000, CreateReason.Other);
        var res = Hero(r, CoinDesk.Sell(MintMetal.Silver, 5_000, m0.Settlement, 0));
        Assert.Equal(HeroResultCode.Ok, res.Code);
        Assert.Equal(purse0 + 384_600 - 28_845, e.Ledger.Balance(r.Ctx.Purse).Mites);                    // 5,000 g = 3,846 sp, struck at once for a walk-in seller, less 7.5%
        Assert.True(e.Ledger.Sourced(SourceReason.Mint).Mites >= minted0 + 384_600);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.True(e.MassAuditHolds());
        Assert.Contains("coinage", Capture(r).Sections.Keys);
    }

    private static SaveFile Capture(FaithRig r) => r.Runtime.AtBarrier(() => WorldSave.Capture(r.State, "h", "0.4.0")).GetAwaiter().GetResult();

    [Fact]
    public void CN2_APhaseSixSave_LoadedAndSavedAgain_KeepsItsBytes_AndHasNoCoinageSection()
    {
        using var r = new FaithRig(runtime: false);
        var file = WorldSave.Capture(r.State, "h", "0.3.0-dev");
        Assert.DoesNotContain("coinage", file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), r.Content, "h", null, r.W.Continent, r.W.History); st.Content = r.Content;
        var again = WorldSave.Capture(st, "h", "0.3.0-dev");
        Assert.DoesNotContain("coinage", again.Sections.Keys);
        Assert.Equal(bytes, SaveCodec.Encode(again));
        var report = WorldSave.Inspect(file, r.Content, "h");
        Assert.Contains(LoadReport.CoinageNotice, report.Notices); Assert.False(report.NeedsConfirmation);
    }

    [Fact]
    public void CN13_MintsWired_YearOfSilver_TheAuditStaysExact()
    {
        using var r = new FaithRig();
        var mw = Mints(r); var e = r.State.Economy!; var led = e.Ledger;
        long seed = led.Audit().Seeded.Mites;
        // the open businesses of the realm sell the silver they hold, every morning
        var silver = Good(r, "silver");
        var biz = r.State.Production!.Businesses.Where(b => b.State == Ravensguard.Sim.Production.BusinessState.Open && mw.Mints.Any(m => r.Region.Settlements[b.Settlement].Realm == m.Realm)).Take(3).ToList();
        Assert.NotEmpty(biz);
        for (int day = 0; day < 60; day++)
        {
            if (day % 10 == 1) foreach (var b in biz) b.Store.Create(silver, 3_000, CreateReason.Mined);
            Advance(r, 1);
            var a = led.Audit();
            Assert.True(a.IsConserved, "day " + day + ": " + a);
            Assert.Equal(seed + a.Minted.Mites + a.Immigrated.Mites, a.Sourced.Mites);
            Assert.True(e.MassAuditHolds(), "mass, day " + day);
        }
        Assert.True(led.Audit().Minted.Mites > 0);
        Assert.True(led.Flow(TransferReason.Seigniorage).Mites > 0);
        Assert.True(r.State.Coinage!.Stock.Conserved);                                                     // gold may come from the region's own mines and hearths: the book still adds up
    }

    [Fact]
    public void CN14_GoldBook_ConservedEveryDay_SaveMidYearIsIdentical()
    {
        using var r = new FaithRig();
        var mw = Mints(r); var e = r.State.Economy!; var led = e.Ledger; var book = r.State.Coinage!;
        var gold = Good(r, "gold");
        var m0 = mw.Mints[0];
        Advance(r, 16);                                                                                    // the mint master is in post
        var cat = e.Goods;
        // the hero strikes gold, changes some back and forth, pays, melts
        led.Mint(r.Ctx.Purse, new Coin(200_000), SourceReason.Seed);
        r.Ctx.Pack.Create(gold, 5_500, CreateReason.Mined);
        Assert.Equal(HeroResultCode.Ok, Hero(r, CoinDesk.Sell(MintMetal.Gold, 5_500, m0.Settlement, 0)).Code);
        var stock = book.Stock;
        Assert.Equal("", stock.Check(led));
        long struck = stock.GoldStruck; Assert.True(struck >= 5_000);                                     // 5,500 g at 1.1 g a piece, and what the region's own hearths sent
        var heroPieces0 = stock.Pieces(r.Ctx.Purse);
        Assert.True(heroPieces0 > 0);                                                                       // the hero is opted in and is paid gold
        for (int day = 0; day < 60; day++)
        {
            if (day == 5) Assert.Equal(HeroResultCode.Ok, Hero(r, CoinDesk.Melt(MintMetal.Gold, 3, 0)).Code);
            Advance(r, 1);
            Assert.Equal("", stock.Check(led));
            Assert.True(led.Audit().IsConserved);
            if (day == 30)
            {
                var file = Capture(r);
                var back = WorldSave.Restore(file, r.Content, "h");
                var again = WorldSave.Capture(back, "h", "0.4.0");
                Assert.True(file.Sections["coinage"].Span.SequenceEqual(again.Sections["coinage"].Span), "the coinage section is identical after load and save");
            }
        }
        Assert.Equal(3, stock.GoldMelted);
    }
}
