using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Farming;

public sealed partial class FarmingSystem
{
    private static readonly FarmTask[] ParcelTasks = [FarmTask.PloughSpring, FarmTask.SowSpring, FarmTask.Weed, FarmTask.Reap, FarmTask.BindCart, FarmTask.PloughFallow, FarmTask.SowWinter, FarmTask.Thresh];

    private readonly record struct Need(int Parcel, int Holder, FarmTask Task, int Deadline, int Tier);
    private int _teamHours;
    /// <summary>Running totals of the farm labour market since the start (diagnostics): hours wanted, worked by the holders themselves, hired, left undone, and the wages paid.</summary>
    public long TotalDemandH, TotalOwnH, TotalHiredH, TotalUnmetH, TotalWagesMites;
    private readonly List<Need> _needs = [];
    private readonly List<(int Hh, int Avail)> IdleLeft = [];
    /// <summary>P8-BL-1 (D13): the worker-hours of each household's adults the day's farm work left over (own parcels first, then the labour pool), set by the day's step and spent at once by <see cref="AfterLabour"/>; never saved.</summary>
    public int[] IdleHours { get; private set; } = [];
    /// <summary>Told after each day's labour with the game day: the soil works draw the idle hours of the holders' own households here (the same step in a straight run and a resumed one).</summary>
    public Action<int>? AfterLabour { get; set; }

    private int RateOf(FarmTask t) => t switch
    {
        FarmTask.PloughSpring or FarmTask.PloughFallow => _d.PloughAcresPerWorkerDay,
        FarmTask.SowSpring or FarmTask.SowWinter => _d.SowAcresPerWorkerDay,
        FarmTask.Weed => _d.WeedAcresPerWorkerDay, FarmTask.Reap => _d.ReapAcresPerWorkerDay, FarmTask.BindCart => _d.BindAcresPerWorkerDay,
        FarmTask.Thresh => _d.ThreshAcresPerWorkerDay, FarmTask.Hay => _d.HayAcresPerWorkerDay, _ => 1,
    };

    private int DeadlineOf(FarmTask t, int p)
    {
        var w = Settings.Info(Crop.Wheat); var b = Settings.Info(Crop.Barley);
        return t switch
        {
            FarmTask.PloughSpring or FarmTask.SowSpring => _d.SpringWorkLastDoy, FarmTask.Weed => 25,
            FarmTask.Reap or FarmTask.BindCart => P.Slot[p] == 0 ? w.RipeDoy + w.ReapGraceDays : b.RipeDoy + b.ReapGraceDays,
            FarmTask.PloughFallow or FarmTask.SowWinter => 40, _ => 1000,
        };
    }

    /// <summary>Within a deadline sowing goes before ploughing: ploughed ground waiting for seed is the better use of an hour.</summary>
    private static int TaskRank(FarmTask t) => t is FarmTask.SowSpring or FarmTask.SowWinter ? 0 : 1 + (int)t;

    private bool HasStandingCrop(int p)
    {
        var parcel = Region.Parcels[p];
        for (int k = 0; k < parcel.FieldCount; k++)
        {
            int f = parcel.FieldStart + k;
            if (F.Crop[f] != Crop.Fallow && !F.Has(f, FieldFlags.Lost) && F.Stage[f] is FieldStage.Sown or FieldStage.Growing) return true;
        }
        return false;
    }

    /// <summary>The ground is workable: the 05:00 temperature of the parcel's cell reached the thaw mark (spring work waits for it, the window runs to the end of spring).</summary>
    private bool Thawed(int p) => _tempToday[_parcelCell[p]] >= _d.ThawTenthsC;

