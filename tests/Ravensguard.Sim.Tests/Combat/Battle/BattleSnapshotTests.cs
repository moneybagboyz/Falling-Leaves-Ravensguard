using System.Text.Json;
using System.Text.RegularExpressions;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Tests.Combat.Melee2;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Phase 7 BB-E (rendering): the read-only snapshot the crowd layer draws from, and BB-18 (a Field mini in three views and a Command icon for every troop role, in the baked atlas the game ships).
/// BB-3 (camera independence) needs the engine and runs in the Game's <c>--selftest</c>; here the snapshot is shown to leave the fight untouched, which is what that check leans on.
/// </summary>
[Collection("fight")]
public class BattleSnapshotTests(FightFixture fx)
{
    private static readonly WeaponInstance Spear = new("spear", "steel");

    private FightWorld Small(int perSide = 40, int rowSize = 20)
    {
        var w = fx.World(9); w.RecordEvents = false;
        var book = w.EnsureFormations();
        for (int team = 0; team < 2; team++)
            for (int k = 0; k < perSide / rowSize; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < rowSize; i++)
                {
                    var kit = (i % 4) switch
                    {
                        0 => fx.Kit($"s{i}", team, 0, 0, shield: "heater"),
                        1 => fx.Kit($"p{i}", team, 0, 0, weapon: Spear),
                        2 => fx.Kit($"w{i}", team, 0, 0),
                        _ => new FighterKit { Name = $"a{i}", Team = team, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Worn = [new WornItem("tunic", "linen")], Skill = 40, ArcherySkill = 40, FaceX = team == 0 ? 1024 : -1024 },
                    };
                    ids.Add(w.Add(kit with { Courage = 100 }).Id);
                }
                book.Form(w, team, (byte)(k + 1), FormationKind.Infantry, ids, team == 0 ? -8_000 : 8_000, (k - 1) * 20_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 4, place: true);
            }
        return w;
    }

    [Fact]
    public void Capture_CopiesEveryFighter_AndLeavesTheFightHashUntouched()
    {
        var a = Small(); var b = Small();
        BattleSnapshot? spare = null;
        for (int t = 0; t < 150; t++)
        {
            a.StepWithAi(); b.StepWithAi();
            spare = BattleSnapshot.Capture(a, spare); // a is captured every tick, b never
        }
        Assert.Equal(b.Hash(), a.Hash());
        Assert.Equal(a.Fighters.Count, spare!.Count);
        for (int i = 0; i < spare.Count; i++)
        {
            var f = a.Fighters[i];
            Assert.Equal(f.X, spare.X[i]); Assert.Equal(f.Y, spare.Y[i]); Assert.Equal(f.Team, spare.Team[i]); Assert.Equal((byte)f.Act, spare.Act[i]);
            Assert.Equal(f.FmRow, spare.Form[i]);
        }
    }

    [Fact]
    public void Roles_ComeFromTheKit_ArchersSpearmenAndShieldmen()
    {
        var w = Small();
        var s = BattleSnapshot.Capture(w);
        var roles = Enumerable.Range(0, s.Count).Select(i => s.Role[i]).Distinct().ToHashSet();
        Assert.Contains(BattleSnapshot.Archer, roles);
        Assert.Contains(BattleSnapshot.Levy, roles);
        Assert.All(roles, r => Assert.InRange(r, (byte)0, (byte)(BattleSnapshot.RoleCount - 1)));
        // an override wins
        var s2 = BattleSnapshot.Capture(w, s, _ => BattleSnapshot.Knight);
        Assert.All(Enumerable.Range(0, s2.Count), i => Assert.Equal(BattleSnapshot.Knight, s2.Role[i]));
    }

    [Fact]
    public void Formations_ShowAsBlocks_WithTheirCountsAndAnExtent()
    {
        var w = Small(40, 20);
        for (int t = 0; t < 30; t++) w.StepWithAi();
        var s = BattleSnapshot.Capture(w);
        Assert.Equal(4, s.FormCount);
        for (int k = 0; k < s.FormCount; k++)
        {
            var m = s.Forms[k];
            Assert.Equal(20, m.Total); Assert.InRange(m.Active, 1, 20);
            Assert.True(m.FrontMm >= 1000 && m.DepthMm >= 1000);
            Assert.True(m.FrontMm < 60_000 && m.DepthMm < 60_000, "twenty men stand in a space of tens of metres");
        }
    }

    [Fact]
    public void Flags_MarkTheDownAndTheDead()
    {
        var w = Small();
        w.Fighters[3].Act = FAct.Down; w.Fighters[4].Act = FAct.Dead; w.Fighters[5].Act = FAct.Fled;
        var s = BattleSnapshot.Capture(w);
        Assert.True(((SnapFlags)s.Flags[3] & SnapFlags.Down) != 0);
        Assert.True(((SnapFlags)s.Flags[4] & SnapFlags.Dead) != 0);
        Assert.True(((SnapFlags)s.Flags[5] & SnapFlags.Gone) != 0);
        Assert.Equal(SnapFlags.None, (SnapFlags)s.Flags[6]);
    }

    // ---------------------------------------------------------------- BB-18

    private static string Repo()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Ravensguard.slnx"))) d = Path.GetDirectoryName(d);
        return d ?? throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void BB18_EveryTroopRoleHasThreeFieldMinisAndACommandIcon_InTheShippedAtlas()
    {
        string root = Repo();
        var roles = Regex.Matches(File.ReadAllText(Path.Combine(root, "content/base/war/troops.jsonc")), "\"type\": \"troop_role\", \"id\": \"([a-z_]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(10, roles.Count);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/Ravensguard.Game/assets/art/index.json")));
        var sp = doc.RootElement.GetProperty("sprites");
        foreach (var r in roles)
        {
            foreach (var v in new[] { "front", "back", "side" })
            {
                Assert.True(sp.TryGetProperty($"mini.troop.{r}.{v}", out var e), $"mini.troop.{r}.{v}");
                Assert.Equal(8, e.GetProperty("w").GetInt32()); Assert.Equal(16, e.GetProperty("h").GetInt32());
            }
            Assert.True(sp.TryGetProperty($"mini.icon.{r}", out var ic), $"mini.icon.{r}");
            Assert.Equal(8, ic.GetProperty("w").GetInt32()); Assert.Equal(8, ic.GetProperty("h").GetInt32());
        }
        // the snapshot's role indices are the art's: the same ten, in the same order
        Assert.Equal(["levy", "spearman", "archer", "crossbowman", "sergeant", "man_at_arms", "knight", "engineer", "sailor", "marine"], roles);
        Assert.Equal(BattleSnapshot.RoleCount, roles.Count);
    }
}
