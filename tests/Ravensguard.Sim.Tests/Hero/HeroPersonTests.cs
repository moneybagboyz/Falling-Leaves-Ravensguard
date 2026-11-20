using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Content;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Tests.Hero;

/// <summary>
/// P5-A4 (overview build step 1): the hero as a person row (inheritance 3, IN-1) and the hero's pack as real stores and item rows of the running world: crafting reads from it (CF-4), loot
/// goes into it (the Phase 4 exploration loot test reopened against the real pack), and both are in the save.
/// </summary>
public class HeroPersonTests
{
    private static WorldState NewGame(ulong seed, string origin, bool female, ContentDatabase? db = null)
    {
        db ??= EconFixture.Content;                                                          // P5-H: the tanner's widow is a base origin now (the test mod is gone)
        var w = SeamWorld.Get(seed);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var hero = HeroRecord.FromOrigin(female ? "Maud Tanner" : "Aldric Vane", female, db.Get<OriginDef>(origin));
        return NewGameWorld.Create(db, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, hero, null);
    }

    private static string RowText(PersonTable pt, int i) =>
        $"{pt.IdOf(i).Value}|{pt.Given[i]}|{pt.Family[i]}|{pt.Sex[i]}|{pt.BirthDay[i]}|{pt.Status[i]}|{pt.Household[i]}|{pt.Father[i]}|{pt.Mother[i]}|{pt.Spouse[i]}|{pt.DeathDay[i]}|{pt.HomeSettlement[i]}";

    // ------------------------------------------------------------------ IN-1

    [Fact]
    public void IN1_ANewGameAsTheTannersWidow_Seed7_IsAPersonRowWithHerHousehold_TheDeadHusband_And0To2ChildrenWithMotherTheHero()
    {
        var st = NewGame(7, "tanners_widow", true);
        var pt = st.Population.People; var hht = st.Population.Households; var hero = st.Hero!;
        int day = (int)st.Clock.Now.TotalDays;
        int row = hero.PersonIndex;
        Assert.True(row >= 0);
        Assert.Equal(EconomyState.HeroOwner, pt.IdOf(row));                                  // the id the purse, the pack and the wounds already use
        Assert.Equal(row, pt.IndexOf(EconomyState.HeroOwner));
        Assert.Equal(PersonStatus.Hero, pt.Status[row]);
        Assert.False(pt.IsAlive(row));                                                       // no NPC system takes her for a villager
        Assert.Equal(Sex.Female, pt.Sex[row]);
        Assert.Equal(("Maud", "Tanner"), (pt.Given[row], pt.Family[row]));
        Assert.InRange(pt.AgeYears(row, day), 28, 36);
        Assert.Equal(Occupation.Tanner, pt.Occupation[row]);
        Assert.Equal(hero.Attribute(HeroAttr.Toughness), pt.Attribute(row, PersonTable.ToughnessIdx));   // the row mirrors the record
        Assert.Equal(Skill.Tanning, pt.SkillAt(row, 0).Skill);
        // her household: of her own, at the start town, no dwelling and no deed; she heads it and is not one of its eating members
        int hh = pt.Household[row];
        Assert.True(hh >= 0);
        Assert.Equal(row, hht.Head[hh]);
        Assert.Equal(DwellingType.None, hht.Dwelling[hh]); Assert.Equal(DeedKind.None, hht.Deed[hh]);
        Assert.Equal(HeroPerson.StartSettlement(st.Population.Region, EconFixture.Content.Get<OriginDef>("tanners_widow")), hht.Settlement[hh]);
        Assert.Equal(SettlementClass.Town, st.Population.Region.Settlements[hht.Settlement[hh]].Class);
        Assert.DoesNotContain(row, hht.Members(hh));
        // the dead husband
        var kin = st.Succession!.Kin;
        int husband = pt.IndexOf(Assert.Single(kin, k => k.Relation == KinRelation.Spouse).Person);
        Assert.Equal(PersonStatus.Dead, pt.Status[husband]); Assert.Equal(Sex.Male, pt.Sex[husband]);
        Assert.Equal(row, pt.Spouse[husband]); Assert.True(pt.DeathDay[husband] < day);
        Assert.Equal(hh, pt.Household[husband]);
        // 0 to 2 children with Mother the hero, alive members of her household
        var children = kin.Where(k => k.Relation == KinRelation.Child).Select(k => pt.IndexOf(k.Person)).ToList();
        Assert.InRange(children.Count, 0, 2);
        foreach (int c in children)
        {
            Assert.Equal(row, pt.Mother[c]); Assert.Equal(husband, pt.Father[c]);
            Assert.Equal(PersonStatus.Alive, pt.Status[c]); Assert.Equal(hh, pt.Household[c]);
            Assert.InRange(pt.AgeYears(c, day), 6, 14);
            Assert.Contains(c, hht.Members(hh));
        }
        Assert.Equal(children.Count, hht.MemberCount[hh]);
        Assert.Contains(children.Count, new[] { 0, 1, 2 });
        // a pure function of the seed (stream 0x560 keyed on the seed and the hero's id): the same rows again, row for row
        var again = NewGame(7, "tanners_widow", true);
        var p2 = again.Population.People;
        Assert.Equal(pt.Count, p2.Count);
        for (int i = row; i < pt.Count; i++) Assert.Equal(RowText(pt, i), RowText(p2, i));
    }

