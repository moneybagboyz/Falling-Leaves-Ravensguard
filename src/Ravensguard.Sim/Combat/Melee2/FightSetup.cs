using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>One side of a fight as the setup sees it: its team, what kind of side it is (who accepts surrenders, how its losers fare), its stance default and its people.</summary>
public sealed record FightSide(int Team, SideKind Kind, IReadOnlyList<FighterKit> Kits);

/// <summary>
/// One entry point for every fight (overview 3, P4-F): the kind (<see cref="FightKind"/>: a road encounter, a raid, an ambush, a site's inhabitants waking, a tournament bout, a
/// practice bout), the sides as kits with places, the terrain and weather, what caused it, the stance defaults by side, the seed and id and the game second it starts. Built by
/// travel encounters (<see cref="Encounter"/>), raids and ambush scripts (<see cref="Raid"/>, <see cref="Ambush"/>), sites (<see cref="SiteInhabitants"/>), the practice yard
/// (<see cref="Practice"/>) and tournaments (<see cref="Tournament"/>). <see cref="Run"/> spawns a live <see cref="FightWorld"/> in the bubble, or, outside any bubble, hands the
/// fight to the abstract resolver (<see cref="IAbstractFights"/>: a stub until abstract-battles-v1, step 6).
/// </summary>
public sealed record FightSetup(FightKind Kind, IReadOnlyList<FighterKit> Kits, ulong Seed, ulong FightId, long GameSec, string Cause = "", int PlayerIndex = -1)
{
    /// <summary>The ground (footing, slopes, rain) the horses read; null: an open field.</summary>
    public FightTerrain? Terrain { get; init; }
    /// <summary>The weather at the start: a <c>WeatherState</c> name ("HeavyRain", "Fog" ...; P4 close-out, the live weather) or the old words "rain", "storm", "snow"; "" none. Its <c>weather_combat</c> row goes on the fight.</summary>
    public string Weather { get; init; } = "";
    /// <summary>What kind of side each team is (who accepts a surrender, the defeat table's winner); unlisted teams are folk.</summary>
    public IReadOnlyDictionary<int, SideKind> SideKinds { get; init; } = new Dictionary<int, SideKind>();
    /// <summary>The stance a side's fighters take when their kit names none (a guard post defensive, a raid aggressive).</summary>
    public IReadOnlyDictionary<int, string> StanceDefaults { get; init; } = new Dictionary<int, string>();
    /// <summary>The hero's stance (watch-mode 3.1, from the hero's saved <c>WatchStance</c>) and whether the fight starts watched (autopilot with Attacked on Ai, or "keep watching").</summary>
    public WatchStance? HeroStance { get; init; }
    public bool StartWatched { get; init; }
    /// <summary>The hero's Tactics (the side's captain picks the formation, watch-mode 4.4) and Leadership (the hero leads a side at 20, morale 4.1 g).</summary>
    public int HeroTactics { get; init; } = -1;
    public int HeroLeadership { get; init; }

    /// <summary>The record's name of the kind ("encounter", "practice", ...); a saved fight's rules come back from it.</summary>
    public string KindName => NameOf(Kind);
    public static string NameOf(FightKind k) => Flavour.FlavourBook.Snake(k.ToString());
    public static FightKind? KindOf(string name) { foreach (var k in Enum.GetValues<FightKind>()) if (NameOf(k) == name) return k; return null; }

    /// <summary>The sides by team, in team order, each with its kits in setup order.</summary>
    public IReadOnlyList<FightSide> Sides => [.. Kits.Select(k => k.Team).Distinct().Order().Select(t => new FightSide(t, SideKinds.TryGetValue(t, out var sk) ? sk : SideKind.Folk, [.. Kits.Where(k => k.Team == t)]))];

    /// <summary>The fight in the live bubble, built and not stepped: fighters in kit order (the hero, if any, driven by the player's inputs), the kind's rules, sides, terrain.</summary>
    public FightWorld Build(FightContent content, WoundLedger? ledger)
    {
        var w = new FightWorld(content, Seed, FightId, GameSec, ledger) { Rules = FightRules.For(Kind), SetupKind = Kind };
        for (int i = 0; i < Kits.Count; i++)
        {
            var k = Kits[i];
            if (StanceDefaults.TryGetValue(k.Team, out var sd) && k.Stance.Length == 0) k = k with { Stance = sd };
            w.Add(k, player: i == PlayerIndex);
        }
        if (PlayerIndex >= 0) { w.LodCentre = (Kits[PlayerIndex].X, Kits[PlayerIndex].Y); w.LodFighter = PlayerIndex; }
        foreach (var (team, kind) in SideKinds) w.SetSide(team, kind);
        var tr = Terrain ?? new FightTerrain();
        if (Weather is "rain" or "storm") tr.HeavyRain = true;
        if (Weather == "storm") tr.Thunderstorm = true;
        // [P4 close-out] the weather row of the live weather (a WeatherState name): the whole 4.8 table, and the horses' footing: no charges in rain or snow, the storm's flashes
        if (FightWorld.WeatherRowOf(content.Db, Weather) is { } row)
        {
            w.WeatherRow = row;
            if (row.NoCharges) tr.HeavyRain = true;
            if (row.State == "Thunderstorm") tr.Thunderstorm = true;
        }
        w.UseTerrain(tr);
        return w;
    }