    /// <summary>Worker-hours the parcel's task can still absorb today (0 when closed, gated or finished).</summary>
    public long HoursFor(int p, FarmTask task, int doy)
    {
        if (P.Holder[p] < 0) return 0;
        int slot = P.Slot[p], n = Region.Parcels[p].FieldCount, acres; int gate = 1_000_000;
        var sw = Settings.Info(Crop.Wheat); var ss = Settings.Info(Crop.Barley);
        switch (task)
        {
            case FarmTask.PloughSpring: if (slot != 1 || doy < 1 || doy > _d.SpringWorkLastDoy || !Thawed(p)) return 0; acres = n; break;
            case FarmTask.SowSpring: if (slot != 1 || doy < ss.SowFirstDoy || doy > _d.SpringWorkLastDoy || !Thawed(p)) return 0; acres = n; gate = P.Progress(p, FarmTask.PloughSpring); break;
            case FarmTask.Weed: if (slot == 2 || doy < 16 || doy > 25 || !HasStandingCrop(p)) return 0; acres = n; break;
            case FarmTask.Reap: if (slot == 2 || P.HarvestSet[p] == 0 || P.ReapDone[p] >= P.HarvestSet[p]) return 0; acres = P.HarvestSet[p]; break;
            case FarmTask.BindCart: if (slot == 2 || P.HarvestSet[p] == 0 || P.BindDone[p] >= P.HarvestSet[p] || P.ReapDone[p] <= P.BindDone[p]) return 0; acres = P.HarvestSet[p]; gate = P.Progress(p, FarmTask.Reap); break;
            case FarmTask.PloughFallow: if (slot != 2 || doy < 16 || doy > 40) return 0; acres = n; break;
            case FarmTask.SowWinter: if (slot != 2 || doy < sw.SowFirstDoy || doy > sw.SowLastDoy) return 0; acres = n; gate = P.Progress(p, FarmTask.PloughFallow); break;
            case FarmTask.Thresh:
                if (_pendingThresh[p] == 0 || (doy > 25 && doy < 36)) return 0;
                return ((long)_pendingThresh[p] * _wh + RateOf(task) - 1) / RateOf(task);
            default: return 0;
        }
        int remaining = gate - P.Progress(p, task);
        if (remaining <= 0) return 0;
        long rate = RateOf(task);
        return ((long)remaining * acres * _wh + rate * 1_000_000 - 1) / (rate * 1_000_000);
    }

