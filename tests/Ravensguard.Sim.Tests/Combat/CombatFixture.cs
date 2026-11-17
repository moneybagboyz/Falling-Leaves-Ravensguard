using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Tests.Combat;

/// <summary>Loads content/base once and builds the derived tables.</summary>
public sealed class CombatFixture
{
    public ContentDatabase Db { get; }
    public CombatTables Tables { get; }
    public StrikeResolver Resolver { get; }
    public BodyPlan Human { get; }

    public CombatFixture()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var r = new ModLoader(ContentTypeRegistry.Default).Load([Path.Combine(root, "content", "base")]);
        if (!r.Success) throw new InvalidOperationException(r.Report.ToString());
        Db = r.Database;
        Tables = new CombatTables(Db);
        Resolver = new StrikeResolver(Tables);
        Human = Tables.BodyPlans["human"];
    }

    public Defender Man(params WornItem[] worn) => new(Human, 70000, worn);
    public static Attacker Avg(int skill = 0) => new(1250, skill, 70000, 70000);
    public static RngStream Rng(uint stream = 1, ulong seed = 42) => new(seed, 0, 1, stream);
}

[CollectionDefinition("combat")]
public sealed class CombatCollection : ICollectionFixture<CombatFixture>;
