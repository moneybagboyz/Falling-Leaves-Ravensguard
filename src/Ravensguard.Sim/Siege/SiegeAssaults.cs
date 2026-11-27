using Ravensguard.Content;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Siege;

/// <summary>A storm the player's bubble has: the battle bubble with the assault laid over its fight, and the persons of the two sides.</summary>
public sealed class LiveStormSpec
{
    public ulong Seed, Id, AttackCommander, HostId; public long GameSec; public int Siege, HeroTeam = -1, AttackRealm, DefendRealm;
    public required SiegeWalls Walls; public required List<BattleUnit> Attackers, Defenders;
    public SiegeChronicle? Chronicle; public EntityId Cause, Place; public string CauseNoteKey = "ch_note_sg_assault"; public SiegeKit? Engines; public int LadderBudget = -1;
    /// <summary>P8-LW-3: the ride book and the host's owners (its baggage and its commander) whose chariots come to the storm (an assault takes no wagon fort).</summary>
    public Ravensguard.Sim.Animals.RideBook? Rides; public List<EntityId> AttackOwners = [];
}

public sealed class LiveAssault
{
    public required BattleBubble Bubble;
    public required AssaultLayer Layer;
    public required List<ulong> Attackers, Defenders;
}

/// <summary>
/// P7-SG wiring of the live assault (SiegeWorld.OpenAssault): when the siege lies inside the hero's bubble (the hero within <see cref="BubbleRadiusM"/> of the besieged place, told by the runtime at the barrier before each hour so the answer is the same on every
/// run) the storm is a <see cref="BattleBubble"/> of the besiegers and the garrison with an <see cref="AssaultLayer"/> over its fight, on the walls the siege stands at; the siege then waits in the Assault state. The bubble is the game's to step (20 Hz) and to
/// close: <see cref="Close"/> reads its result and ends the assault (<see cref="SiegeWorld.EndAssault"/>); a bubble nobody closes is fought abstractly after <see cref="SiegeConfig.LiveFallbackHours"/>. Outside the bubble the answer is false and the abstract storm is fought.
/// </summary>
public sealed class SiegeAssaults(SiegeWorld sw)
{
    /// <summary>The hero is inside the place, in the host or within 1 km (LW-1); a bubble holds 300 at once, a unit is 40 men, a wave comes when a side's live men fall under 60% of its share; the front lines stand 40 m apart across the curtain.</summary>
    public const int BubbleRadiusM = 1_000, UnitMen = 40, LiveCap = 300, WaveUnderPm = 600, GapMm = 40_000;
    private long _hx = long.MinValue, _hy = long.MinValue;
    public SortedDictionary<int, LiveAssault> Live { get; } = [];

    /// <summary>
    /// The game drives live assaults (P8-LW-1): the street view steps each <see cref="Live"/> bubble at 20 Hz and calls <see cref="Close"/> when it ends. The game turns this on when it takes up a world; headless runs leave it off and fight every storm abstractly.
    /// While off, <see cref="Open"/> declines (P7R-4).
    /// </summary>
    public bool GameDrivesLiveAssaults { get; set; }

    /// <summary>Where the hero stands, continent metres (set by the runtime on the tick worker, from the position it was pumped with).</summary>
    public void NoteHero(long xM, long yM) { _hx = xM; _hy = yM; }

    public bool InBubble(int place)
    {
        if (_hx == long.MinValue) return false;
        var (cx, cy) = sw.Armies.CenterMm(place); long dx = cx / 1000 - _hx, dy = cy / 1000 - _hy;
        return dx * dx + dy * dy <= (long)BubbleRadiusM * BubbleRadiusM;
    }

    /// <summary>Men in units of <see cref="UnitMen"/> (the hero, when he is among them, first so his unit forms first): a side's reserve is these, and each wave is some of them.</summary>
    private static List<List<ulong>> Split(List<ulong> men, Func<ulong, bool> hero)
    {
        var ordered = men.Where(hero).Concat(men.Where(m => !hero(m))).ToList(); var res = new List<List<ulong>>();
        for (int i = 0; i < ordered.Count; i += UnitMen) res.Add(ordered.GetRange(i, Math.Min(UnitMen, ordered.Count - i)));
        return res;
    }

