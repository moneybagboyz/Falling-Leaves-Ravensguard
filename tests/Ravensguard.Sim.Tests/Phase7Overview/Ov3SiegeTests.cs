using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Siege;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// OV-3 (overview 4, "an AI faction besieges and breaches your walls"), on <see cref="SiegeWiringTests"/>' setup: the hero's town has a stone ring he built; an AI campaign (the realm of eleven against the realm of none) invests it, builds
/// two trebuchets from its baggage and what its quartermaster buys (<c>army_bought</c> price entries caused by the campaign), breaches the stone wall, offers terms and ends the siege, all on one chronicle chain from <c>siege_begun</c>; coin and
/// mass stay exact (no faucet). Saved mid-siege the world loads and re-saves byte for byte, and the loaded run reaches the same siege book as the straight one. The live assault is declined (<c>SiegeAssaults.GameDrivesLiveAssaults</c> is false), so the storm
/// through the breach is the abstract rule; the battle bubble over the walk's levels is the AssaultLayer's own gate (<c>AssaultTests</c>). The host is 400 men (the setup's size), not the spec's 1,200.
/// </summary>
public class Ov3SiegeTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    [Fact]
    public void OV3_AnAIHostInvestsTheHeroWalledTown_BuildsTwoTrebuchetsFromGoodsItBuys_BreachesTheWall_TermsAndAnEnd_OnOneChain_SavedMidSiegeByteIdentical()
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 7)); k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        var aw = k.Aw; var sw = k.R.Sim.Get<SiegeWorld>()!; var cw = k.R.Sim.Get<ConstructionWorld>()!;
        Assert.False(sw.Assaults!.GameDrivesLiveAssaults);                                               // the live storm is off: the assault is the abstract rule
        var ai = aw.CallCampaign(k.Env(), Mine, Enemy, aw.MusterPointFor(Mine, Enemy), 400, 400); Assert.NotNull(ai);
        int town = aw.Book.Campaigns[ai!.Id].Objective;
        var (cx, cy) = aw.Center(town); SiegeWiringTests.BuildRing(cw, k.State.Chunks, cx, cy, 12, "wall_stone");           // the hero's wall
        Assert.Equal(DefenceClass.Stone, aw.Siege.DefenceOf(aw, town));

        SiegeRow? row = null;
        for (long h = k.Now + 1; h <= k.Now + 60 * 24 && row == null; h++) { k.To(h); row = sw.Peek?.Sieges.Values.FirstOrDefault(s => s.Place == town); }
        Assert.NotNull(row); Assert.Equal(ai.Id, row!.Host);
        var begun = k.Entries("siege_begun").Single(); Assert.Equal(row.Entry, begun.Id.Value); Assert.NotEmpty(begun.Causes);
        output.WriteLine($"day {row.StartDay}: {row.MenAtStart} men invest place {town} (garrison {row.GarrisonAtStart})");

        // saved mid-siege: the engines are being laid
        var led = k.State.Economy!.Ledger; long seeded = led.Sourced(SourceReason.Seed).Mites;
                Assert.True(row.Open, "still besieging at the save point");
        var (back, bytes) = P7Game.RoundTrip(k.State, k.R.W, "OV-3 mid-siege");
        Assert.NotNull(back.Sieges); Assert.Equal(sw.Peek!.Hash(), back.Sieges!.Hash());
        long saveHour = k.Now; ulong siegeHashAtSave = sw.Peek.Hash();

        for (long h = k.Now + 1; h <= k.Now + 45 * 24 && row.Open; h++) k.To(h);
        output.WriteLine($"breach day {row.BreachDay} (+{row.BreachDay - row.StartDay}), terms {row.TermsCount}, ended {row.State} ({row.Why}) day {row.EndDay}; engines {row.Engines.Count}");
        Assert.Equal(2, row.Engines.Count);                                                              // two trebuchets
        Assert.True(row.BreachDay >= 0, "the hero's stone wall was breached");
        Assert.True(row.TermsCount >= 1 && !row.Open);
        Assert.True(row.State is SiegeState.Surrendered or SiegeState.Stormed or SiegeState.Lifted);
        Assert.Equal(seeded, led.Sourced(SourceReason.Seed).Mites);                                     // no faucet: the train was bought, carted, made
        Assert.True(led.Audit().IsConserved); Assert.True(k.State.Economy!.MassAuditHolds());

        // what the host bought is priced in the chronicle, caused by its campaign
        var bought = k.Entries("army_bought").ToList();
        output.WriteLine($"army_bought entries {bought.Count}; engine_built {k.Entries("engine_built").Count()}; siege_starving {k.Entries("siege_starving").Count()}");
        Assert.NotEmpty(k.Entries("engine_built"));                                                    // (one entry for the first engine standing; both are in the siege row)
        Assert.NotEmpty(bought); Assert.All(bought, b => Assert.NotEmpty(b.Causes));

        // one chain from siege_begun
        var ends = k.State.Chronicle.All.Where(c => c.TypeId is "wall_breached" or "siege_terms" or "siege_surrendered" or "siege_stormed" or "town_sacked" or "siege_assault" or "siege_abandoned" or "place_taken").ToList();
        Assert.Contains(ends, e => e.TypeId == "wall_breached"); Assert.Contains(ends, e => e.TypeId == "siege_terms");
        foreach (var e in ends.Where(e => e.TypeId != "place_taken")) Assert.Contains(P7Game.Chain(k.State, e), x => x.TypeId == "siege_begun");
        foreach (var en in k.State.Chronicle.All.Where(c => c.TypeId.StartsWith("siege_") || c.TypeId is "engine_built" or "wall_breached")) Assert.NotEmpty(en.Causes);

        // the loaded run reaches the same siege book as the straight one (same hour, same hash)
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); st.Content = k.R.Content;
        Assert.Equal(siegeHashAtSave, st.Sieges!.Hash());
        using (var rt = new WorldRuntime(st, k.R.Content, new MemoryRegionPager()))
        {
            rt.AdvanceTo(GameTime.FromGameHours(k.Now));
            Assert.Equal(k.State.Sieges!.Hash(), st.Sieges!.Hash());
            Assert.Equal(k.State.Chronicle.Hash(), st.Chronicle.Hash());
        }
        Assert.True(saveHour < k.Now);
    }
}