    private void RunLabour(GameTime now)
    {
        int doy = now.DayOfYear; long demand = 0, supply = 0, own = 0, hired = 0, wages = 0;
        bool premium = doy >= 26 && doy <= 35;
        int wagePct = premium ? _d.HarvestWagePct : 100;
        var pool = new List<(int Hh, int Avail)>(); var childPool = new List<(int Hh, int Avail)>();
        RetrySowing(doy, now);
        if (IdleHours.Length != _hhHours.Length) IdleHours = new int[_hhHours.Length]; else Array.Clear(IdleHours);
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            _needs.Clear();
            foreach (int p in Region.Settlements[s].Parcels)
            {
                int holder = P.Holder[p]; if (holder < 0) continue;
                int tier = P.OwnerKind[p] is OwnerKind.Demesne or OwnerKind.Glebe ? 0 : 1;
                foreach (var t in ParcelTasks)
                {
                    long h = HoursFor(p, t, doy);
                    if (h > 0) { _needs.Add(new Need(p, holder, t, DeadlineOf(t, p), tier)); demand += h; }
                }
            }
            long hayHours = HayHours(s, doy); demand += hayHours;
            _teamHours = State.Livestock.Oxen[s] / 2 * _wh;

            // 1. each household works its own parcels, earliest deadline first
            _needs.Sort((a, b) => a.Holder != b.Holder ? a.Holder.CompareTo(b.Holder) : a.Deadline != b.Deadline ? a.Deadline.CompareTo(b.Deadline) : a.Parcel != b.Parcel ? a.Parcel.CompareTo(b.Parcel) : TaskRank(a.Task).CompareTo(TaskRank(b.Task)));
            pool.Clear(); childPool.Clear();
            var left = new Dictionary<int, int>(); var childLeft = new Dictionary<int, int>();
            foreach (int hh0 in _householdsOf[s]) supply += _hhHours[hh0] + _hhChild[hh0];
            for (int i = 0; i < _needs.Count;)
            {
                int holder = _needs[i].Holder; int remaining = _hhHours[holder], childRem = _hhChild[holder];
                for (; i < _needs.Count && _needs[i].Holder == holder; i++)
                {
                    var nd = _needs[i];
                    if (childRem > 0 && nd.Task is FarmTask.Reap or FarmTask.BindCart)          // the children's hours serve the harvest only, and go first (they can do nothing else)
                    {
                        long c = Math.Min(childRem, HoursFor(nd.Parcel, nd.Task, doy));
                        if (c > 0) { Apply(nd.Parcel, nd.Task, c, now); childRem -= (int)c; own += c; }
                    }
                    long h = Math.Min(remaining, HoursFor(nd.Parcel, nd.Task, doy));
                    if (h <= 0) continue;
                    Apply(nd.Parcel, nd.Task, h, now); remaining -= (int)h; own += h;
                }
                left[holder] = remaining; childLeft[holder] = childRem;
            }
            // 2. the labour pool: households' leftover hours and non-holders' hours
            foreach (int hh in _householdsOf[s])
            {
                int avail;
                avail = left.TryGetValue(hh, out int l) ? l : _hhHours[hh];
                if (avail > 0) pool.Add((hh, avail));
                int cav = childLeft.TryGetValue(hh, out int cl) ? cl : _hhChild[hh];
                if (cav > 0) childPool.Add((hh, cav));
            }
            int first = 0, firstChild = 0;                   // pool entries before this are used up
            _needs.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Deadline != b.Deadline ? a.Deadline.CompareTo(b.Deadline) : a.Parcel != b.Parcel ? a.Parcel.CompareTo(b.Parcel) : TaskRank(a.Task).CompareTo(TaskRank(b.Task)));
            foreach (var nd in _needs)
            {
                long want = HoursFor(nd.Parcel, nd.Task, doy);
                int payer = nd.Holder; var payerPurse = Econ.HouseholdPurse[payer];
                bool harvest = nd.Task is FarmTask.Reap or FarmTask.BindCart;
                for (int pass = harvest ? 0 : 1; pass < 2 && want > 0; pass++)                  // a neighbour's children first (harvest tasks only), then the adults
                {
                    var list = pass == 0 ? childPool : pool; ref int fst = ref (pass == 0 ? ref firstChild : ref first);
                    for (int j = fst; j < list.Count && want > 0; j++)
                    {
                        var (hh, avail) = list[j];
                        if (avail <= 0) { if (j == fst) fst++; continue; }
                        if (hh == payer) continue;
                        long take = Math.Min(want, avail);
                        long wage = _d.WageLabourerMitesPerDay;
                        if (wage > 0)
                        {
                            long bal = Econ.Ledger.Balance(payerPurse).Mites, per = wage * wagePct;   // mites x 100 per day
                            long afford = bal * 100 * _wh / per;
                            take = Math.Min(take, afford);
                            if (take <= 0) break;
                            long pay = take * per / (100L * _wh);
                            if (pay > 0 && Econ.Ledger.Transfer(payerPurse, Econ.HouseholdPurse[hh], Coin.FromMites(pay), TransferReason.Wage)) wages += pay;
                        }
                        Apply(nd.Parcel, nd.Task, take, now); hired += take; want -= take;
                        if (P.HiredHands[nd.Parcel] < 8) P.HiredHands[nd.Parcel]++;
                        list[j] = (hh, avail - (int)take);
                    }
                }
            }
            if (hayHours > 0)
            {
                long want = hayHours;
                foreach (var list in new[] { childPool, pool })                                   // haymaking is harvest work: the children first
                    for (int j = 0; j < list.Count && want > 0; j++)
                    {
                        var (hh, avail) = list[j]; if (avail <= 0) continue;
                        long take = Math.Min(want, avail);
                        ApplyHay(s, take); hired += take; want -= take; list[j] = (hh, avail - (int)take);
                    }
            }
            foreach (var (hh, avail) in pool) if (avail > 0) IdleLeft.Add((hh, avail));
        }
        foreach (var (hh, avail) in IdleLeft) if (hh < IdleHours.Length) IdleHours[hh] = avail;
        IdleLeft.Clear();
        long unmet = 0;
        for (int p = 0; p < P.Count; p++) foreach (var t in ParcelTasks) unmet += HoursFor(p, t, doy);
        for (int s = 0; s < Region.Settlements.Count; s++) unmet += HayHours(s, doy);
        LastLabour = new LabourReport(demand, supply, own, hired, unmet, wages);
        AfterLabour?.Invoke((int)now.TotalDays);
        TotalDemandH += demand; TotalOwnH += own; TotalHiredH += hired; TotalUnmetH += unmet; TotalWagesMites += wages;
    }

    /// <summary>A field whose seed was missing when its turn came is tried again on later days of the window (seed may have arrived from the threshing floor).</summary>
    private void RetrySowing(int doy, GameTime now)
    {
        var sp = Settings.Info(Crop.Barley); var sw = Settings.Info(Crop.Wheat);
        bool spring = doy >= sp.SowFirstDoy && doy <= _d.SpringWorkLastDoy, winter = doy >= sw.SowFirstDoy && doy <= sw.SowLastDoy;
        if (!spring && !winter) return;
        for (int p = 0; p < P.Count; p++)
        {
            if (P.Holder[p] < 0) continue;
            if (spring && P.Slot[p] == 1 && P.Progress(p, FarmTask.SowSpring) > 0) Complete(p, FarmTask.SowSpring, now);
            else if (winter && P.Slot[p] == 2 && P.Progress(p, FarmTask.SowWinter) > 0) Complete(p, FarmTask.SowWinter, now);
        }
    }

    /// <summary>Worker-hours wanted and available today across the region, without doing any work: the labour demand and supply the spec asks to expose.</summary>
    public (long DemandHours, long SupplyHours) LabourOutlook(int doy)
    {
        long demand = 0, supply = 0;
        for (int p = 0; p < P.Count; p++) foreach (var t in ParcelTasks) demand += HoursFor(p, t, doy);
        for (int s = 0; s < Region.Settlements.Count; s++) demand += HayHours(s, doy);
        for (int h = 0; h < _hhHours.Length; h++) supply += _hhHours[h] + _hhChild[h];
        return (demand, supply);
    }

    // ---------- crediting work ----------

    private void Apply(int p, FarmTask task, long hoursL, GameTime now)
    {
        int hours = (int)Math.Min(hoursL, int.MaxValue); if (hours <= 0) return;
        int doy = now.DayOfYear, n = Region.Parcels[p].FieldCount, ti = p * ParcelWork.ProgressTasks + (int)task;
        if (task == FarmTask.Thresh)
        {
            P.Carry[ti] += hours * RateOf(task);
            int whole = P.Carry[ti] / _wh; P.Carry[ti] %= _wh;
            Thresh(p, whole);
            return;
        }
        int acres = task is FarmTask.Reap or FarmTask.BindCart ? P.HarvestSet[p] : n;
        int gate = task switch { FarmTask.SowSpring => P.Progress(p, FarmTask.PloughSpring), FarmTask.SowWinter => P.Progress(p, FarmTask.PloughFallow), FarmTask.BindCart => P.Progress(p, FarmTask.Reap), _ => 1_000_000 };
        int ppm;
        if (task is FarmTask.PloughSpring or FarmTask.PloughFallow)
        {
            int team = Math.Min(hours, _teamHours); _teamHours -= team;
            ppm = YieldMath.CreditPpm(_d.PloughAcresPerWorkerDay, team, acres, _wh, ref P.Carry[ti]);
            ppm += YieldMath.CreditPpm(_d.SpadeAcresPerWorkerDay, hours - team, acres, _wh, ref P.Carry[ti]);
        }
        else ppm = YieldMath.CreditPpm(RateOf(task), hours, acres, _wh, ref P.Carry[ti]);
        P.ProgressPpm[ti] = Math.Min(gate, P.ProgressPpm[ti] + ppm);
        Complete(p, task, now);
    }

    /// <summary>Credits worker-hours to a parcel task directly (golden test G-F12 and scripted scenarios); returns the new progress in ppm.</summary>
    public int CreditHours(int parcel, FarmTask task, int hours, GameTime now) { Apply(parcel, task, hours, now); return P.Progress(parcel, task); }

    private void Complete(int p, FarmTask task, GameTime now)
    {
        var parcel = Region.Parcels[p]; int n = parcel.FieldCount, doy = now.DayOfYear, prog = P.Progress(p, task);
        switch (task)
        {
            case FarmTask.PloughSpring:
            case FarmTask.PloughFallow:
            {
                int done = YieldMath.FieldsDone(prog, n);
                for (int k = 0; k < done; k++) { int f = parcel.FieldStart + k; if (F.Stage[f] == FieldStage.Stubble) F.Stage[f] = FieldStage.Ploughed; }
                break;
            }
            case FarmTask.SowSpring:
            case FarmTask.SowWinter:
            {
                bool spring = task == FarmTask.SowSpring;
                int done = YieldMath.FieldsDone(prog, n);
                for (int k = 0; k < done; k++)
                {
                    int f = parcel.FieldStart + k;
                    if (F.Stage[f] != FieldStage.Ploughed) continue;
                    if (!spring) CreditFallow(f);
                    SowField(f, spring, doy);
                }
                break;
            }
            case FarmTask.Weed: P.WeededPpm[p] = prog; break;
            case FarmTask.Reap:
            {
                int target = YieldMath.FieldsDone(prog, P.HarvestSet[p]);
                while (P.ReapDone[p] < target)
                {
                    int f = NextField(parcel, FieldStage.Ripe, false);
                    if (f < 0) { P.ReapDone[p] = P.HarvestSet[p]; break; }
                    CutField(f); P.ReapDone[p]++;
                }
                break;
            }
            case FarmTask.BindCart:
            {
                int target = Math.Min(YieldMath.FieldsDone(prog, P.HarvestSet[p]), P.ReapDone[p]);
                while (P.BindDone[p] < target)
                {
                    int f = NextField(parcel, FieldStage.Cut, true);
                    if (f < 0) { P.BindDone[p] = P.HarvestSet[p]; break; }
                    BindField(f); P.BindDone[p]++;
                }
                break;
            }
        }
    }

    /// <summary>First field of the parcel in the stage (for Cut: not yet bound), in index order; -1 none.</summary>
    private int NextField(Parcel parcel, FieldStage stage, bool unbound)
    {
        for (int k = 0; k < parcel.FieldCount; k++)
        {
            int f = parcel.FieldStart + k;
            if (F.Stage[f] == stage && (!unbound || !F.Has(f, FieldFlags.Bound))) return f;
        }
        return -1;
    }

    // ---------- sowing ----------

    private void CreditFallow(int f)
    {
        if (F.Has(f, FieldFlags.FallowCredited)) return;
        F.Fertility[f] = YieldMath.AddFertility(F.Fertility[f], YieldMath.FallowBonus(F.Has(f, FieldFlags.Grazed), _d.FallowGrazedFertility, _d.FallowUngrazedFertility));
        F.Set(f, FieldFlags.FallowCredited);
    }

    /// <summary>Sows one field from its holder's store: the preferred crop of the stable per-field mix, else another of the kind the seed allows. False when no seed or fertility allows.</summary>
    private bool SowField(int f, bool spring, int doy)
    {
        int p = F.Parcel[f], hh = P.Holder[p]; if (hh < 0) return false;
        var store = HouseStore(hh); int fert = F.Fertility[f]; long yh = YearHour(State.Year); int gm = _d.GramsPerMeasure;
        Crop pref = spring ? PickSpring(Settings, Seed, yh, F.Id[f], fert, State.Year) : PickWinter(Settings, Seed, yh, F.Id[f], fert, State.Year);
        if (pref == Crop.Fallow) { Diagnostics.SowRefusedFertility++; return false; }
        bool anyFertile = false;
        var pool = spring ? FarmingSettings.SpringCrops : FarmingSettings.WinterCrops;
        int start = Array.IndexOf(pool, pref);
        for (int i = 0; i < pool.Length; i++)
        {
            var c = pool[(start + i) % pool.Length]; var info = Settings.Info(c);
            if (fert < info.MinFertility) continue;
            anyFertile = true;
            if (!spring && c == Crop.Wheat && fert < _d.WheatMinFertilityForWinterPick) continue;
            if (spring && fert < _d.LegumeForcedBelowFertility && info.Kind != CropKind.SpringLegume) continue;
            long seed = info.SeedG(gm);
            if (!store.Destroy(info.Good, seed, DestroyReason.Sown)) continue;
            F.Crop[f] = c; F.Stage[f] = FieldStage.Sown; F.SownDay[f] = (short)doy; F.SownFertility[f] = (byte)fert; F.SeedG[f] = (int)seed; F.DamagePermille[f] = 0; F.YieldG[f] = 0;
            F.Clear(f, FieldFlags.Resown); F.Clear(f, FieldFlags.Lost);
            State.SeedSownG += seed; State.SownAcres++; Diagnostics.SownByClass[(int)Pop.Households.Class[hh]]++;
            return true;
        }
        if (anyFertile) { Diagnostics.SowRefusedSeed++; Diagnostics.RefusedByClass[(int)Pop.Households.Class[hh]]++; } else Diagnostics.SowRefusedFertility++;
        return false;
    }

    // ---------- harvest ----------

    private void CutField(int f)
    {
        F.Stage[f] = FieldStage.Cut;
        int rainTenths = (int)(_rainToday[_cellOfField[f]] * 10 >> 16);
        int wet = YieldMath.WetHarvestPermille(rainTenths, _d.WetHarvest6mmPermille, _d.WetHarvest12mmPermille);
        if (wet < 1000)
        {
            long before = F.YieldG[f]; F.YieldG[f] = (int)(before * wet / 1000); State.AddLoss(LossCause.WetHarvest, before - F.YieldG[f], 1);
        }
    }

    /// <summary>Bind and cart: the harvest split of one field (tithe to the parish, rent to the landlord, the rest to the holder's barn). Grain is created once, into the three stores.</summary>
    private void BindField(int f)
    {
        int p = F.Parcel[f], hh = P.Holder[p], s = _settlementOfParcel[p];
        var info = Settings.Info(F.Crop[f]); ushort good = info.Good;
        var lease = P.Landlord[p] >= 0 ? P.Lease[p] : LeaseKind.None;
        var sp = YieldMath.Split(F.YieldG[f], F.SeedG[f], P.OwnerKind[p], lease, P.ShareBp[p], _d.TithePermille);
        // gleaning (integration decision I24): the poor of the village follow the reapers; 3 percent of the holder's share goes into the village's poor box (the parish granary, from which relief is given)
        long glean = sp.HolderG * _d.GleaningPermille / 1000, holderG = sp.HolderG - glean;
        if (sp.TitheG + glean > 0) Parish(s).Create(good, sp.TitheG + glean, CreateReason.Harvest);
        if (sp.RentG > 0) HouseStore(P.Landlord[p]).Create(good, sp.RentG, CreateReason.Harvest);
        if (holderG > 0) Barn(hh).Create(good, holderG, CreateReason.Harvest);
        State.GleanedG += glean;
        F.SheafG[f] = (int)holderG; F.SheafCrop[f] = (byte)F.Crop[f]; F.Set(f, FieldFlags.Bound); _pendingThresh[p]++;
        State.HarvestG += sp.YieldG; State.TitheG += sp.TitheG; State.RentG += sp.RentG;
    }

    private void Thresh(int p, int acres)
    {
        var parcel = Region.Parcels[p]; int hh = P.Holder[p], s = _settlementOfParcel[p];
        for (int k = 0; k < parcel.FieldCount && acres > 0; k++)
        {
            int f = parcel.FieldStart + k;
            if (!F.Has(f, FieldFlags.Bound)) continue;
            ushort good = Settings.Info((Crop)F.SheafCrop[f]).Good; long g = F.SheafG[f];
            if (g > 0)
            {
                g = Store.MoveUpTo(Barn(hh), HouseStore(hh), good, g);                    // the barn has spoiled some of the sheaf since it was bound; thresh what is left
                if (g > 0) { Fodder(s).Create(_straw, g * _d.StrawGPerKgGrain / 1000, CreateReason.Harvest); RepayFromThreshing(hh, good, g); }
            }
            State.ThreshedG += g; F.SheafG[f] = 0; F.Clear(f, FieldFlags.Bound);
            if (F.Stage[f] == FieldStage.Cut) F.Stage[f] = FieldStage.Threshed;
            _pendingThresh[p]--; acres--;
        }
    }

    // ---------- hay (meadows) ----------

    private long HayHours(int s, int doy)
    {
        var lv = State.Livestock;
        if (doy < 16 || doy > 25 || lv.MeadowAcres[s] == 0 || lv.HayPpm[s] >= 1_000_000) return 0;
        long rate = _d.HayAcresPerWorkerDay;
        return ((long)(1_000_000 - lv.HayPpm[s]) * lv.MeadowAcres[s] * _wh + rate * 1_000_000 - 1) / (rate * 1_000_000);
    }

    private void ApplyHay(int s, long hours)
    {
        var lv = State.Livestock; int meadow = lv.MeadowAcres[s];
        int ppm = YieldMath.CreditPpm(_d.HayAcresPerWorkerDay, (int)hours, meadow, _wh, ref lv.HayCarry[s]);
        ppm = Math.Min(ppm, 1_000_000 - lv.HayPpm[s]); lv.HayPpm[s] += ppm;
        int cult = _cultureOfParcel[Region.Settlements[s].Parcels.Count > 0 ? Region.Settlements[s].Parcels[0] : 0] & 255;
        int yieldPermille = _long[cult].Kind == LongKind.Drought ? 580 : 1000;
        long grams = (long)ppm * meadow * _d.HayKgPerMeadowAcre * yieldPermille / 1_000_000;
        if (grams > 0) Fodder(s).Create(_hay, grams, CreateReason.Cut);
    }
}
