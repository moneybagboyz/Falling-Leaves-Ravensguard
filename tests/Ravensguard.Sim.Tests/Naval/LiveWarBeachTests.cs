using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Naval;

/// <summary>P8-LW-2, the bell and the beach: LW-6 (a landing 2 km away raises BellRung and the warband rides) and LW-7 (the hero arriving with raiders ashore opens a bubble with the ships as carriers), with the save key and the abstract fallback.</summary>
public class LiveWarBeachTests(ITestOutputHelper output)
{
    private static readonly SeaRules Rules = SeaRules.From(EconFixture.Content);

    private sealed class Scene : IDisposable
    {
        public ArmyRig K = null!; public SeaRaid Raid = null!; public int Village; public long LandHour; public ulong Hero;
        public void Dispose() => K.Dispose();
    }

    /// <summary>A victim's village with households and a warband, a raid of two longships and 70 men planned for next dawn, and a hero (a villager of no warband) who is told to the beach fights.</summary>
    private Scene Make()
    {
        var k = new ArmyRig(); k.Raised();
        var aw = k.Aw; var region = k.State.Population.Region; var pt = k.State.Population.People;
        int victim = k.Rule.Fiefs[0].Realm, raider = k.Rule.Fiefs[21].Realm;
        var decl = RuleChron.Of(k.State.Chronicle, k.State.Ids).Chron(GameTime.FromGameHours(k.Now), "h_war_declared", "ch_h_war_declared", [], [], [new Cause(CauseKind.Diplomacy, null, "", "ch_note_h_cb_insult")]);
        k.Pol.Wars.Add(new War { Serial = k.Pol.NextWar++, Attacker = raider, Defender = victim, StartDay = k.Day, Cb = CbKind.Insult, DeclEntry = decl.Value });
        int village = -1;
        foreach (var f in k.Rule.Fiefs.Values.Where(f => f.Realm == victim).OrderBy(f => f.Fief))
            foreach (int s in region.Fiefs[f.Fief].Settlements)
                if (village < 0 && region.Settlements[s].Class != SettlementClass.Town && k.Book.WarbandOfFief(f.Fief) is { Members.Count: > 0 } && k.Aw.Households.MemberCount.Select((m, h) => (m, h)).Count(x => x.m > 0 && k.Aw.Households.Settlement[x.h] == s) >= 3) village = s;
        Assert.True(village >= 0);
        var st = region.Settlements[village];
        int north = Enumerable.Range(0, 64).First(i => Ravensguard.Sim.Faith.ReligionBook.CultureOf(EconFixture.Content, i)?.Id == "north");
        long landHour = (k.Now / 24 + 1) * 24 + Rules.D.LandHourOfDay;
        var raid = new SeaRaid { Realm = raider, Victim = victim, Region = k.State.HomeRegion.Id, Settlement = village, Culture = north, War = true, PlanShips = 2, PlanMen = 70, PlannedHour = k.Now, BeachXmm = (st.CenterX + 1500L) * 1000, BeachYmm = st.CenterY * 1000L, LandHour = landHour };
        var book = k.State.Naval ??= new NavalBook();
        aw.Enlist(k.Env(), book, raid, PoliticsEngine.Context(k.R.Sim, k.Pol, k.Rule, RuleChron.Of(k.State.Chronicle, k.State.Ids), k.Day));
        var inBands = k.Book.Warbands.Values.SelectMany(w => w.Members).ToHashSet();
        ulong hero = 0; for (int r = 0; r < pt.Count && hero == 0; r++) if (pt.IsAlive(r) && pt.Household[r] >= 0 && !inBands.Contains(pt.IdOf(r).Value) && pt.AgeYears(r, k.Day) is >= 20 and <= 40) hero = pt.IdOf(r).Value;
        aw.IsHero = id => id == hero; aw.HeroId = () => hero;
        return new Scene { K = k, Raid = raid, Village = village, LandHour = landHour, Hero = hero };
    }

    [Fact]
    public void LW6_ALandingTwoKilometresAway_RaisesBellRung_TheWarbandRides_AndTheBellPausesTheHero()
    {
        using var s = Make(); var k = s.K; var aw = k.Aw; var raid = s.Raid;
        Assert.Equal(InterruptPolicy.Pause, InterruptBus.DefaultPolicy(InterruptKind.BellRung));
        var (cx, cy) = aw.CenterMm(s.Village);
        aw.BeachFights.GameDrivesLive = true; aw.BeachFights.NoteHero(cx / 1000 + 2000, cy / 1000);                // the hero stands 2 km from the village
        k.To(s.LandHour + Rules.D.RunHours);
        Assert.Equal(RaidState.Raiding, raid.State); Assert.Equal(raid.LootHour, raid.BellHour);
        Assert.True(raid.Defender >= 0 && raid.FightHour > raid.LootHour, "the warband rides");
        var rings = aw.BeachFights.TakeBells();
        Assert.Single(rings); Assert.Equal(raid.Id, rings[0].Raid); Assert.Equal((cx, cy), (rings[0].XMm, rings[0].YMm));
        var bus = new InterruptBus();
        Assert.Equal(1, BeachFights.RaiseBells(bus, rings, _ => "the bell"));
        Assert.Equal(InterruptKind.BellRung, bus.PausedBy!.Value.Kind);
        Assert.Empty(aw.BeachFights.TakeBells());
        // 4 km away the bell is not heard; with the switch off nothing is queued at all
        aw.BeachFights.NoteHero(cx / 1000 + 4000, cy / 1000); Assert.False(aw.BeachFights.Ring(raid, k.Now));
        aw.BeachFights.NoteHero(cx / 1000 + 2000, cy / 1000); aw.BeachFights.GameDrivesLive = false; Assert.False(aw.BeachFights.Ring(raid, k.Now));
    }

