using Ravensguard.Content;
using Ravensguard.Sim.Combat;

namespace Ravensguard.Sim.Clothing;

/// <summary>The weather and place a person stands in for one game minute, in whole units: centi-degrees, centimetres a second, tenths of a millimetre an hour.</summary>
public readonly record struct ExposureEnv(int TempCenti, int WindCmps = 0, int PrecipTenthsMmH = 0, bool Snow = false, bool Sunny = false, bool Roofed = false, bool Fire = false, bool Shade = false, bool InBed = false)
{
    public static ExposureEnv Outdoors(int tempC, int windMps = 0, int precipTenthsMmH = 0, bool snow = false, bool sunny = false) => new(tempC * 100, windMps * 100, precipTenthsMmH, snow, sunny);
}

/// <summary>Hypothermia and heat stages (spec 4.3 and 4.4), ordered by how bad they are within their kind.</summary>
public enum BodyState : byte { Normal, Chilled, Moderate, Severe, Lethal, Warm, Exhausted, Heatstroke }

/// <summary>The numbers of one body in one place for one minute, before the core moves: what the HUD tooltip shows (spec 6).</summary>
public readonly record struct ExposureReading(int IEffCloX100, int IReqCloX100, int ColdStressPm, int HeatStressPm, int WindchillCenti, int TEffCenti, int WindproofPm, int RetentionPm, int EncPm, int WetAvgPm);

/// <summary>
/// The clothing and exposure model (clothing spec 4.2 to 4.5), integer-only and pure: insulation of a worn outfit, required insulation, cold and heat stress, the rate the core moves, wetting and
/// drying. Centi-degrees C for temperatures, clo x 100 for insulation, permille for shares; divisions floor; no floating point.
/// </summary>
public static class ClothingMath
{
    public const int NormalCore = 3700;

    /// <summary>Insulation of one piece in one region: <c>I x coverage x condition x (1000 - wet_loss x wet)</c>, clo x 100 (spec 4.2). <paramref name="wetPm"/> 0..1000. One division at the end, so nothing is rounded away on the way.</summary>
    public static long ItemRegion(Garment g, Region r, int conditionPermille, int wetPm)
        => ItemRegionX1e9(g, r, conditionPermille, wetPm) / 1_000_000_000;

    /// <summary>The same in thousandths of a clo x 100 (for ratios and tests that want no rounding).</summary>
    public static long ItemRegionMilli(Garment g, Region r, int conditionPermille, int wetPm) => ItemRegionX1e9(g, r, conditionPermille, wetPm) / 1_000_000;

    private static long ItemRegionX1e9(Garment g, Region r, int conditionPermille, int wetPm)
    {
        long cond = 500 + Math.Clamp(conditionPermille, 0, 1000) / 2;                       // permille: tattered at 200 is 0.6
        long dry = 1000 - (long)g.WetLossPm * Math.Clamp(wetPm, 0, 1000) / 1000;           // permille
        return g.InsulationCloX100 * (long)g.RegionPm[(int)r] * cond * dry;
    }

    /// <summary>
    /// Effective insulation of a worn outfit, clo x 100: layers add linearly in a region, the regions are weighted (head 10%, torso 40%, arms 15%, hands 5%, legs 22%, feet 8%).
    /// Each piece is (garment, condition permille, wet permille).
    /// </summary>
    public static int IEff(ExposureTuning t, ReadOnlySpan<(Garment G, int Cond, int Wet)> worn)
    {
        long total = 0;
        for (int r = 0; r < Garment.RegionCount; r++)
        {
            long sum = 0;
            foreach (var (g, cond, wet) in worn) sum += ItemRegionX1e9(g, (Region)r, cond, wet);
            total += sum / 1_000_000 * t.RegionsPm[r];                                        // clo x 100 x 1000, weighted in permille
        }
        return (int)(total / 1_000_000);
    }

    /// <summary>The share of wind chill blocked: the most windproof piece covering the torso (the outermost layer dominates), permille.</summary>
    public static int WindproofPm(ReadOnlySpan<(Garment G, int Cond, int Wet)> worn)
    {
        int best = 0;
        foreach (var (g, _, _) in worn) if (g.Covers(Region.Torso) && g.WindproofPm > best) best = g.WindproofPm;
        return best;
    }

    /// <summary>Wind chill in centi-degrees: <c>min(12, max(0, wind - 2) x 0.6) x (1000 - windproof) / 1000</c>.</summary>
    public static int WindChillCenti(ExposureTuning t, int windCmps, int windproofPm)
    {
        long over = Math.Max(0, windCmps - t.Def.WindFreeMps * 100);                         // cm/s above the free wind
        long raw = Math.Min(t.Def.WindChillMaxC * 100L, over * 6 / 10);                       // centi-degrees: 0.6 C per m/s = 0.6 centi per cm/s
        return (int)(raw * (1000 - Math.Clamp(windproofPm, 0, 1000)) / 1000);
    }

