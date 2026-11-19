using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// One entry point for every fight (overview 3): the kind, the sides as kits with places, the seed and id, the game second it starts and what caused it. Built by a road encounter
/// (<see cref="Encounter"/>), the practice yard, a site's inhabitants waking, and the tests; <see cref="Start"/> turns it into a live <see cref="FightWorld"/>.
/// </summary>
public sealed record FightSetup(string Kind, IReadOnlyList<FighterKit> Kits, ulong Seed, ulong FightId, long GameSec, string Cause = "", int PlayerIndex = -1)
{
    /// <summary>The fight in the live bubble: fighters in kit order; the hero (if any) is driven by the player's inputs; the wounds go into <paramref name="ledger"/>.</summary>
    public FightWorld Start(FightContent content, WoundLedger? ledger)
    {
        var w = new FightWorld(content, Seed, FightId, GameSec, ledger);
        for (int i = 0; i < Kits.Count; i++) w.Add(Kits[i], player: i == PlayerIndex);
        if (PlayerIndex >= 0) w.LodCentre = (Kits[PlayerIndex].X, Kits[PlayerIndex].Y);
        return w;
    }

    // ------------------------------------------------------------------ kits

    private static (string Shape, string Material, int Count) Item(string s)
    {
        var p = s.Split(':');
        return (p[0], p.Length > 1 ? p[1] : "iron", p.Length > 2 && int.TryParse(p[2], out var n) ? n : 1);
    }

    public static FightKitDef KitDef(FightContent c, string archetype, string kit)
    {
        var all = c.Db.All<FightKitDef>().ToDictionary(d => d.Id, StringComparer.Ordinal);
        return all.TryGetValue(archetype, out var a) ? a : all.TryGetValue(kit, out var k) ? k : all.TryGetValue("default", out var d) ? d : new FightKitDef { Id = "default" };
    }

    /// <summary>A person of a group, kitted by the row: the weapon (one of the row's, by the person's id), a shield by chance, the worn pieces, a bow by the band's share, skill in the row's range.</summary>
    public static FighterKit Roll(FightContent c, FightKitDef d, ulong seed, ulong person, string name, int team, int x, int y, int faceX, int faceY)
    {
        int R(uint draw, int n) => DetRng.Range(seed, 0, person, RngStreams.StanceNoise, 0x4000_0000u + draw, 0, Math.Max(1, n));
        var weapons = new List<string> { d.Weapon }; weapons.AddRange(d.AltWeapons);
        var (ws, wm, _) = Item(weapons[R(0, weapons.Count)]);
        bool bow = d.BowPm > 0 && d.Launcher.Length > 0 && R(1, 1000) < d.BowPm;
        bool shield = d.Shield.Length > 0 && !bow && R(2, 1000) < d.ShieldPm;
        int skill = d.SkillMin + R(3, Math.Max(1, d.SkillMax - d.SkillMin + 1));
        return new FighterKit
        {
            Name = name, Team = team, Person = person, Weapon = bow ? null : new WeaponInstance(ws, wm), Shield = shield ? d.Shield : null,
            Worn = [.. d.Worn.Select(s => { var (a, m, _) = Item(s); return new WornItem(a, m); })],
            Launcher = bow ? d.Launcher : null, Ammo = bow ? d.Ammo : null, AmmoCount = bow ? d.AmmoCount : 0,
            Throwables = bow ? [] : [.. d.Throwables.Select(s => { var (a, m, n) = Item(s); return new ThrowStock(a, m, n); })],
            Skill = skill, ArcherySkill = skill, ThrowSkill = skill, ShieldSkill = skill, Strength = d.Strength, Toughness = d.Toughness, SizeCm3 = d.SizeCm3, Courage = d.Courage,
            Stance = d.Stance, Plan = d.Plan, X = x, Y = y, FaceX = faceX, FaceY = faceY,
        };
    }

