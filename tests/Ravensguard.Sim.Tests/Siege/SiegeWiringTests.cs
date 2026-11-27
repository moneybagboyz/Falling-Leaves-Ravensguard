using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Armies;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7 wiring of SG-D into AM-E: the walls the hero built are the town's walls, and an AI campaign against them is a real siege (done-when 3).</summary>
public class SiegeWiringTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    private static void BuildRing(ConstructionWorld cw, ChunkStore chunks, int cx, int cy, int r, string piece)
    {
        // the hero was there to build it: the chunks round the ring are visited ground
        var ground = new ChunkContent(); for (int i = 0; i < ChunkContent.Size * ChunkContent.Size; i++) ground.Tiles[i] = TileCode.Make(GroundKind.Dirt, 0, 0, 0);
        for (int kx = ChunkStreamer.FloorDiv(cx - r, ChunkContent.Size); kx <= ChunkStreamer.FloorDiv(cx + r, ChunkContent.Size); kx++)
            for (int ky = ChunkStreamer.FloorDiv(cy - r, ChunkContent.Size); ky <= ChunkStreamer.FloorDiv(cy + r, ChunkContent.Size); ky++) chunks.RecordFirstVisit(new ChunkKey(kx, ky, 0), 1, "h0", ground);
        var cost = cw.Costs[piece];
        var site = new ConstructionSite { Id = ConstructionIds.Make(cw.RegionId, ConstructionIds.SiteSlot, cw.Book.Take()), Region = cw.RegionId, PileX = cx, PileY = cy - r - 2, State = SiteState.Finished };          // the hero's finished works site: a struck wall piece drops its rubble at its pile
        cw.Book.Sites[site.Id] = site;
        for (int i = -r; i <= r; i++)
            foreach (var (x, y) in new[] { (cx + i, cy - r), (cx + i, cy + r), (cx - r, cy + i), (cx + r, cy + i) })
                if (cw.BuiltAt(x, y, 0) == null)
                    cw.Book.Built[ConstructionIds.SlotKey(x, y, 0, GhostSlot.Structure)] = new BuiltCell { X = x, Y = y, Z = 0, Slot = GhostSlot.Structure, Piece = piece, Hp = PieceCosts.BuiltHp(cost, 50), BuiltSkill = 50, Site = site.Id };
        cw.Reindex();
    }

    [Fact]
    public void AnAICampaignAgainstTheHeroTownWhoseWallHeBuilt_IsASiegeOfThatWall()
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 7)); k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        var aw = k.Aw; var sw = k.R.Sim.Get<SiegeWorld>()!; var cw = k.R.Sim.Get<ConstructionWorld>()!;
        Assert.IsType<SiegeHook>(aw.Siege);
        var e = k.Env();
        var ai = aw.CallCampaign(e, Mine, Enemy, aw.MusterPointFor(Mine, Enemy), 400, 400); Assert.NotNull(ai);
        int town = aw.Book.Campaigns[ai!.Id].Objective; Assert.Equal(Enemy, aw.Supply!.RealmOf(town));
        Assert.Null(sw.WallsOf!(town));                                                                  // nothing built yet: no walls, the brain's estimate holds
        var (cx, cy) = aw.Center(town); BuildRing(cw, k.State.Chunks, cx, cy, 12, "wall_stone");
        var walls = sw.WallsOf!(town); Assert.NotNull(walls); Assert.Equal(DefenceClass.Stone, aw.Siege.DefenceOf(aw, town));
        Assert.Equal(cx - 12, walls!.X0); Assert.Equal(cy + 12, walls.Y1);

        SiegeRow? row = null; long start = k.Now;
        for (long h = start + 1; h <= start + 60 * 24 && row == null; h++) { k.To(h); row = sw.Peek?.Sieges.Values.FirstOrDefault(s => s.Place == town); }
        Assert.NotNull(row); output.WriteLine($"siege of place {town} by host {row!.Host} began day {row.StartDay}, {row.MenAtStart} men against {row.GarrisonAtStart}");
        Assert.Equal(ai.Id, row.Host);
        var begun = k.Entries("siege_begun").Single(); Assert.NotEmpty(begun.Causes);                  // rooted in the campaign that marched
        Assert.Equal(row.Entry, begun.Id.Value);
        var led = k.State.Economy!.Ledger; var seeded = led.Sourced(SourceReason.Seed).Mites; var minted = led.Sourced(SourceReason.Mint).Mites;
        for (long h = k.Now + 1; h <= k.Now + 40 * 24 && row.Open; h++) k.To(h);
        output.WriteLine($"seed {seeded}->{led.Sourced(SourceReason.Seed).Mites} mint {minted}->{led.Sourced(SourceReason.Mint).Mites}"); Assert.Equal(seeded, led.Sourced(SourceReason.Seed).Mites);        // no faucet: the train was bought, carted and made, not minted
        Assert.True(k.State.Economy!.MassAuditHolds());                                                  // and the mass of goods is conserved
        Assert.Equal(2, row.Engines.Count);                                                              // two trebuchets from the host's baggage and what its quartermaster bought
        Assert.True(row.BreachDay >= 0, "the AI's engines breached the hero's stone wall (done-when 3)");
        output.WriteLine($"BREACH: siege began day {row.StartDay}, the hero's stone wall breached day {row.BreachDay}: {row.BreachDay - row.StartDay} days to breach; ended {row.State} ({row.Why}) day {row.EndDay}");
        var shown = k.State.Chronicle.All.Where(c => c.TypeId.StartsWith("siege_") || c.TypeId is "place_taken" or "town_sacked" or "wall_breached" or "engine_built").ToList();
        output.WriteLine($"chronicle: {string.Join(", ", shown.Select(c => c.TypeId))}");
        foreach (var en in shown) Assert.True(en.Causes.Count > 0, en.TypeId);
    }

    [Fact]
    public void TheLiveAssaultPort_OpensABattleBubbleWithTheAssaultLayer_OnlyInsideTheHeroBubble()
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 7)); k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        var aw = k.Aw; var sw = k.R.Sim.Get<SiegeWorld>()!; var cw = k.R.Sim.Get<ConstructionWorld>()!;
        var ai = aw.CallCampaign(k.Env(), Mine, Enemy, aw.MusterPointFor(Mine, Enemy), 400, 400); Assert.NotNull(ai);
        int town = aw.Book.Campaigns[ai!.Id].Objective; var (cx, cy) = aw.Center(town); BuildRing(cw, k.State.Chunks, cx, cy, 12, "wall_stone");
        SiegeRow? row = null;
        for (long h = k.Now + 1; h <= k.Now + 60 * 24 && row == null; h++) { k.To(h); row = sw.Peek?.Sieges.Values.FirstOrDefault(s => s.Place == town); }
        Assert.NotNull(row); Assert.NotNull(sw.Assaults); Assert.NotNull(sw.OpenAssault);
        var (mx, my) = aw.CenterMm(town);
        sw.Assaults!.NoteHero(mx / 1000 + 20_000, my / 1000);                                           // the hero is far away: the abstract storm
        Assert.False(sw.OpenAssault!(row!)); Assert.Empty(sw.Assaults.Live);
        sw.Assaults.NoteHero(mx / 1000 + 500, my / 1000);                                               // the hero is at the walls
        Assert.False(sw.Assaults.GameDrivesLiveAssaults); Assert.False(sw.OpenAssault!(row!)); Assert.Empty(sw.Assaults.Live);   // P7R-4: declines (abstract storm) while the game does not drive live assaults
        sw.Assaults.GameDrivesLiveAssaults = true;
        Assert.True(sw.OpenAssault!(row!)); var live = Assert.Single(sw.Assaults.Live).Value;
        Assert.Same(live.Bubble.W, live.Layer.World); Assert.Same(live.Layer, live.Bubble.W.Assault);
        row!.State = SiegeState.Assault; row.LiveSinceHour = k.Now;                                      // what SiegeWorld does when the port answers true
        Assert.False(sw.OpenAssault!(row));                                                             // already open
        Assert.True(sw.Assaults.RunToEnd(k.Env(), row.Id, 20 * 60 * 30));                               // the bubble is stepped to its end and closes the assault
        Assert.NotEqual(SiegeState.Assault, row.State);
        output.WriteLine($"live assault ended: siege state {row.State}, lost {row.LostAttackers}/{row.LostDefenders}");
    }
}