    /// <summary>Air temperature at the person in centi-degrees: roofed adds the indoor offset (a lit fire within four metres the bigger one, roofed or not), a shade lowers it by 4 C for heat.</summary>
    public static int TAir(ExposureTuning t, in ExposureEnv e)
    {
        int tc = e.TempCenti;
        if (e.Fire) tc += t.IndoorFireC * 100;
        else if (e.Roofed) tc += t.IndoorUnheatedC * 100;
        return tc;
    }

    /// <summary>Wind and rain do not reach a person under a roof.</summary>
    public static bool Sheltered(in ExposureEnv e) => e.Roofed;

    /// <summary>Required insulation: <c>max(0, comfort - T_eff) x 1400 / met</c>, clo x 100.</summary>
    public static int IReq(ExposureTuning t, int tEffCenti, Activity a)
    {
        long deg = Math.Max(0, t.Def.ComfortC * 100L - tEffCenti);                           // centi-degrees
        long met = t.MetX100[(int)a];
        return (int)(deg * t.Def.CloPerDegreeX100AtMet100 / 100 / met);
    }

    public static int ColdStressPm(ExposureTuning t, int iReq, int iEff) => (int)Math.Min(1000, Math.Max(0, iReq - iEff) * 1000L / t.Def.MaxDeficitCloX100);

    /// <summary>Heat load as stress permille: <c>(T - 24) x met x (100 + I/3)/100 x (1000 + retention)/1000 / 6.0</c>, capped at 1000.</summary>
    public static int HeatStressPm(ExposureTuning t, int tAirCenti, Activity a, int iEff, int retentionPm)
    {
        long over = tAirCenti - t.Def.HeatFreeC * 100L;
        if (over <= 0) return 0;
        long met = t.MetX100[(int)a];
        long num = over * met * (100 + iEff / 3) * (1000 + (long)retentionPm);               // = HL x 1e9 (centi-degrees x 100, met x 100, factor x 100, retention x 1000)
        return (int)Math.Min(1000, num * 10_000 / ((long)t.Def.HeatFullHlX10 * 1_000_000_000L));   // stress = HL x 1000 / (full HL = x10 / 10)
    }

    /// <summary>
    /// Core temperature change in milli-centi-degrees per game hour (a unit of the core's own scale, see <see cref="CoreUnit"/>): cooling in cold, warming in heat, rewarming when comfortable.
    /// <paramref name="resistPm"/> is the person's cold resistance (1000 average); <paramref name="fed"/> false halves rewarming (spec 4.3).
    /// </summary>
    public static long CoreRatePerHour(ExposureTuning t, int coldStressPm, int heatStressPm, int coreCenti, bool nearFire, int resistPm, bool fed)
    {
        if (coldStressPm > t.Def.ComfortStressPm) return -(long)coldStressPm * t.Def.CoolCcPerHourAtMax * 1000L / Math.Max(1, resistPm);   // milli-cC per hour
        if (heatStressPm > 0) return (long)heatStressPm * t.Def.HeatRiseCcPerHourAtMax;                                                      // x1000/1000
        long rate = 0;
        if (coreCenti < NormalCore) { rate = (nearFire ? t.Def.RewarmFireCcPerHour : t.Def.RewarmCcPerHour) * 1000L; if (!fed) rate /= 2; }
        else if (coreCenti > NormalCore) rate = -t.Def.HeatCoolCcPerHour * 1000L;
        return rate;
    }

    /// <summary>One game minute of a core in units: cC x 60000 per hour is cC x 1000 per minute, so a per-hour rate in milli-cC is exactly the per-minute change of <see cref="CoreUnit"/>.</summary>
    public const long CoreUnit = 60_000;
    public static long CoreUnitsOf(int centi) => centi * CoreUnit;
    public static int CentiOf(long units) => (int)Math.Floor(units / (double)CoreUnit);

    /// <summary>Cold resistance in permille: <c>1000 + (toughness - 50) x 6</c>, plus the person's jitter (spec 4.3 and 5).</summary>
    public static int ResistPm(int toughness, int jitterPm = 0) => 1000 + (toughness - 50) * 6 + jitterPm;

    /// <summary>The state a core temperature is in.</summary>
    public static BodyState StateOf(ExposureTuning t, int coreCenti)
    {
        var d = t.Def;
        if (coreCenti < d.LethalCc) return BodyState.Lethal;
        if (coreCenti < d.SevereCc) return BodyState.Severe;
        if (coreCenti < d.ModerateCc) return BodyState.Moderate;
        if (coreCenti < d.ChilledCc) return BodyState.Chilled;
        if (coreCenti > d.StrokeCc) return BodyState.Heatstroke;
        if (coreCenti > d.ExhaustedCc) return BodyState.Exhausted;
        if (coreCenti > d.WarmCc) return BodyState.Warm;
        return BodyState.Normal;
    }

