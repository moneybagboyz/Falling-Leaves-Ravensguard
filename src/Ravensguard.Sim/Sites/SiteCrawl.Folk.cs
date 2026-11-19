using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// The site's people and monsters (4.3, 4.9; <c>LairBrain</c>): sleep, dormancy and wake triggers, sight and hearing, the hunt along the pathfinder (portals included), the leash
/// and the way home; and the fight when they close in, through <see cref="FightSetup.SiteInhabitants"/> (watch-mode Notes: step 5 made it ready). Behaviour draws key on the
/// sim-second and the creature (stream 0x4B6). Brains run every fourth step while nothing is hunted, every step while hunting (5 Hz and 20 Hz, 5).
/// </summary>
public sealed partial class SiteCrawl
{
    /// <summary>The fight in progress (null outside one).</summary>
    public FightWorld? Fight { get; private set; }
    public FightSetup? FightSetupUsed { get; private set; }
    /// <summary>Fighter index to member (>= 0) or inhabitant (-1 - index).</summary>
    private int[] _fightMap = [];
    /// <summary>The hero's fighter in <see cref="Fight"/> (-1 when the hero is not in it).</summary>
    public int HeroFighter => Array.IndexOf(_fightMap, 0);
    private int _fightOx, _fightOy, _fightLevel, _fights;
    /// <summary>The game day the site was cleared (no hostile left standing), -1 while it is not.</summary>
    public int ClearedDay { get; set; } = -1;
    /// <summary>Monsters slain here (the renown stub, 4.9) and their legend names.</summary>
    public List<string> Slain { get; } = [];
    public string LastFightEnd { get; private set; } = "";

    private SiteMonsterDef? MonsterDef(Inhabitant n) => n.Monster.Length > 0 && C.Monsters.TryGetValue(n.Monster, out var d) ? d : null;

    // ------------------------------------------------------------------ senses

    /// <summary>How far a member's noise carries (4.9): sneaking 8 m, walking 15, running 25, a fight 30; a still member makes none (a resting one, 8).</summary>
    public int NoiseM(CrawlMember m)
    {
        if (m.Dead) return 0;
        if (Fight != null) return 30;
        bool moving = m.MoveDir >= 0 || m.Path != null;
        if (!moving) return Resting ? 8 : 0;
        if (m.Sneak) return 8;
        return m.Mode == CrawlMode.Sprint ? 25 : 15;
    }

    private static long D2(int ax, int ay, int bx, int by) { long dx = ax - bx, dy = ay - by; return dx * dx + dy * dy; }

    /// <summary>Does <paramref name="n"/> notice <paramref name="m"/> this sim-second? Sight by light (or a monster's own dark sight), hearing by the member's noise; a sneaking member within
    /// earshot is noticed at 1% a sim-second per 10 Stealth below 50 (4.9).</summary>
    public bool Notices(Inhabitant n, CrawlMember m, SiteMonsterDef? d, bool asleep)
    {
        if (m.Dead || m.Level != n.Level) return false;
        long d2 = D2(n.X, n.Y, m.X, m.Y);
        var (_, nx, ny) = Map.At(CellOf(n)); var (_, mx, my) = Map.At(m.Cell);
        bool los = AutoMap.Clear(Map, n.Level, nx, ny, mx, my);
        if (!asleep && los)
        {
            long sight = d != null && d.DarkSightM > 0 ? d.DarkSightM * 1000L : d != null && d.HearWalkM > 0 ? 0 : SiteLight.VisionRadiusMm(LightAt(m.Cell));
            if (sight > 0 && d2 <= sight * sight) return true;
        }
        int noise = NoiseM(m);
        if (noise <= 0) return false;
        // people hear less well than the beast that hunts by sound
        int hear = d != null && d.HearWalkM > 0 ? (m.Sneak ? d.HearSneakM : m.Mode == CrawlMode.Sprint ? d.HearRunM : d.HearWalkM) : noise * 6 / 10;
        if (Fight != null && d != null && d.HearFightM > 0) hear = d.HearFightM;
        if (d2 > (long)hear * hear * 1_000_000) return false;
        if (!m.Sneak) return true;
        int pm10 = Math.Max(0, (50 - m.Stealth) / 10) * 10;                       // tenths of a percent a sim-second
        return DetRng.Range(Seed, SimSecond, n.Id, RngStreams.MonsterBehaviour, (uint)Members.IndexOf(m), 0, 1000) < pm10;
    }