    [Fact]
    public void IN1_TwoChildren_AreFedByTheirHousehold_AndTheOriginsKinFollowTheirRule()
    {
        var st = NewGame(7, "tanners_widow", true);
        var pt = st.Population.People; var hht = st.Population.Households; int row = st.Hero!.PersonIndex; int hh = pt.Household[row];
        var children = st.Succession!.Kin.Where(k => k.Relation == KinRelation.Child).Select(k => pt.IndexOf(k.Person)).ToList();
        Assert.Equal(2, children.Count);
        Assert.Equal(2, hht.MemberCount[hh]);
        Assert.True(st.Economy!.HouseholdStore[hh].Qty(st.Economy.Goods.IdOf("wheat")) > 0);        // a month of food, as every household had at world creation
        // the deserter has no kin; the disgraced squire a living father who heads a gentry household
        var soldier = NewGame(1234, "discharged_soldier", false, EconFixture.Content);
        Assert.Null(soldier.Succession);
        Assert.Equal(PersonStatus.Hero, soldier.Population.People.Status[soldier.Hero!.PersonIndex]);
        var squire = NewGame(1234, "disgraced_squire", false, EconFixture.Content);
        var sp = squire.Population.People; int srow = squire.Hero!.PersonIndex;
        var father = Assert.Single(squire.Succession!.Kin, k => k.Relation == KinRelation.Parent);
        int f = sp.IndexOf(father.Person);
        Assert.Equal(f, sp.Father[srow] >= 0 ? sp.Father[srow] : sp.Mother[srow]);
        Assert.Equal(PersonStatus.Alive, sp.Status[f]);
        Assert.Equal(SocialClass.Gentry, squire.Population.Households.Class[sp.Household[f]]);
        // an origin with no kin rows keeps the Phase 4 rule: the grown child of a household, whose head is the row's parent
        var peddler = NewGame(1234, "peddler", false, EconFixture.Content);
        var pp = peddler.Population.People; int prow = peddler.Hero!.PersonIndex;
        Assert.Contains(peddler.Succession!.Kin, k => k.Relation == KinRelation.Parent);
        Assert.True(pp.Father[prow] >= 0 || pp.Mother[prow] >= 0);
        Assert.Contains(peddler.Succession.Kin, k => k.Relation == KinRelation.Sibling);
    }

