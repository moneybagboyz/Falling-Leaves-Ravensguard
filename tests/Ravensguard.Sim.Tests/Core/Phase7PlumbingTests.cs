using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Abstract;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>P7-0A (overview 3, 7, 8.1, 8.4): the shared plumbing. Enums grow append-only (every Phase 6 value keeps its number, the new ones follow in the order of 8.1), the new tick slots, the seventh difficulty value and the Phase 7 seeding skeleton.</summary>
public class Phase7PlumbingTests
{
    private static void Appended<T>(T lastOld, params string[] added) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        int at = Array.IndexOf(names, lastOld.ToString());
        Assert.True(at >= 0);
        // the new names follow the last Phase 6 value directly and in order, so no value in front of them moved
        Assert.Equal(added, names.Skip(at + 1).Take(added.Length));
        Assert.Equal(at + 1 + added.Length, names.Length);
        for (int i = 0; i < added.Length; i++) Assert.Equal(at + 1 + i, Convert.ToInt32(Enum.Parse<T>(added[i])));
        Assert.Equal(at, Convert.ToInt32(lastOld));
    }

    [Fact]
    public void EnumAppends_FollowOverview8_1_InOrder_AndEveryPhase6ValueKeepsItsNumber()
    {
        Appended(TransferReason.RelicSale, "Exchange", "Purveyance", "Scutage", "Plunder");
        Appended(DestroyReason.Other, "Waste", "Trampled");
        Appended(CauseKind.Illness, "Industry", "Supply", "Campaign", "Desertion", "Siege", "Naval");
        Appended(Occupation.Monk, "Soldier", "Bandit", "Sailor");
        Appended(Skill.Prospecting, "Seamanship", "Engineering");
        Appended(FAct.Aiming, "Asleep");
        Appended(Formation.FallBack, "Column", "Wedge", "Schiltron", "WagonFort");
        Appended(Footing.Portal, "Stakes", "Ditch", "Rubble");
        Appended(LedgerTab.Fief, "Industry");
        Appended(DueKind.Labour, "Scutage");
        Appended(OfficeKind.Treasurer, "MintMaster");
        Appended(HeroCommandKind.Unrest, "Industry", "Coin", "WarSupply", "Army", "Soldier", "Naval");
        // a few absolute numbers older saves hold (the first of each block of values)
        Assert.Equal(0, (int)CauseKind.Fire); Assert.Equal(0, (int)Formation.Auto); Assert.Equal(4, (int)Formation.FallBack); Assert.Equal(9, (int)Footing.Portal);
        Assert.Equal(6, (int)OfficeKind.Treasurer); Assert.Equal(8, (int)DueKind.Labour);
    }

    [Fact]
    public void TheLedgerAndMassLedger_SizeTheirCountersFromTheEnums_SoOlderSavesPad()
    {
        Assert.Equal(Enum.GetValues<TransferReason>().Length, Ledger.TransferReasons);
        Assert.Equal(Enum.GetValues<DestroyReason>().Length, MassLedger.DestroyReasons);
        Assert.Equal((int)TransferReason.Plunder + 1, Ledger.TransferReasons);
    }

    [Fact]
    public void SystemOrder_HoldsTheOverview8_4Slots_InTheirPhasesOrder()
    {
        Assert.Equal(1_100, SystemOrder.Armies); Assert.Equal(1_120, SystemOrder.Ships); Assert.Equal(1_140, SystemOrder.Sieges); Assert.Equal(1_150, SystemOrder.HostSupply); Assert.Equal(1_160, SystemOrder.ArmiesDaily);
        Assert.Equal(5_210, SystemOrder.MintDaily); Assert.Equal(5_230, SystemOrder.SoldiersDaily); Assert.Equal(5_240, SystemOrder.SiegesDaily); Assert.Equal(6_970, SystemOrder.S11War);
        Assert.True(SystemOrder.Caravans < SystemOrder.Armies && SystemOrder.Armies < SystemOrder.Ships && SystemOrder.Ships < SystemOrder.Sieges && SystemOrder.Sieges < SystemOrder.HostSupply && SystemOrder.HostSupply < SystemOrder.ArmiesDaily && SystemOrder.ArmiesDaily < SystemOrder.Production);
        Assert.True(SystemOrder.DailyGlobalsTaxes < SystemOrder.MintDaily && SystemOrder.MintDaily < SystemOrder.SoldiersDaily && SystemOrder.SoldiersDaily < SystemOrder.SiegesDaily && SystemOrder.SiegesDaily < SystemOrder.S4Taxes);
        Assert.True(SystemOrder.S10Politics < SystemOrder.S11War && SystemOrder.S11War < SystemOrder.Outbox);
    }

    [Fact]
    public void Difficulty_SeventhValue_IndustryOutput_AndASixValueArrayReadsAs100()
    {
        var d = Difficulty.Default;
        Assert.Equal(100, d.IndustryOutputPct); Assert.True(d.IsDefault);
        Assert.Equal(6, d.ToArray().Length);                                                           // a default world keeps its bytes
        // every save so far holds six values: still read, industry output 100
        var six = (d with { Plague = PlagueSeverity.Rare, AgingPace = 3 }).ToArray();
        Assert.Equal(Difficulty.ValueCount, six.Length);
        Assert.Equal(d with { Plague = PlagueSeverity.Rare, AgingPace = 3 }, Difficulty.FromArray(six));
        Assert.Equal(100, Difficulty.FromArray(six).IndustryOutputPct);
        // fewer than six is still the default
        Assert.Equal(d, Difficulty.FromArray([1, 1, 1, 1]));
        // the seventh value, 100 to 500 in steps of 100, round trips and clamps
        foreach (int pct in new[] { 100, 200, 300, 400, 500 })
            Assert.Equal(pct, Difficulty.FromArray((d with { IndustryOutputPct = pct }).ToArray()).IndustryOutputPct);
        Assert.Equal(7, (d with { IndustryOutputPct = 300 }).ToArray().Length);
        Assert.Equal(500, Difficulty.FromArray([.. six, 99]).IndustryOutputPct);
        Assert.Equal(100, Difficulty.FromArray([.. six, -3]).IndustryOutputPct);
        Assert.False((d with { IndustryOutputPct = 200 }).IsDefault);
    }

    [Fact]
    public void Difficulty_IndustryOutput_SavesAndLoads_AndADefaultOneAddsNoSection()
    {
        var w = EconFixture.WorldWith(null);
        Assert.False(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0").Sections.ContainsKey(DifficultySave.SectionName));
        w.Difficulty = w.Difficulty with { IndustryOutputPct = 300 };
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        Assert.True(file.Sections.ContainsKey(DifficultySave.SectionName));
        var back = WorldSave.Restore(file, EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(300, back.Difficulty.IndustryOutputPct);
    }

    private sealed class FakeStep(string name, string section) : IPhase7SeedStep
    {
        public int Runs; public Phase7SeedContext? Last;
        public string Name => name; public string Section => section;
        public bool Seed(Phase7SeedContext ctx) { Runs++; Last = ctx; return true; }
    }

    [Fact]
    public void Phase7Seeding_NoStepsRegistered_ChangesNothingAndSaysNothing_AndTheSectionRegistryHasTen()
    {
        Assert.Empty(Phase7Seeding.DefaultSteps);
        Assert.Equal(10, Phase7Seeding.SectionNames.Length); Assert.Equal(10, Phase7Seeding.SectionNames.Distinct().Count());
        Assert.Empty(Phase7Seeding.SectionNames.Intersect(Phase6Seeding.SectionNames));
        var st = EconFixture.WorldWith(null);
        Assert.Empty(Phase7Seeding.Run(st, EconFixture.Content, null));
        Assert.Empty(Phase7Seeding.Run(st, EconFixture.Content, ["meta", "people"]));
        Assert.True(Phase7Seeding.HasAnySection(["meta", "coinage"])); Assert.False(Phase7Seeding.HasAnySection(["meta", "faith"]));
    }

    [Fact]
    public void Phase7Seeding_RunsAtANewGameAndAtTheFirstLoadOfAnOldSave_ButNotWhenItsSectionIsSaved()
    {
        var st = EconFixture.WorldWith(null);
        var a = new FakeStep("a", "coinage"); var b = new FakeStep("b", "armies");
        Assert.Empty(Phase7Seeding.Run(st, EconFixture.Content, null, [a, b]));                        // a new game: both run, no load note
        Assert.Equal(1, a.Runs); Assert.Equal(1, b.Runs); Assert.False(a.Last!.FromSave); Assert.Equal(st.Seed, a.Last.Seed);
        var notes = Phase7Seeding.Run(st, EconFixture.Content, ["meta", "people", "unrest"], [a, b]);   // a Phase 6 save: both run and the report says so
        Assert.Equal(["Phase 7 state derived at load"], notes); Assert.True(a.Last!.FromSave);
        Phase7Seeding.Run(st, EconFixture.Content, ["meta", "armies"], [a, b]);                        // a save holding the armies section: only the coinage step derives
        Assert.Equal(3, a.Runs); Assert.Equal(2, b.Runs);
    }

    [Fact]
    public void Phase7Seeding_TheDefaultPassAtLoad_LeavesAPhase6SavesNotesAndBytesAlone()
    {
        var w = AbstractFixture.NewWorld();
        var ws = AbstractFixture.AsWorldState(w, GameTime.FromGameHours(w.State.HourDone));
        var file = WorldSave.Capture(ws, EconFixture.ContentHash, "0.1");
        foreach (var s in Phase7Seeding.SectionNames) Assert.DoesNotContain(s, file.Sections.Keys);
        var back = WorldSave.Restore(file, EconFixture.Content, EconFixture.ContentHash);
        Assert.DoesNotContain(Phase7Seeding.LoadNote, back.LoadNotes);
        var again = WorldSave.Capture(back, EconFixture.ContentHash, "0.1");
        Assert.Equal(file.Sections.Keys.OrderBy(x => x, StringComparer.Ordinal), again.Sections.Keys.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var k in file.Sections.Keys) Assert.True(file.Sections[k].Span.SequenceEqual(again.Sections[k].Span), $"section {k} changed after a load");
    }

    [Fact]
    public void Phase7Trail_IsEmptyUntilFilled_SoNoGoldenLineMoves()
    {
        Assert.Equal("", Ravensguard.Sim.Diagnostics.Phase7Trail.Hash(1));
        Assert.Empty(Ravensguard.Sim.Diagnostics.Phase7Trail.Parts(1234));
    }

    // ---- the by-reason tables: a world that never used the Phase 7 reasons saves the bytes it did before; a save from before them loads; a world that uses them round trips

    private const int LegacyReasons = (int)TransferReason.RelicSale + 1;

    [Fact]
    public void LedgerFlow_IsSavedAsWideAsTheHighestReasonUsed_AndRoundTrips()
    {
        var l = new Ledger();
        var a = l.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 1)); var b = l.Open(PurseKind.Treasury, EntityId.Make(IdKind.Other, 1));
        l.Mint(a, new Coin(10_000), SourceReason.Seed);
        Assert.True(l.Transfer(a, b, new Coin(100), TransferReason.Tithe));
        var old = l.Export();
        Assert.Equal(LegacyReasons, old.FlowCoin.Length); Assert.Equal(LegacyReasons, old.FlowCount.Length);     // no Phase 7 reason used: the pre-Phase 7 width
        Assert.True(l.Transfer(a, b, new Coin(300), TransferReason.Plunder));
        var wide = l.Export();
        Assert.Equal((int)TransferReason.Plunder + 1, wide.FlowCoin.Length);
        var back = Ledger.Import(wide);
        Assert.Equal(300, back.Flow(TransferReason.Plunder).Mites); Assert.Equal(100, back.Flow(TransferReason.Tithe).Mites);
        Assert.Equal(1, back.FlowCount(TransferReason.Plunder));
        Assert.Equal(100, Ledger.Import(old).Flow(TransferReason.Tithe).Mites);                                    // a Phase 6 save loads
    }

    [Fact]
    public void MassLedger_DestroyedIsSavedCompact_ALegacyWidthRestores_AndANewReasonRoundTrips()
    {
        var m = new MassLedger(3);
        var destroy = typeof(MassLedger).GetMethod("Destroyed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [typeof(ushort), typeof(DestroyReason), typeof(long)])!;
        destroy.Invoke(m, [(ushort)1, DestroyReason.Eaten, 7L]); destroy.Invoke(m, [(ushort)2, DestroyReason.Other, 9L]);
        Assert.Equal(3 * MassLedger.LegacyDestroyReasons, m.DestroyedState().Length);                              // no Phase 7 reason used: the old width, the old bytes
        var restored = new MassLedger(3); restored.Restore(m.CreatedState(), m.DestroyedState());
        Assert.Equal(7, restored.Destroyed(1, DestroyReason.Eaten)); Assert.Equal(9, restored.Destroyed(2, DestroyReason.Other)); Assert.Equal(0, restored.Destroyed(2, DestroyReason.Waste));
        destroy.Invoke(m, [(ushort)0, DestroyReason.Trampled, 4L]);
        Assert.Equal(3 * MassLedger.DestroyReasons, m.DestroyedState().Length);
        var wide = new MassLedger(3); wide.Restore(m.CreatedState(), m.DestroyedState());
        Assert.Equal(4, wide.Destroyed(0, DestroyReason.Trampled)); Assert.Equal(9, wide.Destroyed(2, DestroyReason.Other)); Assert.Equal(m.TotalDestroyed(2), wide.TotalDestroyed(2));
        Assert.Equal(-1, MassLedger.DestroyedWidth(3, 3 * MassLedger.DestroyReasons + 3));
    }

    [Fact]
    public void TreasuryTables_AreSavedCompact_AndANewReasonSurvivesASaveAndALoad()
    {
        var s = new Ravensguard.Sim.Taxes.TaxState(); s.Shape(2, 1, 3);
        s.Income[1 * s.Reasons + (int)TransferReason.Tithe] = 11;
        var old = Ravensguard.Sim.Taxes.TaxSave.Capture(s);
        Assert.Equal(LegacyReasons, old.Reasons); Assert.Equal(3 * LegacyReasons, old.Income.Length);              // unchanged bytes for a world that never used a Phase 7 reason
        s.Expense[2 * s.Reasons + (int)TransferReason.Plunder] = 5;
        var wide = Ravensguard.Sim.Taxes.TaxSave.Capture(s);
        Assert.Equal((int)TransferReason.Plunder + 1, wide.Reasons);
        foreach (var save in new[] { old, wide })
        {
            var back = save.Restore(); back.Shape(2, 1, 3);
            Assert.Equal(Ledger.TransferReasons, back.Reasons);
            Assert.Equal(11, back.Income[1 * back.Reasons + (int)TransferReason.Tithe]);
        }
        var b2 = wide.Restore(); b2.Shape(2, 1, 3);
        Assert.Equal(5, b2.Expense[2 * b2.Reasons + (int)TransferReason.Plunder]);
    }

    [Fact]
    public void HeroSave_LeavesAnUntrainedPhase7SkillOut_ALoadReadsItAs0_AndATrainedOneIsKept()
    {
        var skills = Ravensguard.Sim.Characters.HeroRecord.SkillIds.ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
        var hero = new Ravensguard.Sim.Characters.HeroRecord { Name = "H", Origin = "x", Attributes = [50, 50, 50, 50, 50], Skills = skills };
        var save = hero.ToSave();
        Assert.Equal(Ravensguard.Sim.Characters.HeroRecord.SkillIds.Count - 2, save.SkillIds.Length);              // the two Phase 7 skills are not written while 0
        Assert.DoesNotContain("seamanship", save.SkillIds);
        Assert.Equal(0, Ravensguard.Sim.Characters.HeroRecord.FromSave(save).Skill("engineering"));
        skills["seamanship"] = 30;
        var trained = Ravensguard.Sim.Characters.HeroRecord.FromSave(hero.ToSave());
        Assert.Equal(30, trained.Skill("seamanship"));
    }
}