    /// <summary>
    /// The hero's kit from the record: the best weapon carried for the hero's skills (a launcher with arrows when no melee weapon), a shield, a javelin to throw, the worn pieces
    /// given, and the family skills (Melee x0.8 when a family was never trained).
    /// </summary>
    public static FighterKit HeroKit(FightContent c, HeroRecord h, IReadOnlyList<WornItem> worn, ulong heroId, string name = "You")
    {
        int Sk(Skill s) => CombatSkills.Level(k => h.Skill(HeroRecord.SkillId(k)), s);
        var shapes = c.Db.All<WeaponShapeDef>().ToDictionary(s => s.Id, StringComparer.Ordinal);
        WeaponInstance? best = null; int bestScore = -1;
        string? launcher = null, ammo = null; int ammoCount = 0; string? shield = null;
        var throws = new List<ThrowStock>();
        foreach (var it in h.Inventory)
        {
            if (c.Launchers.Values.FirstOrDefault(l => l.Shape == it.Shape) is { } l) { launcher = l.Id; continue; }
            if (c.Ammo.ContainsKey(it.Shape)) { ammo = it.Shape; ammoCount += it.Count; continue; }
            if (c.T.Shields.ContainsKey(it.Shape)) { shield ??= it.Shape; continue; }
            if (it.Shape == "javelin") { throws.Add(new ThrowStock(it.Shape, it.Material, it.Count)); continue; }
            if (!shapes.TryGetValue(it.Shape, out var sh) || sh.Family is "ranged" or "natural" or "practice" or "unarmed" || !c.T.HasMaterial(it.Material)) continue;
            int score = Sk(CombatSkills.Of(FightWorld.FamilyOf(sh))) * 10 + (int)(sh.HeadVolumeCm3 / 100);
            if (score > bestScore) { bestScore = score; best = new WeaponInstance(it.Shape, it.Material); }
        }
        if (best != null) { launcher = null; ammo = null; ammoCount = 0; }
        else if (launcher == null || ammo == null) launcher = null;
        var fam = best != null && shapes.TryGetValue(best.ShapeId, out var bs) ? FightWorld.FamilyOf(bs) : launcher != null ? "archery" : "melee";
        if (best != null && shapes[best.ShapeId].TwoHanded) shield = null;
        return new FighterKit
        {
            Name = name, Team = 0, Person = heroId, Weapon = best, Shield = launcher == null ? shield : null, Worn = worn, Launcher = launcher, Ammo = ammo, AmmoCount = ammoCount, Throwables = throws,
            Skill = Sk(CombatSkills.Of(fam)), ShieldSkill = Sk(Skill.Shields), ThrowSkill = Sk(Skill.Throwing), ArcherySkill = Sk(Skill.Archery),
            Strength = 1250 * Math.Max(1, h.Attribute(HeroAttr.Strength)) / Math.Max(1, HeroRecord.BaselineAttribute), Toughness = 10 * Math.Max(1, h.Attribute(HeroAttr.Toughness)) / Math.Max(1, HeroRecord.BaselineAttribute),
            Stance = "balanced",
        };
    }

    /// <summary>
    /// A road encounter's fight (overview 3, replacing the Phase 3 stub): the hero (and party) at the origin facing east, each cast member kitted by its group's row, placed in a
    /// loose line 7 m ahead (an ambush: around the hero). The fight id is the encounter's second and slot, so the same encounter fought again is the same fight.
    /// </summary>
    public static FightSetup Encounter(FightContent c, EncounterDef d, Travel.EncounterEvent ev, FighterKit hero, IReadOnlyList<FighterKit> party, ulong seed)
    {
        var kits = new List<FighterKit> { hero with { X = 0, Y = 0, FaceX = FightMath.One, FaceY = 0, Team = 0 } };
        for (int i = 0; i < party.Count; i++) kits.Add(party[i] with { Team = 0, X = -800, Y = (i % 2 == 0 ? 1 : -1) * (1000 + 600 * (i / 2)), FaceX = FightMath.One, FaceY = 0 });
        int n = 0;
        bool ambush = d.Script == "ambush_attack";
        foreach (var g in d.Groups)
        {
            var row = KitDef(c, g.Archetype, g.Kit);
            int count = Math.Max(1, ev.Cast.Length / Math.Max(1, d.Groups.Count));
            for (int k = 0; k < count && n < ev.Cast.Length; k++, n++)
            {
                ulong id = ev.Cast[n];
                int x, y;
                if (ambush) { var (dx, dy) = FightMath.Dir((n * 64 / Math.Max(1, ev.Cast.Length) + 8) % 64); x = dx * 7; y = dy * 7; }
                else { x = 7000 + (n % 2) * 800; y = (n - ev.Cast.Length / 2) * 1400; }
                var (fx, fy) = FightMath.Norm(-x, -y);
                kits.Add(Roll(c, row, seed, id, $"{g.Archetype} {k + 1}", 1, x, y, fx, fy));
            }
        }
        ulong fightId = (ulong)ev.Sec * 64 + (ulong)(ev.Leg & 63);
        return new FightSetup("encounter", kits, seed ^ 0x4F17_0000UL, fightId, ev.Sec, ev.Id, PlayerIndex: 0);
    }
}
