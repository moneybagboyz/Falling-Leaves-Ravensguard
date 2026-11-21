using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Trade;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Soldiers;

/// <summary>P7-SC-A golden tests (soldiers-and-companies.md 10): SC-1 to SC-6, with the saves, the desk and the conservation of items.</summary>
public class SoldierTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;
    private static LargeBuildingRulesDef Rules => Db.All<LargeBuildingRulesDef>().First();
    private const long Key = 4242;
    private static readonly ArmouryCapacity Big = new(60, 60, 60, 60, 60, 600, 240, 600, 360, 2400);

    /// <summary>Sizes by table: a piece's is what the test says (default 70,000), every body is 70,000.</summary>
    private sealed class TestFit : IKitFit
    {
        public readonly Dictionary<ulong, int> Fits = [];
        public int Body = 70_000;
        public int FitOf(ulong item) => Fits.GetValueOrDefault(item, 70_000);
        public int BodyOf(ulong person) => Body;
        public bool SetFit(ulong item, int cm3) { Fits[item] = cm3; return true; }
    }

    private sealed class Rig
    {
        public EconomyState Econ = EconFixture.NewEconomy();
        public IdAllocator Ids = new();
        public WorksBook Works = new();
        public SoldierBook Book = new();
        public TroopRoles Roles = new(Db);
        public Armouries Arm;
        public ArmouryFill Fill;
        public LevyGear Levies;
        public TestFit Fit = new();
        public Rig() { Arm = new Armouries(Works, Econ.Items, Econ.Goods, Db, Rules); Fill = new ArmouryFill(Roles, Arm, Econ.Items); Levies = new LevyGear(Roles, Fill, Econ.Items); }

        public static ulong Person(int n) => EntityId.Make(IdKind.Person, (ulong)n).Value;
        public static EntityId Household(int n) => EntityId.Make(IdKind.Household, (ulong)n);

        public EntityId Item(string shape, string material, int massG, ItemQuality q = ItemQuality.Common, int cond = 1000, EntityId? owner = null) =>
            Econ.Items.Add(Ids, shape, q, owner ?? Household(1), [new ItemComponent(material, massG, (short)cond)], conditionPermille: cond);

        public EntityId Stock(string shape, string material, int massG, ItemQuality q = ItemQuality.Common, int cond = 1000)
        {
            var id = Item(shape, material, massG, q, cond);
            Assert.True(Arm.Add(Key, Big, id), shape);
            return id;
        }

        public FillResult FillFrom(List<FillSoldier> s, int day, long lord = 0) => Fill.Apply(Key, Fill.Plan(Key, s, 0, Fit), Book, Fit, day, lord);
        public long TotalMassG() => Econ.Items.LiveRows().Sum(r => Econ.Items.MassG(r, Econ.Goods));
        public string Where(EntityId item)
        {
            if (Works.ArmouryStock.Values.Any(s => s.Contains(item.Value))) return "armoury";
            int row = Econ.Items.IndexOf(item);
            return row < 0 ? "gone" : "owner:" + Econ.Items.At(row).Owner.Value;
        }
    }

    // ---------------------------------------------------------------- SC-1

    [Fact]
    public void SC1_EveryRole_HasATemplate_AWage_AndARequirement()
    {
        var roles = new TroopRoles(Db);
        Assert.Equal(new[] { "knight", "man_at_arms", "sergeant", "crossbowman", "archer", "spearman", "marine", "levy", "sailor", "engineer" }.OrderBy(x => x).ToList(),
            roles.Roles.Select(r => r.Id).OrderBy(x => x).ToList());
        foreach (var r in roles.Roles)
        {
            Assert.NotNull(roles.TemplateFor(r.Id));                                                      // a levy's is the lord's fill; the household's is by wealth
            if (r.Id != "levy") { Assert.True(roles.WageCp(r.Id) > 0, r.Id); Assert.NotNull(roles.Template(r.Loadout)); }
            Assert.True(r.Priority >= 0);
        }
        Assert.Equal(25, roles.WageCp("spearman")); Assert.Equal(37, roles.WageCp("spearman", mercenary: true));
        Assert.Equal(25, roles.WageCp("archer")); Assert.Equal(75, roles.WageCp("man_at_arms")); Assert.Equal(150, roles.WageCp("knight")); Assert.Equal(0, roles.WageCp("levy"));
        Assert.Equal(15, roles.Role("levy")!.WageAfterServiceCp);
        // the Phase 2 soldier wage (250 mites a day) is the spearman's
        Assert.Equal(250, roles.WageCp("spearman") * 10);
        // the requirements: a level-24 spearman is not one, a level-25 with shields 15 is; a knight is never by skills alone
        Assert.False(roles.Meets("spearman", s => s == Skill.Polearms ? 24 : 20)); Assert.True(roles.Meets("spearman", s => s == Skill.Polearms ? 25 : 15));
        Assert.False(roles.Meets("spearman", s => s == Skill.Polearms ? 40 : 14));
        Assert.True(roles.Meets("levy", _ => 0));
        Assert.False(roles.Meets("knight", _ => 100)); Assert.True(roles.Meets("knight", _ => 100, dubbed: true));
        Assert.True(roles.Meets("man_at_arms", s => s switch { Skill.Axes => 45, Skill.Shields => 30, _ => 0 }));
        Assert.False(roles.Meets("man_at_arms", s => s switch { Skill.Axes => 44, Skill.Shields => 30, _ => 0 }));
        // every shape a template accepts exists
        foreach (var t in roles.Templates)
            foreach (var slot in t.Slots) foreach (var shape in slot.Accept)
                Assert.True(Db.TryGet<WeaponShapeDef>(shape, out _) || Db.TryGet<ShieldShapeDef>(shape, out _) || Db.TryGet<ArmourShapeDef>(shape, out _), $"{t.Id}/{slot.Slot}/{shape}");
        // every role names a template that exists and every next role exists
        foreach (var r in roles.Roles) { foreach (var n in r.Next) Assert.NotNull(roles.Role(n)); }
        Assert.Equal(LevyWealth.Poor, roles.WealthOf(0)); Assert.Equal(LevyWealth.Middling, roles.WealthOf(20_000)); Assert.Equal(LevyWealth.WellOff, roles.WealthOf(100_000));
    }

    // ---------------------------------------------------------------- SC-2

    [Fact]
    public void SC2_FillOrder_30Spearmen_10MenAtArms_25MailShirts_MenAtArmsFirst_ThenSpearmenBySkillThenId()
    {
        var k = new Rig();
        for (int i = 0; i < 25; i++) k.Stock("mail_hauberk", "iron", 9000);
        var soldiers = new List<FillSoldier>();
        for (int i = 1; i <= 30; i++) soldiers.Add(new FillSoldier(Rig.Person(i), "spearman", "", 30 + i % 5));        // skills 31..34, 30 (ties by id)
        for (int i = 31; i <= 40; i++) soldiers.Add(new FillSoldier(Rig.Person(i), "man_at_arms", "", 50));
        soldiers.Reverse();                                                                                           // the order given must not matter
        var plan = k.Fill.Plan(Key, soldiers, 0, k.Fit);

        Assert.Equal(40, plan.Soldiers.Count);
        Assert.All(plan.Soldiers.Take(10), s => Assert.Equal("man_at_arms", s.Role));                               // fill order: role priority first
        Assert.All(plan.Soldiers.Take(10), s => Assert.Contains(s.Entries, e => e.Slot == "armour" && e.Source == KitSource.Armoury));
        var armed = plan.Soldiers.Skip(10).Where(s => s.Entries.Any(e => e.Slot == "armour")).Select(s => s.Person).ToList();
        Assert.Equal(15, armed.Count);
        var expected = soldiers.Where(s => s.Role == "spearman").OrderByDescending(s => s.Skill).ThenBy(s => s.Person).Take(15).Select(s => s.Person).OrderBy(x => x).ToList();
        Assert.Equal(expected, armed.OrderBy(x => x).ToList());                                                      // by skill, then id
        // the other fifteen have the optional armour slot as a listed (greyed) gap; the men-at-arms have none missing
        Assert.Equal(15, plan.Optional["spearman/armour"]);
        Assert.False(plan.Required.ContainsKey("man_at_arms/armour"));
        foreach (var s in plan.Soldiers.Skip(10).Where(s => !armed.Contains(s.Person))) Assert.Contains(s.Gaps, g => g.Slot == "armour" && !g.Required);
        // the armoury is empty of weapons: every soldier's required slots are gaps, summed per role and slot ("30 spearmen without spears")
        Assert.Equal(30, plan.Required["spearman/weapon"]); Assert.Equal(30, plan.Required["spearman/helmet"]); Assert.Equal(10, plan.Required["man_at_arms/shield"]);
        // the clothes slots are the soldier's own: never an armoury gap
        Assert.DoesNotContain(plan.Required.Keys, key => key.EndsWith("/over") || key.EndsWith("/cover"));
        // pure: planning moved nothing
        Assert.Equal(25, k.Arm.Stock(Key).Count);
        output.WriteLine($"issuable {plan.Issuable}; gaps {string.Join(", ", plan.Required.Select(kv => kv.Key + "=" + kv.Value))}");
    }

    // ---------------------------------------------------------------- SC-3

    [Fact]
    public void SC3_BestFirst_TwoHelmets_ThePlanPicksPerScore_AndOwnKitIsKeptWhenItScoresAsWell()
    {
        var k = new Rig();
        var good = k.Stock("kettle_hat", "iron", 1600, ItemQuality.Common, 600);              // good, worn 40%
        var fine = k.Stock("kettle_hat", "iron", 1600, ItemQuality.Fine, 900);                // fine, 90%
        var s = new FillSoldier(Rig.Person(1), "spearman", "", 30);
        var plan = k.Fill.Plan(Key, [s], 0, k.Fit);
        var helm = plan.Soldiers[0].Entries.Single(e => e.Slot == "helmet");
        Assert.Equal(fine.Value, helm.Item);                                                    // 300 + 90 + 100 beats 200 + 60 + 100
        Assert.Equal(300 + 90 + 100, helm.Score);

        // two soldiers, two helmets: the higher-ranked takes the fine one, the other the good one
        var plan2 = k.Fill.Plan(Key, [s, new FillSoldier(Rig.Person(2), "spearman", "", 31)], 0, k.Fit);
        Assert.Equal(fine.Value, plan2.Soldiers.Single(p => p.Person == Rig.Person(2)).Entries.Single(e => e.Slot == "helmet").Item);
        Assert.Equal(good.Value, plan2.Soldiers.Single(p => p.Person == Rig.Person(1)).Entries.Single(e => e.Slot == "helmet").Item);

        // his own kit counts first when it scores at least as well: an own fine 900 helmet against the armoury's fine 900
        var own = k.Item("kettle_hat", "iron", 1600, ItemQuality.Fine, 900, owner: new EntityId(Rig.Person(1)));
        var plan3 = k.Fill.Plan(Key, [s], 0, k.Fit);
        var h3 = plan3.Soldiers[0].Entries.Single(e => e.Slot == "helmet");
        Assert.Equal(own.Value, h3.Item); Assert.Equal(KitSource.Own, h3.Source);
        // his own poor helmet loses to the armoury's fine one, and stays his
        var k2 = new Rig(); var fine2 = k2.Stock("kettle_hat", "iron", 1600, ItemQuality.Fine, 900);
        var poor = k2.Item("kettle_hat", "iron", 1600, ItemQuality.Poor, 500, owner: new EntityId(Rig.Person(1)));
        var r = k2.Fill.Apply(Key, k2.Fill.Plan(Key, [s], 0, k2.Fit), k2.Book, k2.Fit, 5);
        Assert.Equal(fine2.Value, k2.Book.Rows[Rig.Person(1)].Worn["helmet"]); Assert.Equal("owner:" + Rig.Person(1), k2.Where(poor));
        Assert.Equal(1, r.Issued);
        // a material floor: a bronze helmet is not "iron, wrought_iron or steel" for the spearman's weapon, but helmets have none, so it fills
        var k3 = new Rig(); k3.Stock("spear", "bronze", 900);
        Assert.Contains(k3.Fill.Plan(Key, [s], 0, k3.Fit).Soldiers[0].Gaps, g => g.Slot == "weapon" && g.Required);   // the spearman's spear needs an iron head
    }

    // ---------------------------------------------------------------- SC-4

    [Fact]
    public void SC4_Fit_AMailShirt20PercentTooSmallScoresMinus300_AndWithAnArmourerFreeARefitIsQueuedBeforeIssue()
    {
        var k = new Rig();
        var small = k.Stock("mail_hauberk", "iron", 9000, ItemQuality.Fine, 1000); k.Fit.Fits[small.Value] = 56_000;     // made for a body 20% smaller
        var s = new FillSoldier(Rig.Person(1), "man_at_arms", "", 50);
        var plan = k.Fill.Plan(Key, [s], 0, k.Fit);
        var e = plan.Soldiers[0].Entries.Single(x => x.Slot == "armour");
        Assert.Equal(300 + 100 + 100 - 300, e.Score); Assert.False(e.NeedsRefit);                                       // issued misfit, scored down
        // a fitting common shirt beats the misfit fine one
        var fits = k.Stock("mail_hauberk", "iron", 9000, ItemQuality.Common, 1000);
        Assert.Equal(fits.Value, k.Fill.Plan(Key, [s], 0, k.Fit).Soldiers[0].Entries.Single(x => x.Slot == "armour").Item);
        // with an armourer free the misfit costs nothing and a refit job is queued before issue: the fine shirt wins and stays in the armoury
        var plan2 = k.Fill.Plan(Key, [s], 1, k.Fit);
        var e2 = plan2.Soldiers[0].Entries.Single(x => x.Slot == "armour");
        Assert.Equal(small.Value, e2.Item); Assert.True(e2.NeedsRefit); Assert.Equal(500, e2.Score);
        var res = k.Fill.Apply(Key, plan2, k.Book, k.Fit, 7);
        Assert.Equal(1, res.RefitsQueued);
        Assert.Equal("armoury", k.Where(small)); Assert.DoesNotContain("armour", k.Book.Rows[Rig.Person(1)].Worn.Keys);
        var job = Assert.Single(k.Book.Refits); Assert.Equal(small.Value, job.Item); Assert.Equal(70_000, job.ToCm3);
        Assert.False(k.Book.IsEmpty);
        // planning again does not queue it twice
        k.Fill.Apply(Key, k.Fill.Plan(Key, [s], 1, k.Fit), k.Book, k.Fit, 7);
        Assert.Single(k.Book.Refits);
        // the armourer finishes: the piece takes his size and is issued to him
        Assert.True(k.Fill.CompleteRefit(k.Book, k.Fit, small.Value, 9));
        Assert.Equal(70_000, k.Fit.FitOf(small.Value)); Assert.Equal("owner:" + Rig.Person(1), k.Where(small)); Assert.Equal(small.Value, k.Book.Rows[Rig.Person(1)].Worn["armour"]);
        Assert.Empty(k.Book.Refits);
        // plate can be taken in, never let out: a plate made for a smaller body is no refit; one made for a bigger body is
        var k2 = new Rig();
        var plate = k2.Stock("breastplate", "steel", 5500); k2.Fit.Fits[plate.Value] = 56_000;
        var knight = new FillSoldier(Rig.Person(2), "knight", "", 60);
        Assert.False(k2.Fill.Plan(Key, [knight], 1, k2.Fit).Soldiers[0].Entries.Single(x => x.Slot == "plate").NeedsRefit);
        k2.Fit.Fits[plate.Value] = 90_000;
        Assert.True(k2.Fill.Plan(Key, [knight], 1, k2.Fit).Soldiers[0].Entries.Single(x => x.Slot == "plate").NeedsRefit);
        // clothing has no fit: a gambeson of any recorded size is never a misfit
        Assert.False(k2.Fill.Misfit("gambeson", 20_000, 70_000)); Assert.True(k2.Fill.Misfit("mail_hauberk", 56_000, 70_000)); Assert.False(k2.Fill.Misfit("mail_hauberk", 62_000, 70_000));
    }

    // ---------------------------------------------------------------- SC-5

    [Fact]
    public void SC5_IssueThenDischarge_EveryItemRowIsBackInTheArmoury_MassAndCountsExact()
    {
        var k = new Rig();
        for (int i = 0; i < 6; i++) { k.Stock("spear", "iron", 900); k.Stock("round_shield", "oak", 3000); k.Stock("kettle_hat", "iron", 1600); }
        for (int i = 0; i < 3; i++) k.Stock("mail_hauberk", "iron", 9000);
        var mine = k.Item("dagger", "iron", 400, owner: new EntityId(Rig.Person(1)));                                  // his own, kept
        var before = k.Econ.Items.LiveRows().Select(r => (Id: k.Econ.Items.At(r).Id.Value, Owner: k.Econ.Items.At(r).Owner.Value)).OrderBy(x => x.Id).ToList();
        long mass = k.TotalMassG(); int count = k.Econ.Items.Count; var stock0 = k.Arm.Stock(Key).Select(x => x.Value).ToList();

        var soldiers = Enumerable.Range(1, 6).Select(i => new FillSoldier(Rig.Person(i), "spearman", "", 30 + i)).ToList();
        var res = k.FillFrom(soldiers, 3);
        Assert.Equal(6 * 3 + 3, res.Issued);                                                                           // spear, shield, helmet each, and the three shirts to the top three
        Assert.Equal(count, k.Econ.Items.Count); Assert.Equal(mass, k.TotalMassG());                                   // nothing made, nothing lost
        Assert.Equal(res.Issued, k.Works.Issues.Count);
        foreach (var row in k.Book.Rows.Values) foreach (var iss in row.Issued) Assert.Equal("owner:" + row.Person, k.Where(new EntityId(iss.Item)));
        Assert.DoesNotContain(k.Arm.Stock(Key), id => k.Book.Rows.Values.Any(r => r.Issued.Any(i => i.Item == id.Value)));    // an item is in exactly one place

        // wear: a piece in use loses condition while issued, and comes back with the wear it has
        var one = k.Book.Rows[Rig.Person(6)].Issued[0].Item; k.Econ.Items.Get(new EntityId(one)).ConditionPermille = 640;

        int back = 0;
        foreach (var s in soldiers) back += k.Fill.Discharge(k.Book, s.Person);
        Assert.Equal(res.Issued, back);
        Assert.Empty(k.Works.Issues); Assert.True(k.Book.IsEmpty);
        Assert.Equal(stock0.OrderBy(x => x), k.Arm.Stock(Key).Select(x => x.Value).OrderBy(x => x));                  // every row back in the stock
        Assert.Equal(count, k.Econ.Items.Count); Assert.Equal(mass, k.TotalMassG());
        Assert.Equal(640, k.Econ.Items.Get(new EntityId(one)).ConditionPermille);
        Assert.Equal("owner:" + Rig.Person(1), k.Where(mine));                                                          // his own kit stays his
        foreach (var (id, owner) in before.Where(b => b.Owner != Rig.Person(1))) Assert.Equal(owner, k.Econ.Items.Get(new EntityId(id)).Owner.Value);
        // a re-fill after a piece is lost on campaign: the lost row is gone from the book and nothing comes back for it
        var res2 = k.FillFrom(soldiers.Take(1).ToList(), 4);
        var lost = k.Book.Rows[Rig.Person(1)].Issued[0].Item; k.Econ.Items.Free(new EntityId(lost));
        Assert.Equal(res2.Issued - 1, k.Fill.Discharge(k.Book, Rig.Person(1)));
        Assert.Empty(k.Works.Issues);
    }

    // ---------------------------------------------------------------- SC-6

    [Fact]
    public void SC6_LeviesByWealth_BringTheListedKindsFromTheirOwnItems_AHouseholdWithoutASpearGivesNone_TheLordsFillGivesSpearsAndShieldsOnly()
    {
        var k = new Rig();
        EntityId[] Gear(EntityId hh) => [
            k.Item("hand_axe", "iron", 1000, owner: hh), k.Item("dagger", "iron", 400, owner: hh), k.Item("spear", "iron", 900, owner: hh), k.Item("round_shield", "oak", 3000, owner: hh),
            k.Item("kettle_hat", "iron", 1600, owner: hh), k.Item("gambeson", "linen", 3200, owner: hh), k.Item("tunic", "wool", 900, owner: hh), k.Item("mail_hauberk", "iron", 9000, owner: hh),
            k.Item("arming_sword", "steel", 1300, owner: hh)];
        var poorHh = Gear(Rig.Household(11)); var midHh = Gear(Rig.Household(12)); var richHh = Gear(Rig.Household(13));
        long mass = k.TotalMassG(); int count = k.Econ.Items.Count;

        var poor = k.Levies.Bring(k.Book, Rig.Person(1), LevyWealth.Poor, poorHh, 0, 1);
        Assert.Equal(["over", "weapon"], poor.Items.Select(i => i.Slot).OrderBy(x => x).ToList());                   // a tool-weapon and his tunic, nothing else
        Assert.Contains(poor.Items, i => i.Slot == "weapon" && k.Econ.Items.Get(new EntityId(i.Item)).Shape is "hand_axe" or "dagger");
        var mid = k.Levies.Bring(k.Book, Rig.Person(2), LevyWealth.Middling, midHh, 0, 1);
        Assert.Equal(["alt_weapon", "helmet", "over", "shield", "weapon"], mid.Items.Select(i => i.Slot).OrderBy(x => x).ToList());
        Assert.Equal("spear", k.Econ.Items.Get(new EntityId(mid.Items.Single(i => i.Slot == "weapon").Item)).Shape);
        Assert.Equal("gambeson", k.Econ.Items.Get(new EntityId(mid.Items.Single(i => i.Slot == "over").Item)).Shape);
        Assert.DoesNotContain(mid.Items, i => i.Slot == "armour");                                                       // no mail for the middling
        var rich = k.Levies.Bring(k.Book, Rig.Person(3), LevyWealth.WellOff, richHh, 0, 1);
        Assert.Equal("arming_sword", k.Econ.Items.Get(new EntityId(rich.Items.Single(i => i.Slot == "weapon").Item)).Shape);
        Assert.Equal("mail_hauberk", k.Econ.Items.Get(new EntityId(rich.Items.Single(i => i.Slot == "armour").Item)).Shape);
        Assert.Equal(count, k.Econ.Items.Count); Assert.Equal(mass, k.TotalMassG());                                    // real items of the household, moved, none made
        foreach (var (slot, item) in poor.Items) Assert.Equal(Rig.Person(1), k.Econ.Items.Get(new EntityId(item)).Owner.Value);
        Assert.Equal(11UL, Rig.Household(11).Counter); Assert.Contains(poorHh, id => k.Econ.Items.Get(id).Owner.Kind == IdKind.Household);   // the household kept what he did not take

        // a household without a spear gives none
        var hh = Rig.Household(20);
        var noSpear = new[] { k.Item("hand_axe", "iron", 1000, owner: hh), k.Item("round_shield", "oak", 3000, owner: hh), k.Item("gambeson", "linen", 3200, owner: hh) };
        var bare = k.Levies.Bring(k.Book, Rig.Person(4), LevyWealth.Middling, noSpear, 0, 1);
        Assert.Contains("weapon", bare.Missing); Assert.DoesNotContain(bare.Items, i => k.Econ.Items.Get(new EntityId(i.Item)).Shape == "spear");
        Assert.Contains(bare.Items, i => i.Slot == "alt_weapon");                                                        // the axe goes in the second slot
        Assert.Contains("helmet", bare.Missing);

        // the lord's fill gives a levy spears and shields only, by default
        for (int i = 0; i < 4; i++) { k.Stock("spear", "iron", 900); k.Stock("round_shield", "oak", 3000); k.Stock("kettle_hat", "iron", 1600); k.Stock("arming_sword", "steel", 1300); }
        var levies = new[] { 7, 8, 9 }.Select(n => new FillSoldier(Rig.Person(n), "levy", "", 0)).ToList();
        var r = k.Fill.Apply(Key, k.Fill.Plan(Key, levies, 0, k.Fit), k.Book, k.Fit, 2, 0);
        Assert.Equal(6, r.Issued);
        foreach (var l in levies)
        {
            var row = k.Book.Rows[l.Person];
            Assert.Equal(["shield", "weapon"], row.Worn.Keys.OrderBy(x => x).ToList());
            Assert.Equal("spear", k.Econ.Items.Get(new EntityId(row.Worn["weapon"])).Shape); Assert.Equal("round_shield", k.Econ.Items.Get(new EntityId(row.Worn["shield"])).Shape);
        }
    }

    // ---------------------------------------------------------------- the hero as a soldier, piece by piece, saves, the desk

    [Fact]
    public void TheHero_Id0_IsASoldierToo_AndOnePieceCanBeRefittedAtATime()
    {
        var k = new Rig(); var hero = EconomyState.HeroOwner.Value; Assert.Equal(0UL, hero);
        var spear = k.Stock("spear", "iron", 900); var better = k.Stock("spear", "steel", 900, ItemQuality.Fine); k.Stock("round_shield", "oak", 3000); k.Stock("kettle_hat", "iron", 1600);
        var res = k.FillFrom([new FillSoldier(hero, "spearman", "", 40)], 1, lord: 0);
        Assert.Equal(3, res.Issued);
        var row = k.Book.Rows[hero]; Assert.Equal(0, row.Lord); Assert.Equal(better.Value, row.Worn["weapon"]);        // the hero's row, with lord 0 meaning "the hero", not "nobody"
        // swap the spear for the other one: the first goes back, the second comes out
        Assert.True(k.Fill.Equip(k.Book, hero, "weapon", spear.Value, Key, 2));
        Assert.Equal(spear.Value, row.Worn["weapon"]); Assert.Equal("armoury", k.Where(better)); Assert.Equal("owner:0", k.Where(spear));
        Assert.Single(row.Issued, i => i.Item == spear.Value);
        Assert.False(k.Fill.Equip(k.Book, hero, "weapon", k.Item("club", "oak", 1500).Value, Key, 2));               // a club is no spear
        Assert.True(k.Fill.Strip(k.Book, hero, "shield")); Assert.False(row.Worn.ContainsKey("shield"));
        Assert.Equal(1, k.Arm.Stock(Key).Count(id => k.Econ.Items.Get(id).Shape == "round_shield"));
        Assert.Equal(2, k.Fill.Discharge(k.Book, hero));
    }

    [Fact]
    public void Save_TheSoldiersSection_IsAbsentWhileEmpty_RoundTripsExactly_AndAnOldSaveKeepsItsBytes()
    {
        var w = EconFixture.WorldWith(null);
        var before = SaveCodec.Encode(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0"));
        Assert.False(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0").Sections.ContainsKey(SoldierSave.SectionName));
        w.Soldiers = new SoldierBook();
        Assert.False(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0").Sections.ContainsKey(SoldierSave.SectionName));        // an empty book is still no section
        Assert.Equal(before, SaveCodec.Encode(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0")));
        Assert.Equal(0UL, w.Soldiers.Hash());

        var row = new SoldierRow { Person = 0, Role = "spearman", Lord = 0, PromotedDay = 12 }; row.Worn["weapon"] = 77; row.Worn["helmet"] = 78; row.Issued.Add(new IssuedItem(77, 4242));
        w.Soldiers.Rows[0] = row; w.Soldiers.Rows[5] = new SoldierRow { Person = 5, Role = "levy", Template = "levy_poor" };
        w.Soldiers.Refits.Add(new RefitJob(90, 5, 4242, "armour", 70_000, 11));
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        Assert.True(file.Sections.ContainsKey(SoldierSave.SectionName));
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(w.Soldiers.Hash(), back.Soldiers!.Hash()); Assert.NotEqual(0UL, w.Soldiers.Hash());
        Assert.Equal(0, back.Soldiers.Rows[0].Lord); Assert.Equal(-1, back.Soldiers.Rows[5].Lord);
        Assert.Equal(SaveCodec.Encode(file), SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        // the section name is one of the ten Phase 7 names
        Assert.Contains(SoldierSave.SectionName, Ravensguard.Sim.Worlds.Phase7Seeding.SectionNames);
    }

    [Fact]
    public void Desk_FillEquipStripRoleDischarge_AreHeroCommands_AndMoveOnlyItems()
    {
        var econ = EconFixture.NewEconomy(); var w = EconFixture.WorldWith(econ);
        var ctx = new HeroContext(w, new HeroParty(), Db);
        var sw = SoldierWiring.For(w, Db); var ids = new IdAllocator();
        EntityId Mk(string shape, string mat, int g) => econ.Items.Add(ids, shape, ItemQuality.Common, Rig.Household(1), [new ItemComponent(mat, g, 1000)]);
        var spear = Mk("spear", "iron", 900); var shield = Mk("round_shield", "oak", 3000); var hat = Mk("kettle_hat", "iron", 1600);
        foreach (var it in new[] { spear, shield, hat }) Assert.True(sw.Armouries.Add(Key, Big, it));
        sw.Book.Rows[0] = new SoldierRow { Person = 0, Role = "spearman", Lord = 0 };
        sw.Book.Rows[9] = new SoldierRow { Person = 9, Role = "spearman", Lord = 123 };                                     // somebody else's
        long mass = econ.Items.LiveRows().Sum(r => econ.Items.MassG(r, econ.Goods)); int count = econ.Items.Count;

        Assert.Equal(HeroCommandKind.Soldier, SoldierDesk.Fill(Key, 86400).Kind);
        var fill = SoldierDesk.Apply(ctx, SoldierDesk.Fill(Key, 3 * 86400));
        Assert.Equal(HeroResultCode.Ok, fill.Code); Assert.Equal("so_filled", fill.Message.Key);
        Assert.Equal(3, sw.Book.Rows[0].Worn.Count(kv => kv.Key is "weapon" or "shield" or "helmet")); Assert.False(sw.Book.Rows[9].Worn.Any());
        Assert.Equal(HeroResultCode.Ok, SoldierDesk.Apply(ctx, SoldierDesk.Strip(0, "helmet", 4 * 86400)).Code);
        Assert.Equal("armoury", sw.Book.Rows[0].Worn.ContainsKey("helmet") ? "x" : "armoury"); Assert.Contains(hat, sw.Armouries.Stock(Key));
        Assert.Equal(HeroResultCode.Ok, SoldierDesk.Apply(ctx, SoldierDesk.Equip(0, "helmet", hat.Value, Key, 5 * 86400)).Code);
        Assert.Equal(HeroResultCode.Refused, SoldierDesk.Apply(ctx, SoldierDesk.Strip(9, "weapon", 5 * 86400)).Code);       // not his soldier
        Assert.Equal(HeroResultCode.Refused, SoldierDesk.Apply(ctx, SoldierDesk.Enlist(77, "nobody", 5 * 86400)).Code);
        Assert.Equal(HeroResultCode.Refused, SoldierDesk.Apply(ctx, SoldierDesk.SetRole(0, "knight", "", 5 * 86400)).Code);   // a knight is dubbed, not appointed
        Assert.Equal(HeroResultCode.Invalid, SoldierDesk.Apply(ctx, new HeroCommand { Kind = HeroCommandKind.Soldier, Tag = "so:dance|1" }).Code);
        var dis = SoldierDesk.Apply(ctx, SoldierDesk.Discharge(0, 6 * 86400));
        Assert.Equal(HeroResultCode.Ok, dis.Code); Assert.Equal(3, sw.Armouries.Stock(Key).Count);
        Assert.Equal(count, econ.Items.Count); Assert.Equal(mass, econ.Items.LiveRows().Sum(r => econ.Items.MassG(r, econ.Goods)));
    }

    [Fact]
    public void Determinism_ThePlan_IsThePureFunctionOfItsInputs_AndTheOrderGivenDoesNotMatter()
    {
        string Run(bool reverse)
        {
            var k = new Rig();
            for (int i = 0; i < 12; i++) { k.Stock("spear", "iron", 900, (ItemQuality)(i % 4), 500 + i * 30); k.Stock("round_shield", "oak", 3000, (ItemQuality)(i % 3), 700); k.Stock("kettle_hat", "iron", 1600, (ItemQuality)(i % 5)); k.Stock("mail_hauberk", "iron", 9000, ItemQuality.Common, 900 + i); }
            var list = Enumerable.Range(1, 20).Select(i => new FillSoldier(Rig.Person(i), i % 4 == 0 ? "man_at_arms" : "spearman", "", i * 7 % 13)).ToList();
            if (reverse) list.Reverse();
            var plan = k.Fill.Plan(Key, list, 0, k.Fit);
            k.Fill.Apply(Key, plan, k.Book, k.Fit, 3);
            return k.Book.Hash().ToString() + ":" + plan.Issuable;
        }
        Assert.Equal(Run(false), Run(true));
    }
}
