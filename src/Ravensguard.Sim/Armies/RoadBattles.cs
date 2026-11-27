using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Armies;

/// <summary>A field battle between two hosts the hero's bubble has opened: the bubble, the two hosts, the persons of each side and the hour it opened.</summary>
public sealed class LiveRoad
{
    public required BattleBubble Bubble;
    public required int HostA, HostB, Place;
    public required long Since;
    public required List<ulong> SideA, SideB;
}

/// <summary>
/// P8-LW-4 (the added item the overview's OV-4 needs): the live road battle. A field battle between two hosts (a host of the hero's, his levy or a warband that joined it, or one he rides with, meeting a hostile host on the road) opens a real
/// <see cref="BattleBubble"/> when the hero is among the men and within <see cref="BubbleRadiusM"/> of the meeting, following the live storm and the beach fight: units of <see cref="UnitMen"/>, at most <see cref="LiveCap"/> live and the rest in waves,
/// formations the hero's F1 to F4 and the order wheel command, and the host's war wagons and chariots through <c>BattleVehicles.Attach</c>. The hosts stand (the campaign waits) while the bubble is open; the game steps it at 20 Hz and calls
/// <see cref="CloseClosed"/> at a barrier, which kills the fallen as persons, folds both hosts back, takes the yielded into the winner's pen and gives the war its events, exactly as the abstract fight does. A bubble nobody closes is fought abstractly after
/// <see cref="LiveFallbackHours"/> (the abstract fight is the fallback; the live fight is the reference). With the switch off (a headless run) nothing opens.
/// </summary>
public sealed class RoadBattles(ArmyWorld aw)
{
    public const int BubbleRadiusM = 2_000, UnitMen = 40, LiveCap = 300, GapMm = 80_000, WaveUnderPm = 600, LiveFallbackHours = 24;
    private long _hx = long.MinValue, _hy = long.MinValue;
    public SortedDictionary<long, LiveRoad> Live { get; } = [];

    /// <summary>The game drives live road battles (P8-LW-4); headless runs leave it off.</summary>
    public bool GameDrivesLive { get; set; }

    public void NoteHero(long xM, long yM) { _hx = xM; _hy = yM; }

    private static long Key(ArmyHost a, ArmyHost b) => ((long)Math.Min(a.Id, b.Id) << 24) | (uint)Math.Max(a.Id, b.Id);

    public bool Pending(ArmyHost a, ArmyHost b) => Live.ContainsKey(Key(a, b));
    public bool Expired(ArmyEnv e, ArmyHost a, ArmyHost b) => Live.TryGetValue(Key(a, b), out var l) && e.Hour - l.Since >= LiveFallbackHours;
    public void Abort(ArmyHost a, ArmyHost b) => Live.Remove(Key(a, b));

