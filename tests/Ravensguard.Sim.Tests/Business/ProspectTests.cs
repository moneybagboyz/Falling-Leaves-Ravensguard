using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Prospecting;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Business;

/// <summary>P5-F: prospecting and extraction businesses, gate PX-1 to PX-8 (docs/specs/phase5/prospecting-and-extraction.md 9).</summary>
public class ProspectTests(ITestOutputHelper output)
{
    private static readonly Ravensguard.Content.ProspectRulesDef Rules = OwnRig.Content.All<Ravensguard.Content.ProspectRulesDef>().Single();

    [Fact]
    public void PX1_Estimates_converge_on_the_truth_as_skill_rises()
    {
        double Mae(int skill)
        {
            long sum = 0; long truth = 7_500_000_000;
            for (int i = 0; i < 500; i++)
            {
                long est = Prospector.Estimate(99, EntityId.Make(IdKind.Person, (ulong)(i + 1)), 1000 + i * 37, ProspectMethod.TrialShaft, 20 + i % 30, ResourceCode.Ore(MineralType.Iron), truth, Prospector.SpreadPm(ProspectMethod.TrialShaft, skill, Rules));
                sum += Math.Abs(est - truth) * 1000 / truth;
            }
            return sum / 500.0;
        }
        double lo = Mae(20), hi = Mae(90);
        output.WriteLine($"mean absolute error: skill 20 {lo:0.0} pm, skill 90 {hi:0.0} pm");
        Assert.True(hi < lo / 3, $"{hi} vs {lo}");
        Assert.Equal(300, Prospector.SpreadPm(ProspectMethod.TrialShaft, 50, Rules));
        Assert.Equal(100, Prospector.SpreadPm(ProspectMethod.TrialShaft, 90, Rules));
    }

    private static string Truth(OwnRig r)
    {
        var h = new System.Text.StringBuilder(r.W.Layer.ComputeHash());
        foreach (var m in r.W.Sys.Mines) h.Append('|').Append(m.RemainingG);
        foreach (var v in r.W.Pop.Region.Veins) h.Append('|').Append(v.Tonnage);
        return h.ToString();
    }

    [Fact]
    public void PX2_A_survey_never_changes_the_truth()
    {
        using var r = OwnRig.Make(crowns: 100, ticker: false);
        string before = Truth(r); string econ = r.W.Econ.ComputeHash();
        var truth = r.O.Truth; var hero = r.Hero; int n = 0;
        for (int i = 0; i < 1000; i++)
        {
            int tile = (i * 7919 + 13) % RegionData.Tiles; int day = i % 60;
            n += Prospector.Walk(truth, Rules, 1, hero, tile, 40, day).Count;
            n += Prospector.TestPit(truth, Rules, 1, hero, tile, 40, day).Count;
            n += Prospector.TrialShaft(truth, Rules, 1, hero, tile, 40, day).Count;
            n += Prospector.ReadLand(truth, Rules, 1, hero, tile, 40, day).Rows.Count;
            n += Prospector.Pan(truth, Rules, tile).Length;
        }
        foreach (var v in r.W.Pop.Region.Veins.Take(20)) n += Prospector.TrialShaft(truth, Rules, 1, hero, RegionData.Index(v.TileX, v.TileY), 60, 3).Count;
        output.WriteLine($"{n} findings over 1,000 surveys");
        Assert.True(n > 0);
        Assert.Equal(before, Truth(r)); Assert.Equal(econ, r.W.Econ.ComputeHash());
    }

