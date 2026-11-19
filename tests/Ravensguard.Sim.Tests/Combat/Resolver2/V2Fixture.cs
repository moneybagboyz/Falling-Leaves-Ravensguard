using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Resolver2;

/// <summary>Loads content/base once, builds the v1 and v2 tables and the v2 resolver.</summary>
public sealed class V2Fixture
{
    public ContentDatabase Db { get; }
    public CombatTables Tables { get; }
    public CombatTablesV2 T => Tables.V2;
    public ResolverV2 R { get; }
    public BodyPlanV2 Human { get; }

    public V2Fixture()
    {
        Db = Load();
        Tables = new CombatTables(Db);
        R = new ResolverV2(T);
        Human = T.Plan("human");
    }

    public static ContentDatabase Load()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var r = new ModLoader(ContentTypeRegistry.Default).Load([Path.Combine(root, "content", "base")]);
        if (!r.Success) throw new InvalidOperationException(r.Report.ToString());
        return r.Database;
    }

    public int P(string id) { int i = Human.IndexOf(id); Assert.True(i >= 0, $"no part {id}"); return i; }

    /// <summary>A steel weapon blow on a human, average strength, skill 50 unless told.</summary>
    public StrikeV2 S(string shape, string attack, string part, int skill = 50, AttackStyle style = AttackStyle.Normal, string material = "steel",
        BodyPlanV2? plan = null, params (string Shape, string Material)[] worn)
    {
        plan ??= Human;
        var w = T.Weapon(shape, material);
        var s = StrikeV2.Create(w, w.AttackIndex(attack), plan, plan.IndexOf(part), skill);
        Assert.True(s.Attack >= 0, $"no attack {attack}");
        Assert.True(s.TargetPart >= 0, $"no part {part}");
        s.Style = style;
        s.Worn = [.. worn.Select(x => WornV2.Of(T.Armour(x.Shape, x.Material)))];
        s.WornCount = s.Worn.Length;
        return s;
    }

    public static BlowKey Key(uint serial, ulong seed = 42, long tick = 0, ulong attacker = 1) => new(seed, tick, attacker, serial);

    public BlowResult Impact(in StrikeV2 s, uint serial = 0)
    {
        var r = new BlowResult();
        R.ResolveImpact(s, Key(serial), r);
        return r;
    }

    /// <summary>The first serial whose coverage rolls all pass (the worked table assumes they do).</summary>
    public BlowResult ImpactCovered(in StrikeV2 s)
    {
        for (uint k = 0; k < 500; k++)
        {
            var r = Impact(s, k);
            bool all = true;
            foreach (var st in r.StepSpan) if (st.IsArmour && st.Outcome == LayerOutcome.NotCovered) all = false;
            if (all) return r;
        }
        throw new InvalidOperationException("no serial with full coverage");
    }

    public string TissueOf(in LayerStep st) => st.IsArmour ? T.MaterialList[st.Tissue].Id + ":" + (st.Piece >= 0 ? "armour" : "") : T.MaterialList[st.Tissue].Id;
}

[CollectionDefinition("resolver2")]
public sealed class V2Collection : ICollectionFixture<V2Fixture>;
