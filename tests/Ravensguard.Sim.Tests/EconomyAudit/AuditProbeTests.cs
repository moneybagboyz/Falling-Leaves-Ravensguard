using System.Text.RegularExpressions;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>
/// P7-EA2: the audit's probes on the economy of the tag <c>p7-audit-base</c> (economy-audit 3 and 4). Each test measures one checklist row, prints the numbers (they are quoted in <c>docs/reviews/phase-7-audit.md</c>) and asserts what is true
/// today. A finding that is a concrete defect has an <c>EA_F&lt;n&gt;</c> test that states the pass criterion; where the fix belongs to a later part (P7-EA3a, CN1) that test carries <c>Skip</c> with its owner and is switched on by the part that
/// fixes it, so CI is never red and the criterion is never lost.
/// </summary>
public class AuditProbeTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;

    // ------------------------------------------------------------------ X5 / EA-4: loot against its material

    public static List<(string Id, int MassG, int Listed, string Material, long MatMites, double Ratio)> LootRatios() => AuditMeasures.LootRatios(Db);

    [Fact]
    public void EA4_X5_LootRows_MeasuredAgainstTheirMaterial()
    {
        var rows = LootRatios(); Assert.NotEmpty(rows);
        output.WriteLine("id                  mass_g  listed  material   material_value  ratio   (a loot item is made as one part of its material, so the pack values it by ItemMath.MaterialMites)");
        foreach (var r in rows.OrderByDescending(x => x.Ratio)) output.WriteLine($"{r.Id,-20}{r.MassG,7}{r.Listed,8}  {r.Material,-9}{r.MatMites,15}{r.Ratio,8:0.0}");
        int over = rows.Count(r => r.Ratio > 1.2);
        output.WriteLine($"{over} of {rows.Count} loot rows are worth more than 1.2x their listed value as material; worst {rows.OrderByDescending(r => r.Ratio).First().Id} at {rows.Max(r => r.Ratio):0}x");
        // EA3a: the listed value is the material value (the audited tag had the gold torc at 206x and the reliquary at 205x)
        Assert.All(rows, r => Assert.InRange(r.Ratio, 0.0, 1.2));
        Assert.DoesNotContain(rows, r => r.Id == "skull");        // listed at nothing by design
        Assert.InRange(rows.Single(r => r.Id == "gold_torc").Ratio, 0.8, 1.2);
    }

    [Fact]
    public void EA_F2_EveryLootRow_IsWithin1p2OfItsMaterialValue()
    {
        var bad = LootRatios().Where(r => r.Ratio > 1.2).Select(r => $"{r.Id} {r.Ratio:0.0}x").ToList();
        Assert.True(bad.Count == 0, string.Join(", ", bad));
        var badEntries = AuditMeasures.LootEntryRatios(Db).Where(r => r.Ratio > 1.2).Select(r => $"{r.Table}/{r.Item} {r.Ratio:0.0}x").ToList();
        Assert.True(badEntries.Count == 0, string.Join(", ", badEntries));   // the loot tables' own rows (what the site budget spends) agree with the pack too
    }

    // ------------------------------------------------------------------ X6 / EA-5: striking and melting against the bullion price

    [Fact]
    public void EA5_X6_StrikeAndMelt_MarginAgainstTheBullionPrice()
    {
        // one crown is 1,300 mg of silver = 100 mites at silver's base price (76,920 mites a kg); the mint keeps 7.5%
        long strike = MintMath.Strike(1_300_000, 750).ToSeller.Mites;       // 1.3 kg = 1,000 crowns: seller gets
        Assert.Equal(92_500, strike);
        output.WriteLine("bullion price (x base)   strike margin (bullion bought, struck)   melt margin (coin melted, bullion sold)");
        foreach (var p in new[] { 0.3, 0.5, 0.8, 0.925, 1.0, 1.5, 2.0, 4.0 })
        {
            double strikeM = strike / (100_000.0 * p) - 1, meltM = p - 1;
            output.WriteLine($"{p,10:0.000}             {strikeM,12:P1}                            {meltM,12:P1}");
        }
        // the seigniorage band: striking pays only while the bullion is below 92.5% of coin parity; melting pays at once above parity and has no fee
        Assert.True(strike / (100_000.0 * 0.925) - 1 is > -0.0001 and < 0.0001);
        Assert.True(strike / (100_000.0 * 0.30) - 1 > 2.0, "at 0.3x bullion a strike returns three times the silver's price");
        Assert.True(Enum.IsDefined(SinkReason.Melt));
    }

    // ------------------------------------------------------------------ X3 / X4 / EA-3: salvage and demolition never return more than the materials

    [Fact]
    public void EA3_X3_X4_SalvageShares_AndTheSalvageRecipes_NeverReturnMoreThanWentIn()
    {
        var pieces = Db.All<BuildPieceDef>().Where(p => p.Build is { Materials.Count: > 0 }).ToList();
        Assert.NotEmpty(pieces);
        var byPm = pieces.GroupBy(p => p.Build!.SalvagePm).OrderBy(g => g.Key).Select(g => $"{g.Key / 10.0:0}%: {g.Count()}").ToList();
        output.WriteLine("build pieces with materials by salvage share: " + string.Join(", ", byPm));
        Assert.All(pieces, p => Assert.InRange(p.Build!.SalvagePm, 0, 900));          // a demolition returns the same goods: value out / value in = the share, never above 90%
        var cat = EconFixture.Catalog();
        double Price(string good) => cat.Def(cat.IdOf(good)).BasePriceMitesPerKg;
        // mail: salvage_mail gives 70% of the mass as mail rings (not tradable) and the rest as scrap iron; armour: 80% of the metal as scrap iron, all the leather as rags, the rest waste
        double ironIn = Db.Get<MaterialDef>("iron").ValuePerKg * 100, steelIn = Db.Get<MaterialDef>("steel").ValuePerKg * 100, leatherIn = Db.Get<MaterialDef>("leather").ValuePerKg * 100;
        double mailOut = 0.7 * Price("mail_rings") + 0.3 * Price("scrap_iron"), armourMetalOut = 0.8 * Price("scrap_iron"), leatherOut = Price("rags");
        output.WriteLine($"salvage_mail: {mailOut:0.0} mites a kg out against {ironIn:0} (iron) in = {mailOut / ironIn:P0}; salvage_armour metal {armourMetalOut:0.0} against {steelIn:0} (steel) = {armourMetalOut / steelIn:P0}; leather to rags {leatherOut:0.0} against {leatherIn:0} = {leatherOut / leatherIn:P0}");
        Assert.True(mailOut < ironIn); Assert.True(armourMetalOut < steelIn); Assert.True(leatherOut < leatherIn);
    }

    // ------------------------------------------------------------------ S-1 / EA-9: raw extraction against its wage

    public static List<(string Id, string Good, double Kg, double Price, double Value, double Ratio)> RawRatios() => AuditMeasures.RawRatios(Db, EconFixture.Catalog());

    [Fact]
    public void EA9_S1_RawExtraction_ValueOfAWorkerDayOverItsWage()
    {
        var rows = RawRatios(); Assert.NotEmpty(rows);
        output.WriteLine("extraction          good        kg/day  mites/kg  value/day  x wage(150)");
        foreach (var r in rows) output.WriteLine($"{r.Id,-19} {r.Good,-11}{r.Kg,8:0}{r.Price,10:0.00}{r.Value,11:0}{r.Ratio,8:0.0}x");
        // EA3a (R1): the 8.7x of the suspects list is 1.0 to 1.5x
        Assert.InRange(rows.Single(r => r.Id == "woodcutting").Ratio, 1.0, 1.5);
        Assert.InRange(rows.Single(r => r.Id == "mining_iron").Ratio, 1.0, 1.5);       // at the composite rate of the dressed ore (109 kg a worker-day)
        Assert.InRange(rows.Single(r => r.Id == "clay_digging").Ratio, 1.0, 1.5);
        // the R1 prices (economy-audit 4.8) that would put each at 1.25 x its wage: price = 150 x 1.25 / kg a day
        output.WriteLine("R1 price for 1.25x wage (mites a kg): " + string.Join(", ", rows.Select(r => $"{r.Good} {150 * 1.25 / r.Kg:0.00}")));
    }

    [Fact]
    public void EA_F1_RawExtraction_EarnsBetween1And1p5TimesItsWage()
    {
        var bad = RawRatios().Where(r => r.Ratio is < 1.0 or > 1.5).Select(r => $"{r.Id} {r.Ratio:0.0}x").ToList();
        Assert.True(bad.Count == 0, string.Join(", ", bad));
    }

    // ------------------------------------------------------------------ S-2 / EA-10: silver yield

    [Fact]
    public void EA10_S2_SilverYield_PerWorkerDayAndAgainstTheMoneySupply()
    {
        var cat = EconFixture.Catalog();
        var mine = Db.Get<ExtractionDef>("mining_silver"); Assert.Equal("galena", mine.OutputGood);
        double galenaKg = mine.RateGPerWorkerDay / 1000.0;
        var smelt = Db.Get<CraftRecipeDef>("smelt_galena"); var cupel = Db.Get<CraftRecipeDef>("cupel_silver");
        // smelt_galena: 1,000 kg galena -> 600 kg argentiferous lead; cupel_silver: 600 kg of it -> 0.15 kg silver (P7-EA3c) and 540 kg lead
        double silverKg = galenaKg * 0.6 / 600 * 0.15, silverPrice = cat.Def(cat.IdOf("silver")).BasePriceMitesPerKg, perDay = silverKg * silverPrice;
        const double MoneySupplyMites = 128_340_000;          // seed 1234 at the tag (AuditFacts: 1.2834 million crowns)
        double perYearCrewDays = MoneySupplyMites * 0.02 / perDay;
        output.WriteLine($"a silver miner digs {galenaKg:0} kg of galena a day = {silverKg:0.00} kg of silver = {perDay:N0} mites a worker-day ({perDay / 150:0}x the unskilled wage)");
        output.WriteLine($"(the generated crews are small: the world probe measures what two silver mines really made in four years)\n2% of the region's money supply ({MoneySupplyMites * 0.02:N0} mites) is {perYearCrewDays:0.0} worker-days of silver mining a year; a crew of 20 for a 60-day year is {20 * 60 * perDay:N0} mites = {20 * 60 * perDay / MoneySupplyMites:P0} of the supply");
        Assert.InRange(silverKg, 0.07, 0.08);
        Assert.Equal(0.015, AuditMeasures.SilverPerWorkerDay(Db, cat).GradePct, 4);
        Assert.NotNull(smelt); Assert.NotNull(cupel);
    }

    // ------------------------------------------------------------------ X2 / EA-2: the border desk against the book

    [Fact]
    public void EA2_X2_DeskBid_AgainstTheBooksAsk_AtTargetStockAndAtTheGlutThreshold()
    {
        var money = Db.All<MoneySettingsDef>().Single();
        const long Ref = 45_000;                                 // wheat, micro-mites a gram
        output.WriteLine($"half spread {money.SpreadHalfPermille}, toll {money.MarketTollPermille}, desk export discount {ContinentTrade.ExportDiscountPermille} (staples {ContinentTrade.StapleExportDiscountPermille}), import markup {ContinentTrade.ImportMarkupPermille} (staples {ContinentTrade.StapleImportMarkupPermille}), glut above {ContinentTrade.GlutAbovePermilleOfCover}/1000 of cover, short below {ContinentTrade.ShortBelowPermilleOfCover}/1000");
        output.WriteLine("stock (x cover)  mid/ref   ask/ref   desk bid after toll/ref (staple)   desk bid / book ask");
        foreach (var s in new[] { 0.3, 0.6, 1.0, 1.5, 2.0, 2.8, 3.5, 5.0 })
        {
            var curve = new PriceCurve(Ref, 1_000_000, money.PriceClampMinPermille, money.PriceClampMaxPermille);
            long stock = (long)(1_000_000 * s), mid = curve.MidMicro(stock), ask = mid * (1000 + money.SpreadHalfPermille) / 1000;
            double bid = Ref * ContinentTrade.StapleExportDiscountPermille / 1000.0 * (1000 - money.MarketTollPermille) / 1000.0;
            output.WriteLine($"{s,10:0.0}      {mid / (double)Ref,6:0.00}    {ask / (double)Ref,6:0.00}    {bid / Ref,10:0.00}                      {bid / ask,8:0.00}");
        }
        var c1 = new PriceCurve(Ref, 1_000_000, money.PriceClampMinPermille, money.PriceClampMaxPermille);
        long askT = c1.MidMicro(1_000_000) * (1000 + money.SpreadHalfPermille) / 1000, askG = c1.MidMicro(2_800_000) * (1000 + money.SpreadHalfPermille) / 1000;
        double bidNet = Ref * ContinentTrade.ExportDiscountPermille / 1000.0 * (1000 - money.MarketTollPermille) / 1000.0;
        Assert.True(bidNet < askT, "at target stock the desk pays less than the book asks: no loop");
        Assert.True(bidNet > askG, "at the glut threshold (where the desk acts) the desk pays more than the book asks");
    }

    // ------------------------------------------------------------------ L1 / EA-7 and L2 / EA-8: the grep tests

    private static string Root()
    {
        string? d = AppContext.BaseDirectory; while (d != null && !File.Exists(Path.Combine(d, "Ravensguard.slnx"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("repository root not found");
    }

    private static IEnumerable<string> SourceFiles(string sub) =>
        Directory.EnumerateFiles(Path.Combine(Root(), sub), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("\\obj\\") && !f.Contains("/bin/") && !f.Contains("\\bin\\"));

    /// <summary>
    /// The documented list of every <c>Ledger.Mint</c> call site (economy-audit 4.4 L1; the same list is in docs/reviews/phase-7-audit.md): file (under src/Ravensguard.Sim unless it begins Game/), number of calls, why it is a source and
    /// where the matching sink is (decision I33: every faucet has a counterpart, or is the one-off seed).
    /// </summary>
    public static readonly (string File, int Calls, string Reason)[] MintSites =
    [
        ("Sim/Economy/EconomyState.cs", 1, "Seed: the starting purse of a new owner (world creation)"),
        ("Sim/Economy/MoneySeeder.cs", 1, "Seed: the region's seed money, once (decision I33)"),
        ("Sim/Economy/MintMath.cs", 1, "Mint: struck silver (no caller in the world yet, S-3; CN1 wires it from mined metal)"),
        ("Sim/Worlds/RegionTransfer.cs", 1, "CoinIn: a household crossing a region border; the other ledger's Sink(Emigrate) is the counterpart"),
        ("Sim/Caravans/CaravanStep.cs", 1, "Immigrate: the continent pays a caravan for exports, within the neighbour's season budget (PayBudgetMites); the Emigrate sinks on its costs and tolls are the other side"),
        ("Sim/Caravans/ContinentDesk.cs", 1, "Immigrate: the border desk pays for a glut it carries off, within PayBudgetMites"),
        ("Sim/People/Vital/Demography.cs", 1, "Immigrate: an immigrant household's stake"),
        ("Sim/Diagnostics/Phase6Trail.cs", 1, "Seed: the Phase 6 trail's fixture"),
        ("Sim/Trade/AbroadDealing.cs", 2, "CoinIn: the hero's dealing abroad, the same coin leaves by CoinOut (Sink Emigrate) on the other call"),
        ("Sim/Hero/StartingEstate.cs", 1, "Seed: the hero's estate at the start"),
        ("Sim/Economy/Mints.cs", 2, "Mint: the mint strikes coin for the gold or silver delivered (into the seller's purse) and token copper at face (P7-CN1); the metal is destroyed as Materialised"),
        ("Game/UI/CraftingSession.Full.cs", 1, "Seed: the dev hamlet's smith (a dev scene, never in a world)"),
        ("Game/World/StreetView.Business.cs", 1, "Seed: the dev faucet of the screenshot scene (DevBusinesses)"),
        ("Game/World/StreetView.Industry.cs", 1, "Seed: the dev faucet of the Industry tab's screenshot scene (P7-IN3)"),
        ("Game/World/StreetView.Contracts.cs", 1, "Seed: a dev command that stocks a stage and its coin"),
        ("Game/World/StreetView.BuildMode.cs", 1, "Seed: a dev command that buys the nearest lot"),
    ];

    [Fact]
    public void EA7_L1_EveryLedgerMintCallSite_IsOnTheDocumentedList()
    {
        var found = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (sub, prefix) in new[] { ("src/Ravensguard.Sim", "Sim/"), ("src/Ravensguard.Game", "Game/") })
            foreach (var f in SourceFiles(sub))
            {
                int calls = File.ReadLines(f).Count(l => Regex.IsMatch(l, @"\.Mint\(") && !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///") && !Regex.IsMatch(l, @"\bpublic\b"));
                if (calls > 0) found[prefix + Path.GetRelativePath(Path.Combine(Root(), sub), f).Replace('\\', '/')] = calls;
            }
        foreach (var kv in found) output.WriteLine($"{kv.Key} x{kv.Value}");
        var documented = MintSites.ToDictionary(x => x.File, x => x.Calls);
        Assert.Equal(documented.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}"), found.Select(k => $"{k.Key} {k.Value}"));
    }

    [Fact]
    public void EA8_L2_NoBurnSink_OutsideTestsAndTheDiagnosticsReader()
    {
        var hits = new List<string>();
        foreach (var sub in new[] { "src/Ravensguard.Sim", "src/Ravensguard.Game" })
            foreach (var f in SourceFiles(sub))
                foreach (var (l, i) in File.ReadLines(f).Select((l, i) => (l, i)))
                    if (l.Contains("SinkReason.Burn") && !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///") && !f.Contains("Diagnostics") && !f.Contains("LedgerTypes") && !f.EndsWith("Ledger.cs")) hits.Add($"{Path.GetFileName(f)}:{i + 1}");
        output.WriteLine(hits.Count == 0 ? "no SinkReason.Burn caller outside Ledger, its enum and the Diagnostics readers" : string.Join(", ", hits));
        Assert.Empty(hits);
    }

    [Fact]
    public void EA_X14_StandingPriceMites_HasNoCallerInTheMarketBook()
    {
        var callers = SourceFiles("src/Ravensguard.Sim").Where(f => !f.EndsWith("Standing.cs") && !f.Contains("Diagnostics")).Where(f => File.ReadAllText(f).Contains("Standing.PriceMites")).Select(f => Path.GetFileName(f)).ToList();
        output.WriteLine(callers.Count == 0 ? "Standing.PriceMites is called by nothing: the rebate is unwired, so no coin can come from it (X14 passes by being absent)" : string.Join(", ", callers));
        Assert.Empty(callers);
    }

    // ------------------------------------------------------------------ the prices the R1 rule and the industries sheets name (overview 15)

    [Fact]
    public void R1_Candidates_RopeAndTheRawGoodsAgainstTheirLabour()
    {
        var cat = EconFixture.Catalog(); double P(string g) => cat.Def(cat.IdOf(g)).BasePriceMitesPerKg;
        output.WriteLine($"rope {P("rope")} mites a kg = {P("rope") / P("iron_bar"):0.0}x bar iron ({P("iron_bar"):0.0}); linen cloth {P("cloth_linen"):0.0}; twine {P("twine"):0.0}");
        // EA3a: rope (EA_F6) is its twine and forty minutes of braiding, not 2,000 a kg; and the R1 table of economy-audit 4.8 is in the content
        Assert.Equal(298, P("rope"), 1);
        Assert.True(P("rope") < 0.7 * P("iron_bar"));
        foreach (var (g, want) in new (string, double)[] { ("logs", 0.2), ("firewood", 0.3), ("clay", 0.1), ("iron_ore_rough", 0.6), ("iron_ore", 1.8), ("charcoal", 12), ("iron_bloom", 240), ("iron_bar", 480), ("timber_planks", 3.0), ("bricks", 1.2) })
            Assert.Equal(want, P(g), 3);
    }
}
