using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>
/// The mounted pairings of the calibration harness (mounted-combat 8, <c>tools/Ravensguard.Calibrate --mounted</c>): one rider charging one foot fighter (or a rider) from 40 m on dry field,
/// under the real AI (the cavalry brain and the combat brain), every blow through resolver v2; and the 12 mounted against 20 foot skirmish of MT-12 and MT-13. Statistics only (a tool,
/// not sim state), so the averages are doubles.
/// </summary>
public static class MountedCalibration
{
    public static readonly WeaponInstance Lance = new("lance", "steel");
    public static readonly WeaponInstance Sword = FightCalibration.Sword;
    public static readonly WeaponInstance Spear = FightCalibration.Spear;
    public static readonly WeaponInstance Pike = new("pike", "steel");

    public static readonly FightCalibration.Kit Mailed = FightCalibration.Mailed, Padded = FightCalibration.Padded;

    /// <summary>A pairing: the rider's weapon, shield and kit, the horse; the other side's weapon and kit, and whether it rides too.</summary>
    public sealed record Pairing(string Name, WeaponInstance RiderWeapon, FightCalibration.Kit RiderKit, WeaponInstance FootWeapon, FightCalibration.Kit FootKit,
        string? RiderShield = null, string? FootShield = null, bool FootMounted = false, string Breed = "warhorse_courser", int Riding = 50, Footing Ground = Footing.Field);

    public static IReadOnlyList<Pairing> MainPairings() =>
    [
        new("couched lance vs sword (padded, unbraced)", Lance, Mailed, Sword, Padded, RiderShield: "heater"),
        new("couched lance vs sword+heater (mail)", Lance, Mailed, Sword, Mailed, RiderShield: "heater", FootShield: "heater"),
        new("couched lance vs braced spear (padded)", Lance, Mailed, Spear, Padded, RiderShield: "heater"),
        new("couched lance vs braced pike (padded)", Lance, Mailed, Pike, Padded, RiderShield: "heater"),
        new("palfrey lance vs braced spear (padded)", Lance, Mailed, Spear, Padded, RiderShield: "heater", Breed: "horse_palfrey", Riding: 40),
        new("mounted sword vs foot sword (padded)", Sword, Padded, Sword, Padded),
        new("mounted sword vs foot sword (mail)", Sword, Mailed, Sword, Mailed),
        new("lance vs lance (mail, both mounted)", Lance, Mailed, Lance, Mailed, RiderShield: "heater", FootShield: "heater", FootMounted: true),
        new("couched lance vs sword on mud", Lance, Mailed, Sword, Padded, RiderShield: "heater", Ground: Footing.Mud),
    ];

    public sealed class Row
    {
        public required string Name { get; init; }
        public int N, RiderWins, FootWins, FirstPassDown, Unhorsed, Refusals, Shatters, HorseFalls, Charges, ChargeHits;
        public double TotalS;
        public long ChargeMomentum;
        public double MeanS => N == 0 ? 0 : TotalS / N;
        public double RiderWinPct => N == 0 ? 0 : 100.0 * RiderWins / N;
        public double FirstPassPct => N == 0 ? 0 : 100.0 * FirstPassDown / N;
        public double Per(int v) => N == 0 ? 0 : (double)v / N;
    }

    public sealed record Duel(double Seconds, int Winner, bool FirstPassDown, int Unhorsed, int Refusals, int Shatters, int HorseFalls, int Charges, int ChargeHits, long ChargeMomentum, ulong Hash);

    public static FighterKit RiderKit(string name, int team, WeaponInstance w, FightCalibration.Kit k, string? shield, int skill, int x, int faceX) =>
        FightCalibration.KitOf(name, team, w, k, shield, skill, x, faceX);

    /// <summary>The world of a charge: the rider 40 m west of the other, both facing; skills 30-59 per (duel, side).</summary>
    public static FightWorld ChargeWorld(FightContent c, Pairing p, int i)
    {
        var w = new FightWorld(c, (ulong)(5000 + i), (ulong)(i + 1), 8 * 3600) { RecordEvents = true };
        var mk = new MountKit { Breed = p.Breed, Riding = p.Riding };
        w.AddMounted(RiderKit("Rider", 0, p.RiderWeapon, p.RiderKit, p.RiderShield, Melee.MeleeScenarios.SkillFor(i, 0), -40_000, FightMath.One), mk);
        var foot = RiderKit("Foot", 1, p.FootWeapon, p.FootKit, p.FootShield, Melee.MeleeScenarios.SkillFor(i, 1), 0, -FightMath.One);
        if (p.FootMounted) w.AddMounted(foot with { X = 40_000 }, mk);
        else w.Add(foot);
        w.Mounts!.Terrain.Base = p.Ground;
        return w;
    }

