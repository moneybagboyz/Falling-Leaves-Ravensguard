using Ravensguard.Content;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.Sim.Tests.Content;

public class OriginTests
{
    private static ModLoadResult Load()
    {
        var r = new ModLoader(ContentTypeRegistry.Default, BaseVocabulary.Create()).Load([ContinentSettings.FindBaseContent()!]);
        Assert.True(r.Success, r.Report.ToString());
        return r;
    }

    [Fact]
    public void Origins_and_ambitions_exist_and_every_reference_resolves()
    {
        var db = Load().Database;
        var origins = db.All<OriginDef>().OrderBy(o => o.Order).ToList();
        Assert.InRange(origins.Count, 6, 8);
        var skills = Enum.GetNames<Skill>().Select(n => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(n)).ToHashSet();
        var attrs = new HashSet<string> { "strength", "toughness", "dexterity", "perception", "wits" };
        foreach (var o in origins)
        {
            Assert.True(db.Contains("ambition", o.Ambition), $"{o.Id}: ambition {o.Ambition}");
            Assert.NotEmpty(o.Kit);
            foreach (var k in o.Kit)
                Assert.True(db.Contains("weapon_shape", k.Item) || db.Contains("armour_shape", k.Item), $"{o.Id}: kit item {k.Item}");
            foreach (var s in o.Skills.Keys) Assert.Contains(s, skills);
            foreach (var a in o.Attributes.Keys) Assert.Contains(a, attrs);
            Assert.Contains(o.StartLocation, new[] { "village", "town", "road", "manor", "monastery", "wilds" });
            Assert.False(string.IsNullOrWhiteSpace(o.Mentor));
            Assert.True(o.Coin >= 0);
        }
        Assert.Equal(origins.Count, origins.Select(o => o.Ambition).Distinct().Count());
    }

    [Fact]
    public void Ambitions_have_three_to_five_steps_with_vocabulary_conditions()
    {
        var db = Load().Database;
        var vocab = BaseVocabulary.Create();
        foreach (var a in db.All<AmbitionDef>())
        {
            Assert.InRange(a.Steps.Count, 3, 5);
            Assert.Equal(a.Steps.Count, a.Steps.Select(s => s.Id).Distinct().Count());
            foreach (var s in a.Steps)
            {
                Assert.NotEmpty(s.Conditions);
                foreach (var c in s.Conditions) Assert.True(vocab.IsCondition(c.Id), $"{a.Id}/{s.Id}: unknown condition {c.Id}");
            }
        }
    }
}