    /// <summary>The port: opens the bubble over the siege's walls when the siege is in the hero's bubble.</summary>
    public bool Open(SiegeRow s)
    {
        foreach (int id in Live.Keys.ToList()) if (sw.Peek?.Sieges.TryGetValue(id, out var o) != true || o!.State != SiegeState.Assault) Live.Remove(id);
        if (!GameDrivesLiveAssaults || !InBubble(s.Place) || Live.ContainsKey(s.Id)) return false;
        var walls = sw.WallsOf?.Invoke(s.Place); var ab = sw.W.Get<AbstractBattleService>();
        if (walls == null || ab is not { Ready: true }) return false;
        var aw = sw.Armies; var pt = aw.People; var fc = ab.Fight;
        var attackers = sw.MenOf(s).ToList(); var defenders = sw.Garrison(s.Place).ToList();
        if (attackers.Count == 0 || defenders.Count == 0) return false;
        var soldier = FightSetup.KitDef(fc, aw.Rules.FightKit, aw.Rules.FightKit); var guard = FightSetup.KitDef(fc, aw.Rules.GuardKit, aw.Rules.GuardKit);
        ulong seed = sw.W.Seed ^ DetRng.Mix((ulong)s.Id * 7919UL + (ulong)sw.LastHour);
        int heroTeam = attackers.Exists(sw.IsHero) ? 0 : defenders.Exists(sw.IsHero) ? 1 : -1;
        List<BattleUnit> Units(List<ulong> men, FightKitDef def, int team)
        {
            var res = new List<BattleUnit>();
            foreach (var chunk in Split(men, sw.IsHero))
            {
                var u = new BattleUnit { Kind = FormationKind.Infantry, Shape = Formation.Line, Ranks = 4, Officers = [0] };
                foreach (ulong m in chunk) { int r = aw.RowOf(m); u.Soldiers.Add(FightSetup.Roll(fc, def, seed, m, r >= 0 ? pt.FullName(r) : "", team, 0, 0, FightMath.One, 0) with { Stance = "balanced" }); }
                res.Add(u);
            }
            return res;
        }
        var host = aw.Book.Hosts.GetValueOrDefault(s.Host);
        var spec = new LiveStormSpec
        {
            Seed = seed, Id = (ulong)s.Id * 4096 + (ulong)(sw.LastHour & 4095), GameSec = sw.LastHour * 3600, Siege = s.Id, Walls = walls,
            Attackers = Units(attackers, soldier, 0), Defenders = Units(defenders, guard, 1), HeroTeam = heroTeam,
            AttackRealm = s.Realm, DefendRealm = sw.Armies.Supply?.RealmOf(s.Place) ?? -1, AttackCommander = host == null ? 0UL : (ulong)host.Commander, HostId = (ulong)s.Host,
            Chronicle = sw.Writer, Cause = new EntityId(s.Entry), Place = sw.PlaceOf(s.Place), CauseNoteKey = "ch_note_sg_assault",
            Engines = sw.KitOf?.Invoke(s) is { } kit && kit.Book.Engines.Values.Any(e => e.Kind is EngineKind.Tower or EngineKind.Ram) ? kit : null,
            Rides = sw.KitOf?.Invoke(s)?.Yard.Rides, AttackOwners = host == null ? [] : [Ravensguard.Sim.WarSupply.HostIds.Baggage(host.Supply), new EntityId(host.Commander)],
        };
        spec.LadderBudget = Provision(s);
        Live[s.Id] = Assemble(fc, spec);
        return true;
    }

