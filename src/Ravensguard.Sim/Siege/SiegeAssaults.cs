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
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Siege;

/// <summary>A storm the player's bubble has: the battle bubble with the assault laid over its fight, and the persons of the two sides.</summary>
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
    public const int BubbleRadiusM = 3_000, SideCap = 150;
    private long _hx = long.MinValue, _hy = long.MinValue;
    public SortedDictionary<int, LiveAssault> Live { get; } = [];

    /// <summary>Where the hero stands, continent metres (set by the runtime on the tick worker, from the position it was pumped with).</summary>
    public void NoteHero(long xM, long yM) { _hx = xM; _hy = yM; }

    public bool InBubble(int place)
    {
        if (_hx == long.MinValue) return false;
        var (cx, cy) = sw.Armies.CenterMm(place); long dx = cx / 1000 - _hx, dy = cy / 1000 - _hy;
        return dx * dx + dy * dy <= (long)BubbleRadiusM * BubbleRadiusM;
    }

    /// <summary>The port: opens the bubble over the siege's walls when the siege is in the hero's bubble.</summary>
    public bool Open(SiegeRow s)
    {
        foreach (int id in Live.Keys.ToList()) if (sw.Peek?.Sieges.TryGetValue(id, out var o) != true || o!.State != SiegeState.Assault) Live.Remove(id);
        if (!InBubble(s.Place) || Live.ContainsKey(s.Id)) return false;
        var walls = sw.WallsOf?.Invoke(s.Place); var ab = sw.W.Get<AbstractBattleService>();
        if (walls == null || ab is not { Ready: true }) return false;
        var aw = sw.Armies; var pt = aw.People; var fc = ab.Fight;
        var attackers = sw.MenOf(s).Take(SideCap).ToList(); var defenders = sw.Garrison(s.Place).Take(SideCap).ToList();
        if (attackers.Count == 0 || defenders.Count == 0) return false;
        var soldier = FightSetup.KitDef(fc, aw.Rules.FightKit, aw.Rules.FightKit); var guard = FightSetup.KitDef(fc, aw.Rules.GuardKit, aw.Rules.GuardKit);
        ulong seed = sw.W.Seed ^ DetRng.Mix((ulong)s.Id * 7919UL + (ulong)sw.LastHour);
        BattleUnit Unit(List<ulong> men, FightKitDef def, int team)
        {
            var u = new BattleUnit { Kind = FormationKind.Infantry, Shape = Formation.Line, Ranks = 4, Officers = [0] };
            foreach (ulong m in men) { int r = aw.RowOf(m); u.Soldiers.Add(FightSetup.Roll(fc, def, seed, m, r >= 0 ? pt.FullName(r) : "", team, 0, 0, FightMath.One, 0) with { Stance = "balanced" }); }
            return u;
        }
        long ox = (walls.X0 + walls.X1) * 500L, oy = (walls.Y0 + walls.Y1) * 500L;
        var op = new BattleOpening { Id = (ulong)s.Id * 4096 + (ulong)(sw.LastHour & 4095), Seed = seed, GameSec = sw.LastHour * 3600, CapTotal = 2 * SideCap, OriginMm = (ox, oy), CauseKind = CauseKind.Siege, CauseEntry = new EntityId(s.Entry), CauseNoteKey = "ch_note_sg_assault", Place = sw.PlaceOf(s.Place) };
        var host = aw.Book.Hosts.GetValueOrDefault(s.Host);
        var a = new BattleSideSpec { Team = 0, Realm = s.Realm, Commander = host == null ? 0UL : (ulong)host.Commander, HostId = (ulong)s.Host, Kind = SideKind.Army }; a.Units.Add(Unit(attackers, soldier, 0));
        var d = new BattleSideSpec { Team = 1, Realm = sw.Armies.Supply?.RealmOf(s.Place) ?? -1, Kind = SideKind.Army }; d.Units.Add(Unit(defenders, guard, 1));
        op.Sides.Add(a); op.Sides.Add(d);
        var bubble = BattleBubble.Open(fc, op);
        var layer = new AssaultLayer(bubble.W, walls.World, 0, 1, ox, oy) { Chronicle = sw.Writer, Cause = new EntityId(s.Entry), Place = sw.PlaceOf(s.Place), Inside = (walls.X0, walls.Y0, walls.X1, walls.Y1) };
        Live[s.Id] = new LiveAssault { Bubble = bubble, Layer = layer, Attackers = attackers, Defenders = defenders };
        return true;
    }

    /// <summary>The bubble has closed: its result ends the assault (the besiegers won when their team stood last; the men each side lost are the bubble's own count).</summary>
    public bool Close(ArmyEnv e, int siege)
    {
        if (!Live.TryGetValue(siege, out var la) || !la.Bubble.Closed || sw.Peek?.Sieges.TryGetValue(siege, out var s) != true) return false;
        var res = la.Bubble.Result; Live.Remove(siege);
        int lostA = res?.Sides.FirstOrDefault(x => x.Team == 0)?.Lost ?? 0, lostD = res?.Sides.FirstOrDefault(x => x.Team == 1)?.Lost ?? 0;
        sw.EndAssault(e, s!, res?.WinnerTeam == 0, lostA, lostD);
        return true;
    }

    /// <summary>Steps a live assault's bubble until it closes or <paramref name="maxTicks"/> pass (a headless run of what the game does at 20 Hz), then closes it.</summary>
    public bool RunToEnd(ArmyEnv e, int siege, int maxTicks)
    {
        if (!Live.TryGetValue(siege, out var la)) return false;
        for (int t = 0; t < maxTicks && !la.Bubble.Closed; t++) la.Bubble.Step();
        return Close(e, siege);
    }
}