    /// <summary>
    /// The campaign, asked when two engaged hosts are due to fight: opens the bubble when the hero is among the men of one of them and within the bubble's range of the meeting. False (the abstract fight goes ahead) when the switch is off, the hero is not
    /// there, or a side has no men on its feet.
    /// </summary>
    public bool Open(ArmyEnv e, ArmyHost a, ArmyHost b, int place)
    {
        if (!GameDrivesLive || _hx == long.MinValue || Live.ContainsKey(Key(a, b))) return false;
        var ab = aw.W.Get<AbstractBattleService>(); var hs = aw.Supply;
        if (ab is not { Ready: true } || hs == null || a.Supply == 0 || b.Supply == 0) return false;
        var menA = aw.Fit(a).Select(m => m.Id).ToList(); var menB = aw.Fit(b).Select(m => m.Id).ToList();
        if (menA.Count == 0 || menB.Count == 0) return false;
        bool heroA = aw.IsHero(a.Commander) || menA.Exists(m => aw.IsHero(m)), heroB = aw.IsHero(b.Commander) || menB.Exists(m => aw.IsHero(m));
        if (heroA == heroB) return false;                                                           // the hero is on one side (a hero on neither, or on both by a bug, makes no live fight)
        long sec = e.Hour * 3600; var (ax, ay) = aw.PositionOfHost(a, sec); var (bx, by) = aw.PositionOfHost(b, sec);
        long mx = (ax + bx) / 2, my = (ay + by) / 2;                                                // continent millimetres (the hosts' own frame)
        long cx = (heroA ? ax : bx) / 1000, cy = (heroA ? ay : by) / 1000;                          // the hero's host, continent metres
        long dx = cx - _hx, dy = cy - _hy;
        if (dx * dx + dy * dy > (long)BubbleRadiusM * BubbleRadiusM) return false;
        var pt = aw.People; var fc = ab.Fight; ulong seed = aw.W.Seed ^ DetRng.Mix((ulong)a.Id * 7919UL + (ulong)b.Id * 104729UL + (ulong)e.Hour);
        var soldier = FightSetup.KitDef(fc, aw.Rules.FightKit, aw.Rules.FightKit); var guard = FightSetup.KitDef(fc, aw.Rules.GuardKit, aw.Rules.GuardKit);
        var kind = aw.Fit(a).Concat(aw.Fit(b)).ToDictionary(m => m.Id, m => m.Kind);
        List<BattleUnit> Units(List<ulong> men, int team)
        {
            var res = new List<BattleUnit>(); var ordered = men.Where(aw.IsHero).Concat(men.Where(m => !aw.IsHero(m))).ToList();
            for (int i = 0; i < ordered.Count; i += UnitMen)
            {
                var u = new BattleUnit { Kind = FormationKind.Infantry, Shape = Formation.Line, Ranks = 3, Officers = [0] };
                foreach (ulong m in ordered.GetRange(i, Math.Min(UnitMen, ordered.Count - i)))
                {
                    int r = aw.RowOf(m);
                    u.Soldiers.Add(FightSetup.Roll(fc, kind.GetValueOrDefault(m) == 0 ? soldier : guard, seed, m, r >= 0 ? pt.FullName(r) : "", team, 0, 0, FightMath.One, 0) with { Stance = "balanced" });
                }
                res.Add(u);
            }
            return res;
        }
        var op = new BattleOpening
        {
            Id = (ulong)Key(a, b) * 4096 + (ulong)(e.Hour & 4095), Seed = seed, GameSec = sec, CapTotal = LiveCap, GapMm = GapMm, WaveUnderPm = WaveUnderPm,
            OriginMm = (mx, my), CauseKind = CauseKind.War, Place = aw.PlaceId(place), CauseNoteKey = "ch_note_am_field",
            Rides = aw.Sea?.Rides(),
        };
        var sa = new BattleSideSpec { Team = 0, Realm = a.Realm, Commander = (ulong)a.Commander, HostId = (ulong)a.Id, Kind = SideKind.Army };
        var sb = new BattleSideSpec { Team = 1, Realm = b.Realm, Commander = (ulong)b.Commander, HostId = (ulong)b.Id, Kind = SideKind.Army };
        sa.Units.AddRange(Units(menA, 0)); sb.Units.AddRange(Units(menB, 1));
        sa.VehicleOwners.AddRange([Ravensguard.Sim.WarSupply.HostIds.Baggage(a.Supply), new EntityId(a.Commander)]);
        sb.VehicleOwners.AddRange([Ravensguard.Sim.WarSupply.HostIds.Baggage(b.Supply), new EntityId(b.Commander)]);
        var heroSide = heroA ? sa : sb; heroSide.HeroUnit = 0; heroSide.HeroIndex = 0;
        op.Sides.Add(sa); op.Sides.Add(sb);
        var bubble = BattleBubble.Open(fc, op);
        Live[Key(a, b)] = new LiveRoad { Bubble = bubble, HostA = a.Id, HostB = b.Id, Place = place, Since = e.Hour, SideA = menA, SideB = menB };
        return true;
    }