    [Fact]
    public void PX3_Panning_names_only_minerals_that_are_upstream()
    {
        using var r = OwnRig.Make(crowns: 100, ticker: false);
        var truth = r.O.Truth; int streams = 0, named = 0;
        for (int t = 0; t < RegionData.Tiles && streams < 300; t += 97)
        {
            if (!truth.IsStream(t)) continue;
            streams++;
            var up = truth.Upstream(t, Rules.PanUpstreamTiles).Select(u => r.W.Pop.Region.Mineral[u] - 1).Where(m => m >= 0).ToHashSet();
            foreach (int m in Prospector.Pan(truth, Rules, t)) { named++; Assert.Contains(m, up); }
        }
        output.WriteLine($"{streams} streams panned, {named} minerals named");
        Assert.True(streams > 10);
        Assert.Empty(Prospector.Pan(truth, Rules, Enumerable.Range(0, RegionData.Tiles).First(t => !truth.IsStream(t))));
    }

    /// <summary>An ore tile with a right holder, the lowest vein index first.</summary>
    private static int OreTile(OwnRig r, out OreBody body)
    {
        foreach (var v in r.W.Pop.Region.Veins)
        {
            if (!ProspectTruth.IsOre(v.Type) || v.Type is MineralType.Silver or MineralType.Gold) continue;
            int t = RegionData.Index(v.TileX, v.TileY);
            if (r.O.Truth.OreAt(t) is { } b && b.Mineral == v.Type && r.O.RightHolder(t, v.Type) != null && !r.O.Book.Holdings.Values.Any(h => h.Vein == b.Vein)) { body = b; return t; }
        }
        throw new InvalidOperationException("no ore");
    }

    [Fact]
    public void PX4_PX6_An_unlicensed_mine_is_recorded_a_licensed_one_pays_royalty_and_the_body_depletes()
    {
        using var r = OwnRig.Make(crowns: 3000, ticker: false);
        var chron = new List<string>();
        r.O.Chron = (when, type, subjects, causes, data) => { chron.Add(type); return default; };
        int tile = OreTile(r, out var body);
        int x = tile % RegionData.Size * RegionData.TileM + 10, y = tile / RegionData.Size * RegionData.TileM + 10;
        Assert.False(r.O.FoundExtraction("mine", x, y, x + 20, y + 20, 2000, r.Day).Ok);           // not surveyed
        foreach (var row in Prospector.TrialShaft(r.O.Truth, Rules, 1, r.Hero, tile, 60, r.Day)) r.O.Book.Prospects.Learn(row);
        var res = r.O.FoundExtraction("mine", x, y, x + 20, y + 20, 2000, r.Day);
        Assert.True(res.Ok, res.Key);
        var b = r.W.Sys.ById(res.Id)!; var h = r.O.Book.Of(b.Id)!;
        Assert.True(h.Unlicensed); Assert.Contains("unlicensed_mine", chron);
        Assert.Equal(body.Vein, h.Vein); Assert.True(b.Mine >= 0);
        // the licence: the fee to the right holder, then 10% of the value mined by the ledger
        var gh = r.O.RightHolder(tile, body.Mineral)!.Value; var gp = r.O.GrantorPurse(gh.Kind, gh.Index);
        long g0 = r.Led.Balance(gp).Mites, coin = r.Coin;
        var lic = r.O.PetitionLicence(tile, r.Day);
        Assert.True(lic.Ok, lic.Key);
        Assert.Equal(coin - lic.Mites, r.Coin); Assert.Equal(g0 + lic.Mites, r.Led.Balance(gp).Mites);
        Assert.False(h.Unlicensed); Assert.Equal(r.O.Book.Licences.Single().Serial, h.Licence);
        foreach (int p in r.Idle(b.Settlement).Take(4)) Assert.True(r.O.Hire(b.Id, p, 160, r.Day).Ok);
        var mw = r.W.Sys.Mines[b.Mine];
        long remain0 = mw.RemainingG, g1 = r.Led.Balance(gp).Mites;
        r.W.Sys.WorkHour(GameTime.FromGameHours((long)r.Day * 24 + 6));
        long mined = remain0 - mw.RemainingG;
        Assert.True(mined > 0);
        var ore = r.W.Econ.Goods.IdOf(MineWorks.OreGood(body.Mineral)!);
        Assert.Equal(mined, b.Store.Qty(ore));
        var book = r.W.Markets.BookForSettlement(b.Settlement);
        long royalty = (long)((Int128)mined * book.MidMicro(ore) / 1_000_000) * 100 / 1000;
        Assert.Equal(g1 + royalty, r.Led.Balance(gp).Mites); Assert.Equal(royalty, h.RoyaltyPaid);
        Assert.True(r.CoinExact());
        // PX-6: the body runs low and output falls with it, at the MineWorks rates (0.5 t a miner-day, never more than is left)
        var k = r.W.Sys.Content.Extractions.First(e => e.IsMine && e.OutGood == ore);
        mw.RemainingG = 300_000;
        Assert.Equal(300_000, mw.DayYieldG(k, 4, -1));
        r.W.Sys.WorkHour(GameTime.FromGameHours((long)(r.Day + 1) * 24 + 6));
        Assert.Equal(0, mw.RemainingG);
        Assert.Equal(0, mw.DayYieldG(k, 4, -1));
        Assert.Equal(mined + 300_000, b.Store.Qty(ore));
    }