    /// <summary>The host's ladders (3.1): six ladders of timber are wanted in the camp's store; what it lacks of logs is felled within 2 km of the place; each ladder built uses up 40 kg (consumed). Returns the ladders there are timber for, -1 when the siege has no camp.</summary>
    private int Provision(SiegeRow s)
    {
        if (sw.KitOf?.Invoke(s) is not { } kit) return -1;
        var goods = kit.Yard.Econ.Goods; ushort logs = goods.IdOf("logs"); long unit = Math.Max(1, goods.MassG(logs, 1));
        const int Wanted = 6;
        long have = kit.Camp.Qty(logs) * unit, want = Wanted * SiegeTimber.LadderG;
        if (have < want) sw.FellPort?.Invoke(s, want - have);
        int n = (int)Math.Min(Wanted, kit.Camp.Qty(logs) * unit / SiegeTimber.LadderG);
        kit.Camp.DestroyUpTo(logs, n * SiegeTimber.LadderG / unit, DestroyReason.Consumed);
        return n;
    }

    /// <summary>
    /// The storm's scene (3.1), the starting geometry: the fight's frame is laid on the curtain the host comes to (west or east of the place, as its engines lie), the attackers stand 20 m outside it with their ladders on their way, the defenders 20 m inside with a third
    /// of their first men up on the walk at each threatened stretch, and the rest of each side wait as waves at the host's camp and in the town. A pure function of the spec (a test builds it on a rig of walls).
    /// </summary>
    public static LiveAssault Assemble(FightContent fc, LiveStormSpec sp)
    {
        var walls = sp.Walls; int midY = (walls.Y0 + walls.Y1) / 2;
        bool west = walls.LayXMm <= (walls.X0 + walls.X1) * 500L;
        int wallX = west ? walls.X0 : walls.X1;
        for (int k = 0; k <= 16; k++) { int x = west ? walls.X0 - 6 + k : walls.X1 + 6 - k; if (!walls.World.IsOpen(x, midY, 0)) { wallX = x; break; } }
        long ox = wallX * 1000L + 500, oy = midY * 1000L + 500;
        int na = sp.Attackers.Sum(u => u.Count), nd = sp.Defenders.Sum(u => u.Count), cap = LiveCap;
        for (; cap > 200; cap--)         // the bubble gives a side at least a quarter of the cap, so a small garrison would push the two shares over 300: the cap comes down until the live men of both sides are 300 at most
        {
            int la = Math.Min(na, Math.Max(cap / 4, (int)((long)cap * na / Math.Max(1, na + nd)))), ld = Math.Min(nd, Math.Max(cap / 4, (int)((long)cap * nd / Math.Max(1, na + nd))));
            if (la + ld <= LiveCap) break;
        }
        var op = new BattleOpening { Id = sp.Id, Seed = sp.Seed, GameSec = sp.GameSec, CapTotal = cap, GapMm = GapMm, WaveUnderPm = WaveUnderPm, OriginMm = (ox, oy), CauseKind = CauseKind.Siege, CauseEntry = sp.Cause, CauseNoteKey = sp.CauseNoteKey, Place = sp.Place };
        var a = new BattleSideSpec { Team = 0, Realm = sp.AttackRealm, Commander = sp.AttackCommander, HostId = sp.HostId, Kind = SideKind.Army };
        var d = new BattleSideSpec { Team = 1, Realm = sp.DefendRealm, Kind = SideKind.Army };
        a.Units.AddRange(sp.Attackers); d.Units.AddRange(sp.Defenders);
        a.VehicleOwners.AddRange(sp.AttackOwners); op.Rides = sp.Rides; op.NoFort = true;
        d.Entry = (west ? 45_000 : -45_000, 0);         // the garrison's reinforcements come out of the town's streets, 45 m inside the curtain (the default entry is 150 m off, beyond the far wall)
        foreach (var (spec, team) in new[] { (a, 0), (d, 1) })
            if (sp.HeroTeam == team && spec.Units.Count > 0) { spec.HeroUnit = 0; spec.HeroIndex = 0; }
        if (west) { op.Sides.Add(a); op.Sides.Add(d); } else { op.Sides.Add(d); op.Sides.Add(a); }
        var bubble = BattleBubble.Open(fc, op);
        var layer = new AssaultLayer(bubble.W, walls.World, 0, 1, ox, oy)
        {
            Chronicle = sp.Chronicle, Cause = sp.Cause, Place = sp.Place, Inside = (walls.X0, walls.Y0, walls.X1, walls.Y1), Live = true, FromWest = west, SiegeId = sp.Siege, LadderBudget = sp.LadderBudget,
            AiTeams = sp.HeroTeam < 0 ? 3 : 3 & ~(1 << sp.HeroTeam),
        };
        if (sp.Engines is { } kit)
        {
            layer.Engines = kit.Book; layer.EngineW = kit.World;
            foreach (var e in kit.Book.Engines.Values) if (e.Kind == EngineKind.Tower) layer.AddTower(e.Vehicle);
        }
        layer.Arrange([.. bubble.W.Fighters.Where(f => f.Team == 1).Select(f => f.Id)]);
        return new LiveAssault { Bubble = bubble, Layer = layer, Attackers = [.. sp.Attackers.SelectMany(u => u.Soldiers.Select(k => k.Person))], Defenders = [.. sp.Defenders.SelectMany(u => u.Soldiers.Select(k => k.Person))] };
    }

