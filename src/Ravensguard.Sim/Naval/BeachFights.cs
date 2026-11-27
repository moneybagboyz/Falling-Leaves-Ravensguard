using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Naval;

/// <summary>The bell of a village under raid, as the hero's interrupt wants it: which raid, where the bell hangs (continent mm) and the game hour it rang.</summary>
public readonly record struct BellRing(int Raid, long XMm, long YMm, long Hour);

/// <summary>A beach fight the hero's bubble has opened: the battle bubble of the raiders ashore and the defenders, the ships as carriers on the sand, and the persons of the two sides.</summary>
public sealed class LiveBeach
{
    public required BattleBubble Bubble;
    public required CarrierLayer Carriers;
    public required int Raid;
    public required List<ulong> Raiders, Defenders;
}

/// <summary>
/// P8-LW-2: the bell and the beach bubble of a sea raid (live-war 3.2), following the live siege assault (<c>SiegeAssaults</c>): the game turns <see cref="GameDrivesLive"/> on, the runtime tells the hero's place at each barrier
/// (<see cref="NoteHero"/>), and the raid's hour opens a <see cref="BattleBubble"/> of the raiders ashore against the riders (and the hero) at the landing, with the raid's ships as carriers lying aground on the sand behind the raiders.
/// The game steps the bubble at 20 Hz and calls <see cref="CloseClosed"/>; a bubble nobody closes is fought abstractly after <see cref="LiveFallbackHours"/> (the abstract fight is the fallback; the live fight is the reference). While the
/// switch is off (a headless run) nothing here opens, rings or queues, and the abstract fight is fought as before.
/// </summary>
public sealed class BeachFights(ArmyWorld aw)
{
    /// <summary>The hero is within 1 km of the landing, or rides with the warband that comes (LW-7); a unit is 40 men, a bubble holds 300 live; the two front lines stand 60 m apart.</summary>
    public const int BubbleRadiusM = 1_000, UnitMen = 40, LiveCap = 300, GapMm = 60_000, LiveFallbackHours = 24;
    private long _hx = long.MinValue, _hy = long.MinValue;
    private readonly List<BellRing> _bells = [];
    public SortedDictionary<int, LiveBeach> Live { get; } = [];

    /// <summary>The game drives live beach fights and takes the bell (P8-LW-2); headless runs leave it off.</summary>
    public bool GameDrivesLive { get; set; }

    public void NoteHero(long xM, long yM) { _hx = xM; _hy = yM; }
    private bool HeroKnown => _hx != long.MinValue;

    private bool HeroNear(long xMm, long yMm, long radiusM)
    {
        if (!HeroKnown) return false;
        long dx = xMm / 1000 - _hx, dy = yMm / 1000 - _hy;
        return dx * dx + dy * dy <= radiusM * radiusM;
    }

    // ------------------------------------------------------------------ the bell

    /// <summary>The bell of the village rings for <paramref name="raid"/> (4.5, called by <c>Loot</c>): when the hero is within the bell's carry (3 km) it is queued for his interrupt. Returns whether it was heard.</summary>
    public bool Ring(SeaRaid raid, long hour)
    {
        if (!GameDrivesLive || Sea == null) return false;
        var (bx, by) = aw.CenterMm(raid.Settlement);
        if (!HeroKnown || !SeaRaids.BellHeard(Sea.Rules, bx, by, _hx * 1000, _hy * 1000)) return false;
        _bells.Add(new BellRing(raid.Id, bx, by, hour));
        return true;
    }

    private SeaRaidHost? Sea => aw.Sea;

    /// <summary>The bells rung since the last call, oldest first (the game takes them at a barrier).</summary>
    public List<BellRing> TakeBells() { var l = new List<BellRing>(_bells); _bells.Clear(); return l; }

    /// <summary>The bell as the hero's interrupt (<see cref="InterruptKind.BellRung"/>): raised once for each ring, at the bell's own place. Returns how many were raised.</summary>
    public static int RaiseBells(InterruptBus bus, IEnumerable<BellRing> rings, Func<int, string> where)
    {
        int n = 0;
        foreach (var r in rings) { bus.Raise(InterruptKind.BellRung, r.Hour * 3600, r.XMm, r.YMm, where(r.Raid)); n++; }
        return n;
    }

    // ------------------------------------------------------------------ the beach bubble

