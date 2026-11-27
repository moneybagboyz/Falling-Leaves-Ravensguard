using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Siege;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// OV-8 (overview 4, "determinism and saves"), as far as the parts exist: a 60-day scripted war trail on seed 1234 from the first summer day: the hero founds an iron works, a war is declared, the hero calls a levy while the AI musters 400 men and marches by
/// road on the hero's walled town (a stone ring), invests it and builds engines; a sea raid lands in the middle of it. At six points (days 8, 18, 28, 38, 48, 58) the save is loaded and saved again byte for byte; the run is made twice, the second time
/// saved at day 28, loaded into a fresh runtime and continued from the loaded state: the trail (the hashes of the books at each point) must equal the straight run's. <b>Not wired:</b> <c>phase7_trail</c> itself (<see cref="Ravensguard.Sim.Diagnostics.Phase7Trail"/>
/// is still the P7-0A skeleton and prints no line; this test's trail is its own, over the books' hashes), so there is no Linux and Windows comparison of it, and <c>phase5_trail</c> / <c>phase6_trail</c> are pinned by <c>GoldenHashes</c> as before.
/// <b>Scenes not in the trail (not built, or not scriptable headless):</b> a muster's purveyance tally beyond what the levy host does by itself, a night attack on a camp, a live battle saved mid-battle (the bubble's own save round trip is BB-14), and a mercenary contract.
/// </summary>
public class Ov8TrailTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;
    private static readonly int[] PointDays = [8, 18, 28, 38, 48, 58];
    private static readonly SeaRules Rules = SeaRules.From(EconFixture.Content);

    private sealed record Trail(string Hash, List<string> Lines, List<string> Scenes);

    private static string Parts(WorldState s) =>
        $"chron {s.Chronicle.Hash():x} army {s.Armies?.Hash() ?? 0:x} siege {s.Sieges?.Hash() ?? 0:x} naval {s.Naval?.Hash() ?? 0:x} soldiers {s.Soldiers?.Hash() ?? 0:x} industry {s.Industry?.Hash() ?? 0:x} coinage {s.Coinage?.Hash() ?? ""} pol {s.Politics!.Hash():x} " +
        $"coin {s.Economy!.Ledger.Audit().InPurses.Mites} sourced {s.Economy.Ledger.Audit().Sourced.Mites} mass {s.Economy.MassAuditHolds()}";

    private Trail Run(bool resumeFromSaveAtDay28, int roundTrips)
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 7)); long t0 = k.Now;
        var war = k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        var aw = k.Aw; var cw = k.R.Sim.Get<ConstructionWorld>()!;
        var scenes = new List<string>();

        // the works
        k.State.Economy!.Ledger.Mint(k.R.Ctx.Purse, new Coin(4_000_000), SourceReason.Seed);
        var found = k.R.Do(IndustryDesk.Found("iron", 0, 1_000_000, k.Now * 3600), k.Now * 3600); scenes.Add("works: " + found.Code);
        // the war: the AI muster against the hero's walled town, the hero's levy
        var e = k.Env();
        var ai = aw.CallCampaign(e, Mine, Enemy, aw.MusterPointFor(Mine, Enemy), 400, 400); Assert.NotNull(ai);
        int town = aw.Book.Campaigns[ai!.Id].Objective; var (cx, cy) = aw.Center(town); SiegeWiringTests.BuildRing(cw, k.State.Chunks, cx, cy, 12, "wall_stone");
        var levy = aw.CallMuster(e, new MusterCall(Enemy, town, Worth: 100_000_000, MaxLevies: 60)); scenes.Add("levy: " + (levy == null ? "none" : levy.State.ToString()));
        // the sea raid, a village of the victim's, planned for day 20
        var region = k.State.Population.Region; int victim = k.Rule.Fiefs[0].Realm, raider = k.Rule.Fiefs[21].Realm;
        int village = -1;
        foreach (var f in k.Rule.Fiefs.Values.Where(f => f.Realm == victim).OrderBy(f => f.Fief))
            foreach (int s in region.Fiefs[f.Fief].Settlements) if (village < 0 && region.Settlements[s].Class != SettlementClass.Town && k.Book.WarbandOfFief(f.Fief) is { Members.Count: >= 3 }) village = s;
        Assert.True(village >= 0); var st0 = region.Settlements[village];
        long landHour = (t0 / 24 + 20) * 24 + Rules.D.LandHourOfDay;
        var raid = new SeaRaid { Realm = raider, Victim = victim, Region = k.State.HomeRegion.Id, Settlement = village, Culture = 0, PlanShips = 1, PlanMen = 31, PlannedHour = k.Now, BeachXmm = (st0.CenterX + 1500L) * 1000, BeachYmm = st0.CenterY * 1000L, LandHour = landHour };
        var nbook = k.State.Naval ??= new NavalBook(); aw.Enlist(k.Env(), nbook, raid);

        var lines = new List<string>(); WorldState cur = k.State; WorldRuntime? rt2 = null; Action<long> to = h => k.To(h);
        for (int i = 0; i < PointDays.Length; i++)
        {
            to(t0 + PointDays[i] * 24L);
            if (i < roundTrips) { P7Game.RoundTrip(cur, k.R.W, "OV-8 point " + i + " (day " + PointDays[i] + ")"); }
            lines.Add($"day {PointDays[i]}: {Parts(cur)}");
            if (resumeFromSaveAtDay28 && i == 2)
            {
                var bytes = SaveCodec.Encode(WorldSave.Capture(cur, "h", "0.4.0-dev"));
                var st = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); st.Content = k.R.Content;
                Assert.Equal(Parts(cur), Parts(st));
                rt2 = new WorldRuntime(st, k.R.Content, new MemoryRegionPager()); cur = st; to = h => rt2.AdvanceTo(GameTime.FromGameHours(h));
            }
        }
        // the scenes of the trail
        var chron = cur.Chronicle.All;
        foreach (var t in new[] { "muster_called", "host_marched", "siege_begun", "engine_built", "wall_breached", "siege_terms", "sea_raid", "raiders_caught", "battle_report", "levy_drawn" }) scenes.Add($"{t}: {chron.Count(c => c.TypeId == t)}");
        rt2?.Dispose();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))))[..16];
        return new Trail(hash, lines, scenes);
    }

    [Fact]
    public void OV8_ASixtyDayWarTrail_SixByteIdenticalRoundTrips_ASaveLoadedMidWayContinuesToTheSameTrail_TwoRunsAreOne()
    {
        var a = Run(resumeFromSaveAtDay28: false, roundTrips: 6);
        foreach (var l in a.Lines) output.WriteLine(l);
        foreach (var s in a.Scenes) output.WriteLine(s);
        output.WriteLine("trail " + a.Hash);
        Assert.Contains(a.Scenes, s => s.StartsWith("muster_called: ") && s != "muster_called: 0");
        Assert.Contains(a.Scenes, s => s.StartsWith("host_marched: ") && s != "host_marched: 0");
        Assert.Contains(a.Scenes, s => s.StartsWith("siege_begun: ") && s != "siege_begun: 0");
        Assert.Contains(a.Scenes, s => s.StartsWith("sea_raid: ") && s != "sea_raid: 0");
        Assert.Contains("works: Ok", a.Scenes);
        Assert.All(a.Lines, l => Assert.EndsWith("mass True", l));

        var b = Run(resumeFromSaveAtDay28: true, roundTrips: 0);                                          // saved at day 28, loaded into a fresh runtime, continued from the loaded state
        Assert.Equal(a.Lines, b.Lines);
        Assert.Equal(a.Hash, b.Hash);
    }
}