    private CrawlMember? Nearest(Inhabitant n, int withinM)
    {
        CrawlMember? best = null; long bd = (long)withinM * withinM * 1_000_000 + 1;
        foreach (var m in Members)
        {
            if (m.Dead || m.Level != n.Level) continue;
            long d = D2(n.X, n.Y, m.X, m.Y);
            if (d < bd) { bd = d; best = m; }
        }
        return best;
    }

    /// <summary>Is the cave beast asleep at this hour (4.9: 06:00 to 22:00 local game time, 16 hours of 24)?</summary>
    public static bool AsleepAt(SiteMonsterDef d, int daySec)
    {
        if (d.SleepHour < 0) return false;
        int h = daySec / 3600;
        return d.SleepHour < d.WakeHour ? h >= d.SleepHour && h < d.WakeHour : h >= d.SleepHour || h < d.WakeHour;
    }

    // ------------------------------------------------------------------ the brains

    private void Brains(bool slow)
    {
        bool second = GameSec % 20 == 0;
        for (int i = 0; i < Map.Inhabitants.Count; i++)
        {
            var n = Map.Inhabitants[i];
            if (n.State is InhabitantState.Dead or InhabitantState.Away or InhabitantState.Fighting) continue;
            if (n.State == InhabitantState.Fled) continue;
            bool hunting = n.State is InhabitantState.Hunt or InhabitantState.Investigate or InhabitantState.Return;
            if (second) Think(n);
            if (hunting || slow) MoveFolk(n, hunting ? 1 : 4);
            if (Fight != null) return;
        }
    }

    /// <summary>The once-a-sim-second decision of one inhabitant (LairBrain).</summary>
    private void Think(Inhabitant n)
    {
        var d = MonsterDef(n);
        if (d != null && d.Dormant) { Draugr(n, d); return; }
        if (d != null) { Beast(n, d); return; }
        Person(n);
    }

    /// <summary>The draugr (4.9): dormant on its bier until a grave good leaves the chamber, a fight is heard within 20 m, or a living person stays 60 sim-seconds in the chamber;
    /// then it wakes at 10% a sim-second, hunts the nearest living by its own senses (12 m, light or none), follows to 100 m from the mouth, and goes back to its bier after
    /// 120 sim-seconds without a living target within 60 m. It never flees and never yields.</summary>
    private void Draugr(Inhabitant n, SiteMonsterDef d)
    {
        int lair = Map.Room[n.Home >= 0 ? n.Home : CellOf(n)];
        if (n.State == InhabitantState.Dormant)
        {
            bool inChamber = Members.Any(m => !m.Dead && lair >= 0 && Map.Room[m.Cell] == lair);
            if (inChamber) n.StayS++;
            bool noise = Fight != null && n.Level == _fightLevel && D2(n.X, n.Y, _fightOx, _fightOy) <= (long)d.WakeNoiseM * d.WakeNoiseM * 1_000_000;
            if (n.StayS >= d.WakeStayS || noise || n.TriggerS > 0) n.TriggerS++;
            if (n.TriggerS > 0 && DetRng.Range(Seed, SimSecond, n.Id, RngStreams.MonsterBehaviour, 1, 0, 1000) < d.WakePmPerS) Wake(n);
            return;
        }
        Hunt(n, d);
    }

    private void Wake(Inhabitant n)
    {
        n.State = InhabitantState.Hunt; n.LostS = 0; n.LungeS = 0;
        Note(CrawlEventKind.MonsterWoke, Map.Inhabitants.IndexOf(n), 0, n.Name);
        foreach (var m in Members) StopPath(m, true);
        StopRest();
    }

