using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>What a block is made of (abstract war 3): a line of foot, missiles, or horse.</summary>
public enum BlockRole : byte { Foot, Missile, Horse }

/// <summary>
/// One block of v2's field (abstract war 3, <c>BattleBlock</c>): a side's foot, missiles or horse, with the frontage of its front rank (mm), its ranks, where it stands along the axis
/// (<see cref="X"/>) and across it (<see cref="Lateral"/>), its cohesion (the share of its start still in the line, permille), the blocks it is engaged with, whether it is held in reserve
/// and whether it is a shield wall.
/// </summary>
public sealed class BattleBlock
{
    public int Side;
    public BlockRole Role;
    public int Frontage, Ranks, X, Lateral, Facing, CohesionPm = 1000, Start;
    public bool Reserve, Wall;
    public readonly List<int> Members = [];
    public readonly HashSet<int> Engaged = [];
}

/// <summary>
/// Abstract battle v2 (abstract-war-and-calibration 4.1, 4.2; <c>AbstractBattleDef.Version</c> 2). The soldiers are grouped into blocks from where the setup stood them (the captains' deployment is in
/// the kits' places); engaged attackers are the front rank within the overlap of two frontages and the overhang beyond the narrower line wraps its flank (those attackers strike the flank
/// members, with the flank bonus and no shield, and the flanked carry the flank term); horse behind the foot is a reserve released when a foot block is under 60% or a flank is wrapped; a block
/// under 600 cohesion loses its shape bonus; a cavalry block's charge is a pass (refusal before braced polearms, the braced strike and the horse brought down, the trample, the fall, shock).
/// Draws: 0x7E0 the flank target, 0x7E1 the pass. Version 1 (and every Phase 4 hash) is untouched: each hook is behind <see cref="IsV2"/>.
/// </summary>
public sealed partial class AbstractBattle
{
    public bool IsV2 { get; private set; }
    public List<BattleBlock> Blocks { get; } = [];
    public PassTally Passes;
    private MountedTuningDef _mt = new() { Id = "default" };
    private int _ax, _ay;
    private readonly int[] _flo = [int.MaxValue, int.MaxValue], _fhi = [int.MinValue, int.MinValue], _bracedPm = new int[2];
    private readonly List<Soldier>[] _front = [[], []], _flankSet = [[], []];

    /// <summary>The v2 row of the content (the Phase 4 row stays the default).</summary>
    public static AbstractBattleDef V2Def(ContentDatabase db) => db.All<AbstractBattleDef>().Where(d => d.Version >= 2).OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new AbstractBattleDef { Id = "v2", Version = 2 };

    private void InitV2(FightContent c)
    {
        IsV2 = true;
        _mt = c.Db.All<MountedTuningDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new MountedTuningDef { Id = "default" };
    }

    private int Rd(Soldier a, uint draw, int n) => DetRng.Range(_seed, Step, (ulong)(a.Index + 1), RngStreams.TrampleAndFall, draw, 0, Math.Max(1, n));

    private static BlockRole RoleOf(Soldier s) => s.Mounted ? BlockRole.Horse : s.Shooter != null ? BlockRole.Missile : BlockRole.Foot;

    // ------------------------------------------------------------------ deployment: the blocks, the fronts, the wrap, the reserves

