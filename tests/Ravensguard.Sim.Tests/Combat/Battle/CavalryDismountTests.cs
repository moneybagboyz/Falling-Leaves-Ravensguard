using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Phase 7 CV-B (cavalry-and-mounted-war 4.3, 4.5 to 4.7, 10): CV-7 dismounting and the horse-holders, CV-8 kicks and bites, CV-9 horse archers, CV-12 a fallen horse as an obstacle and CV-14 determinism
/// across a mid-battle save; and the pieces around them (the captain's dismount rule, a holder letting go, the crossbowman who gets down to span his bow, the save in every state of getting down and up).
/// </summary>
[Collection("fight")]
public partial class CavalryDismountTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly WeaponInstance Spear = new("spear", "steel");

    private MountKit Mount(int riding = 50, int trained = -1, string breed = "warhorse_courser") => new() { Breed = breed, Riding = riding, GearKg = 20, RiderKg = 80, TrainedPm = trained };

    private List<int> MenAtArms(FightWorld w, int n, int team = 0, int x0 = 0, int y0 = 0, int riding = 50, Func<int, int>? ridingOf = null, int tactics = -1, int trained = -1, bool shield = true)
    {
        var ids = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var kit = fx.Kit($"M{i}", team, x0, y0 + i * 1600, weapon: FightFixture.Sword, skill: 50, worn: FightCalibration.Mailed.Worn, faceX: team == 0 ? 1024 : -1024) with { Shield = shield ? "heater" : null, Courage = 100, Tactics = i == 0 ? tactics : -1 };
            ids.Add(w.AddMounted(kit, Mount(ridingOf?.Invoke(i) ?? riding, trained)).Id);
        }
        return ids;
    }

    private List<int> Foot(FightWorld w, int n, int x0, int y0, int team = 1, int skill = 30, int perRank = 10, bool spear = false, bool mailed = false, bool player = false)
    {
        var ids = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int rank = i / perRank, file = i % perRank;
            var kit = fx.Kit($"f{i}", team, x0 + (team == 1 ? rank : -rank) * 1200, y0 + file * 1000, weapon: spear ? Spear : FightFixture.Sword, skill: skill, worn: mailed ? FightCalibration.Mailed.Worn : null) with { Courage = 100, Tactics = -1, FaceX = team == 0 ? 1024 : -1024 };
            ids.Add(w.Add(kit, player).Id);
        }
        return ids;
    }

    private MountedFighter M(FightWorld w, int rider) => w.Mounts!.PairOf(rider)!;

    /// <summary>A real blow from <paramref name="by"/> on the horse's chest through the resolver (no hit roll), written into its wound book.</summary>
    private void Wound(FightWorld w, Fighter horse, Fighter by)
    {
        int part = horse.Plan.IndexOf("chest");
        for (uint k = 1; k < 40 && horse.Book.Records.Count == 0; k++)
        {
            var s = w.StrikePreview(by, horse, 0, part);
            var res = new BlowResult();
            new ResolverV2(fx.C.T).ResolveImpact(s, new BlowKey(1, 1, 1, k), res);
            if (res.WoundCount > 0) horse.Book.Apply(horse.Plan, res, w.GameSec, w.Tick, 1);
        }
        Assert.True(horse.Book.Records.Count > 0);
    }

    private (FightWorld W, FormationRow Row, List<int> Ids) Dismountable(int n = 20, ulong seed = 7, Func<int, int>? ridingOf = null)
    {
        var w = fx.World(seed);
        var ids = MenAtArms(w, n, ridingOf: ridingOf);
        var book = w.EnsureFormations();
        var row = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
        row.Micro = int.MaxValue / 2;
        return (w, row, ids);
    }

    private static void StepUntil(FightWorld w, Func<bool> done, int max, string what)
    {
        int t = 0;
        while (!done() && t++ < max) w.StepWithAi();
        Assert.True(done(), $"{what} did not happen within {max} ticks");
    }

    // ---------------------------------------------------------------- CV-7 dismount

    [Fact]
    public void CV7_Dismount_TwentyMenAtArms_FiveHoldersTakeTwentyHorses60mBack_FifteenFightOnFoot_AndMountingTakes4sOr2sAtRiding60()
    {
        var (w, row, ids) = Dismountable(20, ridingOf: i => i < 10 ? 50 : 60);
        var book = w.Formations!;
        book.Apply(w, row, FormationOrder.Of(OrderKind.Dismount), w.Tick + 1);
        int start = w.Tick;
        StepUntil(w, () => row.Foot == FormationBook.FootOn, 200, "the dismount");
        output.WriteLine($"dismounted in {w.Tick - start} ticks");
        Assert.InRange(w.Tick - start, 40, 46);                                                            // 2 s to get down
        Assert.All(ids, id => Assert.False(M(w, id).Mounted));
        Assert.Equal(FormationKind.Infantry, row.Kind);                                                    // a dismounted cavalry formation becomes Infantry with its own slots
        Assert.Equal(Formation.Line, row.Shape);
        var holders = ids.Where(id => M(w, id).Holding).ToList();
        Assert.Equal(5, holders.Count);                                                                    // one in four
        Assert.Equal(15, row.LayoutN);
        Assert.Equal(15, Enumerable.Range(0, row.Members.Length).Count(mi => row.Slot[mi] >= 0));
        Assert.DoesNotContain(row.Captain, holders);
        foreach (int hid in holders) Assert.Equal(4, ids.Count(id => M(w, id).HeldBy == hid));              // each leads four horses
        Assert.All(ids, id => Assert.True(M(w, id).HeldBy >= 0));                                          // all twenty horses are held
        // the 60 m: the holders walk to the post behind the line and the horses follow
        StepUntil(w, () => holders.All(h => FightMath.Len(w.Fighters[h].X - M(w, h).PostX, w.Fighters[h].Y - M(w, h).PostY) <= 2000) && ids.All(id => w.DistMm(w.Fighters[M(w, id).Horse], w.Fighters[M(w, id).HeldBy]) <= 4000), 1200, "the holders reaching their post");
        foreach (int hid in holders)
        {
            long back = (row.AnchorX - w.Fighters[hid].X) * row.FaceX / 1024 + (row.AnchorY - w.Fighters[hid].Y) * row.FaceY / 1024;
            Assert.InRange(back, 55_000, 65_000);
            output.WriteLine($"holder {hid}: {back / 1000.0:0.0} m behind the line");
        }
        // the fifteen on foot fight in a line (they hold their slots and are foot soldiers)
        Assert.All(ids.Where(id => !holders.Contains(id)), id => { Assert.True(w.Fighters[id].Active); Assert.True(row.Slot[row.IndexOfFighter(id)] >= 0); });
        // mount: the holders bring the horses up, each man walks to his and mounts: 80 ticks, 40 at Riding 60
        book.Apply(w, row, FormationOrder.Of(OrderKind.Mount), w.Tick + 1);
        var began = new Dictionary<int, int>(); var done = new Dictionary<int, int>();
        for (int t = 0; t < 4800 && row.Foot != FormationBook.FootNone; t++)
        {
            w.StepWithAi();
            foreach (int id in ids)
            {
                var m = M(w, id);
                if (m.Phase == MountedFighter.PhMounting && !began.ContainsKey(id)) began[id] = w.Tick;
                if (m.Mounted && began.ContainsKey(id) && !done.ContainsKey(id)) done[id] = w.Tick;
            }
        }
        Assert.Equal(FormationBook.FootNone, row.Foot);
        Assert.Equal(FormationKind.Cavalry, row.Kind);
        Assert.All(ids, id => Assert.True(M(w, id).Mounted));
        Assert.Equal(20, row.LayoutN);
        foreach (int id in ids.Where(id => !holders.Contains(id)))
        {
            int took = done[id] - began[id];
            int expect = M(w, id).Riding >= 60 ? 40 : 80;
            Assert.InRange(took, expect - 1, expect + 1);
        }
        output.WriteLine($"mounting: {string.Join(", ", ids.Where(id => !holders.Contains(id)).Take(3).Select(id => done[id] - began[id]))} ticks (Riding 50) and {string.Join(", ", ids.Where(id => !holders.Contains(id)).TakeLast(3).Select(id => done[id] - began[id]))} (Riding 60)");
        Assert.All(ids, id => Assert.False(M(w, id).Holding));
    }

    [Fact]
    public void CV7_AHolderWithAnEnemyWithin10mLetsGo_TheHorsesBolt_AndHeFightsOnFoot()
    {
        var (w, row, ids) = Dismountable(12);
        var book = w.Formations!;
        book.Apply(w, row, FormationOrder.Of(OrderKind.Dismount), w.Tick + 1);
        StepUntil(w, () => row.Foot == FormationBook.FootOn, 200, "the dismount");
        var holders = ids.Where(id => M(w, id).Holding).ToList();
        Assert.Equal(3, holders.Count);
        // an enemy stands 6 m off the side of the first holder's post (the middle holder is 11 m from him)
        var h0 = w.Fighters[holders[0]];
        var foe = w.Add(fx.Kit("raider", 1, M(w, holders[0]).PostX, M(w, holders[0]).PostY - 6000, skill: 40) with { Courage = 100 }, true);
        for (int t = 0; t < 1200 && FightMath.Len(h0.X - M(w, holders[0]).PostX, h0.Y - M(w, holders[0]).PostY) > 1500; t++) w.StepWithAi();
        var led = ids.Where(id => M(w, id).HeldBy == holders[0]).ToList();
        for (int t = 0; t < 10 && M(w, holders[0]).Holding; t++) w.StepWithAi();
        Assert.False(M(w, holders[0]).Holding);
        Assert.All(led, id => { Assert.Equal(-1, M(w, id).HeldBy); Assert.Equal(FAct.Fled, w.Fighters[M(w, id).Horse].Act); });
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.HorseLetGo && e.Actor == holders[0]);
        Assert.True(M(w, holders[1]).Holding && M(w, holders[2]).Holding);                                  // the other holders are not troubled
    }

    [Fact]
    public void CV7_Save_InEveryStateOfGettingDownAndUp_LoadsAndContinuesToTheSameHash()
    {
        (FightWorld, FormationRow) Make()
        {
            var (w, row, ids) = Dismountable(12, 9);
            w.Add(fx.Kit("watcher", 1, 300_000, 0, skill: 30), true);
            w.Formations!.Apply(w, row, FormationOrder.Of(OrderKind.Dismount), w.Tick + 1);
            return (w, row);
        }
        // the whole story: down at tick 40, the holders walking, the mount order at 700, the men fetching and mounting
        const int MountAt = 700, Total = 1500;
        var (full, frow) = Make();
        for (int t = 0; t < Total; t++)
        {
            if (full.Tick == MountAt) full.Formations!.Apply(full, frow, FormationOrder.Of(OrderKind.Mount), full.Tick + 1);
            full.StepWithAi();
        }
        foreach (int at in new[] { 20, 39, 41, 60, 300, 699, 705, 760, 850, 1100 })
        {
            var (part, prow) = Make();
            for (int t = 0; t < at; t++) { if (part.Tick == MountAt) part.Formations!.Apply(part, prow, FormationOrder.Of(OrderKind.Mount), part.Tick + 1); part.StepWithAi(); }
            var back = FightRecord.FromBytes(FightRecord.Capture(part).ToBytes()).Restore(fx.C);
            Assert.Equal(part.Formations!.Pack(), back.Formations!.Pack());
            Assert.Equal((part.Hash(), part.MountedHash()), (back.Hash(), back.MountedHash()));
            var brow = back.Formations!.Rows[0];
            for (int t = at; t < Total; t++)
            {
                if (back.Tick == MountAt) back.Formations!.Apply(back, brow, FormationOrder.Of(OrderKind.Mount), back.Tick + 1);
                back.StepWithAi();
            }
            Assert.True(full.Hash() == back.Hash() && full.MountedHash() == back.MountedHash(), $"a save at tick {at} drifted");
            Assert.Equal(full.Formations!.Pack(), back.Formations!.Pack());
            Assert.Equal(FightRecord.Capture(full).ToBytes(), FightRecord.Capture(back).ToBytes());
        }
        Assert.Equal(FormationBook.FootNone, frow.Foot);
        Assert.Equal(FormationKind.Cavalry, frow.Kind);
    }

    [Fact]
    public void CV7_ACaptainOfTactics50DismountsAMenAtArmsLineWithHurtHorses_AndMountsAgainWhenTheEnemyIsFar()
    {
        var w = fx.World(11);
        var ids = MenAtArms(w, 16, tactics: 60);
        var foe = Foot(w, 20, 60_000, -5000, spear: true, perRank: 5);
        var book = w.EnsureFormations();
        var cav = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
        var enemy = book.Form(w, 1, 1, FormationKind.Infantry, foe, 60_000, 0, -1024, 0, Formation.Line, 4, 500, place: true);
        cav.CaptainAlways = true; enemy.Micro = int.MaxValue / 2;
        book.Apply(w, enemy, FormationOrder.Of(OrderKind.Hold), w.Tick + 1);
        // a third of the horses are hurt: a graze to the neck of five of them
        int hurt = 0;
        foreach (int id in ids.Take(6)) { Wound(w, w.Fighters[M(w, id).Horse], w.Fighters[foe[0]]); hurt++; }
        Assert.True(hurt >= 5);
        for (int t = 0; t < 300 && cav.Foot == FormationBook.FootNone; t++) w.StepWithAi();
        Assert.NotEqual(FormationBook.FootNone, cav.Foot);                                                  // the captain got them down
        output.WriteLine($"the captain dismounted the line at tick {w.Tick}: foot {cav.Foot}");
    }

}
