using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Armies;

/// <summary>P7-AM-E gate: AM-15 (last-known), AM-16 (campaign scoring), AM-17 (the war score from events) and AM-20 / M-B (the march: a hostile host musters, marches by road on the player's town, the levy meets it, the consequences are written).</summary>
public class CampaignTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    private static (ArmyRig K, War W) Rig(int attacker = Enemy, int defender = Mine)
    {
        var k = new ArmyRig();
        k.To(k.At(16, 7));
        var w = k.War(attacker, defender); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        return (k, w);
    }

    private static IEnumerable<ChronicleEntry> Battles(ArmyRig k) => k.Entries("battle_report").Where(en => en.Causes.Any(c => c.NoteKey == "ch_note_am_field"));

    // ---------------------------------------------------------------- AM-16

    [Fact]
    public void AM16_CampaignScoring_TheFormula_AndTheAIPicksTheHigherScore()
    {
        var (k, war) = Rig(Mine, Enemy); using var _k = k;
        var aw = k.Aw; var e = k.Env(); int point = aw.MusterPointFor(Mine, Enemy); Assert.True(point >= 0);
        var near = new List<(int S, CampaignScore C)>();
        for (int s = 0; s < aw.Region.Settlements.Count; s++)
        {
            if (aw.Supply!.RealmOf(s) != Enemy || aw.PathOf(point, s) == null) continue;
            near.Add((s, aw.ScoreTarget(e, Mine, point, s, 100, 20_000, 0, 0, false)));
        }
        Assert.True(near.Count >= 2, "the enemy holds places within reach");
        foreach (var (s, c) in near)
        {
            // value x p_win / 1000 less the cost, over the days: each term from the formula
            Assert.Equal((c.Value * c.PWinPm / 1000 - c.Cost) / c.Days, c.Score);
            Assert.Equal(c.Days * 20_000, c.Cost);
            Assert.Equal(Math.Max(1, c.MarchDays + c.SiegeDays), c.Days);
            Assert.Equal(aw.Siege.SiegeDays(aw, s, aw.TargetSize(aw.Region.Settlements[s].FiefIndex)), c.SiegeDays);
        }
        foreach (var (s, c) in near) output.WriteLine($"place {s} {aw.Region.Settlements[s].Class}: {aw.Siege.DefenceOf(aw, s)} siege {c.SiegeDays}d march {c.MarchDays}d p_win {c.PWinPm} value {c.Value} score {c.Score}");
        foreach (var (s, c) in near) Assert.Equal(aw.Siege.DefenceOf(aw, s) switch { DefenceClass.Open => 0, DefenceClass.Palisade => CampaignConfig.PalisadeDays, _ => CampaignConfig.StoneDays + aw.TargetSize(aw.Region.Settlements[s].FiefIndex) / 100 }, c.SiegeDays);
        var town = near[0];
        // a harvest in the days costs its shortfall too
        var e24 = new ArmyEnv(k.R.Sim, null, k.At(24, 8)); var dear = aw.ScoreTarget(e24, Mine, point, town.S, 100, 20_000, 5_000_000, 0, false); Assert.Equal(town.C.Cost + 5_000_000, dear.Cost);
        // the AI takes the best by score (ties the lower place)
        var best = aw.BestCampaign(e, Mine, Enemy, point, 100, 20_000, 0, false)!;
        Assert.Equal(near.Max(x => x.C.Score), best.Score); output.WriteLine($"best {best.Target} kind {best.Kind} score {best.Score}; " + string.Join("; ", near.Select(x => $"{x.S}:{x.C.Score}")));
        // the square law from the known strengths, in permille
        Assert.Equal(500, aw.ScoreTarget(e, Mine, point, town.S, 100, 0, 0, 100 - aw.TargetSize(aw.Region.Settlements[town.S].FiefIndex), false).PWinPm);
    }

    // ---------------------------------------------------------------- AM-15

    [Fact]
    public void AM15_LastKnown_ADiscAsWideAsItIsOld_ThePlanFollowsTheNewestWord_WithinTheDay()
    {
        Assert.Equal(CampaignConfig.DiscMinM, ArmyWorld.DiscWidthM(0)); Assert.Equal(12_000, ArmyWorld.DiscWidthM(3));
        var (k, war) = Rig(Mine, Enemy); using var _k = k;
        var aw = k.Aw; var e = k.Env(); int day = (int)e.Day;
        int point = aw.MusterPointFor(Mine, Enemy), pointE = aw.MusterPointFor(Enemy, Mine);
        // a host of the other side the realm has only heard of: seen three days ago, then (fresh) elsewhere
        var hostE = aw.CallMuster(e, new MusterCall(Enemy, pointE, Worth: 1)); Assert.NotNull(hostE);
        var places = Enumerable.Range(0, aw.Region.Settlements.Count).Where(s => s != point && aw.PathOf(point, s) != null).ToList();
        int a = places[0], b = places.First(s => aw.DistM(a, s) > 8000);
        var (ax, ay) = aw.CenterMm(a); var (bx, by) = aw.CenterMm(b);
        aw.Book.Intel[ArmyBook.IntelKey(Mine, hostE!.Id)] = new IntelRow { Observer = Mine, Subject = hostE.Id, Day = day - 3, XMm = ax, YMm = ay, Strength = 100, Source = 0 };
        var old = aw.ScoreHost(e, Mine, point, hostE, 200, 10_000)!;
        Assert.Equal(aw.NearestTo(ax, ay).Settlement, old.Target); Assert.Equal(2, old.Kind);
        Assert.True(aw.KnownStrength(Mine, hostE.Id, day) < 100 && aw.KnownStrength(Mine, hostE.Id, day) >= 100 * CampaignConfig.AgeFloorPm / 1000);   // trusted less with age, never under two fifths
        // the plan is made against the disc; a fresher word changes the target the same day
        var fresh = aw.IntelOf(Mine, hostE.Id)!; fresh.Day = day; fresh.XMm = bx; fresh.YMm = by; fresh.Strength = 120;
        var now = aw.ScoreHost(e, Mine, point, hostE, 200, 10_000)!;
        Assert.Equal(aw.NearestTo(bx, by).Settlement, now.Target); Assert.NotEqual(old.Target, now.Target);
        Assert.Equal(120, aw.KnownStrength(Mine, hostE.Id, day));
    }

    // ---------------------------------------------------------------- AM-17

    [Fact]
    public void AM17_ScoreFromEvents_ABattleWonAPalisadeTakenAndThreeRaids_Is29_AndAWarWithNoMarchesIsWeary()
    {
        var (k, war) = Rig(Mine, Enemy); using var _k = k;
        var aw = k.Aw; int day = k.Day;
        // a season with a battle won (the loser lost 30 of 100: 15 points), a palisade taken (8) and three raids (2 each: 6) = +29, the attacker's view
        void Add(WarEventKind kind, int pts, int lost = 0) => aw.Book.WarEvents.Add(new WarEvent { War = war.Serial, Day = day, Kind = kind, Doer = Mine, Points = pts, Lost = lost, OwnLost = lost / 2 });
        Add(WarEventKind.FieldBattle, Math.Min(CampaignConfig.BattleMax, 30 / 2), 30); Add(WarEventKind.PlaceTaken, CampaignConfig.TakenPalisade);
        for (int i = 0; i < 3; i++) Add(WarEventKind.Raid, CampaignConfig.RaidPoints);
        var rep = aw.ScoreFor(war)!; Assert.Equal(5, rep.Events.Count);
        var c = PoliticsEngine.Context(k.R.Sim, k.Pol, k.Rule, RuleChron.Of(k.State.Chronicle, k.State.Ids), k.Day);
        int g0 = war.GainA; Diplomacy.StepWar(c, war);
        Assert.Equal(29, war.Score); Assert.Equal(29, war.GainA - g0); Assert.Equal(1, war.Battles); Assert.Equal(1, war.Seasons);
        Assert.Empty(aw.Book.WarEvents);                                                      // counted once
        var scored = k.Entries("war_scored").Single(); Assert.NotEmpty(scored.Causes); Assert.Equal(war.DeclEntry, scored.Causes[0].EventId!.Value.Value);
        // no host ever marches: nobody wins, the war's weariness grows each season and the more weary realm sues, the end traced to the declaration
        int seasons = 0; while (war.Alive && seasons++ < 10) Diplomacy.StepWar(c, war);
        Assert.False(war.Alive); output.WriteLine($"peace after {war.Seasons} seasons, score {war.Score}");
        Assert.Contains(k.Entries("realm_peace"), en => en.Causes.Any(c => c.EventId?.Value == war.DeclEntry));
    }

    // ---------------------------------------------------------------- AM-20 / M-B

    [Fact]
    public void AM20_MB_AHostileHostMustersMarchesByRoadOnThePlayersTown_TheLevyMeetsIt_TheConsequencesAreWritten()
    {
        var (k, war) = Rig(Mine, Enemy); using var _k = k;                                   // realm 11 (the AI) declared on the player's realm 0
        var aw = k.Aw; var e = k.Env();
        ulong hero = aw.CommanderOf(Enemy)!.Value; aw.IsHero = id => id == hero;
        int pointAi = aw.MusterPointFor(Mine, Enemy);
        var ai = aw.CallCampaign(e, Mine, Enemy, pointAi, 400, 400);
        Assert.NotNull(ai); var plan = aw.Book.Campaigns[ai!.Id]; Assert.Equal(Enemy, aw.Supply!.RealmOf(plan.Objective));
        output.WriteLine($"AI host {ai.Id}: muster {pointAi}, objective {plan.Objective} kind {plan.Kind} value {plan.Value} score {plan.Score} expected {ai.Expected}");
        var mine = aw.CallMuster(e, new MusterCall(Enemy, plan.Objective, Worth: 100_000_000, MaxLevies: 60)); Assert.NotNull(mine);       // the hero calls 60 levies to the town
        var enemy = ai;
        int liveBubbles = 0; aw.LiveBattle = (_, eng) => { liveBubbles++; return null; };                                  // the bubble declines: the abstract resolver fights it
        long start = k.Now; long? battleAt = null;
        for (long h = start + 1; h <= start + 40 * 24; h++)
        {
            k.To(h);
            if (h % 24 == 0) output.WriteLine($"doy {GameTime.FromGameHours(h).DayOfYear}: enemy {(aw.Book.Hosts.TryGetValue(enemy.Id, out var xe) ? xe.State + " men " + aw.FitMen(xe) + " stage " + (aw.Book.Campaigns.TryGetValue(xe.Id, out var ce) ? ce.Stage : -1) : "gone")} | mine {(aw.Book.Hosts.TryGetValue(mine!.Id, out var xm) ? xm.State + " men " + aw.FitMen(xm) : "gone")} | marches {aw.Book.Marches.Count} intel {aw.Book.Intel.Count}");
            if (battleAt == null && Battles(k).Any()) { battleAt = h; }
            if (battleAt != null && h > battleAt + 48) break;
        }
        output.WriteLine($"hours to battle {(battleAt ?? 0) - start}; hosts {mine!.State}/{enemy.State}; marches {aw.Book.Marches.Count}");
        Assert.NotNull(battleAt);
        var marched = k.Entries("host_marched").Single(); Assert.NotEmpty(marched.Causes);                                // the column set out by road on the town
        var report = Battles(k).First(); Assert.NotEmpty(report.Causes); Assert.True(liveBubbles >= 1, "the hero leads one side: the player's bubble was offered the battle");
        Assert.NotNull(aw.IntelOf(Enemy, ai.Id));                                                                        // the player's scouts saw the column
        var fought = aw.Book.WarEvents.Concat([]).ToList(); output.WriteLine($"events {fought.Count}");
        Assert.Contains(k.Pol.Wars, w => w == war);
        var dead = k.Entries("levy_death").Count(); output.WriteLine($"levy_death {dead}, deaths {k.Entries("death").Count()}, desertion {k.Entries("desertion_wave").Count()}");
        // the season's war step turns the battle into the score
        k.To(k.At(31, 7)); Assert.NotEmpty(k.Entries("war_scored")); output.WriteLine($"war score {war.Score} battles {war.Battles} gains {war.GainA}/{war.GainD} alive {war.Alive}");
        Assert.NotEqual(0, war.Score);
        foreach (var t in new[] { "host_marched", "campaign_planned", "battle_report", "war_scored" }) foreach (var en in k.Entries(t)) Assert.True(en.Causes.Count > 0, t);
    }

    // ---------------------------------------------------------------- the save, mid-campaign

    [Fact]
    public void Save_MidCampaign_LoadAndContinue_EqualsTheUninterruptedRun_CampaignsIntelAndEvents()
    {
        var (k, war) = Rig(Mine, Enemy); using var _k = k;
        var aw = k.Aw; var e = k.Env();
        var ai = aw.CallCampaign(e, Mine, Enemy, aw.MusterPointFor(Mine, Enemy), 400, 400); Assert.NotNull(ai);
        var other = aw.CallCampaign(e, Enemy, Mine, aw.MusterPointFor(Enemy, Mine), 120, 120);
        k.To(k.At(22, 6));                                                                          // a column on the road, scouts out, a plan in each book
        Assert.NotEmpty(aw.Book.Campaigns); Assert.NotEmpty(aw.Book.Marches);
        var bytes = SaveCodec.Encode(WorldSave.Capture(k.State, "h", "0.1")); long h2 = k.At(28, 20);
        k.To(h2);
        var want = k.Book.Hash(); var wantSection = SaveCodec.SerializeCompressed(ArmySave.Capture(k.Book)); var wantChron = k.State.Chronicle.Hash(); var wantPol = k.Pol.Wars.Select(w => (w.Serial, w.Score, w.Battles, w.EndDay)).ToList();
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); st.Content = k.R.Content;
        Assert.NotEmpty(st.Armies!.Campaigns);
        using (var rt = new WorldRuntime(st, k.R.Content, new MemoryRegionPager()))
        {
            rt.AdvanceTo(GameTime.FromGameHours(h2));
            Assert.Equal(want, st.Armies!.Hash()); Assert.Equal(wantSection, SaveCodec.SerializeCompressed(ArmySave.Capture(st.Armies))); Assert.Equal(wantChron, st.Chronicle.Hash());
            Assert.Equal(wantPol, st.Politics!.Wars.Select(w => (w.Serial, w.Score, w.Battles, w.EndDay)).ToList());
        }
        output.WriteLine($"campaigns {k.Book.Campaigns.Count}, intel {k.Book.Intel.Count}, events {k.Book.WarEvents.Count}, armies hash {want:x}");
    }
}
