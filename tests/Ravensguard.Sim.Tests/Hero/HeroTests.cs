using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Hero;

public class HeroTests
{
    private static ContentDatabase Db => TestWorld.Content_.Db;

    [Fact]
    public void Every_origin_makes_a_valid_hero_from_deltas_over_the_baseline()
    {
        foreach (var o in Db.All<OriginDef>())
        {
            var h = HeroRecord.FromOrigin("Test Hero", false, o);
            Assert.Equal(o.Id, h.Origin);
            Assert.Equal(o.Ambition, h.AmbitionId);
            Assert.Equal(0, h.Milestone);
            Assert.Equal(o.Coin, h.Coin);
            Assert.Equal(5, h.Attributes.Length);
            for (int i = 0; i < 5; i++)
                Assert.Equal(Math.Clamp(HeroRecord.BaselineAttribute + o.Attributes.GetValueOrDefault(HeroRecord.AttrIds[i]), 1, 100), h.Attribute((HeroAttr)i));
            foreach (var (id, lvl) in h.Skills) Assert.InRange(lvl, 0, 100);
            foreach (var (id, d) in o.Skills) Assert.Equal(d, h.Skill(id)); // baseline skills are 0, so the delta is the level
            Assert.Equal(h.Skills.Keys.Count(k => true), Enum.GetValues<Skill>().Length - 1);
            Assert.Equal(o.Kit.Count, h.Inventory.Count);
            foreach (var it in h.Inventory)
            {
                Assert.True(Db.Contains("weapon_shape", it.Shape) || Db.Contains("armour_shape", it.Shape), it.Shape);
                Assert.True(Db.Contains("material", it.Material), $"{it.Shape}: material {it.Material}");
                Assert.True(it.Count >= 1);
            }
            Assert.True(Db.Contains("ambition", h.AmbitionId));
        }
    }

    [Fact]
    public void Skill_order_is_stable_and_the_sex_adjustment_matches_the_population_rolls()
    {
        var o = Db.Get<OriginDef>("disgraced_squire");
        var m = HeroRecord.FromOrigin("A", false, o);
        var f = HeroRecord.FromOrigin("B", true, o);
        Assert.Equal(m.Skills.Keys, f.Skills.Keys);
        Assert.Equal(HeroRecord.SkillIds, m.Skills.Keys.ToList());
        Assert.Equal(m.Attribute(HeroAttr.Strength) - 6, f.Attribute(HeroAttr.Strength));
        Assert.Equal(m.Attribute(HeroAttr.Dexterity) + 2, f.Attribute(HeroAttr.Dexterity));
        Assert.Equal(m.Attribute(HeroAttr.Toughness), f.Attribute(HeroAttr.Toughness));
        Assert.Equal(15, m.Skill("melee"));
        Assert.Equal(0, m.Skill("farming"));
        Assert.Equal(54, m.Attribute(HeroAttr.Strength));
    }

    [Fact]
    public void Save_form_roundtrips_exactly_and_rejects_corruption()
    {
        var h = HeroRecord.FromOrigin("Ysolde of the Mere", true, Db.Get<OriginDef>("minor_heir"));
        h.Coin = 123; h.Milestone = 2;
        var bytes = SaveCodec.Serialize(h.ToSave());
        var back = HeroRecord.FromSave(SaveCodec.Deserialize<HeroSave>(bytes));
        Assert.Equal(h.Name, back.Name);
        Assert.Equal(h.Female, back.Female);
        Assert.Equal(h.Attributes, back.Attributes);
        Assert.Equal(h.Skills.Select(kv => (kv.Key, kv.Value)), back.Skills.Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(h.Inventory, back.Inventory);
        Assert.Equal((123, 2, "claim_your_inheritance"), (back.Coin, back.Milestone, back.AmbitionId));
        Assert.Equal(bytes, SaveCodec.Serialize(back.ToSave()));
        var bad = h.ToSave(); bad.Attributes = [1, 2];
        Assert.Throws<SaveException>(() => HeroRecord.FromSave(bad));
    }

    [Fact]
    public void Ambition_milestones_advance_to_the_end()
    {
        var o = Db.Get<OriginDef>("peddler");
        var a = Db.Get<AmbitionDef>(o.Ambition);
        var h = HeroRecord.FromOrigin("P", false, o);
        Assert.Equal(a.Steps[0].Id, h.CurrentStep(a)!.Id);
        while (h.CompleteMilestone(a)) { }
        Assert.Equal(a.Steps.Count, h.Milestone);
        Assert.Null(h.CurrentStep(a));
        Assert.False(h.CompleteMilestone(a));
    }
}