    private void BuildBlocks()
    {
        long Along(Soldier s) => ((long)s.Kit.X * _ax + (long)s.Kit.Y * _ay) / FightMath.One;
        for (int side = 0; side < 2; side++)
            foreach (var role in new[] { BlockRole.Foot, BlockRole.Missile, BlockRole.Horse })
            {
                var members = Side[side].Where(x => RoleOf(x) == role).ToList();
                if (members.Count == 0) continue;
                var front = role == BlockRole.Foot ? members.Where(x => x.Row == 0).ToList() : members;
                if (front.Count == 0) front = members;
                var b = new BattleBlock { Side = side, Role = role, Start = members.Count, Frontage = front.Max(x => x.Lat) - front.Min(x => x.Lat) + 1000, Facing = side == 0 ? 0 : 1 };
                b.Ranks = (members.Count + front.Count - 1) / front.Count;
                b.X = (int)(members.Sum(Along) / members.Count);
                b.Lateral = (int)(members.Sum(x => (long)x.Lat) / members.Count);
                b.Wall = role == BlockRole.Foot && members.Count(x => x.Shield) * 1000 >= K.WallShieldPm * members.Count;
                foreach (var m in members) { m.Block = Blocks.Count; b.Members.Add(m.Index); }
                Blocks.Add(b);
            }
        // the horse behind its side's foot is a reserve (a horse block beside or in front of the foot is not)
        foreach (var b in Blocks.Where(x => x.Role == BlockRole.Horse))
        {
            var foot = Blocks.FirstOrDefault(x => x.Side == b.Side && x.Role == BlockRole.Foot);
            if (foot == null) continue;
            int depth = b.Side == 0 ? foot.X - b.X : b.X - foot.X;
            if (depth > K.ReserveDepthMm) { b.Reserve = true; foreach (int i in b.Members) Soldiers[i].Reserve = true; }
        }
        // each side's foot front: its extent across the axis, in lateral order
        for (int side = 0; side < 2; side++)
        {
            foreach (var x in Side[side]) if (RoleOf(x) == BlockRole.Foot && x.Row == 0) _front[side].Add(x);
            _front[side].Sort(static (p, q) => p.Lat != q.Lat ? p.Lat.CompareTo(q.Lat) : p.Index.CompareTo(q.Index));
            if (_front[side].Count > 0) { _flo[side] = _front[side][0].Lat; _fhi[side] = _front[side][^1].Lat; }
        }
        // the wrap: a front-rank man of one line standing beyond the other line's end takes it in the flank
        for (int side = 0; side < 2; side++)
        {
            int e = 1 - side;
            if (_front[e].Count == 0) continue;
            foreach (var x in _front[side])
                if (x.Lat < _flo[e] || x.Lat > _fhi[e]) { x.Wrap = true; if (x.Position == BattlePosition.Front) x.Position = BattlePosition.Flank; }
        }
        for (int s = 0; s < 2; s++) Report.BlocksOf[s] = Blocks.Count(b => b.Side == s);
    }

    /// <summary>Each step's block bookkeeping: cohesion, the reserves released, who the wrap has flanked, the braced share of each front.</summary>
    private void UpdateBlocks()
    {
        foreach (var b in Blocks)
        {
            int line = 0;
            foreach (int i in b.Members) if (Soldiers[i].InLine) line++;
            b.CohesionPm = line * 1000 / Math.Max(1, b.Start);
        }
        for (int d = 0; d < 2; d++)
        {
            int e = 1 - d, wl = 0, wr = 0, braced = 0, count = 0;
            foreach (var x in _front[d]) { x.Flanked = false; if (x.InLine) { count++; if (x.Braces) braced++; } }
            _bracedPm[d] = count == 0 ? 0 : braced * 1000 / count;
            foreach (var w in _front[e]) if (w.Wrap && w.InLine) { if (w.Lat < _flo[d]) wl++; else if (w.Lat > _fhi[d]) wr++; }
            _flankSet[d].Clear();
            int cap = Math.Max(1, K.OutnumberCapPm);
            Mark(d, (wl * 1000 + cap - 1) / cap, false);
            Mark(d, (wr * 1000 + cap - 1) / cap, true);
        }
        // reserves: released when one of their side's foot blocks is under reserve_pm of its start, or the enemy wraps a flank
        foreach (var b in Blocks)
        {
            if (!b.Reserve) continue;
            bool shaken = Blocks.Any(x => x.Side == b.Side && x.Role == BlockRole.Foot && x.CohesionPm < K.ReservePm);
            if (!shaken && _flankSet[b.Side].Count == 0) continue;
            b.Reserve = false;
            foreach (int i in b.Members) { var s = Soldiers[i]; s.Reserve = false; s.Released = true; }
        }
    }

    private void Mark(int d, int need, bool fromHigh)
    {
        var f = _front[d];
        for (int k = 0; k < f.Count && need > 0; k++)
        {
            var x = f[fromHigh ? f.Count - 1 - k : k];
            if (!x.InLine || x.Flanked) continue;
            x.Flanked = true; _flankSet[d].Add(x); need--;
        }
    }

