using System.Diagnostics;
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
/// Phase 7 CV-A (cavalry-and-mounted-war 10): cavalry formations and the charge. CV-1 the charge sequence, CV-2 the same blow as Phase 4, CV-3 shock, CV-4 breakthrough and re-form, CV-5 the schiltron
/// (CV-13, the scale budget, is below in the timing collection); and the pieces around them: the wedge, slot order, the cavalry captain, the grid against the full scan, the save round trip and determinism.
/// </summary>
[Collection("fight")]
public class CavalryTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly WeaponInstance Spear = new("spear", "steel");
    private static readonly WeaponInstance Pike = new("pike", "steel");

    private MountKit Mount(int riding = 50, string breed = "warhorse_courser", IReadOnlyList<WornItem>? barding = null) => new() { Breed = breed, Riding = riding, GearKg = 20, RiderKg = 80, Barding = barding ?? [] };

    private List<int> Riders(FightWorld w, int n, int team = 0, int x0 = 0, int y0 = 0, int tactics = -1, int skill = 50, int riding = 50)
    {
        var ids = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var kit = fx.Kit($"R{i}", team, x0, y0 + i * 600, weapon: MountedCalibration.Lance, skill: skill, worn: FightCalibration.Mailed.Worn, faceX: team == 0 ? 1024 : -1024) with { Shield = "heater", Courage = 100, Tactics = tactics };
            ids.Add(w.AddMounted(kit, Mount(riding)).Id);
        }
        return ids;
    }

    /// <summary>Foot soldiers in ranks of <paramref name="perRank"/>, a metre between files, 1.2 m between ranks; cls 0 shield, 1 spear, 2 sword, 3 pike.</summary>
    private List<int> Foot(FightWorld w, int n, int x0, int y0, int team = 1, int cls = 2, int skill = 30, int perRank = 10, bool ai = true, bool mailed = false)
    {
        var ids = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int rank = i / perRank, file = i % perRank;
            int x = x0 + (team == 1 ? rank : -rank) * 1200, y = y0 + file * 1000;
            var kit = cls switch
            {
                0 => fx.Kit($"s{i}", team, x, y, skill: skill, shield: "heater"),
                1 => fx.Kit($"p{i}", team, x, y, weapon: Spear, skill: skill),
                3 => fx.Kit($"k{i}", team, x, y, weapon: Pike, skill: skill),
                _ => fx.Kit($"w{i}", team, x, y, skill: skill),
            };
            if (mailed) kit = kit with { Worn = FightCalibration.Mailed.Worn };
            ids.Add(w.Add(kit with { Courage = 100, Tactics = -1, FaceX = team == 0 ? 1024 : -1024 }, !ai).Id);
        }
        return ids;
    }

    /// <summary>A cavalry line of <paramref name="riders"/> at the origin facing +x and a foot formation <paramref name="gapMm"/> ahead (a hold order: it stands and fights what comes).</summary>
    private (FightWorld W, FormationRow Cav, FormationRow Foe) Setup(int riders, int foes, int gapMm, int cls = 2, ulong seed = 1, Formation shape = Formation.Line, int foeRanks = 4, int foeSkill = 30, Formation foeShape = Formation.Line, bool ai = true, bool mailed = false)
    {
        var w = fx.World(seed);
        var rid = Riders(w, riders);
        var foe = Foot(w, foes, gapMm, -(Math.Min(foes, (foes + foeRanks - 1) / foeRanks)) * 500, cls: cls, skill: foeSkill, perRank: (foes + foeRanks - 1) / foeRanks, ai: ai, mailed: mailed);
        var book = w.EnsureFormations();
        var cav = book.Form(w, 0, 1, FormationKind.Cavalry, rid, 0, 0, 1024, 0, shape, 0, 500, place: true);
        var enemy = book.Form(w, 1, 1, FormationKind.Infantry, foe, gapMm, 0, -1024, 0, foeShape, foeRanks, 500, place: true);
        cav.Micro = int.MaxValue / 2; enemy.Micro = int.MaxValue / 2;
        book.Apply(w, enemy, FormationOrder.Of(OrderKind.Hold), w.Tick + 1);
        return (w, cav, enemy);
    }

    private static FormationOrder ChargeAt(FormationRow foe, bool press = false) { var o = FormationOrder.Of(OrderKind.Charge); o.TargetFormation = foe.Index + 1; o.Arg = press ? 1 : 0; return o; }

    private static long Dist(FormationRow a, FormationRow b) => FightMath.Len(a.AnchorX - b.AnchorX, a.AnchorY - b.AnchorY);

    private MountedFighter M(FightWorld w, int rider) => w.Riding(w.Fighters[rider])!;

    // ---------------------------------------------------------------- CV-1 the charge sequence

    [Fact]
    public void CV1_ChargeSequence_AtwentyRiderLineFrom300m_WalksTrotsGallopsAndCouchesAtTheDistances()
    {
        var (w, cav, foe) = Setup(20, 40, 300_000);
        var book = w.Formations!;
        book.Apply(w, cav, ChargeAt(foe), w.Tick + 1);
        var cross = new int[5];
        var speedAt = new Dictionary<int, int[]>();
        for (int t = 1; t <= 2400 && cav.FirstContactTick < 0; t++)
        {
            w.StepWithAi();
            long d = Dist(cav, foe);
            if (cross[1] == 0) cross[1] = t;
            if (cross[2] == 0 && d <= 200_000) cross[2] = t;
            if (cross[3] == 0 && d <= 60_000) cross[3] = t;
            if (cross[4] == 0 && d <= 20_000) cross[4] = t;
            for (int k = 1; k <= 4; k++) if (cav.StageTick[k] >= 0 && !speedAt.ContainsKey(k) && t == cav.StageTick[k] + (k == 1 ? 100 : k == 2 ? 60 : k == 3 ? 60 : 20)) speedAt[k] = cav.Members.Select(id => M(w, id).Speed).ToArray();
        }
        output.WriteLine($"stage ticks {string.Join(", ", cav.StageTick)}; anchor crossings {string.Join(", ", cross)}");
        for (int k = 2; k <= 4; k++) Assert.InRange(cav.StageTick[k], cross[k] - 2, cross[k] + 2);       // the tick of each change +-2
        Assert.InRange(cav.StageTick[1], 0, 2);
        Assert.True(cav.StageTick[2] < cav.StageTick[3] && cav.StageTick[3] < cav.StageTick[4]);
        // and the riders themselves are in the gait: a walk (2.3 m/s of a courser, a little less under its load), a trot (5.2), a gallop (above 9)
        Assert.All(speedAt[1], v => Assert.InRange(v, 1900, 2400));
        Assert.All(speedAt[2], v => Assert.InRange(v, 4400, 5300));
        Assert.All(speedAt[3], v => Assert.InRange(v, 9000, 12_600));
        Assert.All(speedAt[4], v => Assert.InRange(v, 9000, 12_600));
        Assert.True(cav.Cohesion >= 700, $"the line kept its cohesion ({cav.Cohesion})");
        // the couched lances come at the couch stage: no rider has charged before it
        var charges = w.Events.Where(e => e.Kind == FightEventKind.Charge).ToList();
        Assert.NotEmpty(charges);
        Assert.All(charges, e => Assert.True(e.Tick >= cav.StageTick[4] - 2, $"a charge blow at tick {e.Tick} before the couch stage {cav.StageTick[4]}"));
    }

    // ---------------------------------------------------------------- CV-2 the same blow as Phase 4

    [Fact]
    public void CV2_SameBlow_ARiderOfAChargingFormationAndThePhase4SingleRider_AtTheSameSpeedAndTarget_DealTheSameBlow()
    {
        BlowResult One(bool formation, out StrikeV2 strike, out long extra)
        {
            var w = fx.World(3);
            var kit = fx.Kit("Rider", 0, 0, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn) with { Shield = "heater", Courage = 100, Tactics = -1 };
            var r = w.AddMounted(kit, Mount());
            var foot = w.Add(fx.Kit("Foot", 1, 2600, skill: 40), true);
            if (formation)
            {
                var book = w.EnsureFormations();
                var row = book.Form(w, 0, 1, FormationKind.Cavalry, [r.Id], 0, 0, 1024, 0, Formation.Line, 0, 0, place: true);
                Assert.Equal(FormationKind.Cavalry, row.Kind);
            }
            var m = w.Riding(r)!;
            m.VX = 12_000; m.VY = 0; m.Speed = 12_000;
            int part = foot.Plan.IndexOf("upper_body"), atk = r.Weapon.AttackIndex("couch");
            strike = w.StrikePreview(r, foot, atk, part);
            extra = strike.ExtraMomentumMilli;
            var res = new BlowResult();
            new ResolverV2(fx.C.T).Resolve(strike, new BlowKey(3, 10, 77, 1), res);
            return res;
        }
        var a = One(false, out var sa, out long ea);
        var b = One(true, out var sb, out long eb);
        Assert.Equal(69_600, ea);                                                       // 580 kg at 12 m/s, a couched lance
        Assert.Equal(ea, eb);
        Assert.Equal((sa.SituationPoints, sa.Strength, sa.Skill, sa.MomentumPm), (sb.SituationPoints, sb.Strength, sb.Skill, sb.MomentumPm));
        Assert.Equal((a.Hit, a.Blocked, a.Part, a.InitialMilli, a.FinalMilli, a.WoundCount), (b.Hit, b.Blocked, b.Part, b.InitialMilli, b.FinalMilli, b.WoundCount));
        Assert.True(a.InitialMilli > 200_000);

        // and in a real charge every couched blow of the formation carries the horse's own speed: the event's momentum is ChargeMath of the rider's closing speed (the formation adds none)
        var (w, cav, foe) = Setup(20, 40, 120_000);
        w.Formations!.Apply(w, cav, ChargeAt(foe), w.Tick + 1);
        for (int t = 0; t < 1200 && cav.CavPhase < FormationBook.PhReform; t++) w.StepWithAi();
        var charges = w.Events.Where(e => e.Kind == FightEventKind.Charge).ToList();
        Assert.NotEmpty(charges);
        foreach (var e in charges)
        {
            long expected = ChargeMath.ExtraMilli(10, 580, e.B, 1000) / 1000;
            Assert.InRange(e.A, expected * 90 / 100 - 1, expected * 110 / 100 + 1);
        }
    }

    // ---------------------------------------------------------------- CV-3 shock

    /// <summary>A 20-rider line charges 40 foot; <paramref name="off"/> of the riders are held 3 m off their slots every tick, so that the line's cohesion is (20 - off) / 20.</summary>
    private (FightWorld W, FormationRow Cav, FormationRow Foe, List<(int Tick, int Fighter, int X100)> Shocks, HashSet<int> ExpectedStruck) Shock(int off, int cls = 2, Formation foeShape = Formation.Line, int foes = 60)
    {
        var (w, cav, foe) = Setup(20, foes, 90_000, cls: cls, foeShape: foeShape, foeRanks: 6);
        var book = w.Formations!;
        book.Apply(w, cav, ChargeAt(foe), w.Tick + 1);
        HashSet<int> expected = [];
        for (int t = 0; t < 1200 && cav.ShockTick == -1 && cav.CavPhase < FormationBook.PhReform; t++)
        {
            for (int k = 0; k < off; k++)
            {
                var f = w.Fighters[cav.Members[19 - k]];
                book.SlotWorld(cav, cav.Slot[19 - k], w.Tick + 1, out _, out int sy);
                f.Y = sy + 3000;
            }
            var pos = w.Fighters.Select(f => (f.X, f.Y, f.Active && !f.Routing)).ToArray();                  // where everyone stands when the formation layer runs
            w.StepWithAi();
            if (cav.ShockTick > 0)
            {
                var contact = cav.Members.Where((id, i) => cav.ContactTick[i] >= 0 && cav.ShockTick - cav.ContactTick[i] <= 40).ToList();
                foreach (int id in foe.Members)
                    if (pos[id].Item3 && contact.Any(c => FightMath.Len(pos[c].X - pos[id].X, pos[c].Y - pos[id].Y) <= 5000)) expected.Add(id);
            }
        }
        return (w, cav, foe, book.Shocks.Select(s => (s.Tick, s.Fighter, s.X100)).ToList(), expected);
    }

    [Fact]
    public void CV3_Shock_Cohesion800_TakesFifteenMoraleFromTheFootWithin5mOfTheContacts_AndNothingFromTheRest()
    {
        var (w, cav, foe, shocks, expected) = Shock(4);
        output.WriteLine($"cohesion at contact {cav.CohAtContact}, shock at {cav.ShockTick}, struck {shocks.Count}");
        Assert.InRange(cav.CohAtContact, 790, 860);
        Assert.True(cav.ShockTick > 0);
        Assert.NotEmpty(shocks);
        Assert.All(shocks, s => Assert.Equal(1500, s.X100));
        Assert.Equal(shocks.Count, shocks.Select(s => s.Fighter).Distinct().Count());             // once each
        Assert.Equal(cav.ShockHit, shocks.Count);
        // who was within 5 m of a contact point is struck, and nobody else: the foot behind that are farther are not touched
        var struck = shocks.Select(s => s.Fighter).ToHashSet();
        Assert.True(struck.SetEquals(expected), $"struck {struck.Count}, expected {expected.Count}");
        Assert.True(struck.Count < foe.Members.Length, "the rear ranks were not touched");
        Assert.All(struck, id => Assert.Contains(id, foe.Members));
    }

    [Fact]
    public void CV3_Shock_Cohesion600_GivesNone_ARaggedChargeIsNotAShock()
    {
        var (w, cav, foe, shocks, _) = Shock(8);
        output.WriteLine($"cohesion at contact {cav.CohAtContact}, shock tick {cav.ShockTick}");
        Assert.InRange(cav.CohAtContact, 560, 660);
        Assert.Equal(-2, cav.ShockTick);                                                          // judged: ragged
        Assert.Empty(shocks);
    }

    [Fact]
    public void CV3_Shock_AShieldWallTakesFiveNotFifteen()
    {
        var (w, cav, foe, shocks, _) = Shock(4, cls: 0, foeShape: Formation.ShieldWall);
        Assert.True(cav.ShockTick > 0);
        Assert.NotEmpty(shocks);
        Assert.All(shocks, s => Assert.Equal(500, s.X100));
    }

    [Fact]
    public void CV3_Shock_NeedsTenRiders_TheBlowsAreTheRidersOwn()
    {
        // 8 riders in contact within 2 s do not make a shock however whole they are
        var (w, cav, foe) = Setup(8, 40, 90_000);
        w.Formations!.Apply(w, cav, ChargeAt(foe), w.Tick + 1);
        for (int t = 0; t < 1200 && cav.CavPhase < FormationBook.PhReform; t++) w.StepWithAi();
        Assert.True(cav.FirstContactTick > 0);
        Assert.True(cav.CohAtContact >= 700);
        Assert.Empty(w.Formations!.Shocks);
        Assert.NotEqual(cav.ShockTick, cav.FirstContactTick);
    }

    // ---------------------------------------------------------------- CV-4 breakthrough and re-form

    [Fact]
    public void CV4_Breakthrough_RidersAbove4mpsRideOn30m_AndTheFormationReformsBeyondWithin15s()
    {
        var (w, cav, foe) = Setup(20, 40, 90_000);
        var book = w.Formations!;
        // two horses go lame: a rider below 4 m/s is in a melee, not in the charge
        int slow1 = cav.Members[3], slow2 = cav.Members[14];
        w.Fighters[M(w, slow1).Horse].WalkPm = 250; w.Fighters[M(w, slow2).Horse].WalkPm = 250;
        book.Apply(w, cav, ChargeAt(foe), w.Tick + 1);
        var contactSpeed = new Dictionary<int, int>();
        int rideOnTick = -1, reformTick = -1;
        for (int t = 0; t < 2400 && cav.CavPhase != FormationBook.PhDone; t++)
        {
            w.StepWithAi();
            foreach (var (id, i) in cav.Members.Select((id, i) => (id, i))) if (cav.ContactTick[i] == w.Tick && !contactSpeed.ContainsKey(id)) contactSpeed[id] = M(w, id).Speed;
            if (cav.CavPhase == FormationBook.PhRideOn && rideOnTick < 0) rideOnTick = w.Tick;
            if (cav.CavPhase == FormationBook.PhReform && reformTick < 0) reformTick = w.Tick;
        }
        Assert.Equal(FormationBook.PhDone, cav.CavPhase);
        output.WriteLine($"first contact {cav.FirstContactTick}, ride on {rideOnTick}, re-form {reformTick}, whole again {cav.ReformedTick}: {(cav.ReformedTick - cav.FirstContactTick) / 20.0:0.0} s after contact");
        int through = 0, melee = 0;
        for (int i = 0; i < cav.Members.Length; i++)
        {
            if ((cav.CavFlags[i] & FormationBook.CfThrough) != 0) { through++; Assert.True(contactSpeed[cav.Members[i]] > 4000, "a rider who rode through was above 4 m/s at contact"); }
            if ((cav.CavFlags[i] & FormationBook.CfMelee) != 0) melee++;
        }
        Assert.Equal(18, through - (cav.CavFlags[3] & FormationBook.CfThrough) - (cav.CavFlags[14] & FormationBook.CfThrough));
        Assert.True((cav.CavFlags[3] & FormationBook.CfMelee) != 0 && (cav.CavFlags[14] & FormationBook.CfMelee) != 0, "the lame horses' riders are in a melee");
        Assert.True(melee >= 2);
        // the anchor rode 30 m past the contact line, then 80 m, and turned back to face the way it came
        var (fx0, fy0) = (cav.ContactFx, cav.ContactFy);
        int ex = cav.ContactAx + (int)((long)fx0 * 80_000 >> 10), ey = cav.ContactAy + (int)((long)fy0 * 80_000 >> 10);
        Assert.InRange(FightMath.Len(cav.AnchorX - ex, cav.AnchorY - ey), 0, 3500);
        Assert.True(FightMath.Dot(cav.FaceX, cav.FaceY, -fx0, -fy0) >= 1000, "faced back toward the enemy");
        Assert.True(cav.Cohesion >= 600, $"whole again: {cav.Cohesion}");
        Assert.InRange(cav.ReformedTick - cav.FirstContactTick, 1, 15 * 20);                      // within 15 s of the contact
        Assert.True(rideOnTick >= cav.FirstContactTick && reformTick > rideOnTick);
        Assert.InRange(reformTick - rideOnTick, 20, 6 * 20);                                      // 30 m at a gallop is a few seconds
        Assert.Equal(OrderKind.Hold, cav.Order.Kind);                                              // the charge is done; the formation holds
    }

    [Fact]
    public void CV4_ChargeAndPress_StaysInTheMelee_NoRideOn()
    {
        var (w, cav, foe) = Setup(20, 40, 90_000);
        w.Formations!.Apply(w, cav, ChargeAt(foe, press: true), w.Tick + 1);
        for (int t = 0; t < 1200 && cav.Order.Kind == OrderKind.Charge; t++) w.StepWithAi();
        Assert.True(cav.FirstContactTick > 0);
        Assert.Equal(FormationBook.PhApproach, cav.CavPhase);
        Assert.Equal(OrderKind.Hold, cav.Order.Kind);
        Assert.True(cav.CavFlags.Count(f => (f & FormationBook.CfMelee) != 0) >= 18);            // the whole formation fights as Phase 4 riders
        Assert.DoesNotContain(cav.CavFlags, f => (f & FormationBook.CfThrough) != 0);
        int ax = cav.AnchorX;
        for (int t = 0; t < 200; t++) w.StepWithAi();
        Assert.Equal(ax, cav.AnchorX);                                                            // it holds where it hit
    }

    // ---------------------------------------------------------------- CV-5 the schiltron

    private (FightWorld W, FormationRow Cav, FormationRow Ring) Ring(bool grid = true)
    {
        var w = fx.World(9); w.UseGrid = grid;
        var rid = Riders(w, 30, riding: 50);
        var pikes = new List<int>();
        for (int i = 0; i < 60; i++) pikes.Add(w.Add(fx.Kit($"k{i}", 1, 100_000 + i % 10 * 700, -3500 + i / 10 * 700, weapon: Pike, skill: 40) with { Courage = 100, Tactics = -1, FaceX = -1024 }).Id);
        var book = w.EnsureFormations();
        var cav = book.Form(w, 0, 1, FormationKind.Cavalry, rid, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
        var ring = book.Form(w, 1, 1, FormationKind.Infantry, pikes, 100_000, 0, -1024, 0, Formation.Schiltron, 2, 500, place: true);
        cav.Micro = ring.Micro = int.MaxValue / 2;
        book.Apply(w, ring, FormationOrder.Of(OrderKind.Hold), w.Tick + 1);
        return (w, cav, ring);
    }

    [Fact]
    public void CV5_Schiltron_60BracedPikesAgainst30Coursers_RefusalFollowsTheGridsCount_AndTheBracedStrikesLandFirst()
    {
        var (w, cav, ring) = Ring();
        w.Formations!.Apply(w, cav, ChargeAt(ring), w.Tick + 1);
        int viaGrid = -1, brute = -2;
        for (int t = 0; t < 2400 && cav.FirstContactTick < 0; t++)
        {
            w.StepWithAi();
            if (viaGrid != -1 || ring.Members.Count(i => w.Fighters[i].Act == FAct.Bracing) < 20) continue;
            // the ring is braced and the coursers are closing: the grid's count of braced pikes within 6 m facing a horse is the full scan's
            var nearP = w.Fighters[ring.Members.First(i => w.Fighters[i].Act == FAct.Bracing)];
            var horse = w.Fighters[M(w, cav.Members[0]).Horse];
            viaGrid = w.BracedFacing(nearP, horse, 0);
            brute = w.Fighters.Count(b => b.Team == 1 && b.Active && FightWorld.CanBrace(b) && b.Act == FAct.Bracing && (b == nearP || w.DistMm(b, nearP) <= 6000)
                && FightMath.Dot(b.FX, b.FY, FightMath.Norm(horse.X - b.X, horse.Y - b.Y).X, FightMath.Norm(horse.X - b.X, horse.Y - b.Y).Y) >= w.K.FrontArcQ10);
        }
        for (int t = 0; t < 400; t++) w.StepWithAi();
        var balks = w.Events.Where(e => e.Kind == FightEventKind.Balked).ToList();
        var braceStrikes = w.Events.Where(e => e.Kind == FightEventKind.BraceStrike).ToList();
        var riderBlows = w.Events.Where(e => cav.Members.Contains(e.Actor) && e.Kind is FightEventKind.Hit or FightEventKind.Stopped or FightEventKind.Missed or FightEventKind.Blocked or FightEventKind.Parried or FightEventKind.Dodged).ToList();
        output.WriteLine($"balks {balks.Count} (p {string.Join(",", balks.Select(b => b.A).Distinct())}), braced strikes {braceStrikes.Count}, rider blows {riderBlows.Count}, ring braced {ring.Members.Count(i => w.Fighters[i].Act == FAct.Bracing)}/60");
        Assert.True(balks.Count >= 15, "most of the horses balk at a ring of braced pikes");
        // the refusal each horse met is the Phase 4 formula with the count of braced pikes within 6 m facing it, read from the grid: the same count as the full scan
        Assert.True(viaGrid >= 0, "the ring braced as the horses closed");
        output.WriteLine($"braced pikes within 6 m facing a horse: {viaGrid} (grid) {brute} (full scan) -> refusal {ChargeMath.RefusalPm(viaGrid, 900, 50)} permille");
        Assert.Equal(brute, viaGrid);
        Assert.All(balks, e => Assert.InRange(e.A, 0, 950));                                      // never above the cap
        // the braced pikes strike the closing horses before any lance reaches a man (4.5 m against 3.6 m)
        Assert.NotEmpty(braceStrikes);
        if (riderBlows.Count > 0) Assert.True(braceStrikes.Min(e => e.Tick) <= riderBlows.Min(e => e.Tick), "the pike lands first");
    }

    [Fact]
    public void CV5_Schiltron_TheGridIsTheFullScan_SameFightSameHash()
    {
        ulong Run(bool grid)
        {
            var (w, cav, ring) = Ring(grid);
            w.Formations!.Apply(w, cav, ChargeAt(ring), w.Tick + 1);
            for (int t = 0; t < 2000; t++) w.StepWithAi();
            return w.Hash() ^ w.MountedHash() * 31;
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ---------------------------------------------------------------- the worked example

    [Fact]
    public void WorkedExample_ACouchedLanceChargeIntoBracedSpears_AndIntoLooseInfantry()
    {
        (int FoeDown, int FoeRout, int Balks, int BraceStrikes, int HorsesDown, int RidersOff, int Shock, int Dead) Run(int cls, Formation foeShape, bool brace)
        {
            var (w, cav, foe) = Setup(20, 40, 90_000, cls: cls, foeShape: foeShape, foeSkill: 45, mailed: true);
            w.Formations!.Apply(w, cav, ChargeAt(foe), w.Tick + 1);
            if (brace) w.Formations.Apply(w, foe, FormationOrder.Of(OrderKind.SetAgainstCavalry), w.Tick + 1);
            for (int t = 0; t < 1500 && (cav.FirstContactTick < 0 || w.Tick < cav.FirstContactTick + 300); t++) w.StepWithAi();      // ten seconds after the contact
            var ev = w.Events;
            output.WriteLine($"  foe acts: {string.Join(", ", foe.Members.GroupBy(i => w.Fighters[i].Act).Select(g => g.Key + " " + g.Count()))}; riders: {string.Join(", ", cav.Members.GroupBy(i => w.Fighters[i].Act).Select(g => g.Key + " " + g.Count()))}; tick {w.Tick}, phase {cav.CavPhase}, contact {cav.FirstContactTick}");
            return (foe.Members.Count(i => !w.Fighters[i].Active), foe.Members.Count(i => w.Fighters[i].Routing), ev.Count(e => e.Kind == FightEventKind.Balked), ev.Count(e => e.Kind == FightEventKind.BraceStrike),
                cav.Members.Count(i => w.Fighters[w.PairOf(w.Fighters[i])!.Horse].Act is FAct.Dead or FAct.Down), cav.Members.Count(i => w.Riding(w.Fighters[i]) == null), w.Formations.Shocks.Count, foe.Members.Count(i => w.Fighters[i].Act == FAct.Dead));
        }
        var loose = Run(2, Formation.Loose, false);
        var spears = Run(1, Formation.Line, true);
        output.WriteLine($"loose swords : foot down {loose.FoeDown}, routing {loose.FoeRout}, dead {loose.Dead}, balks {loose.Balks}, braced strikes {loose.BraceStrikes}, horses down {loose.HorsesDown}, riders unhorsed {loose.RidersOff}, shocked {loose.Shock}");
        output.WriteLine($"braced spears: foot down {spears.FoeDown}, routing {spears.FoeRout}, dead {spears.Dead}, balks {spears.Balks}, braced strikes {spears.BraceStrikes}, horses down {spears.HorsesDown}, riders unhorsed {spears.RidersOff}, shocked {spears.Shock}");
        Assert.Equal(0, loose.Balks);
        Assert.True(loose.Shock >= 10, "a cohesive charge shocks the loose infantry");
        Assert.True(spears.Balks > 0 && spears.BraceStrikes > 0, "braced spears make the horses balk and strike the ones that come on");
        Assert.Equal(0, spears.Shock);                                                              // ten riders did not reach the line together
        Assert.True(spears.RidersOff >= 1, "a balking horse throws some riders");
        Assert.Equal(0, loose.RidersOff);
    }

    // ---------------------------------------------------------------- the shapes

    [Fact]
    public void Wedge_RankRHolds1Plus2rRiders_At1500x4000_AndThePointIsTheCaptains()
    {
        var w = fx.World(2);
        var ids = Riders(w, 25);
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Wedge, 0, 500, officers: new HashSet<int> { ids[11] }, place: true);
        Assert.Equal(ids[11], r.Captain);
        Assert.Equal(r.Captain, r.FighterAt(0));                                                  // the point
        Assert.Equal(9, r.Files);                                                                 // five ranks: 1, 3, 5, 7, 9
        int s = 0;
        for (int rank = 0; rank < 5; rank++)
            for (int k = 0; k < 1 + 2 * rank; k++, s++)
            {
                Assert.Equal(((2 * rank) - 2 * k) * 750, r.Lat[s]);
                Assert.Equal(rank * 4000, r.Back[s]);
            }
        book.SlotWorld(r, 24, 1, out int x, out int y);
        Assert.Equal((-16_000, -6000), (x, y));                                                   // the right end of the rear rank: 6 m to the right, 16 m behind
        // a wedge closes ranks from behind: the man down in the point is replaced from rank 1's nearest
        w.Fighters[r.FighterAt(0)].Act = FAct.Down;
        for (int t = 0; t < 60; t++) w.StepWithAi();
        Assert.True(r.Occ[0] >= 0, "the point was filled from behind");
        Assert.NotEqual(ids[11], r.FighterAt(0));
        Assert.True(r.Slot[r.IndexOfFighter(r.FighterAt(0))] == 0);
    }

    [Fact]
    public void Slots_TheFrontRankIsTheBestArmoured_BardingFirst_ThenRiding_ThenSkill_ThenId()
    {
        var w = fx.World(4);
        var ids = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            var kit = fx.Kit($"R{i}", 0, 0, i * 800, weapon: MountedCalibration.Lance, skill: 40 + i, worn: FightCalibration.Mailed.Worn) with { Shield = "heater", Courage = 100 };
            IReadOnlyList<WornItem> barding = i is 2 or 7 ? [new WornItem("horse_mail", "iron")] : [];
            ids.Add(w.AddMounted(kit, Mount(riding: 40 + (i * 3) % 11, barding: barding)).Id);
        }
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Line, 0);
        Assert.Equal(2, r.Ranks);
        Assert.Equal(5, r.Files);
        Assert.Equal(new[] { ids[2], ids[7] }.OrderBy(i => i), r.Members.Take(2).OrderBy(i => i));        // the two barded horses lead the front rank
        for (int s = 2; s < 10; s++)                                                                // then Riding descending, then weapon skill, then id
        {
            var a = M(w, r.Members[s - 1]); var b = M(w, r.Members[s]);
            if (s == 2) continue;
            Assert.True(a.Riding > b.Riding || a.Riding == b.Riding && (w.Fighters[a.Rider].Skill > w.Fighters[b.Rider].Skill || w.Fighters[a.Rider].Skill == w.Fighters[b.Rider].Skill && a.Rider < b.Rider));
        }
        book.SlotWorld(r, 0, 1, out int x0, out int y0);
        book.SlotWorld(r, 5, 1, out int x5, out int y5);
        Assert.Equal((0, 3000), (x0, y0));                                                          // lat 1,500 x 4 / 2, the front rank
        Assert.Equal((-4000, 3000), (x5, y5));                                                      // the second rank 4 m behind
    }

    // ---------------------------------------------------------------- the cavalry captain

    [Theory]
    [InlineData(0, true)]
    [InlineData(40, false)]
    public void Captain_AvoidsBracedSpearsFromTactics30_AndChargesTheNearestBelowThat(int tactics, bool nearestIsBraced)
    {
        var w = fx.World(11);
        var rid = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            var kit = fx.Kit($"R{i}", 0, 0, i * 600, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn) with { Shield = "heater", Courage = 100, Tactics = i == 0 ? tactics : 0 };
            rid.Add(w.AddMounted(kit, Mount()).Id);
        }
        var braced = Foot(w, 30, 80_000, -1500, cls: 3, perRank: 10);
        var loose = Foot(w, 30, 120_000, 40_000, cls: 2, perRank: 10);
        var book = w.EnsureFormations();
        var cav = book.Form(w, 0, 1, FormationKind.Cavalry, rid, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
        var rb = book.Form(w, 1, 1, FormationKind.Infantry, braced, 80_000, 0, -1024, 0, Formation.Schiltron, 2, 500, place: true);
        var rl = book.Form(w, 1, 2, FormationKind.Infantry, loose, 120_000, 40_000, -1024, 0, Formation.Line, 4, 500, place: true);
        foreach (var q in book.Rows) { q.Micro = int.MinValue / 2; q.CaptainAlways = true; }
        rb.CaptainAlways = rl.CaptainAlways = false; rb.Micro = rl.Micro = int.MaxValue / 2;
        for (int t = 0; t < 700 && cav.Order.Kind != OrderKind.Charge; t++) w.StepWithAi();
        Assert.Equal(OrderKind.Charge, cav.Order.Kind);
        int target = cav.Order.TargetFormation - 1;
        Assert.Equal(nearestIsBraced ? rb.Index : rl.Index, target);
    }

    // ---------------------------------------------------------------- save and determinism

    private FightWorld Battle(ulong seed = 21, int gap = 90_000)
    {
        var (w, cav, foe) = Setup(20, 40, gap, seed: seed);
        w.Formations!.Apply(w, cav, ChargeAt(foe), w.Tick + 1);
        return w;
    }

    [Theory]
    [InlineData(250)]       // on the approach, at the trot
    [InlineData(-30)]       // 30 ticks after the first contact: mid ride-on
    public void Save_SaveLoadContinue_MidCharge_EqualsTheUninterruptedRun(int at)
    {
        int contact = 0;
        { var probe = Battle(); var row = probe.Formations!.Rows[0]; while (row.FirstContactTick < 0 && probe.Tick < 3000) probe.StepWithAi(); contact = row.FirstContactTick; }
        int saveAt = at >= 0 ? at : contact - at;
        var full = Battle(); var part = Battle();
        for (int t = 0; t < saveAt; t++) { full.StepWithAi(); part.StepWithAi(); }
        var bytes = FightRecord.Capture(part).ToBytes();
        var back = FightRecord.FromBytes(bytes).Restore(fx.C);
        Assert.NotNull(back.Formations);
        Assert.Equal(part.Formations!.Pack(), back.Formations!.Pack());
        Assert.Equal((part.Hash(), part.MoraleHash(), part.MountedHash()), (back.Hash(), back.MoraleHash(), back.MountedHash()));
        for (int t = 0; t < 700; t++) { full.StepWithAi(); back.StepWithAi(); if (t % 50 == 0) Assert.Equal((full.Hash(), full.MoraleHash(), full.MountedHash()), (back.Hash(), back.MoraleHash(), back.MountedHash())); }
        Assert.Equal((full.Hash(), full.MoraleHash(), full.MountedHash()), (back.Hash(), back.MoraleHash(), back.MountedHash()));
        Assert.Equal(full.Formations!.Pack(), back.Formations!.Pack());
        Assert.Equal(FightRecord.Capture(full).ToBytes(), FightRecord.Capture(back).ToBytes());
    }

    [Fact]
    public void Save_ABookWithNoCavalryRowIsByteForByteBBB_AndAnOldSaveLoads()
    {
        var w = fx.World(5);
        var ids = Foot(w, 20, 0, 0, team: 0);
        var book = w.EnsureFormations();
        book.Form(w, 0, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0, Formation.Line, 4, place: true);
        var packed = book.Pack();
        Assert.Equal(1, packed[0]);                                                                 // the format BB-B wrote
        var (w2, _, _) = Setup(10, 10, 50_000);
        Assert.Equal(2, w2.Formations!.Pack()[0]);                                                  // a cavalry row adds its tail
    }

    [Fact]
    public void Determinism_TheSameChargeTwice_TheSameHashesAndTheSameShock()
    {
        (ulong, ulong, ulong, int, int) Run()
        {
            var w = Battle(33);
            for (int t = 0; t < 1200; t++) w.StepWithAi();
            var r = w.Formations!.Rows[0];
            return (w.Hash(), w.MoraleHash(), w.MountedHash(), r.ShockTick, r.ShockHit);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Wedge_AChargeInWedgeShapeBreaksIn()
    {
        var w = fx.World(8);
        var rid = Riders(w, 25);
        var foe = Foot(w, 40, 90_000, -2000, perRank: 10);
        var book = w.EnsureFormations();
        var cav = book.Form(w, 0, 1, FormationKind.Cavalry, rid, 0, 0, 1024, 0, Formation.Wedge, 0, 500, place: true);
        var enemy = book.Form(w, 1, 1, FormationKind.Infantry, foe, 90_000, 0, -1024, 0, Formation.Line, 4, 500, place: true);
        cav.Micro = enemy.Micro = int.MaxValue / 2;
        book.Apply(w, enemy, FormationOrder.Of(OrderKind.Hold), w.Tick + 1);
        book.Apply(w, cav, ChargeAt(enemy), w.Tick + 1);
        for (int t = 0; t < 1500 && cav.CavPhase != FormationBook.PhDone; t++) w.StepWithAi();
        Assert.True(cav.FirstContactTick > 0);
        Assert.Equal(FormationBook.PhDone, cav.CavPhase);
        output.WriteLine($"wedge: contact {cav.FirstContactTick}, whole again {cav.ReformedTick}, foot down {foe.Count(i => !w.Fighters[i].Active)}");
        Assert.True(foe.Count(i => !w.Fighters[i].Active) > 0);
    }
}

/// <summary>CV-13 (cavalry-and-mounted-war 10, 11): 300 cavalry (600 fighters) inside the 2,000-soldier run, by itself in the timing collection.</summary>
[Collection(TimingCollection.Name)]
public class CavalryBudgetTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    private static readonly WeaponInstance Spear = new("spear", "steel");

    private FighterKit KitOf(int cls, int team, int skill, int tactics = -1) => (cls switch
    {
        0 => fx.Kit("s", team, 0, 0, skill: skill, shield: "heater"),
        1 => fx.Kit("p", team, 0, 0, weapon: Spear, skill: skill),
        2 => fx.Kit("w", team, 0, 0, skill: skill),
        _ => new FighterKit { Name = "a", Team = team, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Worn = [new WornItem("tunic", "linen")], Skill = skill, ArcherySkill = skill, FaceX = team == 0 ? 1024 : -1024 },
    }) with { Tactics = tactics, Courage = 100 };

    private FightWorld Big(int perSide, int rowSize, int cavPerSide, int cavRow)
    {
        var w = fx.World(50); w.RecordEvents = false;
        var book = w.EnsureFormations();
        for (int team = 0; team < 2; team++)
        {
            int rows = perSide / rowSize;
            for (int k = 0; k < rows; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < rowSize; i++)
                {
                    int cls = i % 5 == 4 ? 3 : i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : 2;
                    ids.Add(w.Add(KitOf(cls, team, 30 + i * 7 % 40, i == 0 ? 30 : 0)).Id);
                }
                int ax = team == 0 ? -10_000 : 10_000;
                var r = book.Form(w, team, (byte)(k + 1), FormationKind.Infantry, ids, ax, (k - rows / 2) * 34_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 4, place: true);
                r.CaptainAlways = true;
            }
            int crows = cavPerSide / cavRow;
            for (int k = 0; k < crows; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < cavRow; i++)
                {
                    var kit = fx.Kit("R", team, 0, 0, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn, faceX: team == 0 ? 1024 : -1024) with { Shield = "heater", Courage = 100, Tactics = i == 0 ? 50 : 0 };
                    ids.Add(w.AddMounted(kit, new MountKit { Riding = 50 + i % 7, GearKg = 20, RiderKg = 80 }).Id);
                }
                int ax = team == 0 ? -110_000 : 110_000;
                var r = book.Form(w, team, (byte)(40 + k), FormationKind.Cavalry, ids, ax, (k - crows / 2) * 34_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 0, 500, place: true);
                r.CaptainAlways = true;
            }
        }
        w.LodCentre = (-10_000, 0);
        return w;
    }

    [Fact]
    public void CV13_ScaleBudget_300Cavalry600Fighters_Inside2000Soldiers_TheMountedLayerAverages08msOrLess()
    {
        var warm = Big(100, 50, 20, 10);
        for (int i = 0; i < 100; i++) warm.StepWithAi();
        double mounted = 0;
        double Pass()
        {
            var w = Big(1000, 100, 150, 15);
            for (int i = 0; i < 20; i++) w.StepWithAi();
            var L = w.Mounts!; long p0 = L.PerfTicks;
            var sw = new Stopwatch(); double sum = 0, worst = 0; int n = 600; int charges = 0, balks = 0;
            for (int i = 0; i < n; i++) { sw.Restart(); w.StepWithAi(); double ms = sw.Elapsed.TotalMilliseconds; sum += ms; worst = Math.Max(worst, ms); }
            foreach (var r in w.Formations!.Rows) if (r.Kind == FormationKind.Cavalry && r.FirstContactTick > 0) charges++;
            mounted = (L.PerfTicks - p0) * 1000.0 / Stopwatch.Frequency / n;
            output.WriteLine($"2,000 soldiers + 300 cavalry (600 fighters), ticks 21..620: whole tick average {sum / n:0.00} ms, worst {worst:0.0} ms; the mounted layer {mounted:0.000} ms a tick; {charges} cavalry formations made contact {balks}");
            return mounted;
        }
        double avg = TestBudget.BestPass(0.8, 3, Pass);
        Assert.True(avg <= TestBudget.Ms(0.8), $"the mounted layer averaged {avg:0.000} ms a tick");
    }
}