    private void Hunt(Inhabitant n, SiteMonsterDef? d)
    {
        int loseM = d?.LoseM ?? 30, leash = d?.LeashM ?? 200, returnS = d?.ReturnAfterS ?? 60;
        if (n.State == InhabitantState.Return)
        {
            if (n.InvestigateS > 0) { n.InvestigateS--; if (n.Path == null) PathFolk(n, n.Home); return; }
            if (CellOf(n) == n.Home) { n.State = d?.Dormant == true ? InhabitantState.Dormant : InhabitantState.Home; n.TriggerS = 0; n.StayS = 0; n.Path = null; }
            else if (n.Path == null) PathFolk(n, n.Home);
            // a returning beast still answers what it meets
            var near = Nearest(n, 6);
            if (near != null && d?.Dormant != true && Notices(n, near, d, false)) { n.State = InhabitantState.Hunt; }
            return;
        }
        var t = Nearest(n, loseM);
        if (t == null) { if (++n.LostS >= returnS) Return(n); }
        else
        {
            n.LostS = 0;
            if (Notices(n, t, d, false) || n.State == InhabitantState.Hunt)
            {
                n.TargetX = t.X; n.TargetY = t.Y; n.TargetLevel = t.Level;
                if (n.Path == null || n.PathIdx >= n.Path.Count || SimSecond % 2 == 0) PathFolk(n, t.Cell);
            }
        }
        if (FromMouthM(n) > leash) Return(n);
        // the lunge (4.9): 5 sim-seconds at 3.5 m/s once within 6 m, then 20 at rest
        if (d != null)
        {
            if (n.LungeS > 0) { if (--n.LungeS == 0) n.LungeS = -d.RestS; }
            else if (n.LungeS < 0) n.LungeS++;
            else if (t != null && D2(n.X, n.Y, t.X, t.Y) <= 6000L * 6000) n.LungeS = d.LungeS;
        }
        if (t != null && D2(n.X, n.Y, t.X, t.Y) <= 4000L * 4000 && !t.Dead) StartFight(n);
    }

    private void Return(Inhabitant n) { n.State = InhabitantState.Return; n.Path = null; PathFolk(n, n.Home); }

    /// <summary>Distance from the mouth in metres: across the plane and three metres a level down (the leash, 4.9).</summary>
    public int FromMouthM(Inhabitant n)
    {
        var (_, mx, my) = Map.At(Map.Mouth);
        long dx = n.X - (mx * 1000 + 500), dy = n.Y - (my * 1000 + 500);
        return (int)(Isqrt(dx * dx + dy * dy) / 1000) + n.Level * C.K.LevelDepthM;
    }

    internal static long Isqrt(long v) => Ravensguard.Sim.WorldGen.Region.Region.Isqrt(v);

    /// <summary>The cave beast (4.9): asleep 06:00-22:00 in its den, it hunts by sound (walking 15 m, running 25, sneaking 8, a fight 30), investigates a sound for 20 sim-seconds, attacks
    /// within 8 m; it fled below 30% blood and recovers at its lair.</summary>
    private void Beast(Inhabitant n, SiteMonsterDef d)
    {
        bool asleep = AsleepAt(d, DaySec);
        if (n.State is InhabitantState.Asleep or InhabitantState.Home)
        {
            if (!asleep && n.State == InhabitantState.Asleep) n.State = InhabitantState.Home;
            if (asleep && n.State == InhabitantState.Home && CellOf(n) == n.Home) n.State = InhabitantState.Asleep;
            foreach (var m in Members)
                if (Notices(n, m, d, n.State == InhabitantState.Asleep))
                {
                    n.State = InhabitantState.Investigate; n.InvestigateS = d.InvestigateS; n.TargetX = m.X; n.TargetY = m.Y; n.TargetLevel = m.Level;
                    PathFolk(n, m.Cell);
                    Note(CrawlEventKind.MonsterWoke, Map.Inhabitants.IndexOf(n), 1, n.Name);
                    break;
                }
            return;
        }
        if (n.State == InhabitantState.Investigate)
        {
            var t = Nearest(n, d.AttackM);
            if (t != null && Notices(n, t, d, false)) { n.State = InhabitantState.Hunt; Hunt(n, d); return; }
            foreach (var m in Members) if (Notices(n, m, d, false)) { n.TargetX = m.X; n.TargetY = m.Y; n.InvestigateS = d.InvestigateS; PathFolk(n, m.Cell); break; }
            if (--n.InvestigateS <= 0) Return(n);
            return;
        }
        Hunt(n, d);
    }

