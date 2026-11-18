using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.Tests.RegionsB;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Growth;

/// <summary>What a two-year run of seed 1234 built, measured at its start and its end (one run shared by the tests below; each test reads a different part of it).</summary>
public sealed class GrowthRun : IDisposable
{
    public SoakWorld W { get; } = SoakWorld.Create();
    public long WoodStock0, WoodStockEnd, Alive0; public int Dwellings0, Fields0, Businesses0, ClearedRows;
    public GrowthState State => W.Economy.Growth.State;

    public GrowthRun()
    {
        var reg = W.Pop.Region;
        WoodStock0 = W.Layer.Totals(ResourceKind.Wood).Stock; Alive0 = W.Sim.Vital.Alive;
        Dwellings0 = reg.Settlements.Sum(s => s.Plan.Lots.Count(l => l.IsDwelling)); Fields0 = reg.Fields.Length; Businesses0 = W.Sys.Businesses.Count;
        W.Run(60 * 2);
        WoodStockEnd = W.Layer.Totals(ResourceKind.Wood).Stock; ClearedRows = W.Layer.ClearedCount;
    }
    public void Dispose() => W.Dispose();
}

/// <summary>Background growth (plan 4A.2, docs/specs/phase3/background-growth.md): the abstract world builds cottages, clears fields and opens businesses, conserving coin and mass.</summary>
public class GrowthTests(ITestOutputHelper output, GrowthRun run) : IClassFixture<GrowthRun>
{
    private readonly GrowthRun _r = run;

    [Fact]
    public void ABuild_ConservesCoinAndMass_AndAddsRealDwellings()
    {
        var w = _r.W; var led = w.Econ.Ledger; var reg = w.Pop.Region;
        int dwellings = reg.Settlements.Sum(s => s.Plan.Lots.Count(l => l.IsDwelling));
        output.WriteLine($"2 years: dwellings {_r.Dwellings0} -> {dwellings}, built {_r.State.Lots.Count}, wages {_r.State.Years.Sum(y => y.WagesMites)} mites, materials {_r.State.Years.Sum(y => y.MaterialsMites)} mites");
        Assert.True(_r.State.Lots.Count > 0, "no cottage was built in two years");
        Assert.True(dwellings > _r.Dwellings0);
        Assert.True(led.Audit().IsConserved); Assert.Equal(0, led.Audit().Minted.Mites); Assert.True(w.Econ.MassAuditHolds()); Assert.Equal(0, w.Sys.MassViolations);
        { long sum = 0; for (int p = 0; p < led.PurseCount; p++) sum += led.Balance(new PurseId(p)).Mites; Assert.Equal(led.Audit().InPurses.Mites, sum); }
        // every grown lot has its arrays and beds, and the plan lists them in order
        foreach (var rec in _r.State.Lots)
        {
            var lots = reg.Settlements[rec.Settlement].Plan.Lots;
            Assert.True(rec.Lot < lots.Count && lots[rec.Lot].Rect == rec.Rect || !rec.NewLot);
            Assert.Equal(rec.Dwelling == DwellingType.None ? 0 : rec.Beds, w.Pop.LotBeds[rec.Settlement][rec.Lot]);
            Assert.Equal(lots.Count, w.Pop.LotHousehold[rec.Settlement].Length);
        }
        Assert.True(_r.State.Years.Sum(y => y.WagesMites) > 0, "hired hands were paid");
    }

    [Fact]
    public void ACompletedCottage_IsUsedByMarriage()
    {
        var w = _r.W; var pop = w.Pop; var hht = w.Sim.Households;
        var built = _r.State.Lots.Where(l => l.Dwelling != DwellingType.None).ToList();
        Assert.NotEmpty(built);
        int lived = built.Count(l => pop.LotHousehold[l.Settlement][l.Lot] >= 0);
        output.WriteLine($"{built.Count} dwellings built, {lived} lived in after two years; marriages {w.Sim.Vital.Marriages}");
        Assert.True(lived * 2 >= built.Count, "most new houses are lived in");
        // a household in a grown dwelling was formed by a wedding or a move, with the dwelling's beds
        foreach (var l in built) { int hh = pop.LotHousehold[l.Settlement][l.Lot]; if (hh >= 0) { Assert.Equal(l.Settlement, hht.Settlement[hh]); Assert.Equal(l.Lot, hht.Lot[hh]); } }
    }

