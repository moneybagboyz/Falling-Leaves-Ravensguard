using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;

namespace Ravensguard.Sim.Tests.Contracts;

/// <summary>P6-C2 odds and ends: the legends a villager tells with no news (CR-12), the autopilot's road encounters by the odds rule, the order of the hero commands.</summary>
public class ContractExtrasTests
{
    [Fact]
    public void CR12_AVillagerWithNoFreshRumourTellsTheLocalLegend_TheSameOneEachTime_AndNothingWhenNoLegendLiesNear()
    {
        var w = TestWorld.Get(2); LandmarkPlacer.Place(w.Region, w.History, w.Content);
        var book = CodexBook.Build(w.History, w.Region, []);
        // a landmark with a legend hanging on it
        var (landmark, x, y) = book.LandmarkPlaces.First(p => p.Entry.Sources.Any(s => book.Get("legend." + s.Counter) != null));
        var near = Legends.Near(book, x, y);
        Assert.NotEmpty(near); Assert.All(near, l => Assert.StartsWith("legend.", l.Id));
        Assert.True(near.Select(l => l.Id).Distinct().Count() == near.Count);                                    // one entry per legend
        var told = Legends.Tell(book, x, y, 3)!;
        Assert.Equal(told, Legends.Tell(book, x, y, 3));                                                         // the same person tells the same tale
        Assert.Contains(near, l => l.Id == told.Id); Assert.True(book.Get(told.Id)!.Unlocked);                  // and the page is open for the hero
        Assert.Contains(Enumerable.Range(0, near.Count * 2).Select(p => Legends.Tell(book, x, y, p)!.Id), id => id != told.Id || near.Count == 1);   // others tell the others
        Assert.False(string.IsNullOrEmpty(told.Text)); Assert.NotNull(landmark);
        Assert.Null(Legends.Tell(book, -50_000_000, -50_000_000, 3));                                           // no legend within ten kilometres
    }

    [Fact]
    public void TheAutopilotAtARoadEncounterFightsOnlyWhenTheOddsRuleSaysSo_ElseItPaysOrFlees()
    {
        EncounterOption Fight(int odds) => new(EncounterChoice.Fight, "ENC_FIGHT") { OddsPm = odds };
        var pay = new EncounterOption(EncounterChoice.Pay, "ENC_PAY", 50); var flee = new EncounterOption(EncounterChoice.Flee, "ENC_FLEE");
        // the medium risk (500): fights from 500 permille up
        Assert.Equal(EncounterChoice.Fight, EncounterOutcomes.AutopilotChoose([pay, Fight(700), flee], 500, 1_000));
        Assert.Equal(EncounterChoice.Fight, EncounterOutcomes.AutopilotChoose([pay, Fight(500), flee], 500, 1_000));
        Assert.Equal(EncounterChoice.Pay, EncounterOutcomes.AutopilotChoose([pay, Fight(300), flee], 500, 1_000));           // a likely loss: pay the toll
        Assert.Equal(EncounterChoice.Flee, EncounterOutcomes.AutopilotChoose([pay, Fight(300), flee], 500, 20));               // nothing to pay with: run
        Assert.Equal(EncounterChoice.Flee, EncounterOutcomes.AutopilotChoose([Fight(-1), flee], 800, 0));                      // odds not estimated: never a fight
        Assert.Equal(EncounterChoice.Fight, EncounterOutcomes.AutopilotChoose([Fight(250), flee], 800, 0));                    // a high-risk hero takes worse odds
        Assert.Equal(EncounterChoice.GoOn, EncounterOutcomes.AutopilotChoose([new(EncounterChoice.Trade, "ENC_TRADE"), new(EncounterChoice.GoOn, "ENC_GOON")], 500, 0));
    }

    [Fact]
    public void TheContractHeroCommandIsAppendedAfterJustice_SoARecordedTraceKeepsItsMeaning()
    {
        Assert.True((byte)HeroCommandKind.Contract > (byte)HeroCommandKind.Justice);
        Assert.True((byte)HeroCommandKind.Contract > (byte)HeroCommandKind.Rule);   // step 3 merge order: Justice, Disease, Rule, Contract (appended members only, so a trace keeps its meaning)
        var c = Ravensguard.Sim.Contracts.ContractDesk.Take(7, 90_000);
        Assert.Equal(HeroCommandKind.Contract, c.Kind); Assert.Equal("ct:take|7", c.Tag);
        Assert.Equal(CommandOrigin.Human, c.Origin);
    }
}

/// <summary>The contracts' hour on a real new game: attached to the runtime, thirty game days run with no fault, whatever was posted is escrowed, and the coin and mass audits stay exact.</summary>
public class ContractRuntimeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void TheContractHourRunsOnARealWorldForThirtyDays_PostsOnlyWhatItCanEscrow_AndTheAuditsHold()
    {
        var w = Ravensguard.Sim.Tests.Seamless.SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Ravensguard.Sim.People.Populator.Populate(region, w.History, w.Content);
        var hero = Ravensguard.Sim.Characters.HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<Ravensguard.Content.OriginDef>().OrderBy(o => o.Order).First());
        var st = Ravensguard.Sim.Worlds.NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, hero, null);
        using var rt = new Ravensguard.Sim.Worlds.WorldRuntime(st, w.Content, new Ravensguard.Sim.Worlds.MemoryRegionPager());
        Ravensguard.Sim.Contracts.ContractLink.Attach(rt);
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 30 * 24));
        Assert.Null(rt.Fault);
        var book = rt.State.Contracts;
        output.WriteLine(book == null ? "no contract posted in 30 days" : $"{book.Contracts.Count} contracts: " + string.Join(", ", book.Contracts.Values.GroupBy(x => x.Kind + ":" + x.State).Select(g => g.Key + " " + g.Count())) + $"; {book.Contracts.Values.Select(x => x.TargetOwner).Distinct().Count()} owners, {book.Contracts.Values.Select(x => x.Good).Distinct().Count()} goods, by day: " + string.Join(" ", book.Contracts.Values.GroupBy(x => x.PostedDay).OrderBy(g => g.Key).Select(g => g.Count())) + $"; businesses {rt.State.Production!.Businesses.Count}, idle {rt.State.Production.Businesses.Count(b => b.IdleDays >= 2)}");
        if (book != null) foreach (var c in book.Contracts.Values.Where(c => c.Live)) Assert.True(rt.State.Economy!.Ledger.Balance(new Ravensguard.Sim.Economy.PurseId(c.Escrow)).Mites >= 0 && c.PayMites > 0);
        Assert.True(rt.State.Economy!.Ledger.Audit().IsConserved); Assert.True(rt.State.Economy.MassAuditHolds());
    }
}