    /// <summary>People and animals of the site (4.3): a guard who sees or hears an intruder comes for him; a sleeper wakes to noise; the peaceful (a hermit, miners) only answer a robbery.</summary>
    private void Person(Inhabitant n)
    {
        if (!n.Hostile) return;
        if (n.State is InhabitantState.Home or InhabitantState.Asleep)
        {
            foreach (var m in Members)
                if (Notices(n, m, null, n.State == InhabitantState.Asleep))
                {
                    n.State = InhabitantState.Hunt;
                    Note(CrawlEventKind.Noticed, Map.Inhabitants.IndexOf(n), 0, n.Name);
                    foreach (var o in Members) StopPath(o, true);
                    StopRest();
                    break;
                }
            return;
        }
        Hunt(n, null);
    }

    private void PathFolk(Inhabitant n, int cell)
    {
        var (tl, tx, ty) = Map.At(cell);
        var r = Pathfinder.FindPathBounded(new Cell3(n.X / 1000, n.Y / 1000, SiteMap.Z(n.Level)), new Cell3(tx, ty, SiteMap.Z(tl)), 6000);
        if (r == null) { n.Path = null; return; }
        n.Path = r.Cells; n.PathIdx = 1;
    }

    private void MoveFolk(Inhabitant n, int steps)
    {
        if (n.Path == null || n.PathIdx >= n.Path.Count) return;
        var d = MonsterDef(n);
        int speed = d != null ? (n.LungeS > 0 ? d.LungeMmps : d.WalkMmps) : n.State == InhabitantState.Hunt ? (n.Role is "wolves" or "bear" ? 4500 : 3000) : 1500;
        int budget = speed * steps / 20;
        while (budget > 0 && n.PathIdx < n.Path.Count)
        {
            var next = n.Path[n.PathIdx];
            int nl = SiteMap.LevelOf(next.Z);
            if (nl != n.Level) { n.Level = nl; n.X = next.X * 1000 + 500; n.Y = next.Y * 1000 + 500; n.PathIdx++; continue; }
            int tx = next.X * 1000 + 500, ty = next.Y * 1000 + 500, ex = tx - n.X, ey = ty - n.Y;
            int dist = Math.Max(Math.Abs(ex), Math.Abs(ey));
            int c = Map.Index(nl, next.X, next.Y);
            if (Map.Tiles[c] == SiteTile.Door) OpenDoor(c);
            if (Members.Any(m => !m.Dead && m.Cell == c)) break;                    // they stop at the intruder (the fight starts within 4 m)
            if (dist <= budget) { n.X = tx; n.Y = ty; budget -= dist; n.PathIdx++; }
            else { n.X += Math.Sign(ex) * Math.Min(Math.Abs(ex), budget); n.Y += Math.Sign(ey) * Math.Min(Math.Abs(ey), budget); budget = 0; }
        }
    }

    // ------------------------------------------------------------------ the fight

    private static SideKind SideOf(Inhabitant n) => n.Monster == "draugr" ? SideKind.Draugr : n.Role is "wolves" or "bear" or "vermin" || n.Monster.Length > 0 ? SideKind.Beasts
        : n.Role is "bandits" or "smugglers" or "scavengers" ? SideKind.Bandits : SideKind.Folk;

    /// <summary>The kit of an inhabitant: its role's fight kit row rolled for its id, its own skill and name; a monster on its plan.</summary>
    public FighterKit KitOf(Inhabitant n, int x, int y, int fx, int fy)
    {
        var def = FightSetup.KitDef(Fc, n.Kit, n.Kit);
        var k = FightSetup.Roll(Fc, def, Seed, n.Id, n.Name.Length > 0 ? n.Name : n.Role, 1, x, y, fx, fy);
        return k with { Skill = n.Skill, ShieldSkill = n.Skill, Person = n.Id, Name = n.Name.Length > 0 ? n.Name : n.Monster.Length > 0 ? n.Monster : n.Role };
    }