    /// <summary>One seeded charge duel, up to <paramref name="maxS"/> seconds.</summary>
    public static Duel RunDuel(FightContent c, Pairing p, int i, int maxS = 120)
    {
        var w = ChargeWorld(c, p, i);
        int firstContact = -1; bool firstDown = false;
        int charges = 0, chargeHits = 0; long chargeMom = 0;
        var rider = w.Fighters[0];
        var foot = w.Fighters.First(f => f.Name == "Foot");
        w.OnBlow = (a, t, r, s) =>
        {
            if (a.Id == rider.Id && s.ExtraMomentumMilli > 0 && w.Riding(a) is { } m && m.Speed >= w.Mounts!.D.GallopChargeMinMmps)
            {
                charges++; chargeMom += r.InitialMilli;
                if (r.WoundCount > 0 && !r.Blocked) chargeHits++;
            }
            if (firstContact < 0 && (a.Id == rider.Id || t.Id == rider.Id)) firstContact = w.Tick;
        };
        int max = maxS * 20;
        while (!w.IsOver && w.Tick < max)
        {
            w.StepWithAi();
            if (firstContact >= 0 && w.Tick <= firstContact + 40 && !foot.Active) firstDown = true;
        }
        int unhorsed = 0, refusals = 0, shatters = 0, horseFalls = 0;
        foreach (var e in w.Events)
        {
            if (e.Kind == FightEventKind.Unhorsed) unhorsed++;
            else if (e.Kind == FightEventKind.Balked) refusals++;
            else if (e.Kind == FightEventKind.Shattered) shatters++;
            else if (e.Kind == FightEventKind.HorseFell) horseFalls++;
        }
        return new Duel(w.Tick / 20.0, w.WinnerTeam, firstDown, unhorsed, refusals, shatters, horseFalls, charges, chargeHits, chargeMom, w.Hash());
    }

    public static Row Run(FightContent c, Pairing p, int duels)
    {
        var d = new Duel[duels];
        Parallel.For(0, duels, i => d[i] = RunDuel(c, p, i));
        var row = new Row { Name = p.Name };
        foreach (var x in d)
        {
            row.N++; row.TotalS += x.Seconds;
            if (x.Winner == 0) row.RiderWins++; else if (x.Winner == 1) row.FootWins++;
            if (x.FirstPassDown) row.FirstPassDown++;
            row.Unhorsed += x.Unhorsed; row.Refusals += x.Refusals; row.Shatters += x.Shatters; row.HorseFalls += x.HorseFalls;
            row.Charges += x.Charges; row.ChargeHits += x.ChargeHits; row.ChargeMomentum += x.ChargeMomentum;
        }
        return row;
    }

    public static string Table(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Pairing | n | mean s | rider wins | other wins | down on first pass | charges/duel | charge wounds | charge M | unhorsed/duel | refusals/duel | lances broken/duel | horse falls/duel |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Name} | {r.N} | {r.MeanS:0.0} | {r.RiderWinPct:0}% | {100.0 * r.FootWins / Math.Max(1, r.N):0}% | {r.FirstPassPct:0}% | {r.Per(r.Charges):0.0} | {100.0 * r.ChargeHits / Math.Max(1, r.Charges):0}% | {(r.Charges == 0 ? "-" : $"{r.ChargeMomentum / 1000.0 / r.Charges:0}")} | {r.Per(r.Unhorsed):0.00} | {r.Per(r.Refusals):0.00} | {r.Per(r.Shatters):0.00} | {r.Per(r.HorseFalls):0.00} |");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ the skirmish (MT-12, MT-13)

