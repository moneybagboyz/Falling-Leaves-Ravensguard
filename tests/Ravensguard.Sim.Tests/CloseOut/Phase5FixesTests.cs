using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Hero;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Succession;
using Xunit.Abstractions;
using Sk = Ravensguard.Sim.People.Skill;

namespace Ravensguard.Sim.Tests.CloseOut;

/// <summary>
/// Phase 5 close-out, part 1 (docs/reviews/phase-5-open.md): one test for each small defect fixed at its cause, each failing on the code before the fix. The speed budgets are in
/// <c>ConstructionBudgetTests</c>; the contract builder's bid in <c>ContractTests</c>.
/// </summary>
public class Phase5FixesTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ 1a: the heir does not keep the dead hero's craft

    [Fact]
    public void TheHeir_StartsFromHisOwnSkillsAndRecipes_NotTheDeadHeros()
    {
        var r = InheritanceTests.Rig.Of(InheritanceTests.Game(1234, "disgraced_squire"));
        int son = r.Person(Ravensguard.Sim.People.Sex.Male, 17);
        r.Pt.Father[son] = r.HeroRow; r.St.Population.ResetKin();
        r.St.Succession!.Kin.Add(new HeroKinLink(r.Pt.IdOf(son), KinRelation.Child));
        var cs = r.St.Crafting ??= new CraftState();
        var hero = r.Ctx.Party.Hero;
        cs.Know.Learn(hero, 7); cs.Know.SetPm(hero, 3, 800); cs.Know.AddTry(hero, 7); cs.Know.AddWatch(hero, 7, 600);
        cs.Book.Set(hero, Sk.Smithing, 90 * CraftMath.MicroPerLevel);
        var other = EntityId.Make(IdKind.Person, 9_000_001UL);
        cs.Know.Learn(other, 7); cs.Book.Set(other, Sk.Smithing, 40 * CraftMath.MicroPerLevel);
        Assert.Equal(90, cs.Book.Level(hero, Sk.Smithing));
        Assert.True(r.Die(r.Pt.IdOf(son)).Ok);
        Assert.False(cs.Know.Learnt(hero, 7));
        Assert.Equal(0, cs.Know.Pm(hero, 3)); Assert.Equal(0, cs.Know.WatchSeconds(hero, 7)); Assert.Equal(0, cs.Know.Tries(hero, 7));
        Assert.DoesNotContain(cs.Book.MicroRows(), row => row.Person == hero.Value);
        Assert.NotEqual(90, cs.Book.Level(hero, Sk.Smithing));                       // the heir's own level (the Base rule reads his record)
        Assert.True(cs.Know.Learnt(other, 7)); Assert.Equal(40, cs.Book.Level(other, Sk.Smithing));      // nobody else's changed
    }

    // ------------------------------------------------------------------ 1b: no trailing space in a name without a family name

    [Fact]
    public void AName_WithoutAFamilyName_HasNoTrailingSpace()
    {
        var r = InheritanceTests.Rig.Of(InheritanceTests.Game(1234, "disgraced_squire"));
        int p = r.Person(Ravensguard.Sim.People.Sex.Male, 30);
        string given = r.Pt.Given[p];
        r.Pt.Family[p] = ""; r.Pt.Byname[p] = "";
        Assert.Equal(given, r.Pt.FullName(p));
        r.Pt.Byname[p] = "the Red";
        Assert.Equal(given + " the Red", r.Pt.FullName(p));
        r.Pt.Family[p] = "Vane"; r.Pt.Byname[p] = "";
        Assert.Equal(given + " Vane", r.Pt.FullName(p));
    }

    // ------------------------------------------------------------------ 1c: an untempered blade breaks on a rigid stop, in the fight

    private static int Breaks(FightFixture fx, bool brittle, out int stops)
    {
        int broke = 0; stops = 0;
        for (int i = 0; i < 300; i++)
        {
            var w = new FightWorld(fx.C, (ulong)(500 + i), (ulong)(i + 1), gameSec0: 8 * 3600) { RecordEvents = false };
            int s = 0;
            w.OnBlow = (_, _, res, _) => { if (res.RigidStop) s++; };
            var a = w.Add(new FighterKit
            {
                Name = "A", Team = 0, Weapon = new WeaponInstance("arming_sword", "steel", ItemQuality.Common, brittle, 0x5EED0000UL + (ulong)i), Worn = FightCalibration.Padded.Worn,
                Skill = 60, X = -1000, FaceX = FightMath.One,
            });
            w.Add(new FighterKit { Name = "B", Team = 1, Weapon = new WeaponInstance("arming_sword", "steel"), Worn = FightCalibration.Plated.Worn, Skill = 60, X = 1000, FaceX = -FightMath.One });
            while (!w.IsOver && w.Tick < 60 * 20) w.StepWithAi();
            stops += s;
            if (a.Broke) broke++;
        }
        return broke;
    }

    [Collection("fight")]
    public sealed class BrittleInTheFight(FightFixture fx)
    {
        [Fact]
        public void AnUntemperedBlade_BreaksWhenPlateStopsIt_ATemperedOneNever()
        {
            int brittle = Breaks(fx, true, out int stops), tempered = Breaks(fx, false, out _);
            Assert.True(stops > 200, $"the plate stopped {stops} blows");
            Assert.True(brittle >= 5, $"{brittle} of 300 brittle blades broke");
            Assert.Equal(0, tempered);
        }
    }

    // ------------------------------------------------------------------ 1d: a growth project's business kind follows the content

    [Fact]
    public void AGrowthProjects_BusinessKind_IsRemappedByName_WhenTheKindTableMoves()
    {
        var db = EconFixture.Content;
        var v8 = LegacyIdTables.V8.Get(IdTables.Businesses)!; var cur = IdTables.Of(db).Tables.Get(IdTables.Businesses)!;
        int moved = Array.FindIndex(v8, id => Array.IndexOf(cur, id) != Array.IndexOf(v8, id));
        Assert.True(moved >= 0, "the eight founded-only kinds moved some index");
        string id = v8[moved];
        var g = new GrowthSave { PBusiness = [moved, 5, -1, 6] };
        var file = new SaveFile(new SaveHeader(), new Dictionary<string, byte[]> { [WorldSave.GrowthSection] = SaveCodec.SerializeCompressed(g) });      // no "tables" section: the frozen v8 numbering
        var mapped = ContentRemap.Apply(file, db);
        var back = SaveCodec.DeserializeCompressed<GrowthSave>(mapped.File.Sections[WorldSave.GrowthSection]);
        Assert.Equal(Array.IndexOf(cur, id), back.PBusiness[0]);
        Assert.Equal(5, back.PBusiness[1]); Assert.Equal(-1, back.PBusiness[2]); Assert.Equal(6, back.PBusiness[3]);
        Assert.NotEqual(moved, back.PBusiness[0]);
    }

    // ------------------------------------------------------------------ 1e: the vehicles are in the live hash trail

    [Fact]
    public void TheLiveStateHash_ChangesWithAVehiclesPlace_AndIsUnchangedWhileThereAreNone()
    {
        var g = new LevelGrid(40, 20, 0, 1);
        g.FillRect(0, 0, 39, 19, 0, true);
        var w = new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        ulong none = LiveStateHash.Compute(w);
        var econ = EconFixture.NewEconomy(false);
        var book = new RideBook(EconFixture.Content, econ, 7);
        w.AttachRide(book);
        Assert.Equal(none, LiveStateHash.Compute(w));                                // an empty ride book leaves the trail as it was
        var (a, v) = VehicleSites.Order(book, "ox_wagon", EntityId.Make(IdKind.Person, 77), 10_000, 20_000);
        Assert.Equal(SiteAnswer.Ok, a);
        ulong with = LiveStateHash.Compute(w);
        Assert.NotEqual(none, with);
        v!.XMm += 1500;
        Assert.NotEqual(with, LiveStateHash.Compute(w));
    }

    // ------------------------------------------------------------------ 1f: a vehicle site's crew is paid through the ledger

    [Fact]
    public void AVehicleSitesCrew_IsPaidByTheOwnerEachHourTheyWork_AndTheSiteStandsWhenHeCannotPay()
    {
        var econ = EconFixture.NewEconomy(false);
        var b = new RideBook(EconFixture.Content, econ, 7);
        var owner = EntityId.Make(IdKind.Person, 77); var hand = EntityId.Make(IdKind.Person, 78);
        var ids = new IdAllocator();
        var (a, v) = VehicleSites.Order(b, "ox_wagon", owner, 10_000, 20_000);
        Assert.Equal(SiteAnswer.Ok, a);
        foreach (var part in VehicleSites.Wanted(v!, b.Templates)) VehicleSites.Deposit(b, econ.Items, v!, VehicleDesign.VehicleBuildTests.PartItem(econ, ids, part, owner), owner);
        var site = b.Works.Sites[v!.Id];
        site.Crew.Add(hand); site.CrewSkill.Add(60);
        var led = econ.Ledger; var op = led.Open(PurseKind.Person, owner); var hp = led.Open(PurseKind.Person, hand);
        long wage = Commissions.WagePerHour(60);
        Assert.True(wage > 0);
        led.Mint(op, new Coin(wage * 3 + 5), SourceReason.Seed);
        long total = led.Audit().InPurses.Mites;
        long hour = 9;                                                                // 09:00: a working hour
        for (int i = 0; i < 3; i++) VehicleSites.SiteHour(b, econ.Items, site, hour + i, led);
        Assert.Equal(wage * 3, led.Balance(hp).Mites);
        Assert.Equal(5, led.Balance(op).Mites);
        long worked = site.WorkedMilli; Assert.True(worked > 0);
        VehicleSites.SiteHour(b, econ.Items, site, hour + 3, led);                    // he cannot pay the fourth hour: the hand does not work it
        Assert.Equal(worked, site.WorkedMilli); Assert.Equal(wage * 3, led.Balance(hp).Mites);
        Assert.Equal(total, led.Audit().InPurses.Mites);
        // his own hours are free
        site.Crew.Clear(); site.CrewSkill.Clear(); site.Crew.Add(owner); site.CrewSkill.Add(50);
        VehicleSites.SiteHour(b, econ.Items, site, hour + 4, led);
        Assert.Equal(5, led.Balance(op).Mites);
    }

    // ------------------------------------------------------------------ 1g: the Ledger values a leasehold at what is left in the lease

    [Fact]
    public void ALeasehold_IsWorthItsDuesForTheSeasonsLeft_NotThePlotsPrice()
    {
        var r = InheritanceTests.Rig.Of(InheritanceTests.Game(1234, "disgraced_squire"));
        var deed = r.Lot(r.Town, 0, r.Ctx.Party.Hero, PurseKind.Person);
        long price = r.Land.ValueOf(deed);
        Assert.True(price > 1000);
        Assert.Equal(price, r.Land.HoldingValue(deed, r.Day));                       // a freehold: the plot's price
        deed.Tenure = Tenure.Leasehold; deed.DuesMitesPerSeason = 40; deed.LeaseEndDay = r.Day + 2 * TimeConst.DaysPerSeason;
        Assert.Equal(80, r.Land.HoldingValue(deed, r.Day));                          // two seasons of dues
        deed.DuesMitesPerSeason = 1_000_000;
        Assert.Equal(price, r.Land.HoldingValue(deed, r.Day));                       // never more than the plot
        deed.DuesMitesPerSeason = 40; deed.LeaseEndDay = r.Day - 1;
        Assert.Equal(0, r.Land.HoldingValue(deed, r.Day));                           // run out
        deed.LeaseEndDay = r.Day + 2 * TimeConst.DaysPerSeason;
        var row = r.Own.Ledger(r.Day).Tab(LedgerTab.Property).Single(x => x.Id == deed.Id);
        Assert.Equal(80, row.Keys[4]);                                            // the Property tab's value column
    }

    // ------------------------------------------------------------------ 1h: the game's loaded mods are what the load report compares

    [Fact]
    public void TheLoader_NamesTheModsItLoaded_InLoadOrder_ForTheSaveHeaderAndTheLoadReport()
    {
        var load = new ModLoader(ContentTypeRegistry.Default).Load([ContinentSettings.FindBaseContent()!]);
        Assert.True(load.Success);
        var m = Assert.Single(load.Manifests);
        Assert.Equal("base", m.Id); Assert.False(string.IsNullOrEmpty(m.Version));
        var installed = load.Manifests.Select(x => new ModEntry(x.Id, x.Version)).ToList();
        // a save made with another mod beside the base: the report names the one that is gone, and the one added
        var header = new SaveHeader { Mods = [new ModEntry("base", m.Version), new ModEntry("old_tools", "1.2.0")] };
        var compat = new SaveReader().CheckCompatibility(header, "x", installed);
        Assert.Equal(["old_tools"], compat.MissingMods.Select(x => x.Id).ToArray());
        Assert.Empty(compat.ExtraMods);
        var compat2 = new SaveReader().CheckCompatibility(new SaveHeader { Mods = [] }, "x", installed);
        Assert.Equal(["base"], compat2.ExtraMods.Select(x => x.Id).ToArray());
    }

    // ------------------------------------------------------------------ 3: a bid on a short book is priced at the book's own ask

    [Fact]
    public void ABid_OnAnEmptyBook_IsPricedAtTheBooksAsk_NotItsReference()
    {
        var db = EconFixture.Content; var pop = EconFixture.Pop;
        var e = EconomyState.Create(pop, db, EconFixture.Seed);
        var markets = new Ravensguard.Sim.Economy.Markets.MarketSystem(e, pop, EconFixture.Seed);
        var book = markets.AllBooks().First();
        ushort iron = e.Goods.IdOf("iron_bar");
        var gb = book.Goods[iron];
        gb.StockUnits = 0;
        long qty = 100_000;                                                              // 100 kg
        long refCost = (long)((System.Int128)qty * gb.RefMicro * (1000 + book.Money.SpreadHalfPermille) / 1000 / Ravensguard.Sim.Economy.Markets.PriceCurve.Micro);
        long ask = Ravensguard.Sim.Construction.ConstructionWorld.AskFor(book, iron, qty);
        output.WriteLine($"100 kg of iron on an empty book: ask {ask}, at its reference {refCost} ({ask * 100 / refCost}%)");
        Assert.True(ask >= refCost * 3, $"{ask} against {refCost} at the reference");         // an empty book asks its clamp (4 x), what the first months really cost
        // a book that holds some prices that part as it sells it and the rest at its ask
        gb.StockUnits = 40_000;
        long part = Ravensguard.Sim.Construction.ConstructionWorld.AskFor(book, iron, qty);
        Assert.True(part < ask, $"{part}");
        // a book that holds it all prices it as it sells it
        gb.StockUnits = 100_000_000;
        Assert.Equal(book.QuoteBuy(iron, qty), Ravensguard.Sim.Construction.ConstructionWorld.AskFor(book, iron, qty));
    }
}
