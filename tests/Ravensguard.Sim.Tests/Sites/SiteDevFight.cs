using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Sites;

[Collection("sites")]
public class SiteDevFight(SiteFixture fx, ITestOutputHelper o)
{
    [Fact(Skip = "dev")]
    public void Fight()
    {
        int tendons = 0, structs = 0, broken = 0, heroDown = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var hero = ExFixtures.Hero().Kit with { X = 0, Y = 0, FaceX = 1000, FaceY = 0 };
            var def = FightSetup.KitDef(fx.C, "grave_robber", "grave_robber");
            var rob = FightSetup.Roll(fx.C, def, seed, 99, "Rob", 1, 3000, 0, -1000, 0);
            var f = FightSetup.SiteInhabitants([hero], [rob], Ravensguard.Sim.Combat.Morale.SideKind.Bandits, seed, 4401, 0, 9 * 3600, 0).Start(fx.C, new Ravensguard.Sim.Combat.Wounds.WoundLedger(seed, 9 * 3600));
            f.Fighters[0].Player = false;
            for (int t = 0; t < 20 * 120 && !f.IsOver; t++) f.StepWithAi();
            var h = f.Fighters[0];
            if (!h.Active) heroDown++;
            foreach (var w in h.Book.Records)
            {
                if (w.Structure >= 0) { structs++; if (h.Plan.Parts[w.Part].Structures[w.Structure].Kind == StructureKind.Tendon) tendons++; }
                if (w.Cat == Ravensguard.Sim.Combat.Wounds.WoundCat.Bone && w.Grade >= 2) broken++;
            }
        }
        o.WriteLine($"structs {structs} tendons {tendons} broken {broken} heroDown {heroDown}");
    }
}