    /// <summary>
    /// <paramref name="riders"/> riders (lances, mail, coursers; or the same men on foot with swords and heaters when <paramref name="mounted"/> is false) 40 m west of
    /// <paramref name="foot"/> foot (swords and heaters, every fourth a spear; padded), on dry field; skills 30-64 by (seed, side, index).
    /// </summary>
    /// <para><paramref name="footMix"/>: 0 swords and heaters with every fourth a spear; 1 swords and heaters only (no polearm to brace: the abstract bonus's "not braced" unit);
    /// 2 swords without shields.</para>
    public static FightWorld Skirmish(FightContent c, ulong seed, int riders = 12, int foot = 20, bool mounted = true, int footMix = 0, Footing ground = Footing.Field)
    {
        var w = new FightWorld(c, seed, seed, 8 * 3600);
        for (int i = 0; i < riders; i++)
        {
            int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)i, 7, 0) % 35);
            int y = (i * 2 - (riders - 1)) * 1500;
            var k = FightCalibration.KitOf($"Rider {i + 1}", 0, mounted ? Lance : Sword, Mailed, "heater", skill, -40_000, FightMath.One) with { Y = y };
            if (mounted) w.AddMounted(k, new MountKit { Riding = 40 + skill / 3 });
            else w.Add(k with { X = -10_000 });
        }
        for (int i = 0; i < foot; i++)
        {
            int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)(100 + i), 7, 0) % 35);
            int y = (i * 2 - (foot - 1)) * 800;
            bool spear = footMix == 0 && i % 4 == 3;
            w.Add(FightCalibration.KitOf($"Foot {i + 1}", 1, spear ? Spear : Sword, Padded, spear || footMix == 2 ? null : "heater", skill, 0, -FightMath.One) with { Y = y });
        }
        if (w.Mounts != null) w.Mounts.Terrain.Base = ground;
        return w;
    }

    /// <summary>The first pass of a charge: every rider has struck once and none has come round again (the second pass lands 8 to 10 s after the first).</summary>
    public const int FirstPassS = 10;

    /// <summary>
    /// MT-12: the charge's own bonus. The same 12 riders against the same 20 foot (swords and heaters, no polearm to brace: the abstract bonus's unit "neither braced nor on mud",
    /// <paramref name="footMix"/> 1), once on dry field where they charge and once on mud where no charge is possible (the abstract rule's own exception), with the same morale on both;
    /// the foot downed or killed by blows in the first pass from the first wound (routs and yields are morale's, which the abstract battle counts by its own rout rule). Seeds 900 on,
    /// in parallel (each its own seeded world). Returns the two means and the bonus in percent (the abstract battle's first-contact <c>charge_pm</c> is +60%).
    /// </summary>
    public static (double Charge, double NoCharge, double BonusPct) ChargeBonus(FightContent c, int seeds, int footMix = 1)
    {
        var res = new (int M, int F)[seeds];
        Parallel.For(0, seeds, s => res[s] = (FirstContact(Skirmish(c, (ulong)(900 + s), footMix: footMix), FirstPassS).Downed,
            FirstContact(Skirmish(c, (ulong)(900 + s), footMix: footMix, ground: Footing.Mud), FirstPassS).Downed));
        double m = res.Average(x => x.M), f = res.Average(x => x.F);
        return (m, f, 100.0 * (m / Math.Max(0.01, f) - 1));
    }

    /// <summary>The foot side's casualties (down, dead, fled or yielded) in the <paramref name="windowS"/> seconds from the first blow that lands on it, and of them those
    /// downed or killed by blows (morale's routs and yields left out).</summary>
    public static (int Casualties, int Contact, int Downed) FirstContact(FightWorld w, int windowS, int maxS = 120, Action<Fighter, Fighter, Resolver2.BlowResult, Resolver2.StrikeV2>? onBlow = null)
    {
        int contact = -1;
        w.OnBlow = (a, t, r, s) => { if (contact < 0 && t.Team == 1 && r.WoundCount > 0) contact = w.Tick; onBlow?.Invoke(a, t, r, s); };
        int max = maxS * 20;
        while (!w.IsOver && w.Tick < max && (contact < 0 || w.Tick < contact + windowS * 20)) w.StepWithAi();
        int cas = 0, downed = 0;
        foreach (var f in w.Fighters)
        {
            if (f.Team != 1 || w.IsHorse(f) || f.Active) continue;
            cas++;
            if (f.Act is FAct.Down or FAct.Dead) downed++;
        }
        return (cas, contact, downed);
    }
}