    [Fact]
    public void PX5_An_extraction_business_is_refused_off_its_resource_and_works_at_the_extraction_rates_on_it()
    {
        using var r = OwnRig.Make(crowns: 3000, ticker: false);
        var layer = r.W.Layer; var truth = r.O.Truth;
        int stone = Enumerable.Range(0, RegionData.Tiles).First(t => truth.SurfaceG(t, ResourceKind.Stone) > 0 && r.O.NearestSettlement(t) >= 0);
        int bare = Enumerable.Range(0, RegionData.Tiles).First(t => truth.SurfaceG(t, ResourceKind.Stone) == 0 && Math.Abs(t % RegionData.Size - stone % RegionData.Size) > 3);
        (int X, int Y) At(int t) => (t % RegionData.Size * RegionData.TileM + 20, t / RegionData.Size * RegionData.TileM + 20);
        foreach (var row in Prospector.TestPit(truth, Rules, 1, r.Hero, stone, 50, r.Day)) r.O.Book.Prospects.Learn(row);
        var (bx, by) = At(bare);
        var off = r.O.FoundExtraction("quarry", bx, by, bx + 10, by + 10, 1000, r.Day);
        Assert.False(off.Ok); Assert.Equal("px_off_resource", off.Key);
        var (sx, sy) = At(stone);
        var on = r.O.FoundExtraction("quarry", sx, sy, sx + 10, sy + 10, 1000, r.Day);
        Assert.True(on.Ok, on.Key);
        var b = r.W.Sys.ById(on.Id)!;
        var staff = r.Idle(b.Settlement).Take(3).ToList();
        foreach (int p in staff) Assert.True(r.O.Hire(b.Id, p, 150, r.Day).Ok);
        var now = GameTime.FromGameHours((long)r.Day * 24 + b.Index % 24);
        r.W.Sys.DailySlice(now);
        Assert.Single(b.Jobs); int row0 = b.Jobs[0].Row;
        Assert.Equal(stone, layer.TileOf(row0));                                                  // on its own tile, not the settlement's best
        var k = r.W.Sys.Content.Extractions.First(e => e.Id == "quarrying");
        long stock = layer.StockG(row0), cap = layer.CapG(row0);
        var work = GameTime.FromGameHours((long)(r.Day + 1) * 24 + 8);
        long expect = 0;
        foreach (int p in staff) expect += Extraction.GramsPerWorkerHour(k, work, 1000, r.P.SkillLevelOf(p, k.Skill), stock, cap, ProductionSystem.WorkdayHours);
        r.W.Sys.WorkHour(work);
        Assert.Equal(expect, b.Store.Qty(k.OutGood));
        Assert.Equal(stock - expect, layer.StockG(row0));
    }

