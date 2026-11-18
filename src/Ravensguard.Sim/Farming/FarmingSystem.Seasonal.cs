using Ravensguard.Sim.People;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Farming;

public sealed partial class FarmingSystem
{
    /// <summary>
    /// The seasonal steps farming owns (spec section 4), called at hour 0 of doy 1, 16, 31 and 46: S1 year rollover (rotation, fertility ledger, blight roll,
    /// herd growth), S2 grazing flag and growth stage, S3 at doy 46 coin rent and the winter fodder cull. The first call on the state's own year opens the
    /// year without rotating (the state was built for it).
    /// </summary>
    public void StepSeasonal(GameTime now)
    {
        int doy = now.DayOfYear;
        SyncPopulation();
        if (doy == 1)
        {
            while (State.Year < now.Year) RollYear(now);
            OpenYear(now);
            Shear();
            ReletVacant();
        }
        else if (doy == 16) { SetGrazing(); for (int f = 0; f < F.Count; f++) if (F.Stage[f] == FieldStage.Sown) F.Stage[f] = FieldStage.Growing; UpdateSeedReserve(doy); }
        else if (doy == 31) { LendSeed(winter: true); MakeCheese(); }
        else if (doy == 46) { CollectCoinRent(now); CullHerds(now); }
    }

    // ---------- S1: the re-let (integration decision I24) ----------

    /// <summary>Fields a member is meant to have before a holder is no longer taken for a land-hungry household.</summary>
    public const int ReletFieldsPerMember = 3;
    public long Relets;