    /// <summary>
    /// The bubble has closed (called at a barrier): the fallen of both hosts are killed as persons (the battle's entry, a root caused by the war, is their cause), the yielded are the captured of the loser, and the rest is as after the abstract fight
    /// (<c>FinishField</c>): both hosts fold back, the winner's pen takes the yielded, the war's events are written. Returns false when no closed bubble is waiting under this key.
    /// </summary>
    public bool Close(ArmyEnv e, long key)
    {
        if (!Live.TryGetValue(key, out var lr) || !lr.Bubble.Closed) return false;
        var res = lr.Bubble.Result; Live.Remove(key);
        if (!aw.Book.Hosts.TryGetValue(lr.HostA, out var a) || !aw.Book.Hosts.TryGetValue(lr.HostB, out var b)) return true;
        var pt = aw.People; var ob = e.Ob ?? new RegionOutbox(aw.W.Ids);
        var cmds = new List<BattleCommand>(); int winner = a.Id; ulong report = 0;
        if (res != null)
        {
            var sa = res.Sides.Count > 0 ? res.Sides[0] : null; var sb = res.Sides.Count > 1 ? res.Sides[1] : null;
            int sideA = res.WinnerTeam == 0 ? 1 : res.WinnerTeam == 1 ? 0 : (sa == null || sb == null ? 1 : sa.Standing + sa.Reserve >= sb.Standing + sb.Reserve ? 1 : 0);
            winner = sideA == 1 ? a.Id : b.Id;
            var entry = e.Chronicler.Chron(e.Time, "battle_fought", "ch_bt_battle",
                [ChronParam.Place("place", aw.PlaceId(lr.Place)), ChronParam.Person("commander_a", new EntityId(a.Commander)), ChronParam.Person("commander_b", new EntityId(b.Commander)),
                 ChronParam.Count("men_a", sa?.Men ?? 0), ChronParam.Count("men_b", sb?.Men ?? 0), ChronParam.Count("lost_a", sa?.Lost ?? 0), ChronParam.Count("lost_b", sb?.Lost ?? 0),
                 ChronParam.Count("waves_a", sa?.Waves ?? 0), ChronParam.Count("waves_b", sb?.Waves ?? 0), ChronParam.TextKey("outcome", res.OutcomeKey)],
                [new EntityId(a.Commander), new EntityId(b.Commander)], [new Cause(CauseKind.War, null, "", "ch_note_am_field")]);
            report = entry.Value;
            var cause = new Cause(CauseKind.Battle, entry, "", "ch_note_bt_field");
            foreach (var x in res.Soldiers.Where(x => x.Person != 0).OrderBy(x => x.Person))
            {
                int r = aw.RowOf(x.Person); if (r < 0) continue;
                switch (x.Act)
                {
                    case FAct.Dead:
                        if (pt.Status[r] == PersonStatus.Alive && !aw.IsHero(x.Person)) Demography.Kill(aw.W, r, DeathCause.Violence, e.Time, ob, chain: cause);
                        break;
                    case FAct.Surrendered: cmds.Add(new BattleCommand(x.Person, BattleCommandKind.Status, SoldierStatus.Surrendered)); break;
                    case FAct.Fled: cmds.Add(new BattleCommand(x.Person, BattleCommandKind.Status, SoldierStatus.Fled)); break;
                    case FAct.Down: cmds.Add(new BattleCommand(x.Person, BattleCommandKind.Status, SoldierStatus.Down)); break;
                }
            }
        }
        else winner = a.Id;
        if (e.Ob == null) ob.ApplyTo(aw.W.Chronicle, null, new Dictionary<string, long>(StringComparer.Ordinal));
        aw.FinishField(e, a, b, lr.Place, winner, cmds, report);
        return true;
    }

    /// <summary>The game's barrier job: every road battle whose bubble has closed ends now, so the hour is the same on every replay. False when none had.</summary>
    public bool CloseClosed(ArmyEnv e)
    {
        bool any = false;
        foreach (long k in Live.Keys.ToList()) if (Live[k].Bubble.Closed) any |= Close(e, k);
        return any;
    }

    /// <summary>Steps a road battle until it closes or <paramref name="maxTicks"/> pass (a headless run of what the game does at 20 Hz), then closes it.</summary>
    public bool RunToEnd(ArmyEnv e, ArmyHost a, ArmyHost b, int maxTicks)
    {
        if (!Live.TryGetValue(Key(a, b), out var lr)) return false;
        for (int t = 0; t < maxTicks && !lr.Bubble.Closed; t++) lr.Bubble.Step();
        return Close(e, Key(a, b));
    }
}
