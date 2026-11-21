using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-EA2: the bots and the duplication probe (economy-audit 3 pass c; EA-1, EA-6, EA-13, EA-14). The bots trade through the books' own calls; their capital is a one-off test faucet, every later mite is a ledger transfer.</summary>
public class AuditBotTests(ITestOutputHelper output)
{
    private static void Advance(WarLoadRun run, int day, int hour) => run.Runtime.AdvanceTo(GameTime.FromGameHours((long)day * 24 + hour));

    /// <summary>One game year of a bot's trading on a fresh world after a 30-day warm-up; returns (profit a day, best day, best trip text, leftover kg, coin conserved).</summary>
    private (double PerDay, long BestDay, string BestLoop, long BestTrip, long LeftoverKg, bool Conserved, long Trips) Year(int loadKg, double kmh, double loadHours, long seed, int days = 60)
    {
        using var run = AuditWorld.Create();
        int day0 = run.NextSlotDay + 30;                    // thirty days to let the books settle after the start
        Advance(run, day0, 11);
        var bot = new TradeBot(run, 1, loadKg, kmh, loadHours, seed);
        long cash0 = run.Econ.Ledger.Balance(bot.Purse).Mites;
        for (int d = day0; d < day0 + days; d++) { Advance(run, d, 12); bot.Day(d); }
        long profit = run.Econ.Ledger.Balance(bot.Purse).Mites - cash0;
        foreach (var l in bot.Log.Take(8)) output.WriteLine("  trip: " + l);
        output.WriteLine($"load {loadKg} kg at {kmh} km/h: {bot.Trips} trips in {days} days, cost {bot.CostMites}, takings {bot.TakingsMites}, toll {bot.TollMites}; profit {profit} = {(double)profit / days:0.0} a day; best day {bot.DayProfit.Max()}; best trip {bot.BestTripProfit} ({bot.BestLoop}); {bot.LeftoverKg} kg left unsold");
        return ((double)profit / days, bot.DayProfit.Count == 0 ? 0 : bot.DayProfit.Max(), bot.BestLoop, bot.BestTripProfit, bot.LeftoverKg, run.CoinConserved && run.MassOk, bot.Trips);
    }

    /// <summary>EA-1 / EA-14 (X1): the trader on foot, 30 kg, between books at most 10 km apart, a game year. The exploit rule: at most 525 mites a day (1.5 x the skilled wage).</summary>
    [Fact, Trait("Category", "Soak")]
    public void EA1_EA14_X1_TraderOnFoot_30Kg_ProfitAGameDay()
    {
        var r = Year(30, 5.0, 1.0, 5_000);
        Assert.True(r.Conserved);
        Assert.True(r.PerDay <= 525, $"{r.PerDay:0.0} a day on foot");
    }

    /// <summary>EA-1 / EA-14 (X1): the same by cart, 500 kg at 4 km/h: allowed only where the books show a real gap (a trade profit, logged); the number is the audit's evidence.</summary>
    [Fact, Trait("Category", "Soak")]
    public void EA1_EA14_X1_TraderByCart_500Kg_ProfitAGameDay_IsReported()
    {
        var r = Year(500, 4.0, 2.0, 100_000);
        Assert.True(r.Conserved);
        output.WriteLine($"by cart: {r.PerDay:0.0} mites a game day ({r.PerDay / 350:0.0}x the skilled wage of 350, the rule's line is 525)");
    }

    // ---------------------------------------------------------------- X11 / EA-6: duplication

