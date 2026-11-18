using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Farming;

public sealed partial class FarmingSystem
{
    /// <summary>
    /// The daily step at hour 0 (H5, then the day's labour credit): weather accumulation, late frost, hail, ripening and overdue loss, labour assignment and
    /// credit, harvest splits of finished fields, winter fodder. Order inside the day is the spec's (section 4).
    /// </summary>
    public void StepDaily(GameTime now)
    {
        int doy = now.DayOfYear; long day = now.TotalDays; long hourKey = now.TotalHours;
        if (doy == 46) Array.Clear(State.Climate.WinterBadDays);
        SyncPopulation();
        RecomputeHours((int)day);                                   // ages move on: the same state always yields the same capacity
        AccumulateWeather(doy, day);
        if (doy >= 3 && doy <= 20) Frost(doy, hourKey);
        if (doy >= 16 && doy <= 30) Hail(doy, hourKey);
        if (doy == Settings.Info(Crop.Wheat).RipeDoy) Ripen(0, doy, hourKey);
        if (doy == Settings.Info(Crop.Barley).RipeDoy) Ripen(1, doy, hourKey);
        Overdue(doy);
        if (doy is 38 or 41 or 44) LendSeed(winter: true);        // new grain has been threshed: neighbours with seed to spare can sell it
        else if (doy is 6 or 10) LendSeed(winter: false);
        RunLabour(now);
        if (doy >= 46) FeedHerds();
        _lastDoy = doy;
    }

    // ---------- weather accumulation (spec 2.3, 3.3) ----------

    /// <summary>
    /// Rain ratio (permille of normal) of a growth window for a culture-year, from its long condition (spec 3.3, calibrated; see the farming spec notes): a drought
    /// covering at least 40% of the window gives the spec's 0.3, less coverage eases it, a wet year gives 1.5, anything else is normal.
    /// </summary>
    public static int RainRatioPermille(LongCondition lc, int first, int last, FarmingSettingsDef d)
    {
        if (lc.Kind == LongKind.WetYear) return d.WetYearRatioPermille;
        if (lc.Kind != LongKind.Drought) return 1000;
        int overlap = Math.Max(0, Math.Min(last, lc.EndDay) - Math.Max(first, lc.StartDay) + 1), len = last - first + 1;
        int sev = Math.Min(1000, overlap * 1000 / len * 1000 / Math.Max(1, d.DroughtFullCoverPermille));
        return 1000 - (1000 - d.DroughtRatioPermille) * sev / 1000;
    }

    private int WinterRatio(int culture) => RainRatioPermille(_long[culture], 1, 25, _d);
    private int SpringRatio(int culture) => RainRatioPermille(_long[culture], 6, 30, _d);

    private void AccumulateWeather(int doy, long day)
    {
        var c = State.Climate;
        bool temp = doy <= 20 || doy >= 46;
        bool storm = doy >= 16 && doy <= 30;
        foreach (int cell in _usedCells)
        {
            int cc = CellContinent(cell);
            if (doy >= 26) _rainToday[cell] = Weather.DailyRainRaw(cc, day);        // the harvest days: rain rots the cut grain
            _tempToday[cell] = temp ? Weather.TempAt5Tenths(cc, day) : int.MinValue;
            if ((doy >= 46 || doy <= 10) && _tempToday[cell] < _d.WinterkillTenthsC && Weather.SnowDepthCm(cc, day) < _d.WinterkillSnowMinCm && c.WinterBadDays[cell] < 255) c.WinterBadDays[cell]++;
            _stormToday[cell] = storm && Weather.Thunderstorm(cc, day);
            if (_stormToday[cell]) c.StormMask[cell] |= 1UL << (doy - 1);
        }
    }

    // ---------- late frost (spec 3.3, F7) ----------

