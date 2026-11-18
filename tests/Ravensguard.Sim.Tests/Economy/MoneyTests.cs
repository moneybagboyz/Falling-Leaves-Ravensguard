using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Tests.Economy;

public class CoinTests
{
    [Fact]
    public void Arithmetic_IsChecked_AndNeverWraps()
    {
        Assert.Equal(new Coin(350), Coin.FromCrowns(3) + new Coin(50));
        Assert.Equal(new Coin(-1), new Coin(49) - new Coin(50));
        Assert.Throws<OverflowException>(() => new Coin(long.MaxValue) + new Coin(1));
        Assert.Throws<OverflowException>(() => new Coin(long.MinValue) - new Coin(1));
        Assert.Throws<OverflowException>(() => Coin.FromCrowns(long.MaxValue / 50));
        Assert.Throws<OverflowException>(() => new Coin(long.MaxValue) * 2);
        Assert.Throws<OverflowException>(() => -new Coin(long.MinValue));
    }

    [Theory]
    [InlineData(123405, "1234.05")] [InlineData(0, "0.00")] [InlineData(7, "0.07")] [InlineData(-7, "-0.07")] [InlineData(-123456, "-1234.56")] [InlineData(100, "1.00")]
    public void Formatting_IsInvariant_AndRoundTrips(long mites, string text)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            // the test host may run without ICU, so build cultures that would break naive formatting: comma decimals, odd group and negative signs
            foreach (var (dec, group, neg) in new[] { (".", ",", "-"), (",", ".", "-"), (",", " ", "\u2212"), ("\u066B", "\u066C", "-") })
            {
                var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                c.NumberFormat.NumberDecimalSeparator = dec; c.NumberFormat.NumberGroupSeparator = group; c.NumberFormat.NegativeSign = neg;
                CultureInfo.CurrentCulture = c;
                Assert.Equal(text, new Coin(mites).ToString());
            }
        }
        finally { CultureInfo.CurrentCulture = old; }
        Assert.True(Coin.TryParse(text, out var back));
        Assert.Equal(new Coin(mites), back);
    }

    [Fact]
    public void Formatting_HandlesMinValue_AndParsingRejectsJunk()
    {
        Assert.Equal("-92233720368547758.08", new Coin(long.MinValue).ToString());
        foreach (var bad in new[] { "", "abc", "1.234", "1,5", "--1", "1.", ".5", "99999999999999999999" }) Assert.False(Coin.TryParse(bad, out _), bad);
        Assert.True(Coin.TryParse("2.5", out var c)); Assert.Equal(new Coin(250), c);
    }

    [Fact]
    public void CrownsAndRemainder() { var c = new Coin(12345); Assert.Equal(123, c.WholeCrowns); Assert.Equal(45, c.MiteRemainder); }

    [Fact]
    public void MulDiv_RoundsHalfUp_WithoutOverflow()
    {
        Assert.Equal(new Coin(389), new Coin(7776).MulDiv(50, 1000));            // 388.8
        Assert.Equal(new Coin(2), new Coin(5).MulDiv(1, 3));                      // 1.67 -> 2
        Assert.Equal(new Coin(2), new Coin(3).MulDiv(1, 2));                      // 1.5 -> 2 (half up)
        Assert.Equal(new Coin(1), new Coin(4).MulDiv(1, 3));                      // 1.33 -> 1
        Assert.Equal(new Coin(long.MaxValue / 2), new Coin(long.MaxValue / 2).MulDiv(1_000_000_000_000, 1_000_000_000_000));
    }
}