    /// <summary>
    /// At the year's start the lord's reeve lets the holdings that fell back to him (no heir, the household left) to the households of the same village that have the
    /// fewest fields for their members, landless ones first, on the village's share rent, until each has <see cref="ReletFieldsPerMember"/> fields a head. Idle land never
    /// sits while a household next to it starves (the farming spec's "re-let", the subsistence fallback of integration decision I24).
    /// </summary>
    private void ReletVacant()
    {
        var hht = Pop.Households; int nh = hht.Count;
        var held = new int[nh];
        for (int p = 0; p < P.Count; p++) if (P.Holder[p] >= 0 && P.Holder[p] < nh) held[P.Holder[p]] += Region.Parcels[P.Holder[p] >= 0 ? p : 0].FieldCount;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            List<int>? vacant = null;
            foreach (int p in Region.Settlements[s].Parcels)
            {
                int h = P.Holder[p]; if (h < 0 || h >= nh || hht.Class[h] != SocialClass.Gentry) continue;
                var kind = Region.Parcels[p].Owner.Kind; if (kind is OwnerKind.Demesne or OwnerKind.Glebe) continue;
                (vacant ??= []).Add(p);
            }
            if (vacant == null) continue;
            var takers = _householdsOf[s].Where(h => hht.Class[h] != SocialClass.Gentry && hht.MemberCount[h] > 0).ToList();
            if (takers.Count == 0) continue;
            int fief = Region.Settlements[s].FiefIndex; int lord = fief >= 0 && fief < Pop.FiefLordHousehold.Length ? Pop.FiefLordHousehold[fief] : -1;
            foreach (int p in vacant)
            {
                int best = -1; long bestScore = long.MaxValue;
                foreach (int h in takers)
                {
                    if (held[h] >= ReletFieldsPerMember * hht.MemberCount[h]) continue;
                    long score = (long)held[h] * 1000 / hht.MemberCount[h];                          // fewest fields a head first, lower index on a tie
                    if (score < bestScore) { bestScore = score; best = h; }
                }
                if (best < 0) break;
                int old = P.Holder[p], fc = Region.Parcels[p].FieldCount;
                P.Holder[p] = best; _popHolder[p] = best; Pop.ParcelHolder[p] = best;
                hht.ParcelCount[old]--; hht.ParcelCount[best]++; held[best] += fc; held[old] -= fc;
                if (lord >= 0 && P.Landlord[p] < 0) { P.Landlord[p] = lord; P.Lease[p] = LeaseKind.Share; P.ShareBp[p] = (short)_d.RentShareBpDefault; }
                Relets++; _holderStale = true;
            }
        }
        if (_holderStale) SyncPopulation();
    }

    // ---------- S1: the year boundary ----------

    private void RollYear(GameTime now)
    {
        CloseYear(now);
        for (int p = 0; p < P.Count; p++)
        {
            P.Slot[p] = (byte)((P.Slot[p] + 1) % 3); P.ResetYear(p);
        }
        for (int f = 0; f < F.Count; f++)
        {
            int old = F.Slot[f];
            if (old == 2) CreditFallow(f);                                   // a fallow year that was never sown still rested the soil
            F.Slot[f] = (byte)((old + 1) % 3);
            bool bound = F.Has(f, FieldFlags.Bound);
            F.Flags[f] = (byte)(bound ? (byte)FieldFlags.Bound : 0);
            F.YieldG[f] = 0; F.DamagePermille[f] = 0;
            if (F.Slot[f] == 0 && F.Crop[f] != Crop.Fallow) F.Stage[f] = FieldStage.Growing;      // winter grain sown last autumn stands
            else { F.Crop[f] = Crop.Fallow; F.Stage[f] = FieldStage.Stubble; F.SeedG[f] = 0; F.SownDay[f] = 0; }
        }
        State.Year++;
        var c = State.Climate;
        Array.Clear(c.FrostKillDay); Array.Clear(c.StormMask);
        var lv = State.Livestock;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            lv.HayPpm[s] = 0; lv.HayCarry[s] = 0; lv.Grazed[s] = false;
            int growth = lv.Sheep[s] * _d.SheepGrowthPermilleYear;
            int add = growth / 1000, rem = growth % 1000;
            if (DetRng.Range(Seed, now.TotalHours, (ulong)s, RngStreams.Herds, 0, 0, 1000) < rem) add++;
            int acres = 0; foreach (int p in Region.Settlements[s].Parcels) acres += Region.Parcels[p].FieldCount;
            int cap = acres / 3 * _d.SheepPerFallowAcreCapX100 / 100;
            int grown = Math.Min(Math.Max(lv.Sheep[s], cap), lv.Sheep[s] + add);
            Slaughtered(s, "sheep", lv.Sheep[s] + add - grown);                 // lambs above the pasture cap go to the butcher
            lv.Sheep[s] = grown;
            HerdYear(s, now.TotalHours);
        }
        RefreshLong(State.Year);
    }

    private void OpenYear(GameTime now)
    {
        RefreshLong(State.Year);
        var c = State.Climate; Array.Clear(c.BlightCulture);
        foreach (var st in Region.Settlements)
        {
            int cult = st.Culture & 255;
            if (c.BlightCulture[cult]) continue;
            c.BlightCulture[cult] = YieldMath.Blight(Seed, now.TotalHours, cult, now.Year, _long[cult].Kind == LongKind.WetYear, _d.BlightPermilleYear, _d.BlightWetYearMult);
        }
        RecountPending();
        LendSeed(winter: false);
        UpdateSeedReserve(1);
    }

    private void RecountPending()
    {
        for (int p = 0; p < P.Count; p++)
        {
            var parcel = Region.Parcels[p]; int n = 0;
            for (int k = 0; k < parcel.FieldCount; k++) if (F.Has(parcel.FieldStart + k, FieldFlags.Bound)) n++;
            _pendingThresh[p] = n;
        }
    }

    // ---------- the closing of a year: summary and chronicle ----------

    private void CloseYear(GameTime now)
    {
        long unharv = 0;
        for (int f = 0; f < F.Count; f++)
        {
            bool uncut = F.Stage[f] == FieldStage.Ripe || (F.Stage[f] == FieldStage.Cut && !F.Has(f, FieldFlags.Bound));
            if (!uncut) continue;
            unharv++; State.AddLoss(LossCause.Overdue, F.YieldG[f], 1); F.YieldG[f] = 0;
        }
        State.UnharvestedAcres += unharv;
        var sum = new YearSummary(State.Year, State.HarvestG, State.SeedSownG, State.ThreshedG, State.TitheG, State.RentG, State.SownAcres, State.LostAcres.Sum(), State.UnharvestedAcres,
            State.Gross0G, (long[])State.LostG.Clone(), (int[])State.LostAcres.Clone());
        State.Summaries.Add(sum);
        WriteChronicle(now, sum);
        State.HarvestG = State.SeedSownG = State.ThreshedG = State.TitheG = State.RentG = State.Gross0G = State.SownAcres = State.UnharvestedAcres = 0;
        Array.Clear(State.LostG); Array.Clear(State.LostAcres);
    }

    private void WriteChronicle(GameTime now, YearSummary sum)
    {
        if (sum.Gross0G <= 0) return;
        var c = State.Climate;
        foreach (var cause in Enum.GetValues<LossCause>())
        {
            long lost = sum.LostG[(int)cause];
            if (lost * 100 <= sum.Gross0G * 2) continue;                      // the spec's threshold: more than 2% of the region's yield
            string note = cause switch
            {
                LossCause.Drought => "drought: the spring rains failed",
                LossCause.Frost => $"late frost on day {FirstFrostDay(c)}",
                LossCause.Hail => "hail flattened the standing crop",
                LossCause.Blight => "blight in the grain",
                LossCause.ColdSummer => "a cold summer",
                LossCause.Winterkill => "winterkill: hard frost without snow cover",
                LossCause.WetHarvest => "wet harvest: grain rotted in the sheaf",
                LossCause.Overdue => "harvest overdue: too few hands",
                LossCause.Waterlogged => "a wet year: waterlogged fields", LossCause.Trampling => "trampling by armies", LossCause.Fire => "fields burned", _ => "poor weeding",
            };
            if (cause == LossCause.Weeds) continue;
            var kind = cause switch { LossCause.Trampling => CauseKind.Raid, LossCause.Fire => CauseKind.Fire, LossCause.Overdue => CauseKind.Other, _ => CauseKind.Weather };
            Chronicle.Add(new ChronicleEntry(NewEvent(), now, "farm.yield_loss", [], [new Cause(kind, null, note)],
                new Dictionary<string, string> { ["cause"] = cause.ToString(), ["year"] = sum.Year.ToString(), ["acres"] = sum.LostAcresByCause[(int)cause].ToString(), ["lost_g"] = lost.ToString(), ["of_yield_permille"] = (lost * 1000 / sum.Gross0G).ToString() }));
        }
        if (sum.UnharvestedAcres > 0)
            Chronicle.Add(new ChronicleEntry(NewEvent(), now, "farm.unharvested", [], [new Cause(CauseKind.Famine, null, "fields unharvested")],
                new Dictionary<string, string> { ["year"] = sum.Year.ToString(), ["acres"] = sum.UnharvestedAcres.ToString() }));
    }

    private static int FirstFrostDay(CellCropClimate c) { int d = 99; foreach (byte k in c.FrostKillDay) if (k > 0 && k < d) d = k; return d == 99 ? 0 : d; }

    // ---------- seed reserve ----------

    /// <summary>
    /// Grams of seed every household must still keep for its next sowing of each kind: winter grain for its fallow fields (and next year's, the fields now in spring
    /// grain), spring grain for the fields about to be sown (and next year's, the fields now in winter grain). The household eating algorithm keeps this out of the
    /// pot (spec 3.5). Indexed by household.
    /// </summary>
    public (long[] Winter, long[] Spring) SeedNeeds()
    {
        int nh = Pop.Households.Count; var win = new long[nh]; var spr = new long[nh];
        long sp = Settings.Info(Crop.Barley).SeedG(_d.GramsPerMeasure), wi = Settings.Info(Crop.Wheat).SeedG(_d.GramsPerMeasure);
        for (int p = 0; p < P.Count; p++)
        {
            int hh = P.Holder[p]; if (hh < 0) continue;
            var parcel = Region.Parcels[p]; int slot = P.Slot[p], n = parcel.FieldCount, unsown = 0;
            for (int k = 0; k < n; k++) if (F.Crop[parcel.FieldStart + k] == Crop.Fallow) unsown++;
            if (slot == 0) spr[hh] += n * sp;                                   // next spring's seed
            else if (slot == 1) { win[hh] += n * wi; spr[hh] += unsown * sp; }  // next autumn's seed; this spring's still unsown
            else win[hh] += unsown * wi;                                        // this autumn's
        }
        return (win, spr);
    }

    public long SeedNeedG(int household, bool winter) { var (w, s) = SeedNeeds(); return winter ? w[household] : s[household]; }

    /// <summary>Sets each holder's seed reserve (total grams, kept out of eating) to the seed of the sowings still ahead.</summary>
    private void UpdateSeedReserve(int doy)
    {
        var (w, s) = SeedNeeds();
        for (int h = 0; h < w.Length; h++) if (_isHolder[h] || Econ.SeedReserveG[h] != 0) Econ.SeedReserveG[h] = (int)Math.Min(int.MaxValue, w[h] + s[h]);
    }

    /// <summary>
    /// World start: creates (reason Seed) each holder's seed for the spring sowing and for the autumn sowing of its fallow fields, which the previous harvest would
    /// have left in the barn. Run once before the first step.
    /// </summary>
    public void ProvisionInitialStock()
    {
        int gm = _d.GramsPerMeasure; long yh = YearHour(State.Year);
        for (int p = 0; p < P.Count; p++)
        {
            int hh = P.Holder[p]; if (hh < 0) continue;
            var parcel = Region.Parcels[p]; int slot = P.Slot[p]; if (slot == 0) continue;
            var store = HouseStore(hh);
            for (int k = 0; k < parcel.FieldCount; k++)
            {
                int f = parcel.FieldStart + k; int fert = F.Fertility[f];
                var crop = slot == 1 ? PickSpring(Settings, Seed, yh, F.Id[f], fert, State.Year) : PickWinter(Settings, Seed, yh, F.Id[f], Math.Min(100, fert + _d.FallowGrazedFertility), State.Year);
                if (crop == Crop.Fallow) continue;
                var info = Settings.Info(crop); store.Create(info.Good, info.SeedG(gm), CreateReason.Seed);
            }
        }
    }

    /// <summary>
    /// World start: the previous harvest's grain still in the barns (reason Seed). Fields now in the winter-grain slot (slot 1 after the rotation) were last year's
    /// winter grain, fields now fallow (slot 2) last year's spring grain; each gives <paramref name="pct"/> percent of its average yield, split like a real harvest
    /// (tithe to the parish granary, rent to the landlord, the rest to the holder's house store, threshed). Run once, after <see cref="ProvisionInitialStock"/>.
    /// </summary>
    public void ProvisionCarryOver(int pct)
    {
        int gm = _d.GramsPerMeasure; long yh = YearHour(State.Year - 1);
        for (int p = 0; p < P.Count; p++)
        {
            int hh = P.Holder[p]; if (hh < 0) continue;
            int slot = P.Slot[p]; if (slot == 0) continue;
            var parcel = Region.Parcels[p]; int s = _settlementOfParcel[p];
            var lease = P.Landlord[p] >= 0 ? P.Lease[p] : LeaseKind.None;
            for (int k = 0; k < parcel.FieldCount; k++)
            {
                int f = parcel.FieldStart + k; int fert = F.Fertility[f];
                var crop = slot == 1 ? PickWinter(Settings, Seed, yh, F.Id[f], fert, State.Year - 1) : PickSpring(Settings, Seed, yh, F.Id[f], fert, State.Year - 1);
                if (crop == Crop.Fallow) continue;
                var info = Settings.Info(crop);
                long yield = YieldMath.Gross0G(info.BaseG(gm), fert) * pct / 100;
                var sp = YieldMath.Split(yield, info.SeedG(gm) * pct / 100, P.OwnerKind[p], lease, P.ShareBp[p], _d.TithePermille);
                if (sp.TitheG > 0) Parish(s).Create(info.Good, sp.TitheG, CreateReason.Seed);
                if (sp.RentG > 0) HouseStore(P.Landlord[p]).Create(info.Good, sp.RentG, CreateReason.Seed);
                if (sp.HolderG > 0) HouseStore(hh).Create(info.Good, sp.HolderG, CreateReason.Seed);
            }
        }
    }

    // ---------- seed loans (spec 7) ----------

    private static readonly Crop[] AllGrains = [Crop.Wheat, Crop.Rye, Crop.Barley, Crop.Oats, Crop.Peas, Crop.Beans];

    /// <summary>Who lends a holder seed and takes its rent: the landlord of its first parcel when it has one, otherwise the parish granary of its settlement.</summary>
    private Store LenderOf(int household)
    {
        for (int p = 0; p < P.Count; p++)
            if (P.Holder[p] == household) return P.Landlord[p] >= 0 ? HouseStore(P.Landlord[p]) : Parish(_settlementOfParcel[p]);
        return Parish(Pop.Households.Settlement[household]);
    }

    /// <summary>
    /// S1 (spring grain) and S3 (winter grain at doy 31): a holder short of seed for the fields it has yet to sow of that kind borrows the shortfall from its lord or the
    /// parish, repayable next harvest with 20% on top. Takes whatever grain of the right kind the lender holds, in crop order.
    /// </summary>
    public void LendSeed(bool winter)
    {
        int gm = _d.GramsPerMeasure; var kinds = winter ? FarmingSettings.WinterCrops : FarmingSettings.SpringCrops;
        var need = new long[Pop.Households.Count];
        for (int p = 0; p < P.Count; p++)
        {
            int hh = P.Holder[p]; if (hh < 0 || P.Slot[p] != (winter ? 2 : 1)) continue;
            var parcel = Region.Parcels[p];
            for (int k = 0; k < parcel.FieldCount; k++) if (F.Crop[parcel.FieldStart + k] == Crop.Fallow) need[hh] += Settings.Info(kinds[0]).SeedG(gm);
        }
        for (int h = 0; h < need.Length; h++)
        {
            if (need[h] == 0) continue;
            var own = HouseStore(h); long have = 0;
            foreach (var c in kinds) have += own.Qty(Settings.Info(c).Good);
            long short_ = need[h] - have; if (short_ <= 0) continue;
            var lender = LenderOf(h); long got = 0;
            foreach (var c in kinds)
            {
                if (got >= short_) break;
                ushort g = Settings.Info(c).Good;
                if (ReferenceEquals(lender, own)) break;
                got += Store.MoveUpTo(lender, own, g, short_ - got);
            }
            State.SeedDebtG[h] += got * 120 / 100;
        }
        ExchangeSeed(winter, need);
        BuySeedAtMarket(winter, need);
    }

    /// <summary>Set by the integration layer: buys up to a quantity of a good for a household at its market and returns what it got (coin through the ledger).</summary>
    public Func<int, ushort, long, long>? BuyFromMarket { get; set; }
    public long SeedBoughtG;

    /// <summary>
    /// What neighbours cannot spare, a holder with coin buys at the village market (integration decision I24): the seed corn is the one purchase a farmer makes before his own
    /// bread, so a household that still holds coin does not leave its fields unsown. Crops in sowing order, whichever the book holds.
    /// </summary>
    private void BuySeedAtMarket(bool winter, long[] need)
    {
        if (BuyFromMarket == null) return;
        var kinds = winter ? FarmingSettings.WinterCrops : FarmingSettings.SpringCrops;
        for (int h = 0; h < need.Length; h++)
        {
            if (need[h] == 0) continue;
            var own = HouseStore(h); long have = 0;
            foreach (var c in kinds) have += own.Qty(Settings.Info(c).Good);
            long lack = need[h] - have; if (lack <= 0 || Econ.Ledger.Balance(Econ.HouseholdPurse[h]).Mites <= 0) continue;
            foreach (var c in kinds)
            {
                if (lack <= 0) break;
                long got = BuyFromMarket(h, Settings.Info(c).Good, lack);
                lack -= got; SeedBoughtG += got;
            }
        }
    }

    /// <summary>
    /// What the lord cannot lend, a holder buys from neighbours with seed to spare (a stand-in for the village market until the market system lands): within the
    /// settlement, from the household with the most surplus first, at the good's base price, paid through the ledger (reason Trade).
    /// </summary>
    private void ExchangeSeed(bool winter, long[] need)
    {
        var kinds = winter ? FarmingSettings.WinterCrops : FarmingSettings.SpringCrops;
        var cat = Econ.Goods;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            var hs = _householdsOf[s]; List<(int Hh, long Surplus)>? donors = null;
            foreach (int h in hs)
            {
                if (need[h] == 0) continue;
                var own = HouseStore(h); long have = 0;
                foreach (var c in kinds) have += own.Qty(Settings.Info(c).Good);
                long lack = need[h] - have; if (lack <= 0) continue;
                donors ??= hs.Select(d => { long st = 0; foreach (var c in kinds) st += HouseStore(d).Qty(Settings.Info(c).Good); return (d, st - need[d]); }).Where(t => t.Item2 > 0).OrderByDescending(t => t.Item2).ThenBy(t => t.d).ToList();
                for (int i = 0; i < donors.Count && lack > 0; i++)
                {
                    var (dh, sur) = donors[i]; if (sur <= 0 || dh == h) continue;
                    foreach (var c in kinds)
                    {
                        if (lack <= 0) break;
                        ushort g = Settings.Info(c).Good;
                        long price = cat.BasePriceMilliMites(g);                                       // milli-mites per kg
                        long want = Math.Min(Math.Min(lack, sur), HouseStore(dh).Qty(g));
                        long afford = price == 0 ? want : Econ.Ledger.Balance(Econ.HouseholdPurse[h]).Mites * 1_000_000 / price;
                        want = Math.Min(want, afford);
                        if (want <= 0) continue;
                        long moved = Store.MoveUpTo(HouseStore(dh), HouseStore(h), g, want);
                        long pay = moved * price / 1_000_000;
                        if (pay > 0) Econ.Ledger.Transfer(Econ.HouseholdPurse[h], Econ.HouseholdPurse[dh], Coin.FromMites(pay), TransferReason.Trade);
                        lack -= moved; sur -= moved; donors[i] = (dh, sur);
                    }
                }
            }
        }
    }

    /// <summary>A holder with a seed debt pays it from the grain it has just threshed, in kind, before the grain reaches its own stock (spec 7: repaid next harvest).</summary>
    private void RepayFromThreshing(int household, ushort good, long threshedG)
    {
        long debt = State.SeedDebtG[household]; if (debt <= 0 || threshedG <= 0) return;
        var own = HouseStore(household); var lender = LenderOf(household); if (ReferenceEquals(lender, own)) return;
        long moved = Store.MoveUpTo(own, lender, good, Math.Min(debt, threshedG));
        State.SeedDebtG[household] = debt - moved;
    }

    // ---------- S3: coin rent (spec 3.4, 3.5) ----------

    private void CollectCoinRent(GameTime now)
    {
        for (int p = 0; p < P.Count; p++)
        {
            if (P.Lease[p] != LeaseKind.Coin) continue;
            int hh = P.Holder[p], ll = P.Landlord[p]; if (hh < 0 || ll < 0 || hh == ll) continue;
            long due = P.CoinArrears[p] + (long)P.CoinMitesPerFieldYear[p] * Region.Parcels[p].FieldCount;
            long pay = Math.Min(due, Econ.Ledger.Balance(Econ.HouseholdPurse[hh]).Mites);
            if (pay > 0 && Econ.Ledger.Transfer(Econ.HouseholdPurse[hh], Econ.HouseholdPurse[ll], Coin.FromMites(pay), TransferReason.Rent)) due -= pay;
            P.CoinArrears[p] = due;
        }
    }
}
