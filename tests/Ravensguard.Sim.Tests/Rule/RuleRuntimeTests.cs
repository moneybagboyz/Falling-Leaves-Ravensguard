using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Rule;

/// <summary>A new game of seed 1234's embark region with its rule book seeded (P6-C3), as the runtime tests start from; each rig is its own world.</summary>
internal sealed class RuleWorldRig : IDisposable
{
    public SeamWorld W { get; }
    public WorldState State { get; }
    public WorldRuntime Runtime { get; }
    public RuleBook Book => State.Rule!;
    public ContentDatabase Content => W.Content;
    public HeroContext Ctx { get; }
    public HeroDesk Desk { get; } = new();

    public RuleWorldRig(bool runtime = true)
    {
        W = SeamWorld.Get(1234);
        var home = W.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(W.Continent, W.History, home, W.Settings);
        LandmarkPlacer.Place(region, W.History, W.Content);
        var pop = Populator.Populate(region, W.History, W.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, W.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        State = NewGameWorld.Create(W.Content, W.Continent, W.History, cs, hs, W.Settings, home.CellX, home.CellY, pop, h, null);
        State.Content = W.Content;
        Runtime = runtime ? new WorldRuntime(State, W.Content, new MemoryRegionPager()) : null!;
        Ctx = new HeroContext(State, State.Party!, W.Content);
    }

    public void Dispose() => Runtime?.Dispose();
    public EntityId Hero => State.Party!.Hero;
    public Ravensguard.Sim.Abstract.SimWorld Sim => State.HomeRecords!.Sim;

    public HeroResult Do(HeroCommand c, long sec)
    {
        var cmd = Desk.Issue(c with { IssuedSec = sec });
        Desk.ApplyDue(Ctx, cmd.Hour);
        return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
    }
}

/// <summary>Rule and income on the running world: the seeding pass for a new game and an older save (RI-1), the section's round trip and resume, the hero's commands, a season on the tick (RI-19).</summary>
public class RuleRuntimeTests(ITestOutputHelper output)
{
    [Fact]
    public void RI1_ANewGameHasItsFiefDeeds_AnOlderSaveDerivesTheSameBookWhenTheGameRuns_AndASavedBookIsNotDerivedAgain()
    {
        using var r = new RuleWorldRig(runtime: false);
        Assert.NotNull(r.State.Rule); Assert.NotEmpty(r.State.Rule!.Fiefs);
        Assert.Equal(r.State.Population.Region.Fiefs.Count, r.Book.Fiefs.Count);
        var file = WorldSave.Capture(r.State, "h", "0.1");
        Assert.Contains(RuleSave.SectionName, file.Sections.Keys);
        var older = new SaveFile(file.Header, file.Sections.Where(kv => kv.Key != RuleSave.SectionName).Select(kv => new KeyValuePair<string, byte[]>(kv.Key, kv.Value.ToArray())));
        var loaded = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(older)), r.Content, "h", null, r.W.Continent, r.W.History); loaded.Content = r.Content;
        Assert.Null(loaded.Rule); Assert.True(loaded.RuleSeedPending);                                          // marked, and the bytes of the rest are left alone
        Assert.Equal(file.Sections.Keys.Where(k => k != RuleSave.SectionName), WorldSave.Capture(loaded, "h", "0.1").Sections.Keys.Where(k => k != RuleSave.SectionName));
        using (new WorldRuntime(loaded, r.Content, new MemoryRegionPager()))
        {
            Assert.NotNull(loaded.Rule);
            Assert.Equal(r.Book.Hash(), loaded.Rule!.Hash());
            Assert.Contains(Phase6Seeding.LoadNote, loaded.LoadNotes);
        }
        var again = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), r.Content, "h", null, r.W.Continent, r.W.History);
        Assert.Equal(r.Book.Hash(), again.Rule!.Hash()); Assert.False(again.RuleSeedPending);
        // and a save with the section keeps its bytes: load and resave
        var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), r.Content, "h", null, r.W.Continent, r.W.History); st.Content = r.Content;
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1")));
    }

    [Fact]
    public void TheSectionRoundTrips_EveryTableOfIt_AndASaveInTheMiddleOfASeasonResumesToTheSameBook()
    {
        using var r = new RuleWorldRig();
        var b = r.Book; var f = b.Fiefs.Values.First(x => x.Holder >= 0);
        f.Receipts = [1, 2, 3, 4]; f.WardUntilDay = 99; f.Guardian = 7; f.Forfeited = false; f.RentInCoin = true; f.CommuteDay = 40;
        b.Customs[3] = 9; b.LastSubsidyDay[3] = 50; b.CreditClosedUntil[(1, 77)] = 300; b.EvictionNotices[4] = 90; b.EvictionOrders[5] = 91; b.DemesneHours[0] = 800; b.LabourCoin[0] = 450;
        b.MarketCharters[2] = (60, 12345); b.Extended.Add(3); b.Revoked.Add(4); b.Rulers[1] = 55;
        b.Dues.Add(new DueRow { Serial = b.NextDue++, Kind = DueKind.Aid, Payer = Party.OfFief(1), Payee = Party.OfRealm(2), AmountMites = 777, Units = 1, DueDay = 80, PaidMites = 7, State = DueState.Arrears, Entry = 9 });
        b.Loans.Add(new Loan { Serial = b.NextLoan++, Lender = Party.OfHousehold(new EntityId(5)), Borrower = Party.OfRealm(1), PrincipalMites = 1000, RatePmPerSeason = 40, StartDay = 3, TermSeasons = 4, Security = LoanSecurity.Deed, DeedId = 99, State = LoanState.Open });
        var council = new CouncilRecord { Serial = b.NextCouncil++, Day = 12, Realm = 1, Cause = "defence", Yes = 2, No = 1, Passed = true, AmountMites = 500 }; council.Votes.Add((5, true, 12)); council.Votes.Add((6, false, -3)); b.Councils.Add(council);
        b.Levies.Add(new LevyRow { Serial = 2, Realm = 1, DueDay = 90, TownPermille = 100, CountryPermille = 67 });
        b.Grievances[11] = new GrievanceRow { Points = 2, UntilDay = 120, Entry = 4 }; b.Opinions[(1, 2)] = -40;
        var bytes = SaveCodec.SerializeCompressed(RuleSave.Capture(b));
        var back = SaveCodec.DeserializeCompressed<RuleSave>(bytes).Restore();
        Assert.Equal(b.Hash(), back.Hash()); Assert.Equal(bytes, SaveCodec.SerializeCompressed(RuleSave.Capture(back)));
        Assert.False(back.IsEmpty); Assert.True(new RuleBook().IsEmpty);

        using var straight = new RuleWorldRig();
        long h0 = straight.Runtime.HourDone + (24 - straight.Runtime.HourDone % 24) % 24;
        straight.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 8));
        var save = SaveCodec.Encode(WorldSave.Capture(straight.State, "h", "0.1"));
        straight.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 40));                        // past two season starts
        var want = SaveCodec.SerializeCompressed(RuleSave.Capture(straight.Book));
        var st = WorldSave.Restore(SaveCodec.Decode(save), straight.Content, "h", null, straight.W.Continent, straight.W.History); st.Content = straight.Content;
        using var resumed = new WorldRuntime(st, straight.Content, new MemoryRegionPager());
        resumed.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 40));
        Assert.Equal(want, SaveCodec.SerializeCompressed(RuleSave.Capture(st.Rule!)));
        Assert.Equal(straight.Book.Hash(), st.Rule!.Hash());
        Assert.True(st.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void AWorldRunsTwoSeasonsWithTheRuleOn_CoinConserved_OfficialsPaid_AndTheSameTwice()
    {
        string Run()
        {
            using var r = new RuleWorldRig();
            long h0 = r.Runtime.HourDone + (24 - r.Runtime.HourDone % 24) % 24;
            var audit0 = r.State.Economy!.Ledger.Audit();
            r.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 31));
            var led = r.State.Economy!.Ledger;
            Assert.True(led.Audit().IsConserved); Assert.Equal(audit0.Seeded, led.Audit().Seeded);   // P7-CN1: the mints strike what the hearths send, so Minted moves; the audit stays exact
            Assert.True(led.Flow(TransferReason.Wage).Mites > 0);
            Assert.True(r.Book.LastDay > 0); Assert.Contains(r.Book.Fiefs.Values, f => f.Receipts[0] > 0);       // the receipts were rolled from the treasuries' books
            output.WriteLine($"offices {r.Book.Offices.Count(o => o.InOffice)} collected {r.Book.Offices.Sum(o => o.Collected)} skimmed {r.Book.Offices.Sum(o => o.Skimmed)} expected {r.Book.Offices.Sum(o => o.Expected)}");
            Assert.True(r.Book.Offices.Sum(o => o.Collected) > 0);
            return r.Book.Hash().ToString("X") + "/" + r.State.Chronicle.Hash();
        }
        string a = Run(), b = Run();
        output.WriteLine(a); Assert.Equal(a, b);
    }

    [Fact]
    public void TheHeroOnlyRulesWhatHeHolds_AFiefsLordSetsTheTaxAndChangesHisRentsOnceAYear_AOutsiderIsRefused()
    {
        using var r = new RuleWorldRig();
        long sec = r.Runtime.HourDone * 3600 + 600;
        int fief = r.Book.Fiefs.Values.First(f => f.Holder >= 0).Fief;
        var no = r.Do(RuleDesk.Level(fief, TaxLevel.High, sec), sec);
        Assert.Equal(HeroResultCode.Refused, no.Code); Assert.Contains("not your fief", no.Text);
        Assert.Equal(HeroResultCode.Refused, r.Do(RuleDesk.Council(0, "defence", sec + 1), sec + 1).Code);        // not a ruler
        Assert.Equal(HeroResultCode.Refused, r.Do(RuleDesk.Customs(0, 12, sec + 2), sec + 2).Code);
        r.Book.Fiefs[fief].Holder = (long)r.Hero.Value;                                                         // the diplomacy step grants him the fief
        var ok = r.Do(RuleDesk.Level(fief, TaxLevel.High, sec + 3), sec + 3);
        Assert.Equal(HeroResultCode.Ok, ok.Code);
        Assert.Equal(TaxLevel.High, r.State.HomeRecords!.Economy.Taxes.LevelOfFief(fief));
        Assert.Equal(HeroResultCode.Ok, r.Do(RuleDesk.Rent(fief, true, sec + 4), sec + 4).Code);
        Assert.Contains("once a year", r.Do(RuleDesk.Rent(fief, false, sec + 5), sec + 5).Text);
        // a ruler calls a council and sets the customs; out-of-range customs are refused
        int realm = r.Book.Fiefs[fief].Realm; r.Book.Rulers[realm] = (long)r.Hero.Value;
        Assert.Equal(HeroResultCode.Ok, r.Do(RuleDesk.Customs(realm, 12, sec + 6), sec + 6).Code); Assert.Equal(12, r.Book.CustomsPct(realm));
        Assert.Equal(HeroResultCode.Invalid, r.Do(RuleDesk.Customs(realm, 40, sec + 7), sec + 7).Code);
        var council = r.Do(RuleDesk.Council(realm, "defence", sec + 8), sec + 8);
        Assert.Equal(HeroResultCode.Ok, council.Code); Assert.Single(r.Book.Councils);
        // an appointment: a real adult of the fief, who gets a roll of honesty and a wage
        var pt = r.State.Population.People; int person = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && pt.AgeYears(i, (int)(sec / 86400)) is >= 25 and < 60 && pt.Occupation[i] == Occupation.Labourer);
        var appoint = r.Do(RuleDesk.Appoint(OfficeKind.Steward, fief, (long)pt.IdOf(person).Value, sec + 9), sec + 9);
        Assert.Equal(HeroResultCode.Ok, appoint.Code);
        var steward = r.Book.OfficeOf(OfficeKind.Steward, fief)!; Assert.Equal(350, steward.WageMitesPerDay); Assert.Contains((ulong)steward.Holder, r.Book.Honesty.Keys);
        Assert.Equal(HeroResultCode.Ok, r.Do(RuleDesk.Dismiss(steward.Serial, sec + 10), sec + 10).Code); Assert.False(steward.InOffice);
        Assert.True(r.State.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void TheLedgersFiefTab_ShowsAHeldFiefsBooks_ReceiptsArrearsOfficialsDuesAndLoans_AndOnlyThePolicyOfAFiefHeDoesNotHold()
    {
        using var r = new RuleWorldRig();
        long h0 = r.Runtime.HourDone + (24 - r.Runtime.HourDone % 24) % 24;
        r.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 20));                               // a season rolled: there are receipts to show
        int day = (int)(r.Runtime.HourDone / 24);
        var outsider = RuleLedger.Rows(r.State, day);
        Assert.NotEmpty(outsider); Assert.All(outsider, row => Assert.Equal(Ravensguard.Sim.Production.LedgerTab.Fief, row.Tab));
        Assert.True(outsider.Count <= 3, "a traveller sees a fief's policy, not its books");     // only the policy of the fief he lives in
        int fief = r.Book.Fiefs.Values.First(f => f.Holder >= 0 && f.Realm >= 0).Fief;
        r.Book.Fiefs[fief].Holder = (long)r.Hero.Value;
        var c = RuleWiring.Context(r.Sim, r.Book, RuleChron.Silent);
        var bailiff = r.Book.OfficeOf(OfficeKind.Bailiff, fief); if (bailiff != null) { bailiff.Expected = 1000; bailiff.Collected = 400; }
        RuleDues.AddDue(c, DueKind.Relief, Party.OfFief(fief), Party.OfRealm(r.Book.Fiefs[fief].Realm), 5000, fief, day - 2, EntityId.None).State = DueState.Arrears;
        var rows = RuleLedger.Rows(r.State, day);
        Assert.True(rows.Count > outsider.Count);
        Assert.Contains(rows, x => x.Cells[0] == "@ru_row_arrears"); Assert.Contains(rows, x => x.Cells[0] == "@ru_due_relief" && x.Alerts != Ravensguard.Sim.Production.OwnerAlert.None);
        if (bailiff != null) Assert.Contains(rows, x => x.Cells[0] == "@ru_office_bailiff" && x.Alerts == Ravensguard.Sim.Production.OwnerAlert.Shortfall);
    }
}