public class LedgerTests
{
    private static (Ledger L, PurseId A, PurseId B, PurseId C) Three()
    {
        var l = new Ledger();
        var a = l.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 1));
        var b = l.Open(PurseKind.Treasury, EntityId.Make(IdKind.Other, 1));
        var c = l.Open(PurseKind.Chest, EntityId.Make(IdKind.Settlement, 1));
        return (l, a, b, c);
    }

    [Fact]
    public void Open_IsIdempotent_AndSeparatesKinds()
    {
        var l = new Ledger(); var owner = EntityId.Make(IdKind.Settlement, 5);
        var a = l.Open(PurseKind.Settlement, owner); var b = l.Open(PurseKind.Chest, owner);
        Assert.NotEqual(a, b);
        Assert.Equal(a, l.Open(PurseKind.Settlement, owner));
        Assert.Equal(2, l.PurseCount);
        Assert.True(l.TryFind(PurseKind.Chest, owner, out var f) && f == b);
        Assert.False(l.TryFind(PurseKind.Hoard, owner, out _));
        Assert.Equal(PurseKind.Chest, l.KindOf(b)); Assert.Equal(owner, l.OwnerOf(b));
    }

    [Fact]
    public void Transfer_MovesCoin_AndLogsTheReason()
    {
        var (l, a, b, _) = Three();
        l.Mint(a, Coin.FromCrowns(10), SourceReason.Seed);
        Assert.True(l.Transfer(a, b, new Coin(250), TransferReason.Tithe));
        Assert.Equal(new Coin(750), l.Balance(a)); Assert.Equal(new Coin(250), l.Balance(b));
        Assert.Equal(new Coin(250), l.Flow(TransferReason.Tithe)); Assert.Equal(1, l.FlowCount(TransferReason.Tithe));
        var last = l.Recent().Last();
        Assert.Equal(TransferReason.Tithe, last.Transfer); Assert.Equal(a, last.From); Assert.Equal(b, last.To); Assert.Equal(new Coin(250), last.Amount);
        var first = l.Recent().First();
        Assert.Equal(SourceReason.Seed, first.Source); Assert.True(first.From.IsNone);
        Assert.Equal(2, l.Sequence);
    }

    [Fact]
    public void Transfer_RefusesOverdraft_AndBadAmounts_WithoutChange()
    {
        var (l, a, b, _) = Three();
        l.Mint(a, new Coin(100), SourceReason.Seed);
        Assert.False(l.Transfer(a, b, new Coin(101), TransferReason.Trade));
        Assert.Equal(new Coin(100), l.Balance(a)); Assert.Equal(Coin.Zero, l.Balance(b));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Transfer(a, b, Coin.Zero, TransferReason.Trade));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Transfer(a, b, new Coin(-5), TransferReason.Trade));
        Assert.Throws<ArgumentException>(() => l.Transfer(a, a, new Coin(1), TransferReason.Trade));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Transfer(a, new PurseId(99), new Coin(1), TransferReason.Trade));
        Assert.Equal(0, l.FlowCount(TransferReason.Trade));
    }

    [Fact]
    public void Transfer_Overflow_Throws_AndLeavesBalancesIntact()
    {
        var (l, a, b, _) = Three();
        l.Mint(a, new Coin(long.MaxValue - 10), SourceReason.Seed);
        l.Mint(b, new Coin(11), SourceReason.Mint);
        Assert.Throws<OverflowException>(() => l.Transfer(b, a, new Coin(11), TransferReason.Trade));
        Assert.Equal(new Coin(11), l.Balance(b)); Assert.Equal(new Coin(long.MaxValue - 10), l.Balance(a)); Assert.Equal(0, l.FlowCount(TransferReason.Trade));
    }

    [Fact]
    public void MintAndSink_AreTheOnlyFaucetAndDrain_AndAreAudited()
    {
        var (l, a, _, c) = Three();
        l.Mint(a, new Coin(1000), SourceReason.Seed);
        l.Mint(a, new Coin(500), SourceReason.Mint);
        Assert.True(l.Sink(a, new Coin(300), SinkReason.Melt));
        Assert.False(l.Sink(a, new Coin(5000), SinkReason.Burn));
        Assert.True(l.Transfer(a, c, new Coin(200), TransferReason.Hoard));
        var r = l.Audit();
        Assert.True(r.IsConserved, r.ToString());
        Assert.Equal(new Coin(1500), r.Sourced); Assert.Equal(new Coin(300), r.Sunk);
        Assert.Equal(new Coin(1200), r.InPurses); Assert.Equal(new Coin(1000), l.Balance(a));
        Assert.Equal(new Coin(1000), l.Sourced(SourceReason.Seed)); Assert.Equal(new Coin(500), l.Sourced(SourceReason.Mint));
        Assert.Equal(new Coin(300), l.Sunk(SinkReason.Melt)); Assert.Equal(Coin.Zero, l.Sunk(SinkReason.Burn));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Mint(a, Coin.Zero, SourceReason.Mint));
    }

    [Fact]
    public void Audit_NoticesCoinThatBypassedTheLedger()
    {
        // the audit is computed from balances, so a state that does not balance cannot be imported
        var (l, a, _, _) = Three();
        l.Mint(a, new Coin(100), SourceReason.Seed);
        var s = l.Export();
        var tampered = s with { Balance = [101, 0, 0] };
        Assert.Throws<InvalidDataException>(() => Ledger.Import(tampered));
        Assert.Throws<InvalidDataException>(() => Ledger.Import(s with { Balance = [-1, 101, 0] }));
        Assert.True(Ledger.Import(s).Audit().IsConserved);
    }

    [Fact]
    public void HoardPurses_AreAuditedButNotCirculating()
    {
        var l = new Ledger();
        var a = l.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 1));
        var h = l.Open(PurseKind.Hoard, EntityId.Make(IdKind.Settlement, 3));
        l.Mint(a, new Coin(1000), SourceReason.Seed);
        l.Transfer(a, h, new Coin(400), TransferReason.Hoard);
        var r = l.Audit();
        Assert.Equal(new Coin(1000), r.InPurses); Assert.Equal(new Coin(400), r.InHoards); Assert.Equal(new Coin(600), r.Circulating); Assert.True(r.IsConserved);
        Assert.Equal(new Coin(400), l.TotalOf(PurseKind.Hoard));
    }

    [Fact]
    public void RecentLog_IsABoundedRing_TotalsAreNot()
    {
        var l = new Ledger(recentLogCapacity: 8);
        var a = l.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 1)); var b = l.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 2));
        l.Mint(a, new Coin(1000), SourceReason.Seed);
        for (int i = 0; i < 100; i++) l.Transfer(i % 2 == 0 ? a : b, i % 2 == 0 ? b : a, new Coin(1), TransferReason.Gift);
        var recent = l.Recent().ToList();
        Assert.Equal(8, recent.Count);
        Assert.Equal(Enumerable.Range(93, 8).Select(i => (long)i), recent.Select(r => r.Seq));   // seq 0 is the mint, 1..100 the transfers
        Assert.Equal(100, l.FlowCount(TransferReason.Gift)); Assert.Equal(101, l.Sequence);
        Assert.Equal(new Coin(100), l.Flow(TransferReason.Gift));
    }

    [Fact]
    public void RandomTransferStorm_KeepsTheTotal()
    {
        const ulong seed = 42; var l = new Ledger(256);
        var purses = new List<PurseId>(); var kinds = Enum.GetValues<PurseKind>();
        for (int i = 0; i < 200; i++) purses.Add(l.Open(kinds[i % kinds.Length], EntityId.Make(IdKind.Other, (ulong)i + 1)));
        long sourced = 0, sunk = 0;
        for (int i = 0; i < 20; i++) { l.Mint(purses[i], new Coin(50_000), SourceReason.Seed); sourced += 50_000; }
        var reasons = Enum.GetValues<TransferReason>();
        int refused = 0;
        for (long n = 0; n < 100_000; n++)
        {
            int a = DetRng.Range(seed, n, 1, 1, 0, 0, 200), b = DetRng.Range(seed, n, 2, 1, 0, 0, 200);
            long amt = 1 + DetRng.Range(seed, n, 3, 1, 0, 0, 20_000);
            int op = DetRng.Range(seed, n, 4, 1, 0, 0, 100);
            if (op < 90) { if (a == b) continue; if (!l.Transfer(purses[a], purses[b], new Coin(amt), reasons[(int)(n % reasons.Length)])) refused++; }
            else if (op < 95) { l.Mint(purses[a], new Coin(amt), op % 2 == 0 ? SourceReason.Mint : SourceReason.Seed); sourced += amt; }
            else if (l.Sink(purses[a], new Coin(amt), op % 2 == 0 ? SinkReason.Melt : SinkReason.Burn)) sunk += amt;
            if (n % 5000 == 0) { var mid = l.Audit(); Assert.True(mid.IsConserved, mid.ToString()); }
        }
        var r = l.Audit();
        Assert.True(r.IsConserved, r.ToString());
        Assert.Equal(sourced, r.Sourced.Mites); Assert.Equal(sunk, r.Sunk.Mites); Assert.Equal(sourced - sunk, r.InPurses.Mites);
        Assert.True(refused > 0, "the storm should have hit overdrafts");
        for (int i = 0; i < 200; i++) Assert.False(l.Balance(purses[i]).IsNegative);
    }

    [Fact]
    public void ExportImport_RoundTripsEverything()
    {
        var (l, a, b, c) = Three();
        l.Mint(a, new Coin(900), SourceReason.Seed); l.Transfer(a, b, new Coin(100), TransferReason.Wage); l.Sink(b, new Coin(40), SinkReason.Melt); l.Transfer(a, c, new Coin(5), TransferReason.Fine);
        var back = Ledger.Import(l.Export());
        Assert.Equal(l.Audit(), back.Audit());
        Assert.Equal(l.Balance(b), back.Balance(b)); Assert.Equal(l.Sequence, back.Sequence);
        Assert.Equal(l.Flow(TransferReason.Wage), back.Flow(TransferReason.Wage));
        Assert.Equal(l.Export().Owner, back.Export().Owner);
    }
}

public class MintTests
{
    [Fact] // G-M1
    public void G_M1_StrikeSilver()
    {
        var r = MintMath.Strike(10_000_000, 750);
        Assert.Equal(new Coin(769_200), r.Struck); Assert.Equal(new Coin(57_690), r.Seigniorage); Assert.Equal(new Coin(711_510), r.ToSeller); Assert.Equal(400, r.DustMg);

        var l = new Ledger();
        var seller = l.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 1)); var treasury = l.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 1));
        MintMath.StrikeInto(l, seller, treasury, 10_000_000, 750);
        Assert.Equal(new Coin(711_510), l.Balance(seller)); Assert.Equal(new Coin(57_690), l.Balance(treasury));
        Assert.Equal(new Coin(769_200), l.Sourced(SourceReason.Mint)); Assert.Equal(new Coin(57_690), l.Flow(TransferReason.Seigniorage));
        Assert.True(l.Audit().IsConserved);
    }

    [Fact]
    public void Strike_LessThanACrown_StrikesNothing() => Assert.Equal(new StrikeResult(Coin.Zero, Coin.Zero, Coin.Zero, 1299), MintMath.Strike(1299, 750));
}