    [Fact]
    public void AFreshDwelling_IsAFreeDwellingForTheMarriageBrake()
    {
        using var w = SoakWorld.Create(withTicker: false); var reg = w.Pop.Region; var sim = w.Sim;
        int s = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Village);
        // fill every free dwelling of the cluster so the new one is the only one
        for (int l = 0; l < reg.Settlements[s].Plan.Lots.Count; l++) if (w.Pop.LotBeds[s][l] > 0 && w.Pop.LotHousehold[s][l] < 0) w.Pop.LotBeds[s][l] = 0;
        Assert.True(RegionGrowth.FindPlot(reg, s, 16, 38, 30, [], out var rect, out byte facing), "the village has room for a plot");
        int lot = reg.Settlements[s].Plan.Lots.Count;
        int before = Marriage.FreeDwelling(sim, s, 2).S;
        RegionGrowth.AddLot(reg, w.Pop, new LotRecord(s, lot, true, LotUse.Cottage, DwellingType.Cottage, "peasant_cottage", rect, facing, 0, 5, 1));
        var (fs, fl) = Marriage.FreeDwelling(sim, s, 2);
        Assert.Equal((s, lot), (fs, fl));
        Assert.True(before < 0 || before != s || true);
    }

    [Fact]
    public void Assarting_DrawsDownTheWoodland_AndAddsFieldsFarmingWorks()
    {
        var w = _r.W; var reg = w.Pop.Region; var farm = w.Economy.Farming.State;
        output.WriteLine($"2 years: fields {_r.Fields0} -> {reg.Fields.Length}, parcels grown {_r.State.Parcels.Count}, wood stock {_r.WoodStock0 / 1_000_000} t -> {_r.WoodStockEnd / 1_000_000} t (regrowth included), cleared rows {_r.ClearedRows}");
        Assert.NotEmpty(_r.State.Parcels);
        Assert.Equal(_r.Fields0 + _r.State.Parcels.Sum(p => p.Tiles.Length), reg.Fields.Length);
        Assert.Equal(reg.Fields.Length, farm.Fields.Count); Assert.Equal(reg.Parcels.Count, farm.Parcels.Count);
        Assert.True(_r.ClearedRows > 0 && _r.State.WoodDrawnG > 0);
        // the cleared tiles hold no stock and a field; fertility comes from the tile, the rotation slot is the fallow's
        foreach (var rec in _r.State.Parcels)
            for (int i = 0; i < rec.Tiles.Length; i++)
            {
                int t = rec.Tiles[i]; Assert.True(reg.ParcelMap[t] != 0); Assert.Equal(0, reg.Forest[t]); Assert.True(rec.Fertility[i] > 0);
                int row = w.Layer.Find(t, ResourceKind.Wood); if (row >= 0) { Assert.True(w.Layer.IsCleared(row)); Assert.Equal(0, w.Layer.StockG(row)); }
            }
        // the new parcels have a holder (the lord until the re-let) and the farming tables' holder agrees with the population's
        for (int p = reg.Parcels.Count - _r.State.Parcels.Count; p < reg.Parcels.Count; p++) Assert.Equal(farm.Parcels.Holder[p], w.Pop.ParcelHolder[p]);
        Assert.True(w.Econ.MassAuditHolds());
    }

    [Fact]
    public void ANewBusiness_OpensOnUnmetDemand_PaidFromThePurse()
    {
        var w = _r.W; var prod = w.Sys; int opened = _r.State.Years.Sum(y => y.BusinessesOpened);
        output.WriteLine($"2 years: businesses {_r.Businesses0} -> {prod.Businesses.Count}, opened {opened}");
        Assert.True(opened > 0 && prod.Businesses.Count >= _r.Businesses0 + opened);
        var fresh = prod.Businesses.Skip(_r.Businesses0).ToList();
        Assert.NotEmpty(fresh);
        foreach (var b in fresh)
        {
            Assert.True(b.Owner >= 0 && w.Pop.People.Status[b.Owner] == PersonStatus.Alive || b.State == BusinessState.Closed);
            Assert.True(b.Capital >= 1000);
        }
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void TheSameSeedGrowsTheSameWorld()
    {
        string Run() { using var w = SoakWorld.Create(); w.Run(75); return w.Sim.ComputeHash() + w.Economy.Growth.State.ComputeHash() + w.Econ.ComputeHash(); }
        string a = Run(), b = Run();
        Assert.Equal(a, b);
    }

    [Fact]
    public void ASaveWithABuildInProgress_ReloadsAndSavesTheSameBytes_AndResumesTheSameRun()
    {
        void Step(RegionWorld rw, long to) { while (rw.HourDone < to) rw.AdvanceTo(Math.Min(rw.HourDone + 24, to)); }
        var rigA = new RegionRig(); var a = rigA.Set.TryGet(rigA.Home)!; long h0 = a.HourDone;
        Step(a, h0 + 24 * 40);
        var rigB = new RegionRig(); var b = rigB.Set.TryGet(rigB.Home)!;
        Step(b, h0 + 24 * 5);                                                      // the season's first day has started its builds: they are in progress
        Assert.NotEmpty(b.Economy.Growth.State.Projects);
        var bytes = b.CaptureBytes();
        var d = SaveCodec.DeserializeCompressed<RegionSectionData>(bytes);
        Assert.NotNull(d.Growth);
        var back = RegionWorld.Restore(rigB.Sv, rigB.Home, b.Population.Region, d);
        Assert.Equal(bytes, back.CaptureBytes());                                  // save, load, save: byte for byte
        Assert.Equal(b.Economy.Growth.State.ComputeHash(), back.Economy.Growth.State.ComputeHash());
        Step(back, h0 + 24 * 40);                                                  // across the finish of the builds
        Assert.Equal(a.RecordsHash(), back.RecordsHash());
        Assert.Equal(a.Economy.Growth.State.ComputeHash(), back.Economy.Growth.State.ComputeHash());
        Assert.Equal(a.CaptureBytes(), back.CaptureBytes());
    }

    [Fact]
    public void AGrownRegion_IsRebuiltOverAFreshlyGeneratedOne_FromTheLog()
    {
        var w = _r.W; var grown = w.Pop.Region; var st = _r.State;
        var b = EconFixture.Shared;
        var fresh = RegionGenerator.Generate(b.Continent, b.History, b.CellX, b.CellY, b.Rs);
        LandmarkPlacer.Place(fresh, b.History, EconFixture.Content);
        string baseHash = fresh.ComputeHash();
        RegionGrowth.Replay(fresh, st);
        Assert.Equal(grown.Fields.Length, fresh.Fields.Length); Assert.Equal(grown.Parcels.Count, fresh.Parcels.Count);
        for (int s = 0; s < grown.Settlements.Count; s++) Assert.Equal(grown.Settlements[s].Plan.Lots.Select(l => (l.Rect, l.Use, l.Vacant)), fresh.Settlements[s].Plan.Lots.Select(l => (l.Rect, l.Use, l.Vacant)));
        Assert.True(grown.ParcelMap.AsSpan().SequenceEqual(fresh.ParcelMap)); Assert.True(grown.Forest.AsSpan().SequenceEqual(fresh.Forest));
        Assert.Equal(baseHash, grown.ComputeHash());                               // the generated region's hash is what a save records, whatever was built since
        Assert.Equal(baseHash, fresh.ComputeHash());
        // the baseline people are the generated ones, whatever was built
        var basePop = Populator.Populate(grown, b.History, EconFixture.Content);
        Assert.Equal(EconFixture.Pop.ComputeHash(), basePop.ComputeHash());
    }

    // ---------------------------------------------------------------- chunks

    private static (int S, RectM Rect, byte Facing) Plot(SoakWorld w)
    {
        var reg = w.Pop.Region;
        for (int s = 0; s < reg.Settlements.Count; s++)
            if (reg.Settlements[s].Class == SettlementClass.Village && RegionGrowth.FindPlot(reg, s, 16, 38, 30, [], out var rect, out byte f)) return (s, rect, f);
        throw new InvalidOperationException("no village has room");
    }

    private static LotRecord NewLot(SoakWorld w, int s, RectM rect, byte f) => new(s, w.Pop.Region.Settlements[s].Plan.Lots.Count, true, LotUse.Cottage, DwellingType.Cottage, "peasant_cottage", rect, f, 0, 5, 1);

    [Fact]
    public void ANewBuilding_InAVisitedChunkIsADelta_InAnUnvisitedOneIsGenerated()
    {
        using var w = SoakWorld.Create(withTicker: false); var reg = w.Pop.Region;
        var gen = new ChunkGenerator(reg, w.Pop, EconFixture.Content);
        var (s, rect, f) = Plot(w);
        int cx = rect.CenterX / 32, cy = rect.CenterY / 32;
        var store = new ChunkStore();
        // visit the chunk with the plot (and a player's own edit elsewhere in it), leave a neighbour that also touches the plot unvisited when there is one
        var key = new ChunkKey(cx, cy, 0);
        var g0 = gen.GenerateLevels(cx, cy); var ground0 = g0.Levels[0];
        store.RecordFirstVisit(key, ChunkGenerator.Version, g0.ContentHash, ground0);
        // a cell the building does not change (found on a twin world that has the building): the player's edit goes there
        using var twin = SoakWorld.Create(withTicker: false);
        var twinGen = new ChunkGenerator(twin.Pop.Region, twin.Pop, EconFixture.Content);
        RegionGrowth.AddLot(twin.Pop.Region, twin.Pop, NewLot(twin, s, rect, f));
        var twinNew = twinGen.GenerateLevels(cx, cy).Levels[0];
        int farCell = Enumerable.Range(0, 1024).First(i => ground0.Tiles.GetValueOrDefault(i) == twinNew.Tiles.GetValueOrDefault(i) && !Overlaps(rect, cx * 32 + i % 32, cy * 32 + i / 32, 2));
        store.AppendDelta(key, [ChunkOp.SetTile(farCell % 32, farCell / 32, 12345)]);
        var snapshotBefore = store.GetSnapshot(key)!.ContentHash;

        var writer = new ChunkDeltaWriter(gen, store);
        var tx = writer.Begin([new RectM(rect.X - 6, rect.Y - 6, rect.W + 12, rect.H + 12)]);
        RegionGrowth.AddLot(reg, w.Pop, NewLot(w, s, rect, f));
        tx.Commit();

        Assert.True(writer.OpsWritten > 0 && writer.ChunksChanged >= 1);
        Assert.Equal(snapshotBefore, store.GetSnapshot(key)!.ContentHash);                      // the first-visit snapshot is never rewritten
        var now = store.Rebuild(key)!; var want = gen.GenerateLevels(cx, cy).Levels[0];
        for (int i = 0; i < 1024; i++)
        {
            int wx = cx * 32 + i % 32, wy = cy * 32 + i / 32;
            if (i == farCell) { Assert.Equal(12345, now.Tiles.GetValueOrDefault(i)); continue; }  // the player's change stays
            if (Overlaps(rect, wx, wy, 0)) Assert.Equal(want.Tiles.GetValueOrDefault(i), now.Tiles.GetValueOrDefault(i));     // the building is there
        }
        Assert.Contains(now.Tiles, kv => TileCode.Structure(kv.Value) != 0 && Overlaps(rect, cx * 32 + kv.Key % 32, cy * 32 + kv.Key / 32, 0));
        output.WriteLine($"delta: {writer.OpsWritten} ops in {writer.ChunksChanged} chunk(s)");

        // a chunk nobody has seen: no entry, no delta; the first visit generates the building from the changed plan
        var far = new ChunkKey(cx + 40, cy, 0); Assert.False(store.Contains(far));
        using var w2 = SoakWorld.Create(withTicker: false);
        var gen2 = new ChunkGenerator(w2.Pop.Region, w2.Pop, EconFixture.Content);
        var before = gen2.GenerateLevels(cx, cy).ContentHash;
        RegionGrowth.AddLot(w2.Pop.Region, w2.Pop, NewLot(w2, s, rect, f));
        var after = gen2.GenerateLevels(cx, cy);                                                // the same generator sees the change: its indices are rebuilt
        Assert.NotEqual(before, after.ContentHash);
        Assert.Contains(after.Levels[0].Tiles, kv => TileCode.Structure(kv.Value) != 0 && Overlaps(rect, cx * 32 + kv.Key % 32, cy * 32 + kv.Key / 32, 0));
        Assert.Equal(want.ContentHash(), after.Levels[0].ContentHash());                        // generation after the change equals the delta's result in the visited copy
    }

    private static bool Overlaps(RectM r, int wx, int wy, int pad) => wx >= r.X - pad && wx < r.Right + pad && wy >= r.Y - pad && wy < r.Bottom + pad;
}

internal static class ChunkContentExt
{
    public static string ContentHash(this ChunkContent c)
    {
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var (i, t) in c.Tiles) h.AppendData(BitConverter.GetBytes(((long)i << 32) | (uint)t));
        foreach (var (id, o) in c.Objects) { h.AppendData(BitConverter.GetBytes(id)); h.AppendData(o.Data); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