    /// <summary>A wrapper strikes the flank members of the line he overlaps (his target holds while it stands).</summary>
    private Soldier PickFlank(Soldier a, int e)
    {
        if (a.Target >= 0 && Soldiers[a.Target] is { } held && held.Side != a.Side && held.Active && !held.Routing && held.Flanked) return held;
        var set = _flankSet[e];
        var tgt = set[DetRng.Range(_seed, Step, (ulong)(a.Index + 1), RngStreams.BlockPlacement, 0, 0, set.Count)];
        if (a.Target >= 0) Soldiers[a.Target].TargetedBy = Math.Max(0, Soldiers[a.Target].TargetedBy - 1);
        a.Target = tgt.Index; tgt.TargetedBy++;
        return tgt;
    }

    // ------------------------------------------------------------------ the pass (4.2)

    /// <summary>A rider's charge at contact: refusal before braced polearms, the braced strike and the horse brought down, then the trample on the man overrun. True when he charged home.</summary>
    private bool Pass(Soldier a)
    {
        a.PassDone = true;
        int e = 1 - a.Side;
        var R = _mt.Refusal;
        int arcPm = K.PassArcMen * _bracedPm[e];
        int n = arcPm / 1000 + (Rd(a, 0, 1000) < arcPm % 1000 ? 1 : 0);
        int riding = 40 + a.Skill / 3;
        if (n > 0)
        {
            int p = ChargeMath.RefusalPm(n, K.PassTrainedPm, riding, R.BasePm, R.PerBracedPolearmPm, R.TrainedK, R.TrainedRef, R.RidingK, R.MaxPm, Input.Terrain?.Thunderstorm == true ? _mt.StormRefusalPm : 1000);
            Passes.Rolls++; Passes.ExpectedRefusalPm += p;
            if (Rd(a, 1, 1000) < p)
            {
                Passes.Refusals++; a.Refused = true;
                if (Rd(a, 2, 1000) < _mt.Unhorse.RefusalThrowPm) { Passes.Threw++; Unhorse(a, 3); }
                return false;
            }
            if (Rd(a, 4, 1000) < K.BraceStrikePm && Rd(a, 5, 1000) < K.BraceDownPm) { Passes.BraceDowns++; Unhorse(a, 6); return false; }
        }
        Passes.Passes++;
        if (Rd(a, 10, 1000) < K.TramplePm)
        {
            Passes.Tramples++;
            var f = _front[e];
            int live = 0; foreach (var x in f) if (x.InLine) live++;
            if (live > 0)
            {
                int pick = Rd(a, 11, live); Soldier? v = null;
                foreach (var x in f) if (x.InLine && pick-- == 0) { v = x; break; }
                if (v != null && Wound(v, WeaponClass.Hafted, K.TrampleLightPm, K.TrampleSeriousPm, K.TrampleGravePm, a, 12, 14)) Passes.TrampleHits++;
            }
        }
        if (Rd(a, 20, 1000) >= K.StickPm) a.ReformUntil = Step + K.ReformS;       // rides through and re-forms behind; the others stick in the melee
        return true;
    }

    private void Unhorse(Soldier a, uint draw)
    {
        Passes.Falls++; a.Unhorsed = true; a.Mounted = false;
        Wound(a, WeaponClass.Hafted, K.FallLightPm, K.FallSeriousPm, K.FallGravePm, null, draw, 15);
    }

    /// <summary>A blunt wound of a drawn class (the rows of this class mix, then a recorded template of the class), queued as a hit at the step's second <paramref name="k"/>; true when one landed.</summary>
    private bool Wound(Soldier v, WeaponClass wc, int lightPm, int seriousPm, int gravePm, Soldier? by, uint draw, int k)
    {
        int r = Rd(by ?? v, draw, 1000);
        var o = r < lightPm ? OutcomeClass.Light : r < lightPm + seriousPm ? OutcomeClass.Serious : r < lightPm + seriousPm + gravePm ? OutcomeClass.Grave : OutcomeClass.Miss;
        if (o == OutcomeClass.Miss) return false;
        var lib = Tables.Templates(wc, v.Armour, o);
        WoundTemplate? tpl = lib.Length > 0 ? lib[Rd(by ?? v, draw + 1, lib.Length)] : null;
        long sec = StepStartSec + (long)StepTicks * (k + 1) / 17;
        _hits.Add(new Hit(sec, v.Index, by?.Index ?? -1, k, tpl, o, wc));
        return true;
    }