    [Fact]
    public void PX7_A_survey_report_carries_its_knowledge_to_whoever_reads_it()
    {
        using var r = OwnRig.Make(crowns: 100, ticker: false);
        int tile = OreTile(r, out _);
        foreach (var row in Prospector.TrialShaft(r.O.Truth, Rules, 1, r.Hero, tile, 70, r.Day)) r.O.Book.Prospects.Learn(row);
        foreach (var row in Prospector.TestPit(r.O.Truth, Rules, 1, r.Hero, tile, 70, r.Day)) r.O.Book.Prospects.Learn(row);
        var w = r.O.WriteReport(tile, r.Sec);
        Assert.True(w.Ok, w.Key);
        var item = w.Id; Assert.True(r.W.Econ.Items.IsLive(item)); Assert.Equal("survey_report", r.W.Econ.Items.Get(item).Shape);
        var reader = EntityId.Make(IdKind.Person, 424_242);
        Assert.Empty(r.O.Book.Prospects.At(reader, tile));
        Assert.True(r.O.ReadReport(item, reader).Ok);
        var mine = r.O.Book.Prospects.At(r.Hero, tile).OrderBy(x => x.Resource).ToList();
        var theirs = r.O.Book.Prospects.At(reader, tile).OrderBy(x => x.Resource).ToList();
        Assert.Equal(mine.Count, theirs.Count);
        for (int i = 0; i < mine.Count; i++) { Assert.Equal(mine[i].EstG, theirs[i].EstG); Assert.Equal(mine[i].ConfidencePm, theirs[i].ConfidencePm); Assert.Equal(mine[i].Resource, theirs[i].Resource); Assert.Equal(ProspectMethod.Report, theirs[i].Method); }
        // knowledge is property: the item can be sold like any other (its owner changes; the report still reads)
        r.W.Econ.Items.Get(item).Owner = reader;
        Assert.True(r.O.ReadReport(item, EntityId.Make(IdKind.Person, 1)).Ok);
    }

    [Fact]
    public void PX8_Save_load_save_is_byte_identical_with_prospect_rows()
    {
        using var r = OwnRig.Make(crowns: 3000, ticker: false);
        int tile = OreTile(r, out _);
        for (int i = 0; i < 40; i++)
        {
            int t = (tile + i * 131) % RegionData.Tiles;
            r.O.Walk(t, r.Sec);
            foreach (var row in Prospector.TestPit(r.O.Truth, Rules, 1, r.Hero, t, 40, r.Day)) r.O.Book.Prospects.Learn(row);
        }
        foreach (var row in Prospector.TrialShaft(r.O.Truth, Rules, 1, r.Hero, tile, 60, r.Day)) r.O.Book.Prospects.Learn(row);
        var (_, hints) = Prospector.ReadLand(r.O.Truth, Rules, 1, r.Hero, tile, 60, r.Day); r.O.Book.Prospects.Hints.AddRange(hints);
        r.O.Book.Prospects.Pans.Add(new PanFinding(r.Hero, tile, [0, 1], r.Day));
        r.O.WriteReport(tile, r.Sec);
        r.O.StartProspect(ProspectMethod.TrialShaft, tile + 1, ["pick"], r.Sec);
        r.O.PetitionLicence(tile, r.Day);
        var b = r.Own("smithy", r.Town(), 500);
        r.O.Orders(b.Id, "recipe=forge_nails,keep_good=nails,keep_qty=500,send_days=7");
        Assert.True(r.O.Book.Prospects.Rows.Count > 40);
        var hash = r.W.Econ.Goods.Hash();
        var a = SaveCodec.SerializeCompressed(OwnershipSave.Capture(r.O.Book, hash));
        var back = SaveCodec.DeserializeCompressed<OwnershipSave>(a).Restore();
        var c = SaveCodec.SerializeCompressed(OwnershipSave.Capture(back, hash));
        Assert.Equal(a, c);
        Assert.Equal(r.O.Book.ComputeHash(), back.ComputeHash());
        output.WriteLine($"ownership section {a.Length} bytes, {r.O.Book.Prospects.Rows.Count} rows");
    }
}
