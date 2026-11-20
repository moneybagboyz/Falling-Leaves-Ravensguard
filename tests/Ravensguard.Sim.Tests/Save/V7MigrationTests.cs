using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Save;

/// <summary>
/// IQ-12 and the v7 -> v8 step (P5-A1, P5-A4) on a real v7 save: <c>Save/Data/v7_fight_in_progress.rgsave</c> was written by the Phase 4 code at 1a10645 before the bump (the Phase 4
/// review world with a ten-fighter road fight at tick 60, every weapon byte 0 = Standard and every worn byte 0), with the fight hash at load and at the fight's end recorded beside it.
/// </summary>
public class V7MigrationTests
{
    private static string Data(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tests", "Ravensguard.Sim.Tests", "Save", "Data", file))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "tests", "Ravensguard.Sim.Tests", "Save", "Data", file);
    }

    [Fact]
    public void IQ12_ARealV7SaveWithAFightInProgress_LoadsAsCommon_AndTheResumedFightHashesAsTheV7Run()
    {
        var raw = SaveCodec.Decode(File.ReadAllBytes(Data("v7_fight_in_progress.rgsave")));
        Assert.Equal(7, raw.Header.FormatVersion);
        var v7 = SaveCodec.DeserializeCompressed<FightRecord>(raw.Sections[FightRecord.SectionName].ToArray());
        Assert.All(v7.Fighters, f => { Assert.Equal(0, f.WeaponQuality); Assert.Equal(0, f.NowQuality); Assert.All(f.WornQualities, q => Assert.Equal(0, q)); });
        // the one step: the DF bytes become the one scale's (byte 0 Standard is Common, not Crude), the hero's row is marked for the load
        var file = SaveMigrator.CreateDefault().Migrate(raw);
        Assert.Equal(SaveFormat.CurrentVersion, file.Header.FormatVersion);
        var v8 = SaveCodec.DeserializeCompressed<FightRecord>(file.Sections[FightRecord.SectionName].ToArray());
        Assert.All(v8.Fighters, f =>
        {
            Assert.Equal((byte)ItemQuality.Common, f.WeaponQuality); Assert.Equal((byte)ItemQuality.Common, f.NowQuality);
            Assert.Equal([(byte)ItemQuality.Common, (byte)ItemQuality.Common], f.WornQualities);
        });
        Assert.Equal(HeroPerson.Pending, SaveCodec.Deserialize<Ravensguard.Sim.Characters.HeroSave>(file.Sections[WorldSave.HeroSection].ToArray()).PersonIndex);
        Assert.Equal(new byte[] { 2, 2, 3, 3, 3, 4, 4 }, Enumerable.Range(0, 7).Select(b => SaveMigrator.QualityV8((byte)b)).ToArray());

        // the world loads (the content of this build differs from the one the save was made with only by Phase 5 rows the regenerated world does not read, so the load is told the save's own hash)
        var st = WorldSave.Restore(file, EconFixture.Content, raw.Header.ContentHash);
        var pt = st.Population.People;
        int row = st.Hero!.PersonIndex;
        Assert.True(row >= 0);
        Assert.Equal(PersonStatus.Hero, pt.Status[row]);
        Assert.Equal(EconomyState.HeroOwner, pt.IdOf(row));
        foreach (var k in st.Succession!.Kin.Where(k => k.Relation == KinRelation.Parent))               // the Phase 4 kin the save held are the row's parents now
            Assert.True(pt.Father[row] == pt.IndexOf(k.Person) || pt.Mother[row] == pt.IndexOf(k.Person));

        // the resumed fight: Common is DF's standard (1,000), so the fight is the v7 fight, hash for hash, to its end
        var fc = new FightContent(new CombatTables(EconFixture.Content), EconFixture.Content);
        var resumed = st.Fight!.Restore(fc, st.Wounds);
        Assert.All(resumed.Fighters, f => Assert.Equal(ItemQuality.Common, f.Weapon!.Quality));
        Assert.Equal(60, resumed.Tick);
        Assert.Equal(0xEBF66B68E34AE6BAUL, resumed.Hash());
        for (int i = 0; i < 600 && !resumed.IsOver; i++) resumed.StepShared();
        Assert.Equal(237, resumed.Tick);
        Assert.Equal(0xB50E27CBAE0E5009UL, resumed.Hash());

        // and the migrated world is a v8 world: save, load, save give the same bytes, with the hero's row in the people section
        var bytes = SaveCodec.Encode(WorldSave.Capture(st, raw.Header.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, raw.Header.ContentHash);
        Assert.Equal(row, back.Hero!.PersonIndex);
        Assert.Equal(PersonStatus.Hero, back.Population.People.Status[row]);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, raw.Header.ContentHash, "0.1.0")));
    }
}