    /// <summary>
    /// The port, asked each hour for a raid whose riders have come (its fight hour) while raiders are ashore: opens the bubble when the hero is near the landing or rides with the warband. False when the switch is off, the hero is far, or the
    /// raid has no men on the sand or no riders.
    /// </summary>
    public bool Open(ArmyEnv e, SeaRaid raid)
    {
        var book = Sea?.Peek();
        foreach (int id in Live.Keys.ToList()) if (book?.Raids.TryGetValue(id, out var o) != true || o!.Over || o.LiveSince < 0) Live.Remove(id);
        if (!GameDrivesLive || Sea == null || raid.Fought || raid.LiveSince >= 0 || Live.ContainsKey(raid.Id) || raid.Defender < 0) return false;
        var ab = aw.W.Get<AbstractBattleService>(); var rides = Sea.Rides();
        if (ab is not { Ready: true } || rides == null || !aw.Book.Warbands.TryGetValue(raid.Defender, out var wb)) return false;
        bool heroRides = wb.Members.Exists(m => aw.IsHero(m));
        long bx = aw.OriginXMm + raid.BeachXmm, by = aw.OriginYMm + raid.BeachYmm;
        if (!heroRides && !HeroNear(bx, by, BubbleRadiusM)) return false;
        var pt = aw.People;
        var ashore = raid.Raiders.Where(m => aw.RowOf(m) is var r && r >= 0 && pt.Status[r] == PersonStatus.Alive).ToList();
        var riders = wb.Members.Where(m => aw.RowOf(m) is var r && r >= 0 && pt.Status[r] == PersonStatus.Alive).ToList();
        ulong hid = aw.HeroId();
        if (!heroRides && hid != 0 && aw.RowOf(hid) >= 0 && !riders.Contains(hid)) riders.Insert(0, hid);
        if (ashore.Count == 0 || riders.Count == 0) return false;
        var fc = ab.Fight;
        var raiderKit = FightSetup.KitDef(fc, "bandit_light", "bandit_light"); var soldier = FightSetup.KitDef(fc, aw.Rules.FightKit, aw.Rules.FightKit);
        ulong seed = aw.W.Seed ^ DetRng.Mix((ulong)raid.Id * 7919UL + (ulong)e.Hour);
        int heroTeam = riders.Exists(m => aw.IsHero(m)) ? 1 : -1;
        List<BattleUnit> Units(List<ulong> men, FightKitDef def, int team)
        {
            var res = new List<BattleUnit>();
            var ordered = men.Where(m => aw.IsHero(m)).Concat(men.Where(m => !aw.IsHero(m))).ToList();
            for (int i = 0; i < ordered.Count; i += UnitMen)
            {
                var u = new BattleUnit { Kind = FormationKind.Infantry, Shape = Formation.Line, Ranks = 3, Officers = [0] };
                foreach (ulong m in ordered.GetRange(i, Math.Min(UnitMen, ordered.Count - i))) { int r = aw.RowOf(m); u.Soldiers.Add(FightSetup.Roll(fc, def, seed, m, pt.FullName(r), team, 0, 0, FightMath.One, 0) with { Stance = "balanced" }); }
                res.Add(u);
            }
            return res;
        }
        var la = Assemble(fc, new LiveBeachSpec
        {
            Seed = seed, Id = (ulong)raid.Id * 4096 + (ulong)(e.Hour & 4095), GameSec = e.Hour * 3600, Raid = raid.Id, HeroTeam = heroTeam, OriginMm = (bx, by),
            Raiders = Units(ashore, raiderKit, 0), Defenders = Units(riders, soldier, 1), RaiderRealm = raid.Realm, DefendRealm = raid.Victim, Place = aw.PlaceId(raid.Settlement),
            Ships = [.. raid.Ships.Select(s => rides.Vehicles.Find(new EntityId(s))).Where(v => v != null).Select(v => (v!.Id, rides.Vehicles.DesignOf(v!).Id))], Cat = rides.Templates,
            Rides = rides, DefenderOwners = [new Ravensguard.Sim.Core.EntityId(wb.Lord == 0 ? hid : wb.Lord)],
        });
        Live[raid.Id] = la; raid.LiveSince = e.Hour; book!.Touch();
        return true;
    }

    /// <summary>The scene of the beach: the raiders (team 0) stand on the sand 30 m from the water's edge, the riders 30 m up the beach, and the raid's longships lie aground in a row behind the raiders, bows to the land, as carriers in the fight. A pure function of the spec.</summary>
    public static LiveBeach Assemble(FightContent fc, LiveBeachSpec sp)
    {
        var op = new BattleOpening { Id = sp.Id, Seed = sp.Seed, GameSec = sp.GameSec, CapTotal = LiveCap, GapMm = GapMm, OriginMm = sp.OriginMm, CauseKind = CauseKind.Raid, Place = sp.Place, CauseNoteKey = "ch_note_sh_raid_culture" };
        var a = new BattleSideSpec { Team = 0, Realm = sp.RaiderRealm, Kind = SideKind.Army }; var d = new BattleSideSpec { Team = 1, Realm = sp.DefendRealm, Kind = SideKind.Army };
        a.Units.AddRange(sp.Raiders); d.Units.AddRange(sp.Defenders);
        a.VehicleOwners.AddRange(sp.RaiderOwners); d.VehicleOwners.AddRange(sp.DefenderOwners); op.Rides = sp.Rides;
        if (sp.HeroTeam == 1 && d.Units.Count > 0) { d.HeroUnit = 0; d.HeroIndex = 0; }
        op.Sides.Add(a); op.Sides.Add(d);
        var bubble = BattleBubble.Open(fc, op);
        var layer = new CarrierLayer(bubble.W, new Swimmers(sp.Seed));
        int k = 0;
        foreach (var (id, design) in sp.Ships)
        {
            var f = ShipFrame.Of(id, sp.Cat.Get(design.ToString()), sp.Cat, -GapMm / 2 - 40_000, (k - (sp.Ships.Count - 1) / 2) * 12_000 + (k & 1) * 0, 0);
            layer.Add(f); k++;
        }
        return new LiveBeach { Bubble = bubble, Carriers = layer, Raid = sp.Raid, Raiders = [.. sp.Raiders.SelectMany(u => u.Soldiers.Select(s => s.Person))], Defenders = [.. sp.Defenders.SelectMany(u => u.Soldiers.Select(s => s.Person))] };
    }