    /// <summary>Dexterity, speed and stamina-regeneration multipliers in permille for a state (spec 4.3, 4.4).</summary>
    public static (int Dexterity, int Speed, int StaminaRegen) Effects(BodyState s) => s switch
    {
        BodyState.Chilled => (900, 1000, 1000), BodyState.Moderate => (750, 850, 700), BodyState.Severe or BodyState.Lethal => (500, 600, 400),
        BodyState.Warm => (1000, 1000, 750), BodyState.Exhausted => (750, 800, 600), BodyState.Heatstroke => (500, 500, 300), _ => (1000, 1000, 1000),
    };

    /// <summary>Mood change of a state (spec 4.3: chilled -15, warm -15).</summary>
    public static int Mood(BodyState s) => s switch { BodyState.Normal => 0, BodyState.Chilled or BodyState.Warm => -15, BodyState.Moderate or BodyState.Exhausted => -25, _ => -40 };

    // ---------------------------------------------------------------- wetness

    /// <summary>Wetness gained in one game minute by a piece in the open, in 1/100 permille: <c>mm/h x 5 x (1000 - waterproof)/1000</c> permille (snow counts half).</summary>
    public static int WetGainPerMinute(ExposureTuning t, int precipTenthsMmH, bool snow, int waterproofPm)
    {
        long g = (long)precipTenthsMmH * t.Def.WetGainPmPerMinPerMmH * (1000 - Math.Clamp(waterproofPm, 0, 1000)) * 100 / (10 * 1000);
        return (int)(snow ? g / 2 : g);
    }

    /// <summary>Drying per game minute in 1/100 permille: by a fire worn 250 an hour (hung 400), indoors 60 (90), outdoors 70 plus 40 in wind and 30 in sun, at most 20 in rain or fog.</summary>
    public static int DryPerMinute(ExposureTuning t, in ExposureEnv e, bool hung = false)
    {
        int perHour;
        bool wet = e.PrecipTenthsMmH > 0;
        if (e.Fire) perHour = hung ? t.FireHung : t.FireWorn;
        else if (e.Roofed) perHour = hung ? t.IndoorHung : t.IndoorWorn;
        else
        {
            perHour = t.OutdoorWorn + (e.WindCmps > 400 ? t.WindBonus : 0) + (e.Sunny ? t.SunBonus : 0);
            if (wet) perHour = Math.Min(perHour, t.RainDry);
        }
        return perHour * 100 / 60;
    }

    /// <summary>Mass of a piece in grams: dry mass x (100 + absorb% x wet)/100 (spec 4.5: a soaked 1.8 kg wool cloak is 2.7 kg).</summary>
    public static long WetMassG(long dryMassG, int absorbPct, int wetPm) => dryMassG * (100 + (long)absorbPct * Math.Clamp(wetPm, 0, 1000) / 1000) / 100;

    // ---------------------------------------------------------------- stains

    /// <summary>Stain kinds (spec 3.2) and the most value each takes off, permille (blood 400, dirt 200, mud 150, grease 250, soot 150).</summary>
    public const byte Blood = 1, Dirt = 2, Mud = 4, Grease = 8, Soot = 16, Set = 128;
    public static int MaxLossPm(byte kind) => kind switch { Blood => 400, Dirt => 200, Mud => 150, Grease => 250, Soot => 150, _ => 0 };

    /// <summary>Value lost to a stain of <paramref name="amount"/> (0..255) of one kind: <c>min(max, amount x max / 255)</c> permille (C-10: 60 blood is 94).</summary>
    public static int StainLossPm(byte kind, int amount) { int max = MaxLossPm(kind); return (int)Math.Min(max, Math.Clamp(amount, 0, 255) * (long)max / 255); }

    // ---------------------------------------------------------------- fit and layers

    /// <summary>A piece made for a body of <paramref name="itemSizeCm3"/> fits anyone within a seventh of it (spec 4.1, C-9).</summary>
    public static bool Fits(int itemSizeCm3, int bodySizeCm3) => Fit.Fits(itemSizeCm3, bodySizeCm3);

    /// <summary>Agility in permille after the encumbrance of the worn pieces, floor 300 (spec 4.1).</summary>
    public static int AgilityPm(int encSumPm) => Math.Max(300, 1000 - encSumPm);

    /// <summary>
    /// Can two pieces be worn together? Different layers always stack (under, over, armour, cover). Two in one layer stack when they sit in different slots (shirt and braies), or in the same slot with
    /// different orders and the DF size rule: the outer one's layer size within the inner one's permit (a gambeson over a tunic; never two tunics, two cloaks, boots and shoes).
    /// </summary>
    public static bool CanStack(Garment a, Garment b)
    {
        if (a.Layer != b.Layer) return true;
        if (a.Slot != b.Slot) return true;
        if (a.Order == b.Order) return false;
        var (upper, lower) = a.Order > b.Order ? (a, b) : (b, a);
        return upper.Def.LayerSize <= lower.Def.LayerPermit;
    }
}