    [Fact]
    public void LW7_TheHeroArrivingWithRaidersAshore_OpensABubbleWithTheShipsAsCarriers_TheFightEndsTheRaidAsTheAbstractOneDoes_AndTheKeySavesByteIdentical()
    {
        using var s = Make(); var k = s.K; var aw = k.Aw; var raid = s.Raid; var bf = aw.BeachFights; var pt = k.State.Population.People;
        var st = k.State.Population.Region.Settlements[s.Village];
        bf.GameDrivesLive = true; bf.NoteHero(aw.OriginXMm / 1000 + st.CenterX + 1500, aw.OriginYMm / 1000 + st.CenterY);        // he stands on the beach
        long h = s.LandHour;
        for (; h <= s.LandHour + 14 && raid.LiveSince < 0 && !raid.Over; h++) k.To(h);
        Assert.True(raid.LiveSince >= 0, $"a live fight opened (state {raid.State})");
        var live = bf.Live[raid.Id]; Assert.False(live.Bubble.Closed);
        Assert.Equal(2, live.Carriers.Frames.Count); Assert.Same(live.Carriers, live.Bubble.W.Carriers);
        Assert.Equal(raid.Ships.OrderBy(x => x), live.Carriers.Frames.Select(f => f.Id.Value).OrderBy(x => x));
        Assert.Contains(s.Hero, live.Defenders); Assert.Equal(1, live.Bubble.Hero >= 0 ? 1 : 0);
        output.WriteLine($"opened at +{raid.LiveSince - s.LandHour}h: {live.Raiders.Count} raiders ashore vs {live.Defenders.Count} riders; {raid.Guards.Count} guards stay with the ships");
        // the save key is optional, and a raid with a live fight saves, loads and saves identically
        var file = WorldSave.Capture(k.State, "h", "0.1"); var bytes = SaveCodec.Encode(file);
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); back.Content = k.R.Content;
        Assert.Equal(raid.LiveSince, back.Naval!.Raids[raid.Id].LiveSince); Assert.Equal(k.State.Naval!.Hash(), back.Naval.Hash());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, "h", "0.1")));
        // while it is open the ships wait for it: the raid does not push off
        long openAt = k.Now;
        k.To(openAt + 6); Assert.False(raid.Over); Assert.True(raid.LiveSince >= 0);
        // the game steps it and closes it at a barrier: the fallen are killed as persons, the raid goes on as the abstract one does
        int dead0 = raid.Dead; Assert.True(bf.RunToEnd(k.Env(), raid.Id, 20 * 60 * 20), "the bubble closed");
        Assert.True(raid.Fought); Assert.Equal(-1, raid.LiveSince); Assert.False(bf.Live.ContainsKey(raid.Id));
        foreach (ulong m in live.Raiders) if (pt.Status[pt.IndexOf(new EntityId(m))] != PersonStatus.Alive) Assert.DoesNotContain(m, raid.Raiders);       // the dead are off the beach
        output.WriteLine($"fought: {raid.Dead - dead0} raiders dead, {raid.Raiders.Count} raiders left");
        for (long hh = k.Now + 1; hh <= k.Now + 24 && !raid.Over; hh++) k.To(hh);
        Assert.True(raid.Over); Assert.Single(k.Entries("sea_raid"));
        Assert.True(k.State.Economy!.MassAuditHolds());
    }

    [Fact]
    public void LW7_WithTheSwitchOff_TheAbstractFightIsFought_AsBefore()
    {
        using var s = Make(); var k = s.K; var raid = s.Raid; var st = k.State.Population.Region.Settlements[s.Village];
        k.Aw.BeachFights.NoteHero(k.Aw.OriginXMm / 1000 + st.CenterX + 1500, k.Aw.OriginYMm / 1000 + st.CenterY);
        for (long h = s.LandHour; h <= s.LandHour + 14 && !raid.Over; h++) k.To(h);
        Assert.True(raid.Over); Assert.True(raid.Fought); Assert.Equal(-1, raid.LiveSince); Assert.Empty(k.Aw.BeachFights.Live); Assert.Empty(k.Aw.BeachFights.TakeBells());
    }
}
