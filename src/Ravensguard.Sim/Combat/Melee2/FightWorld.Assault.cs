using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Siege;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>P7-SG-C: walls that have levels (sieges-and-engines 4.4). A fighter stands on a floor a number of millimetres above the ground (the wall-walk at 3,000, a tower's top deck, a rung of a ladder); a blow reaches only people near his floor, a missile flies in three dimensions and meets the walls of the level it is in, and rubble slows the feet.</summary>
public interface ILevelWalls : IFighterWalls
{
    /// <summary>The height (mm) of the floor the fighter stands on.</summary>
    int BaseMm(int fighter);
    /// <summary>Whether a blow can pass from one fighter to another (the same floor, near enough).</summary>
    bool CanStrike(int attacker, int target);
    /// <summary>Whether a missile at (x, y) mm and the height z mm (above the ground) is stopped by what stands there (a wall, a shut gate, a merlon by the draw keyed with the shooter and the projectile's serial).</summary>
    bool MissileStopped(int xMm, int yMm, int zMm, int shooter, uint serial);
    /// <summary>The share (permille) of his walk the ground under the fighter allows (rubble 500).</summary>
    int FootingPm(int fighter);
}

/// <summary>
/// [P7-SG-C] The assault on several levels (sieges-and-engines 4.4): with <see cref="Assault"/> set, <c>Run</c> is called once at the start of each tick (ladders, towers, murder holes, sallies and the people held by them), and the fight's
/// walls are the layer's (<see cref="ILevelWalls"/>). The blows an assault makes that are not a weapon's go through the one resolver here: a fall, a falling stone, a pour of boiling water. A fight with no assault has none of this,
/// runs exactly as before and its hash is unchanged.
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The assault laid over this fight (ladders, towers, drawbridges, murder holes, sallies); null: none.</summary>
    public AssaultLayer? Assault { get; set; }

    internal int BaseOf(Fighter f) => Walls is ILevelWalls lw ? lw.BaseMm(f.Id) : 0;

    /// <summary>The numbers of the assault (content <c>assault_rules</c>) when a layer is set, else the defaults.</summary>
    internal Ravensguard.Content.AssaultRulesDef AssaultRules => Assault?.R ?? AssaultLayer.DefaultRules;

    /// <summary>Whether a wall of the attacker's own floor lies between him and a target (the layer is told whose body it is testing).</summary>
    private bool WallBetweenFor(Fighter a, Fighter t)
    {
        if (Walls is IFighterWalls fw) fw.Using(a.Id);
        return WallBetween(a.X, a.Y, t.X, t.Y);
    }

    /// <summary>The weight of a fighter in kilograms for the fall rules: his body (a centimetre-cubed of flesh is about a gram) and what he wears.</summary>
    private static int MassKg(Fighter f) => Math.Max(20, f.Kit.SizeCm3 / 1000 + f.ArmourG / 1000 + f.OverKg);

    /// <summary>A fight drops <paramref name="f"/> <paramref name="heightMm"/> to the ground (a ladder pushed off, a floor gone): the Phase 4 fall (100 x kg x (3 x m)) through the resolver on a part drawn by weight, stunned for a moment. Returns the wounds made.</summary>
    public int FallFrom(Fighter f, int heightMm, ulong key)
    {
        if (f.Act is FAct.Dead or FAct.Fled or FAct.Surrendered || heightMm <= 0) return 0;
        long m = LayerTest.FallMomentumMilli(MassKg(f), heightMm, 0);
        int before = f.Book.Records.Count;
        FallBlow(f, m, FallPart(f), (uint)(key & 0xFFFF), f);
        if (f.Act is not (FAct.Dead or FAct.Down)) { f.Act = FAct.Stagger; f.ActTicks = Math.Max(f.ActTicks, 40); }
        return f.Book.Records.Count - before;
    }

    /// <summary>A stone of <paramref name="massKg"/> falls <paramref name="heightMm"/> on a man's head or shoulders (murder holes, 4.4): a blunt thrown-rock blow through the resolver, its momentum the mass times the speed a body reaches in that fall (<see cref="AssaultMath.FallSpeedMmps"/>, from a table). Returns the wounds made.</summary>
    public int StoneFalls(Fighter t, int massKg, int heightMm, ulong key)
    {
        if (!t.Active) return 0;
        long milli = (long)massKg * AssaultMath.FallSpeedMmps(heightMm);          // kg x mm/s = milli kg m/s
        return ImpactMilli(t, milli, key);
    }

    /// <summary>A blunt blow of <paramref name="milli"/> (milli kg m/s) through the one resolver on a head or shoulders drawn by weight: what a falling stone is, and what a car's thrust or hooves are (P8-LW-3). Returns the wounds made.</summary>
    public int ImpactMilli(Fighter t, long milli, ulong key)
    {
        if (!t.Active) return 0;
        var w = Content.ImprovisedWeapon(Content.Improvised["rock"]);
        int attack = 0; var at = w.Attacks;
        for (int i = 0; i < at.Length; i++) if (at[i].Type == AttackType.Blunt) { attack = i; break; }
        var plan = t.Plan;
        int head = plan.IndexOf("head"), body = plan.IndexOf("upper_body");
        int part = DetRng.Range(Seed, Tick, key ^ (ulong)(t.Id + 1) << 20, RngStreams.FallPlacement, 0x3000_0000u, 0, 100) < 50 && head >= 0 ? head : Math.Max(0, body);
        var s = new StrikeV2
        {
            Strength = 50, Skill = 50, SizeCm3 = t.Kit.SizeCm3, SpeciesSizeCm3 = plan.SizeCm3, MomentumPm = 1000, Weapon = w, Attack = attack, Style = AttackStyle.Normal,
            Plan = plan, DefenderSizeCm3 = t.Kit.SizeCm3, DefenderSkill = t.Skill, Worn = t.Worn, WornCount = t.Worn.Length, MissingMask = t.MissingMask, Prone = false,
            TargetPart = part, TargetGap = -1, Facing = Facing.Front, Missile = true, ExtraMomentumMilli = milli,
        };
        Resolver.ResolveImpact(s, new BlowKey(Seed, Tick, key | 0xA000_0000UL, (uint)t.Id + 1), _r);
        if (_r.WoundCount == 0) { Emit(FightEventKind.Stopped, -1, t.Id, part, (int)(milli / 1000)); return 0; }
        t.Book.Apply(t.Plan, _r, GameSec, Tick, key | 1UL);
        RefreshBody(t);
        MoraleOnHit(t, Facing.Front);
        Emit(FightEventKind.Hit, -1, t.Id, _r.Part, _r.WoundCount, (int)Facing.Front << 4);
        if (t.Act is FAct.Drawing or FAct.Aiming) t.Act = FAct.Idle;
        if (t.Book.Dead) Kill(t);
        else if (!t.Book.Conscious && t.Act != FAct.Down) GoDown(t);
        return _r.WoundCount;
    }

    /// <summary>Boiling water poured on a man (murder holes, 4.4): a burn wound of the given intensity (1 scorch, 2 burn, 3 deep burn) on a part drawn by area (the head, the shoulders and arms, the trunk), the Phase 6 burn rows. Returns 1 when it burned.</summary>
    public int Scald(Fighter t, int intensity, ulong key)
    {
        if (!t.Active) return 0;
        var plan = t.Plan;
        int roll = DetRng.Range(Seed, Tick, key ^ (ulong)(t.Id + 1) << 20, RngStreams.FallPlacement, 0x3100_0000u, 0, 100);
        string id = roll < 25 ? "head" : roll < 60 ? "upper_body" : roll < 80 ? "upper_arm_l" : "upper_arm_r";
        int idx = plan.IndexOf(id); if (idx < 0) idx = Math.Max(0, plan.IndexOf("upper_body"));
        var part = plan.Parts[idx];
        var skin = part.Layers.FirstOrDefault(l => l.IsSoft) ?? part.Layers[0];
        int depth = intensity switch { 1 => 250, 2 => 600, _ => 900 };
        var wounds = new List<WoundV2> { new() { Part = (short)idx, Tissue = (short)skin.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Burn, DepthPm = depth, Flags = WoundFlags.Burn } };
        if (intensity >= 3 && part.Layers.FirstOrDefault(l => l.IsMuscle) is { } muscle)
            wounds.Add(new WoundV2 { Part = (short)idx, Tissue = (short)muscle.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Burn, DepthPm = 600, Grade = (byte)MuscleGrade.Torn, Flags = WoundFlags.Burn });
        t.Book.Apply(plan, [.. wounds], GameSec, Tick, key | 2UL, 0, false);
        RefreshBody(t);
        MoraleOnHit(t, Facing.Front);
        Emit(FightEventKind.Hit, -1, t.Id, idx, wounds.Count, (int)Facing.Front << 4);
        if (t.Act is FAct.Drawing or FAct.Aiming) t.Act = FAct.Idle;
        if (t.Book.Dead) Kill(t);
        else if (!t.Book.Conscious && t.Act != FAct.Down) GoDown(t);
        return 1;
    }

    /// <summary>A side's whole body loses heart together (the wall is lost, 4.4): every active fighter of the team takes this many hundredths off his morale, once.</summary>
    public void MoraleBlow(int team, int x100)
    {
        foreach (var f in Fighters)
        {
            if (f.Team != team || !f.Active) continue;
            EnsureMorale(f);
            f.M.PenaltyX100 += x100; f.M.LastLossTick = Tick;
        }
    }
}