    /// <summary>The inhabitants close in: a fight of the party against every awake hostile within 15 m, at the hero's place (FightKind.SiteInhabitants).</summary>
    public void StartFight(Inhabitant trigger)
    {
        if (Fight != null) return;
        var h = Hero;
        int ox = h.Level == trigger.Level ? h.X : trigger.X, oy = h.Level == trigger.Level ? h.Y : trigger.Y, level = trigger.Level;
        var intruders = new List<FighterKit>(); var map = new List<int>();
        int heroIndex = -1;
        foreach (var m in Members)
        {
            if (m.Dead || m.Unconscious || m.Level != level || D2(m.X, m.Y, ox, oy) > 20_000L * 20_000) continue;
            if (m.Hero) heroIndex = intruders.Count;
            intruders.Add(m.Kit with { X = m.X - ox, Y = m.Y - oy, FaceX = trigger.X - m.X, FaceY = trigger.Y - m.Y, Person = m.Person, Name = m.Name });
            map.Add(Members.IndexOf(m));
        }
        if (intruders.Count == 0) return;
        var folk = new List<FighterKit>(); var side = SideOf(trigger);
        for (int i = 0; i < Map.Inhabitants.Count; i++)
        {
            var n = Map.Inhabitants[i];
            bool awake = n == trigger || n.State is InhabitantState.Hunt or InhabitantState.Investigate || n.State == InhabitantState.Home && n.Hostile && n.Monster.Length == 0;
            if (!awake || !n.Hostile || n.Level != level || n.State is InhabitantState.Dead or InhabitantState.Fled or InhabitantState.Away || D2(n.X, n.Y, trigger.X, trigger.Y) > 15_000L * 15_000) continue;
            folk.Add(KitOf(n, n.X - ox, n.Y - oy, ox - n.X, oy - n.Y));
            map.Add(-1 - i);
            n.State = InhabitantState.Fighting;
        }
        // every intruder first, then the site's side: the map lists them in that order
        _fightMap = [.. map.Take(intruders.Count), .. map.Skip(intruders.Count)];
        var setup = FightSetup.SiteInhabitants(intruders, folk, side, Seed, Map.SiteId, _fights++, GameSec, heroIndex);
        FightSetupUsed = setup;
        var f = setup.Start(Fc, Ledger);
        if (HeroAi && heroIndex >= 0) f.Fighters[heroIndex].Player = false;
        f.SiteLight = (x, y) => { int cx = (ox + x) / 1000, cy = (oy + y) / 1000; return Map.In(level, cx, cy) ? _frame.Total(cx, cy, SiteMap.Z(level)) : 0; };
        f.SitePlacePoints = fi => { int idx = fi.Id < _fightMap.Length ? _fightMap[fi.Id] : -1; return idx >= 0 ? AirOf(Members[idx]).AccuracyPoints : 0; };
        Fight = f; _fightOx = ox; _fightOy = oy; _fightLevel = level;
        foreach (var m in Members) { StopPath(m, true); m.MoveDir = -1; }
        StopRest();
        Loud();
        Note(CrawlEventKind.FightStarted, folk.Count, (int)side, trigger.Name);
    }

    /// <summary>A torch thrust (Q menu Burn, or the fight's own): a draugr within 2 m of a member with a lit flame catches fire at the head (4.9).</summary>
    public bool Burn(CrawlMember m)
    {
        if (Fight == null || !m.Light.Lit) { Note(CrawlEventKind.NothingHere); return false; }
        int me = Array.IndexOf(_fightMap, Members.IndexOf(m));
        if (me < 0) return false;
        var mf = Fight.Fighters[me];
        foreach (var f in Fight.Fighters)
        {
            if (f.Team == mf.Team || !f.Active || f.Plan.Id != "draugr" || Fight.DistMm(mf, f) > 2000) continue;
            int head = f.Plan.IndexOf("head");
            if (head >= 0 && Fight.Ignite(f.Id, head)) { Note(CrawlEventKind.Burning, f.Id); return true; }
        }
        return false;
    }

    private void FightStep()
    {
        var f = Fight!;
        if (OwnsFight) f.StepWithAi();
        for (int i = 0; i < f.Fighters.Count && i < _fightMap.Length; i++)
        {
            var fi = f.Fighters[i]; int idx = _fightMap[i];
            int x = _fightOx + fi.X, y = _fightOy + fi.Y;
            bool ok = Map.In(_fightLevel, x / 1000, y / 1000) && SiteTiles.Walkable(Map.Tiles[Map.Index(_fightLevel, x / 1000, y / 1000)]);
            if (idx >= 0) { var m = Members[idx]; if (ok) { m.X = x; m.Y = y; int c = CellOf(m); if (c != m.Cell) { m.Cell = c; m.Crumbs.Add(c); _lightDirty = true; } } }
            else { var n = Map.Inhabitants[-1 - idx]; if (ok) { n.X = x; n.Y = y; } }
        }
        if (f.IsOver) EndFight();
    }