    private void Frost(int doy, long hourKey)
    {
        var c = State.Climate; int gm = _d.GramsPerMeasure;
        foreach (int cell in _usedCells)
        {
            if (_tempToday[cell] >= _d.LateFrostTenthsC) continue;
            for (int k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
            {
                int f = _cellFields[k];
                if (F.Slot[f] != 1 || F.Crop[f] == Crop.Fallow || F.Has(f, FieldFlags.Lost)) continue;
                if (F.Stage[f] is not (FieldStage.Sown or FieldStage.Growing)) continue;
                if (F.SownDay[f] < doy - _d.FrostKillWindowDays) continue;
                if (c.FrostKillDay[cell] == 0) c.FrostKillDay[cell] = (byte)doy;                 // the day frost first hurt a sown field here
                var info = Settings.Info(F.Crop[f]);
                long potential = YieldMath.Gross0G(info.BaseG(gm), F.SownFertility[f]);
                int p = F.Parcel[f], holder = P.Holder[p];
                if (doy <= _d.ResowLastDoy && !F.Has(f, FieldFlags.Resown) && holder >= 0 && HouseStore(holder).Destroy(info.Good, info.SeedG(gm), DestroyReason.Sown))
                {
                    F.Set(f, FieldFlags.Resown); F.SownDay[f] = (short)doy; State.SeedSownG += info.SeedG(gm);
                    State.AddLoss(LossCause.Frost, potential * (1000 - _d.ResowYieldPermille) / 1000, 0);
                }
                else
                {
                    F.Set(f, FieldFlags.Lost); F.YieldG[f] = 0; State.AddLoss(LossCause.Frost, potential, 1);
                }
            }
        }
    }

    // ---------- hail (spec 3.3, G-F18) ----------

    private void Hail(int doy, long hourKey)
    {
        for (int p = 0; p < P.Count; p++)
        {
            if (!_stormToday[_parcelCell[p]]) continue;
            if (!YieldMath.HailHit(Seed, hourKey, Region.Parcels[p].Id, doy, _d.HailChancePermillePerStormDay)) continue;
            var parcel = Region.Parcels[p];
            for (int k = 0; k < parcel.FieldCount; k++)
            {
                int f = parcel.FieldStart + k;
                if (F.Crop[f] == Crop.Fallow || F.Has(f, FieldFlags.Lost) || F.Stage[f] >= FieldStage.Cut) continue;
                if (F.Stage[f] == FieldStage.Ripe)
                {
                    long before = F.YieldG[f]; F.YieldG[f] = (int)(before * (1000 - _d.HailDamagePermille) / 1000);
                    State.AddLoss(LossCause.Hail, before - F.YieldG[f], 1);
                }
                else F.DamagePermille[f] = (short)Math.Min(1000, F.DamagePermille[f] + _d.HailDamagePermille);
            }
        }
    }

    // ---------- ripening: the yield is fixed (spec 3.4) ----------

    private void Ripen(int slot, int doy, long hourKey)
    {
        int gm = _d.GramsPerMeasure; var c = State.Climate; int year = State.Year;
        for (int p = 0; p < P.Count; p++)
        {
            if (P.Slot[p] != slot) continue;
            var parcel = Region.Parcels[p]; int cult = _cultureOfParcel[p] & 255;
            bool cold = _long[cult].Kind == LongKind.ColdSummer, blight = c.BlightCulture[cult];
            int weed = YieldMath.WeedFactorPermille(P.WeededPpm[p], _d.WeedingMaxLossPermille);
            int cell = _parcelCell[p];
            int harvestable = 0;
            for (int k = 0; k < parcel.FieldCount; k++)
            {
                int f = parcel.FieldStart + k;
                if (F.Crop[f] == Crop.Fallow || F.Has(f, FieldFlags.Lost) || F.Stage[f] is not (FieldStage.Sown or FieldStage.Growing)) continue;
                var info = Settings.Info(F.Crop[f]); bool winter = info.Kind == CropKind.WinterGrain;
                cell = _cellOfField[f];
                long baseG = info.BaseG(gm);
                long g0 = YieldMath.Gross0G(baseG, F.SownFertility[f]);
                int ratio = winter ? WinterRatio(cult) : SpringRatio(cult);
                int rain = YieldMath.RainFactorPermille(ratio);
                int coldF = cold ? (winter ? _d.ColdSummerWinterPermille : _d.ColdSummerSpringPermille) : 1000;
                int wk = winter && c.WinterBadDays[cell] >= _d.WinterkillDays ? 1000 - _d.WinterkillLossPermille : 1000;
                int bl = blight ? _d.BlightYieldPermille : 1000;
                int rs = F.Has(f, FieldFlags.Resown) ? _d.ResowYieldPermille : 1000;
                int wx = YieldMath.Combine(rain, coldF, wk, bl, weed, rs);
                int var = YieldMath.VariancePermille(Seed, hourKey, F.Id[f], _d.VarianceSpreadPermille);
                long gross = YieldMath.GrossG(baseG, F.SownFertility[f], wx, var, F.DamagePermille[f]);
                // attribute the loss to its causes (each against the undamaged base yield; overlaps are second order)
                void Attr(LossCause cause, int factor) { if (factor < 1000) State.AddLoss(cause, g0 * (1000 - factor) / 1000, 1); }
                Attr(ratio > 1000 ? LossCause.Waterlogged : LossCause.Drought, rain); Attr(LossCause.ColdSummer, coldF); Attr(LossCause.Winterkill, wk); Attr(LossCause.Blight, bl); Attr(LossCause.Weeds, weed);
                Attr(LossCause.Hail, 1000 - F.DamagePermille[f]);
                F.YieldG[f] = (int)Math.Min(gross, int.MaxValue); F.Stage[f] = FieldStage.Ripe; State.Gross0G += g0; harvestable++;
                F.Fertility[f] = YieldMath.AddFertility(F.Fertility[f], info.FertilityDelta);
            }
            P.HarvestSet[p] = harvestable;
        }
    }

    // ---------- overdue (spec 3.5, F8) ----------

    private void Overdue(int doy)
    {
        var w = Settings.Info(Crop.Wheat); var b = Settings.Info(Crop.Barley);
        bool winterLate = doy >= w.RipeDoy + w.ReapGraceDays, springLate = doy >= b.RipeDoy + b.ReapGraceDays;
        if (!winterLate && !springLate) return;
        for (int f = 0; f < F.Count; f++)
        {
            if (F.Stage[f] != FieldStage.Ripe) continue;
            if (!(F.Slot[f] == 0 ? winterLate : springLate)) continue;
            long before = F.YieldG[f]; F.YieldG[f] = (int)YieldMath.Overdue(before, _d.OverdueLossPermillePerDay);
            State.AddLoss(LossCause.Overdue, before - F.YieldG[f], 0);
        }
    }

    // ---------- extra damage channel (armies, fire; spec 3.3) ----------

    /// <summary>Adds damage (trampling +300 per day, say) to a field; fire sets <see cref="FieldFlags.Burned"/> and zeroes the yield. Standing crops only.</summary>
    public void AddDamage(int field, int permille, LossCause cause)
    {
        if (F.Crop[field] == Crop.Fallow || F.Stage[field] >= FieldStage.Cut) return;
        if (cause == LossCause.Fire)
        {
            F.Set(field, FieldFlags.Burned); F.Set(field, FieldFlags.Lost);
            State.AddLoss(LossCause.Fire, F.YieldG[field], 1); F.YieldG[field] = 0; return;
        }
        if (F.Stage[field] == FieldStage.Ripe)
        {
            long before = F.YieldG[field]; F.YieldG[field] = (int)(before * (1000 - Math.Clamp(permille, 0, 1000)) / 1000); State.AddLoss(cause, before - F.YieldG[field], 1);
        }
        else F.DamagePermille[field] = (short)Math.Min(1000, F.DamagePermille[field] + permille);
    }
}