    /// <summary>EA-6: 1,000 random moves of goods between stores (households, businesses and the other stores), a save and load between every hundred, the mass ledger exact and each good's net created-less-destroyed unchanged, the item table's count unchanged.</summary>
    [Fact]
    public void EA6_X11_ThousandRandomMoves_WithSavesBetween_KeepMassAndItemsExact()
    {
        using var run = AuditWorld.Create(links: false);
        var st = run.State; var content = st.Content!;
        var econ = st.Economy!; int goods = econ.Goods.Count;
        // forty item rows (a made-up pot, mail and silver piece each in turn) owned by forty households: their owners change with the moves and must come out of every save the same
        for (int k = 0; k < 40; k++) econ.Items.Add(st.Ids, k % 3 == 0 ? "iron_pot" : k % 3 == 1 ? "mail_hauberk" : "silver_chalice", ItemQuality.Common, st.Population.Households.Count > k ? EntityId.Make(IdKind.Person, 8_000_000UL + (ulong)k) : EntityId.None, [new ItemComponent(k % 3 == 0 ? "iron" : k % 3 == 1 ? "steel" : "silver", 500 + 10 * k, 1000)]);
        long[] net0 = Enumerable.Range(0, goods).Select(g => econ.Mass.Net((ushort)g)).ToArray(); int items0 = econ.Items.Count; Assert.Equal(40, items0);
        string ItemSig(EconomyState e) { var l = new List<string>(); for (int r = 0; r < e.Items.Count; r++) l.Add($"{e.Items.At(r).Shape}:{e.Items.At(r).Owner.Value}"); l.Sort(StringComparer.Ordinal); return string.Join("|", l); }
        string sig0 = ItemSig(econ);
        Assert.True(econ.MassAuditHolds());
        var marketStores = Enumerable.Range(0, st.Population.Region.Settlements.Count).Select(EconIds.MarketStore).ToHashSet();
        int moved = 0, refused = 0;
        for (int i = 0; i < 1000; i++)
        {
            ulong key = (ulong)i;
            var stores = new List<Store>(); for (int h = 0; h < econ.HouseholdStore.Length; h++) if (econ.HouseholdStore[h] != null) stores.Add(econ.HouseholdStore[h]);
            foreach (var kv in econ.OtherStores.OrderBy(k => k.Key.Value)) if (!marketStores.Contains(kv.Key)) stores.Add(kv.Value);          // businesses' and others' stores; a book's own store is held to its lots by the book, so moves there go through the book's calls (the bots)
            int a = DetRng.Range(st.Seed, i, key, RngStreams.ExploitBot, 0, 0, stores.Count), b2 = DetRng.Range(st.Seed, i, key, RngStreams.ExploitBot, 1, 0, stores.Count);
            if (i % 5 == 0 && econ.Items.Count > 0) { int row = DetRng.Range(st.Seed, i, key, RngStreams.ExploitBot, 4, 0, econ.Items.Count); econ.Items.At(row).Owner = EntityId.Make(IdKind.Person, 8_000_000UL + (ulong)DetRng.Range(st.Seed, i, key, RngStreams.ExploitBot, 5, 0, 40)); sig0 = ItemSig(econ); }
            if (a == b2) continue;
            var from = stores[a]; var rows = from.Rows(); if (rows.Length == 0) continue;
            var (g, q) = rows[DetRng.Range(st.Seed, i, key, RngStreams.ExploitBot, 2, 0, rows.Length)];
            long take = 1 + DetRng.Range(st.Seed, i, key, RngStreams.ExploitBot, 3, 0, (int)Math.Min(int.MaxValue - 1, q));
            if (Store.Move(from, stores[b2], g, take)) moved++; else refused++;
            Assert.True(econ.MassAuditHolds(), $"mass audit after move {i}");
            if (i % 100 == 99)
            {
                var file = WorldSave.Capture(st, "h", "0.1"); var bytes = SaveCodec.Encode(file);
                st = WorldSave.Restore(SaveCodec.Decode(bytes), content, "h", null, st.Continent, st.History); st.Content = content;
                econ = st.Economy!; Assert.Equal(items0, econ.Items.Count); Assert.Equal(sig0, ItemSig(econ));
                Assert.True(econ.MassAuditHolds(), $"mass audit after the save and load at move {i}");
            }
        }
        for (int g = 0; g < goods; g++) Assert.Equal(net0[g], econ.Mass.Net((ushort)g));
        output.WriteLine($"1,000 random moves: {moved} moved, {refused} refused (capacity); 10 save/load cycles; mass exact and net per good unchanged for {goods} goods; items {items0} (owners changed 200 times, equal after every save)");
        Assert.True(moved > 300);
    }

    // ---------------------------------------------------------------- WL-15 / EA-13: the rich player

    private static long StapleIndex(WarLoadRun run)
    {
        var cat = run.Econ.Goods; var ids = new[] { "wheat", "rye", "barley", "bread", "iron_bar", "timber_planks", "firewood", "charcoal" }.Where(g => cat.TryIdOf(g, out _)).Select(g => cat.IdOf(g)).ToArray();
        long sum = 0, n = 0; foreach (var b in run.Economy.Markets.AllBooks()) foreach (var g in ids) { sum += b.MidMicro(g); n++; }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>EA-13 (WL-15): the rich-player bot holds 500,000 mites and trades by cart (500 kg, 4 km/h) for five game years; the price index (staples, iron, planks, fuel over every book) stays within 1,200 permille of a run without the bot, and the bot's gain is ledger transfers only (the audit holds every day).</summary>
    [Fact, Trait("Category", "Soak")]
    public void EA13_RichPlayerBot_5Years_KeepsThePriceIndexWithin1200Permille()
    {
        const int Days = 300;
        long idxBot, idxCtl, cash0, cashEnd, trips, leftover; bool coinOk;
        using (var run = AuditWorld.Create())
        {
            int day0 = run.NextSlotDay + 30; Advance(run, day0, 11);
            var bot = new TradeBot(run, 2, 500, 4.0, 2.0, 500_000); cash0 = run.Econ.Ledger.Balance(bot.Purse).Mites;
            for (int d = day0; d < day0 + Days; d++) { Advance(run, d, 12); bot.Day(d); }
            idxBot = StapleIndex(run); cashEnd = run.Econ.Ledger.Balance(bot.Purse).Mites; trips = bot.Trips; leftover = bot.LeftoverKg; coinOk = run.CoinConserved && run.MassOk;
            output.WriteLine($"rich bot: {bot.Trips} trips in {Days} days; purse {cash0} -> {cashEnd} ({cashEnd - cash0:+#;-#} = {(double)(cashEnd - cash0) / Days:0.0} a day); {leftover} kg unsold; coin and mass exact: {coinOk}");
        }
        using (var run = AuditWorld.Create()) { int day0 = run.NextSlotDay + 30; Advance(run, day0 + Days, 12); idxCtl = StapleIndex(run); }
        long permille = idxBot * 1000 / Math.Max(1, idxCtl);
        output.WriteLine($"price index with the bot {idxBot}, without {idxCtl}: {permille} permille (pass <= 1,200 and >= 833)");
        Assert.True(coinOk);
        Assert.InRange(permille, 833, 1200);
    }
}