    /// <summary>
    /// The bubble has closed: its result ends the fight on the sand. The fallen of both sides are killed as persons (the raid is the cause, a root of the chronicle); the raiders who died, yielded or fled are off the beach; the raid then goes on
    /// as the abstract one does (the ships push off with the men left, or lie on the sand when none is). The riders' fight is over: <see cref="SeaRaid.Fought"/>.
    /// </summary>
    public bool Close(ArmyEnv e, int raidId)
    {
        var book = Sea?.Peek();
        if (!Live.TryGetValue(raidId, out var lb) || !lb.Bubble.Closed || book?.Raids.TryGetValue(raidId, out var raid) != true) return false;
        var res = lb.Bubble.Result; Live.Remove(raidId);
        var pt = aw.People; var ob = e.Ob ?? new RegionOutbox(aw.W.Ids);
        int dead = 0;
        if (res != null)
        {
            var cause = new Cause(CauseKind.Raid, null, "", "ch_note_sh_raid_culture");
            foreach (var x in res.Soldiers.Where(x => x.Person != 0 && x.Act == FAct.Dead).OrderBy(x => x.Person))
            {
                int r = aw.RowOf(x.Person); if (r < 0 || pt.Status[r] != PersonStatus.Alive || aw.IsHero(x.Person)) continue;
                Demography.Kill(aw.W, r, DeathCause.Violence, e.Time, ob, chain: cause);
                if (x.Team == 0) dead++;
            }
            var yielded = res.Soldiers.Where(x => x.Team == 0 && x.Person != 0 && x.Act == FAct.Surrendered).Select(x => x.Person).ToList();
            if (yielded.Count > 0 && aw.Book.Warbands.TryGetValue(raid!.Defender, out var holder))                        // P8-LW-4: the raiders who yielded are the riders' prisoners
                aw.TakePrisoners(e, (ulong)raidId * 4096 + (ulong)(e.Hour & 4095), holder.Lord, raid.Settlement, holder.Members.Count, yielded);
            var off = res.Soldiers.Where(x => x.Team == 0 && x.Person != 0 && x.Act is FAct.Dead or FAct.Surrendered or FAct.Fled).Select(x => x.Person).ToHashSet();
            raid!.Raiders.RemoveAll(off.Contains);
        }
        if (e.Ob == null) ob.ApplyTo(aw.W.Chronicle, null, new Dictionary<string, long>(StringComparer.Ordinal));
        raid!.Dead += dead; raid.Fought = true; raid.LiveSince = -1; book!.Touch();
        return true;
    }

    /// <summary>The game's barrier job: every beach fight whose bubble has closed ends now, so the hour is the same on every replay. False when none had.</summary>
    public bool CloseClosed(ArmyEnv e)
    {
        bool any = false;
        foreach (int id in Live.Keys.ToList()) if (Live[id].Bubble.Closed) any |= Close(e, id);
        return any;
    }

    /// <summary>Steps a beach fight until it closes or <paramref name="maxTicks"/> pass (a headless run of what the game does at 20 Hz), then closes it.</summary>
    public bool RunToEnd(ArmyEnv e, int raidId, int maxTicks)
    {
        if (!Live.TryGetValue(raidId, out var lb)) return false;
        for (int t = 0; t < maxTicks && !lb.Bubble.Closed; t++) lb.Bubble.Step();
        return Close(e, raidId);
    }
}

/// <summary>What a beach fight is made of (<see cref="BeachFights.Assemble"/>).</summary>
public sealed class LiveBeachSpec
{
    public ulong Seed, Id; public long GameSec; public int Raid, HeroTeam = -1, RaiderRealm, DefendRealm; public (long X, long Y) OriginMm; public EntityId Place;
    public required List<BattleUnit> Raiders, Defenders;
    public required List<(EntityId Id, string Design)> Ships;
    public required Ravensguard.Sim.Carts.VehicleCatalog Cat;
    /// <summary>P8-LW-3: the ride book and the owners (a warband's lord) whose war wagons and chariots come onto the sand with each side.</summary>
    public Ravensguard.Sim.Animals.RideBook? Rides;
    public List<Ravensguard.Sim.Core.EntityId> RaiderOwners = [], DefenderOwners = [];
}