    /// <summary>The live fight, ready to step: built, the hero's stance and watch set (the same inputs a player sends, so a replay agrees), the captain's formation chosen.</summary>
    public FightWorld Start(FightContent content, WoundLedger? ledger)
    {
        var w = Build(content, ledger);
        if (PlayerIndex >= 0)
        {
            if (HeroStance is { } st) { w.EnsureWatch(PlayerIndex); w.SetStance(PlayerIndex, st, -1); }
            if (HeroTactics >= 0) w.CaptainFormation(Kits[PlayerIndex].Team, PlayerIndex, HeroTactics);
            if (StartWatched) w.SetWatch(PlayerIndex, true, takeBackOnInput: true);
        }
        return w;
    }

    /// <summary>
    /// Runs the fight where it is: in the live bubble (<paramref name="inBubble"/>) a live <see cref="FightWorld"/> to step, else the abstract resolver's report. The seam of overview 3:
    /// abstract-battles-v1 (step 6) is <see cref="Abstract.AbstractBattles"/> behind <see cref="IAbstractFights"/> (the stub when the content has no wound tables); the callers do not change.
    /// </summary>
    public FightRun Run(FightContent content, WoundLedger? ledger, bool inBubble, IAbstractFights? abstractFights = null)
        => inBubble ? new FightRun(Start(content, ledger), null) : new FightRun(null, (abstractFights ?? Abstract.AbstractBattles.Default).Resolve(this, content));   // P4-G: the abstract battle

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
    /// given, and the family skills (Melee x0.8 when a family was never trained). <paramref name="carried"/> is what the hero really carries (P5-A4: the pack's item rows with their quality,
    /// <see cref="Ravensguard.Sim.Inventory.HeroPack.Things"/>); without it the record's starting kit is read, as before.
    /// </summary>
    public static FighterKit HeroKit(FightContent c, HeroRecord h, IReadOnlyList<WornItem> worn, ulong heroId, string? name = null, IReadOnlyList<HeroItem>? carried = null)
    {
        int Sk(Skill s) => CombatSkills.Level(k => h.Skill(HeroRecord.SkillId(k)), s);
        var shapes = c.Db.All<WeaponShapeDef>().ToDictionary(s => s.Id, StringComparer.Ordinal);
        WeaponInstance? best = null; int bestScore = -1;
        string? launcher = null, ammo = null; int ammoCount = 0; string? shield = null;
        var throws = new List<ThrowStock>();
        foreach (var it in carried ?? h.Inventory)
        {
            if (c.Launchers.Values.FirstOrDefault(l => l.Shape == it.Shape) is { } l) { launcher = l.Id; continue; }
            if (c.Ammo.ContainsKey(it.Shape)) { ammo = it.Shape; ammoCount += it.Count; continue; }
            if (c.T.Shields.ContainsKey(it.Shape)) { shield ??= it.Shape; continue; }
            if (it.Shape == "javelin") { throws.Add(new ThrowStock(it.Shape, it.Material, it.Count)); continue; }
            if (!shapes.TryGetValue(it.Shape, out var sh) || sh.Family is "ranged" or "natural" or "practice" or "unarmed" || !c.T.HasMaterial(it.Material)) continue;
            int score = Sk(CombatSkills.Of(FightWorld.FamilyOf(sh))) * 10 + (int)(sh.HeadVolumeCm3 / 100) + (int)it.Quality;    // P5-A1: of two alike, the better made
            if (score > bestScore) { bestScore = score; best = new WeaponInstance(it.Shape, it.Material, it.Quality); }
        }
        if (best != null) { launcher = null; ammo = null; ammoCount = 0; }
        else if (launcher == null || ammo == null) launcher = null;
        var fam = best != null && shapes.TryGetValue(best.ShapeId, out var bs) ? FightWorld.FamilyOf(bs) : launcher != null ? "archery" : "melee";
        if (best != null && shapes[best.ShapeId].TwoHanded) shield = null;
        return new FighterKit
        {
            Name = name ?? h.Name, Team = 0, Person = heroId, Weapon = best, Shield = launcher == null ? shield : null, Worn = worn, Launcher = launcher, Ammo = ammo, AmmoCount = ammoCount, Throwables = throws,
            Skill = Sk(CombatSkills.Of(fam)), ShieldSkill = Sk(Skill.Shields), ThrowSkill = Sk(Skill.Throwing), ArcherySkill = Sk(Skill.Archery),
            Strength = 1250 * Math.Max(1, h.Attribute(HeroAttr.Strength)) / Math.Max(1, HeroRecord.BaselineAttribute), Toughness = 10 * Math.Max(1, h.Attribute(HeroAttr.Toughness)) / Math.Max(1, HeroRecord.BaselineAttribute),
            Stance = "balanced", Tactics = h.Skill(HeroRecord.SkillId(Skill.Tactics)),
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
        var kind = ambush ? FightKind.Ambush : FightKind.Encounter;
        var side = d.Kind switch { "Animal" => SideKind.Beasts, "Ambush" or "Demand" => SideKind.Bandits, "Check" or "Column" => SideKind.Guards, _ => SideKind.Folk };
        return new FightSetup(kind, kits, seed ^ 0x4F17_0000UL, fightId, ev.Sec, ev.Id, PlayerIndex: 0)
        {
            SideKinds = side == SideKind.Folk ? new Dictionary<int, SideKind>() : new Dictionary<int, SideKind> { [1] = side },
            HeroTactics = party.Count > 0 ? hero.Tactics : -1,
        };
    }

    // ------------------------------------------------------------------ the other kinds (overview 3)

    /// <summary>The practice weapon of a weapon family: a staff for hafted and bare hands, a spear for polearms, else a sword (the yard's rack, onboarding 3.4).</summary>
    public static string PracticeShape(string family) => family switch { "polearms" => "practice_spear", "hafted" or "melee" => "practice_staff", _ => "practice_sword" };

    /// <summary>
    /// A practice bout in the yard (onboarding 3.4 on the real fight): the hero against the trainer 2 m apart, both with the rack's blunted practice weapons (x0.5 momentum, edges
    /// counted as blunt), no shields or armour beyond what is worn; first to three clean hits (<see cref="FightRules.For"/>), and a fighter who reaches the pain knock-out yields.
    /// </summary>
    public static FightSetup Practice(FightContent c, FighterKit hero, FighterKit trainer, ulong seed, long gameSec, ulong yardId)
    {
        string fam(FighterKit k) => k.Weapon == null ? "melee" : FightWorld.FamilyOf(c.T.Weapon(k.Weapon.ShapeId, k.Weapon.MaterialId).Shape);
        FighterKit Blunt(FighterKit k, int team, int x, int faceX) => k with
        {
            Team = team, X = x, Y = 0, FaceX = faceX, FaceY = 0, Weapon = new WeaponInstance(PracticeShape(fam(k)), "oak"), Shield = null, Launcher = null, Ammo = null, AmmoCount = 0, Throwables = [],
            Improvised = null, Blunted = true, Stance = "balanced",
        };
        var kits = new List<FighterKit> { Blunt(hero, 0, 0, FightMath.One), Blunt(trainer, 1, 2000, -FightMath.One) };
        return new FightSetup(FightKind.Practice, kits, seed ^ 0x9A4C_0000UL, yardId * 4096 + (ulong)(gameSec / 300 & 4095), gameSec, "yard", PlayerIndex: 0);
    }

    /// <summary>
    /// A tournament bout (plan 4A.3): the hero against an entrant 3 m apart, both with their own weapons blunted (x0.5 momentum, edges and points count as blunt), yielding at the pain
    /// knock-out. The purse is paid from the town treasury by <see cref="TournamentPurse.Pay"/> (coin moves, none is made).
    /// </summary>
    public static FightSetup Tournament(FightContent c, FighterKit hero, FighterKit entrant, ulong seed, long gameSec, ulong settlementId, int bout)
    {
        var kits = new List<FighterKit>
        {
            hero with { Team = 0, X = 0, Y = 0, FaceX = FightMath.One, FaceY = 0, Blunted = true, Launcher = null, Ammo = null, AmmoCount = 0, Throwables = [] },
            entrant with { Team = 1, X = 3000, Y = 0, FaceX = -FightMath.One, FaceY = 0, Blunted = true, Launcher = null, Ammo = null, AmmoCount = 0, Throwables = [] },
        };
        return new FightSetup(FightKind.Tournament, kits, seed ^ 0x7E11_0000UL, settlementId * 1024 + (ulong)(bout & 1023) + ((ulong)(gameSec / 86400) << 32), gameSec, "tournament", PlayerIndex: 0);
    }

    /// <summary>
    /// A raid on a place (bandits on a farmstead, a reaver band on a village edge): the raiders come in a loose line from 15 m, aggressive by default; the defenders stand where they
    /// are, defensive. The hero, if among the defenders, is <paramref name="heroIndex"/> of <paramref name="defenders"/>.
    /// </summary>
    public static FightSetup Raid(IReadOnlyList<FighterKit> defenders, IReadOnlyList<FighterKit> raiders, ulong seed, ulong fightId, long gameSec, string cause, int heroIndex = -1, SideKind raiderKind = SideKind.Bandits)
    {
        var kits = new List<FighterKit>();
        for (int i = 0; i < defenders.Count; i++) kits.Add(defenders[i] with { Team = 0, X = defenders[i].X, Y = defenders[i].Y == 0 && i > 0 ? (i % 2 == 0 ? 1 : -1) * 900 * ((i + 1) / 2) : defenders[i].Y, FaceX = FightMath.One, FaceY = 0 });
        for (int i = 0; i < raiders.Count; i++) kits.Add(raiders[i] with { Team = 1, X = 15_000 + (i % 2) * 700, Y = (i - raiders.Count / 2) * 1300, FaceX = -FightMath.One, FaceY = 0 });
        return new FightSetup(FightKind.Raid, kits, seed, fightId, gameSec, cause, heroIndex)
        {
            SideKinds = new Dictionary<int, SideKind> { [1] = raiderKind },
            StanceDefaults = new Dictionary<int, string> { [0] = "defensive", [1] = "aggressive" },
        };
    }

    /// <summary>An ambush (a script or a site's trap): the ambushers close in a ring 6 m around the victims' centre, aggressive; the victims start where they stand.</summary>
    public static FightSetup Ambush(IReadOnlyList<FighterKit> victims, IReadOnlyList<FighterKit> ambushers, ulong seed, ulong fightId, long gameSec, string cause, int heroIndex = -1, SideKind ambusherKind = SideKind.Bandits)
    {
        var kits = new List<FighterKit>();
        foreach (var v in victims) kits.Add(v with { Team = 0 });
        for (int i = 0; i < ambushers.Count; i++)
        {
            var (dx, dy) = FightMath.Dir((i * 64 / Math.Max(1, ambushers.Count) + 8) % 64);
            int x = dx * 6, y = dy * 6; var (fx, fy) = FightMath.Norm(-x, -y);
            kits.Add(ambushers[i] with { Team = 1, X = x, Y = y, FaceX = fx, FaceY = fy });
        }
        return new FightSetup(FightKind.Ambush, kits, seed, fightId, gameSec, cause, heroIndex)
        {
            SideKinds = new Dictionary<int, SideKind> { [1] = ambusherKind },
            StanceDefaults = new Dictionary<int, string> { [1] = "aggressive" },
        };
    }

    /// <summary>
    /// A site's inhabitants waking (exploration-and-sites, step 7, builds the kits from the site's history and calls this): the intruders at the door, the inhabitants where the site
    /// put them; beasts and draugr never accept a surrender (morale 4.2).
    /// </summary>
    public static FightSetup SiteInhabitants(IReadOnlyList<FighterKit> intruders, IReadOnlyList<FighterKit> inhabitants, SideKind inhabitantKind, ulong seed, ulong siteId, int serial, long gameSec, int heroIndex = -1, FightTerrain? terrain = null)
    {
        var kits = new List<FighterKit>();
        foreach (var k in intruders) kits.Add(k with { Team = 0 });
        foreach (var k in inhabitants) kits.Add(k with { Team = 1 });
        return new FightSetup(FightKind.SiteInhabitants, kits, seed, siteId * 256 + (ulong)(serial & 255), gameSec, "site", heroIndex)
        {
            Terrain = terrain ?? new FightTerrain { Base = Footing.Rough },
            SideKinds = new Dictionary<int, SideKind> { [1] = inhabitantKind },
            StanceDefaults = new Dictionary<int, string> { [1] = "aggressive" },
        };
    }
}

/// <summary>
/// A tournament's purse (plan 4A.3: "the purse comes from the town treasury"; Phase 3's contest purse): paid to the winner by one ledger transfer from the treasury, never more
/// than the treasury holds, so coin is conserved (the ledger's audit stays exact). The reason is the ledger's <c>Gift</c> (a prize), as the jobs' escrow aliases its reasons.
/// </summary>
public static class TournamentPurse
{
    public const Economy.TransferReason Prize = Economy.TransferReason.Gift;

    /// <summary>Pays <paramref name="mites"/> (at most what the treasury holds) from <paramref name="treasury"/> to <paramref name="winner"/>. Returns what was paid.</summary>
    public static long Pay(Economy.Ledger ledger, Economy.PurseId treasury, Economy.PurseId winner, long mites)
    {
        if (mites <= 0 || treasury == Economy.PurseId.None || winner == Economy.PurseId.None || treasury == winner) return 0;
        long pay = Math.Min(mites, ledger.Balance(treasury).Mites);
        return pay > 0 && ledger.Transfer(treasury, winner, new Economy.Coin(pay), Prize) ? pay : 0;
    }
}