    /// <summary>
    /// The auto-dress stop-gap below ground (as the road fight's, StreetView.AutoDress): the hero's bleeding wounds bound with the bandages he carries down, by himself when he is awake,
    /// else by the first member who can act, worst first, each in its real time (the dresser is busy that long). A crawl's rope is a climbing rope and is not cut up for a tourniquet.
    /// </summary>
    private void DressHeroAfterFight()
    {
        var h = Hero;
        if (h.Dead) return;
        var book = BookOf(h);
        if (Treatment.WorstBleeder(book, -1, GameSec) < 0) return;
        var by = book.Conscious && !h.Unconscious ? h : Members.FirstOrDefault(m => m != h && m.CanAct);
        if (by == null || by.Bandages <= 0) return;
        DressOutcome o;
        lock (Ledger.Sync) o = FieldDressing.Dress(book, h.Plan, by.Bandages, false, by.FirstAid, by.Person, GameSec);
        if (o.Steps.Count == 0) return;
        by.Bandages = Math.Max(0, by.Bandages - o.BandagesUsed);
        by.BusyS = Math.Max(by.BusyS, (int)((o.EndSec - GameSec + MedicineTables.TicksPerSimS - 1) / MedicineTables.TicksPerSimS)); by.BusyTarget = -1;
        foreach (var st in o.Steps) Note(CrawlEventKind.Treated, Members.IndexOf(by), (int)st.Kind);
    }

    /// <summary>The fight is over: the dead stay dead, the fled come back in three days (a beast to its lair, bleeding), a slain monster is a chronicle event with renown (4.9).</summary>
    public void EndFight()
    {
        var f = Fight;
        if (f == null) return;
        string heroName = Hero.Name;
        for (int i = 0; i < f.Fighters.Count && i < _fightMap.Length; i++)
        {
            var fi = f.Fighters[i]; int idx = _fightMap[i];
            if (idx >= 0)
            {
                var m = Members[idx];
                if (fi.Act == FAct.Dead) Die(m, "Violence");
                else if (fi.Act == FAct.Down) m.Unconscious = !m.Book.Conscious;
                continue;
            }
            var n = Map.Inhabitants[-1 - idx];
            n.LossPm = fi.Book.LossPm;
            if (fi.Act == FAct.Dead || fi.Act == FAct.Down && n.Monster.Length > 0)
            {
                n.State = InhabitantState.Dead;
                if (n.Monster.Length > 0 && MonsterDef(n) is { } d)
                {
                    Slain.Add(n.Name);
                    Note(CrawlEventKind.MonsterSlain, -1 - idx, d.Renown, n.Name);
                    Chronicle.Add(("site_monster_slain", new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["site"] = Map.SiteId.ToString(), ["name"] = n.Name, ["monster"] = n.Monster, ["attacker"] = heroName, ["renown"] = d.Renown.ToString(), ["text_key"] = d.SlainKey }));
                }
            }
            else if (fi.Act is FAct.Fled or FAct.Surrendered or FAct.Down)
            {
                n.State = n.Monster.Length > 0 ? InhabitantState.Return : InhabitantState.Fled;
                n.FledDay = Day;
                if (n.State == InhabitantState.Return) { n.Path = null; PathFolk(n, n.Home); }
            }
            else { n.State = InhabitantState.Return; n.InvestigateS = 30; n.Path = null; PathFolk(n, n.Home); }     // the fight broke off (one side ran): they go home and stay off for 30 sim-seconds
        }
        int winner = f.WinnerTeam;
        LastFightEnd = $"tick {f.Tick} " + string.Join(",", f.Fighters.Select(x => x.Name + ":" + x.Act + ":t" + x.Team));
        Fight = null;
        Note(CrawlEventKind.FightEnded, winner);
        if (AutoDress) DressHeroAfterFight();
        if (!Map.Inhabitants.Any(n => n.Hostile && n.State is not (InhabitantState.Dead or InhabitantState.Fled or InhabitantState.Away))) ClearedDay = Day;
        _lightDirty = _mapDirty = true;
    }

    /// <summary>The owner of the fight (the live world) ended it on its side: the crawl settles what it can see.</summary>
    public void FightDetached() { if (Fight != null && Fight.IsOver) EndFight(); }
}