    [Fact]
    public void TheHerosRow_SavesLoadsAndSavesToTheSameBytes_AndTheLoadFindsIt()
    {
        var st = NewGame(7, "tanners_widow", true);
        int row = st.Hero!.PersonIndex;
        var bytes = SaveCodec.Encode(WorldSave.Capture(st, "widow", "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, "widow");
        back.Content = EconFixture.Content;                                                          // the people go in as a difference from the regenerated ones (as the game's runtime does)
        Assert.Equal(row, back.Hero!.PersonIndex);
        var pt = back.Population.People;
        Assert.Equal(PersonStatus.Hero, pt.Status[row]);
        Assert.Equal(EconomyState.HeroOwner, pt.IdOf(row));                                  // the foreign id is in the people section
        Assert.Equal(row, HeroPerson.RowOf(pt, back.Hero, EconomyState.HeroOwner));
        for (int i = row; i < pt.Count; i++) Assert.Equal(RowText(st.Population.People, i), RowText(pt, i));
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, "widow", "0.1.0")));
    }

    [Fact]
    public void WhenTheHeroDies_TheRowIsDead_AndTheHeirsRowIsTheHerosNow()
    {
        var st = NewGame(7, "tanners_widow", true);
        var ctx = new HeroContext(st, st.Party!, EconFixture.Content); var desk = new HeroDesk();
        var pt = st.Population.People; int row = st.Hero!.PersonIndex; int hh = pt.Household[row];
        long sec = st.Clock.Now.Seconds;
        // the children are under 16: the heir is a companion or nobody; make the elder child of age by the clock's view
        var heirs = SuccessionDesk.Candidates(st, st.Party!, (int)(sec / 86400) + 60 * 20);
        var heir = heirs.FirstOrDefault(c => c.Rule is SuccessionRule.Child or SuccessionRule.YoungChild);
        Assert.NotNull(heir);
        long later = sec + 86400L * 60 * 20;                                                  // twenty years on: the eldest child is an adult
        var cmd = desk.Issue(SuccessionDesk.HeirCommand(heir!.Person, 0, 0, later));
        desk.ApplyDue(ctx, cmd.Hour);
        Assert.True(desk.View!.Results.Last().Ok, desk.View.Results.Last().Text);
        Assert.Equal(PersonStatus.Dead, pt.Status[row]);                                     // the hero's own row is a dead person's now
        int hrow = st.Hero!.PersonIndex;
        Assert.Equal(heir.PersonIndex, hrow);
        Assert.Equal(PersonStatus.Hero, pt.Status[hrow]);                                    // never brought back as a returning emigrant
        Assert.NotEqual(row, hht(st).Head[hh]);
        static HouseholdTable hht(WorldState s) => s.Population.Households;
    }

    // ------------------------------------------------------------------ CF-4: crafting from the real pack, in the save

    [Fact]
    public void CF4_CraftFromTheRealPack_TheToolsAndGoodsAreThePacks_SaveLoadSaveIsByteIdenticalWithAJobInProgress_AndItGoesOnTheSame()
    {
        var w = new TradeWorld(); var st = w.State; st.Markets = null;
        var torch = w.Ctx.Recipes.Recipes.First(r => r.Id == "make_torch");
        w.Ctx.Pack.Create(w.G("sticks"), 1000, CreateReason.Seed); w.Ctx.Pack.Create(w.G("cloth_linen"), 300, CreateReason.Seed); w.Ctx.Pack.Create(w.G("tallow"), 200, CreateReason.Seed);
        var noTools = new CraftSiteSpec(200, true, [], [], []);
        // the screen sends no tool: the squire's pack has none that cuts, so nothing starts
        var refused = w.Do(new HeroCommand { Kind = HeroCommandKind.CraftStart, Recipe = torch.Index, Site = noTools });
        Assert.False(refused.Ok);
        // a dagger in the pack (an item row of the economy) is the knife the recipe needs
        var dagger = w.State.Party!.Carried.MakeItem(w.Econ, w.Ctx.Content, w.Ctx.Rules, w.Ctx.Factory, st.Ids, "dagger");
        Assert.Contains("knife", w.Ctx.PackTools());
        var started = w.Do(new HeroCommand { Kind = HeroCommandKind.CraftStart, Recipe = torch.Index, Site = noTools });
        Assert.True(started.Ok, started.Text);
        var job = new EntityId((ulong)started.Qty);
        long start = w.Sec;
        w.Sec += 90;
        w.Do(new HeroCommand { Kind = HeroCommandKind.CraftWork, Job = job, UntilSec = w.Sec });
        var j = st.Crafting!.Get(job)!;
        Assert.False(j.Closed); Assert.True(j.StepSeconds > 0);                                // in progress
        Assert.Equal(1000 - 400, w.Packed("sticks"));                                           // the components left the pack for the job's own store
        Assert.Equal(400, w.Econ.OtherStores[job].Qty(w.G("sticks")));
        // save, load, save: the same bytes; the job's store and the pack are in the economy section, the job in the crafting section
        var bytes = SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(400, back.Economy!.OtherStores[job].Qty(w.G("sticks")));
        Assert.Equal(600, back.Party!.Carried.Pack(back.Economy).Qty(w.G("sticks")));
        Assert.True(back.Economy.Items.IsLive(dagger));
        Assert.NotNull(back.Crafting!.Get(job));
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        // and the loaded world finishes the same torch as the world that went on
        var ctx2 = new HeroContext(back, back.Party, EconFixture.Content); var desk2 = new HeroDesk();
        long end = start + 3600;
        w.Sec = end; w.Do(new HeroCommand { Kind = HeroCommandKind.CraftWork, Job = job, UntilSec = end });
        var c2 = desk2.Issue(new HeroCommand { Kind = HeroCommandKind.CraftWork, Job = job, UntilSec = end, IssuedSec = end });
        desk2.ApplyDue(ctx2, c2.Hour);
        Assert.True(st.Crafting.Get(job)!.Closed);
        Assert.Equal(st.Crafting.Get(job)!.State, back.Crafting.Get(job)!.State);
        Assert.Equal(w.Packed("torch"), back.Party.Carried.Pack(back.Economy).Qty(w.G("torch")));
        Assert.Equal(w.Packed("ash"), back.Party.Carried.Pack(back.Economy).Qty(w.G("ash")));
    }

    // ------------------------------------------------------------------ the Phase 4 loot test, reopened against the real pack

    [Fact]
    public void ACrawlsHaulGoesIntoTheHerosRealPack_ThePersonRowOwnsIt_ItIsSaved_AndTheHeroFightsAndCraftsWithIt()
    {
        var st = NewGame(1234, "disgraced_squire", false, EconFixture.Content);
        var db = EconFixture.Content;
        var ctx = new HeroContext(st, st.Party!, db); var desk = new HeroDesk();
        var pt = st.Population.People; int row = st.Hero!.PersonIndex;
        long sec = st.Clock.Now.Seconds + 600;
        long mass0 = st.Party!.Carried.MassG(st.Economy!);
        var econ = st.Economy!;
        var loot = new List<LootItem> { new("longsword", "steel", 900, 1400, 900), new("chisel", "iron", 60, 250, 1000), new("silver_chalice", "", 420, 450, 1000) };
        var cmd = desk.Issue(SiteDesk.TakeCommand(42, loot, 0, sec));
        desk.ApplyDue(ctx, cmd.Hour);
        Assert.True(desk.View!.Results.Last().Ok, desk.View.Results.Last().Text);
        // the haul is item rows of the economy owned by the hero, whose person row has that id
        var owner = pt.IdOf(row);
        var rows = st.Party.Carried.Rows(st.Economy!).Select(i => st.Economy!.Items.At(i)).ToList();
        foreach (var shape in new[] { "longsword", "chisel", "silver_chalice" }) Assert.Contains(rows, r => r.Shape == shape && r.Owner == owner);
        Assert.Equal(mass0 + 1400 + 250 + 450, st.Party.Carried.MassG(econ));
        // the running world sees it: the fight takes the looted longsword, the crafting site the looted chisel
        var things = HeroPack.Things(econ, st.Party.Carried);
        var fc = new FightContent(new CombatTables(db), db);
        var kit = FightSetup.HeroKit(fc, st.Hero, [new WornItem("tunic", "linen")], owner.Value, carried: things);
        Assert.Equal("longsword", kit.Weapon!.ShapeId);
        Assert.Contains("chisel", ctx.PackTools());
        // and the save holds it: the rows and their owner come back
        var bytes = SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), db, EconFixture.ContentHash);
        back.Content = db;
        var backRows = back.Party!.Carried.Rows(back.Economy!).Select(i => back.Economy!.Items.At(i)).ToList();
        foreach (var shape in new[] { "longsword", "chisel", "silver_chalice" }) Assert.Contains(backRows, r => r.Shape == shape && r.Owner == back.Population.People.IdOf(back.Hero!.PersonIndex));
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
    }
}
