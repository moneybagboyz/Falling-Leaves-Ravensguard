using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Tests.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Melee2;

/// <summary>Content, the v2 tables and the fight content, loaded once.</summary>
public sealed class FightFixture
{
    public ContentDatabase Db { get; }
    public CombatTables Tables { get; }
    public FightContent C { get; }

    public FightFixture()
    {
        Db = V2Fixture.Load();
        Tables = new CombatTables(Db);
        C = new FightContent(Tables, Db);
    }

    public static readonly WeaponInstance Sword = new("arming_sword", "steel");

    public FighterKit Kit(string name, int team, int x, int y = 0, WeaponInstance? weapon = null, int skill = 50, string? shield = null, IReadOnlyList<WornItem>? worn = null,
        int faceX = 0, int faceY = 0, string? launcher = null, string? ammo = null, int ammoCount = 0, IReadOnlyList<ThrowStock>? throws = null, string? improvised = null)
    {
        if (faceX == 0 && faceY == 0) faceX = team == 0 ? FightMath.One : -FightMath.One;
        return new FighterKit
        {
            Name = name, Team = team, X = x, Y = y, Weapon = launcher == null && improvised == null ? weapon ?? Sword : weapon, Skill = skill, Shield = shield, Worn = worn ?? [new WornItem("tunic", "linen")],
            FaceX = faceX, FaceY = faceY, Launcher = launcher, Ammo = ammo, AmmoCount = ammoCount, Throwables = throws ?? [], Improvised = improvised,
        };
    }

    public FightWorld World(ulong seed = 1) => new(C, seed, 1, 8 * 3600) { RecordEvents = true };

    /// <summary>A duel 1.6 m apart, A facing B: both under the AI unless <paramref name="aPlayer"/>.</summary>
    public (FightWorld W, Fighter A, Fighter B) Duel(ulong seed = 1, int skillA = 50, int skillB = 50, bool aPlayer = false, bool bPlayer = false, int gap = 1600)
    {
        var w = World(seed);
        var a = w.Add(Kit("A", 0, 0, skill: skillA), aPlayer);
        var b = w.Add(Kit("B", 1, gap, skill: skillB), bPlayer);
        return (w, a, b);
    }
}

[CollectionDefinition("fight")]
public sealed class FightCollection : ICollectionFixture<FightFixture>;