    /// <summary>Takes up a live assault restored from a save (the bubble over its fight, the layer over the walls): the siege is waiting in the Assault state for it.</summary>
    public void Adopt(int siege, BattleBubble bubble, AssaultLayer layer) => Live[siege] = new LiveAssault { Bubble = bubble, Layer = layer, Attackers = [], Defenders = [] };

    /// <summary>The bubble has closed: its result ends the assault. The dead of the fight are killed as persons (the siege is the cause; people exact), and the attackers won when their team stood last.</summary>
    public bool Close(ArmyEnv e, int siege)
    {
        if (!Live.TryGetValue(siege, out var la) || !la.Bubble.Closed || sw.Peek?.Sieges.TryGetValue(siege, out var s) != true) return false;
        var res = la.Bubble.Result; Live.Remove(siege);
        int lostA = 0, lostD = 0;
        if (res != null)
        {
            var dead = res.Soldiers.Where(x => x.Act == FAct.Dead && x.Person != 0).OrderBy(x => x.Person).ToList();
            lostA = sw.SlayFallen(e, s!, dead.Where(x => x.Team == 0).Select(x => x.Person));
            lostD = sw.SlayFallen(e, s!, dead.Where(x => x.Team == 1).Select(x => x.Person));
        }
        bool won = res?.WinnerTeam == 0;
        if (won && res != null && sw.Armies.Book.Hosts.TryGetValue(s!.Host, out var holder))                                                                // P8-LW-4: the garrison who yielded are the host's prisoners
        {
            var yielded = res.Soldiers.Where(x => x.Team == 1 && x.Person != 0 && x.Act == FAct.Surrendered).Select(x => x.Person).ToList();
            if (yielded.Count > 0) sw.Armies.TakePrisoners(e, res.Id, (ulong)holder.Commander, s.Place, sw.MenOf(s).Count, yielded);
        }
        sw.EndAssault(e, s!, won, lostA, lostD);
        return true;
    }

    /// <summary>The game's barrier job (P8-LW-1): every live assault whose bubble has closed ends its siege now, at the barrier, so the hour it ends in is the same on every replay. False when none had closed.</summary>
    public bool CloseClosed()
    {
        bool any = false;
        foreach (int id in Live.Keys.ToList()) if (Live[id].Bubble.Closed) any |= Close(new ArmyEnv(sw.W, null, sw.LastHour), id);
        return any;
    }

    /// <summary>Steps a live assault's bubble until it closes or <paramref name="maxTicks"/> pass (a headless run of what the game does at 20 Hz), then closes it.</summary>
    public bool RunToEnd(ArmyEnv e, int siege, int maxTicks)
    {
        if (!Live.TryGetValue(siege, out var la)) return false;
        for (int t = 0; t < maxTicks && !la.Bubble.Closed; t++) la.Bubble.Step();
        return Close(e, siege);
    }
}