    /// <summary>Shock (cavalry 4.1): ten riders or more home against a front that is not braced shake the whole front.</summary>
    private void Shock(int homeRiders, int e)
    {
        if (homeRiders < K.ShockMinRiders || _bracedPm[e] >= K.ShockBracedMaxPm) return;
        Passes.Shocks++;
        foreach (var x in _front[e]) if (x.InLine) x.PenaltyX100 += K.ShockX100;
    }

    // ------------------------------------------------------------------ morale terms

    private int V2FlankX100(Soldier s, bool pressTerm)
    {
        int term = pressTerm ? M.FlankedX100 * K.MeleeFlankedPm / 1000 : 0;
        return term + (s.Flanked || _rearFlanked[s.Side] ? M.FlankedX100 : 0);
    }

    private readonly bool[] _rearFlanked = new bool[2];

    private int V2BlockX100(Soldier s)
    {
        if (s.Block < 0) return 0;
        var b = Blocks[s.Block];
        if (b.CohesionPm < K.BrokenPm) return -K.BrokenX100;
        return b.Wall ? K.WallX100 : 0;
    }

    // ------------------------------------------------------------------ the aftermath: prisoners, rank classes and kit left on the field

    private RankClass RankOf(Soldier x) => x.Captain ? RankClass.Officer : x.Armour >= ArmourClass.Mail || x.Skill >= K.VeteranSkill ? RankClass.Veteran : RankClass.Common;

    private void V2Aftermath()
    {
        var rep = Report;
        int holder = Winner >= 0 ? Team[Winner] : -1;
        foreach (var x in Soldiers)
        {
            if (x.Status == SoldierStatus.Captured && Winner >= 0 && x.Side != Winner) rep.Prisoners.Add(new BattlePrisoner(x.Person, x.Name, x.Side, RankOf(x), holder, x.Yielded));
            if (x.Status is SoldierStatus.Dead or SoldierStatus.Captured)
                rep.Loot.Add(new BattleLoot(x.Person, x.Name, x.Side, x.Status, holder, x.Kit.Weapon?.ShapeId ?? "", x.Kit.Shield ?? "", [.. x.Kit.Worn.Select(w => w.ShapeId)]));
        }
        rep.Passes = Passes;
    }

    private void V2Commands()
    {
        foreach (var x in Soldiers.Where(x => x.Person != 0).OrderBy(x => x.Person))
        {
            if (x.Status == SoldierStatus.Captured && Winner >= 0 && x.Side != Winner && RankOf(x) >= RankClass.Veteran)
                Commands.Add(new BattleCommand(x.Person, BattleCommandKind.Ransom, x.Status, null, Team[Winner], (int)RankOf(x)));
            if (x.Status == SoldierStatus.Dead && Winner >= 0 && x.Side == Winner)
                Commands.Add(new BattleCommand(x.Person, BattleCommandKind.Loot, x.Status, null, Team[Winner]));
        }
    }

    private ulong V2Hash(ulong h)
    {
        h = FightMath.Mix(h, Blocks.Count);
        foreach (var b in Blocks) { h = FightMath.Mix(h, b.Frontage); h = FightMath.Mix(h, b.CohesionPm); }
        h = FightMath.Mix(h, Passes.Passes); h = FightMath.Mix(h, Passes.Refusals); h = FightMath.Mix(h, Passes.Falls); h = FightMath.Mix(h, Passes.TrampleHits);
        foreach (var p in Report.Prisoners) { h = FightMath.Mix(h, (long)p.Person); h = FightMath.Mix(h, (long)p.Rank); }
        return FightMath.Mix(h, Report.Loot.Count);
    }
}
