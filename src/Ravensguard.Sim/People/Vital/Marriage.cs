using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.People.Vital;

/// <summary>
/// Marriage matching gated on housing (population spec 3.3, the European marriage pattern): per cluster (the settlements of one fief, standing in for "within
/// 5 km by road", decision V2) the proposed women are matched to unmarried men by age gap and class, kin excluded, and a couple weds only when it can be
/// housed: one of them heads a household with a free bed, or a vacant dwelling of the cluster is free. Couples without a home wait (<c>MarriageWait</c>).
/// </summary>
public static class Marriage
{
    /// <summary>One proposal round. <paramref name="seasonal"/>: the S6 round (35 percent of eligible women are proposed to and waiting counters grow); otherwise the weekly catch-up for women already waiting.</summary>
    public static int Run(SimWorld w, GameTime when, RegionOutbox ob, bool seasonal)
    {
        var pop = w.Population; var pt = pop.People; var hht = pop.Households; var rates = w.Rates;
        int day = (int)when.TotalDays, nFief = pop.Region.Fiefs.Count, clusters = nFief + 1;
        pop.ResetKin();                       // births since the last round: the graph is rebuilt, but only if a proposal needs it
        KinGraph? kin = null;
        var women = new List<int>[clusters]; var men = new List<int>[clusters];
        for (int c = 0; c < clusters; c++) { women[c] = []; men[c] = []; }
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.Spouse[p] >= 0) continue;
            int hh = pt.Household[p];
            if (hh < 0 || pt.Homeless[p] == HomelessReason.Vagrant) continue;
            int age = w.AgeOn(p, day), cl = ClusterOf(pop, hht.Settlement[hh]);
            if (pt.Sex[p] == Sex.Female) { if (age >= rates.MarryAgeMinFemale && age <= rates.MarryAgeMaxFemale) women[cl].Add(p); }
            else if (age >= rates.MarryAgeMinMale && age <= rates.MarryAgeMaxMale && pt.Occupation[p] != Occupation.Priest) men[cl].Add(p);
        }
        int weddings = 0;
        for (int c = 0; c < clusters; c++)
        {
            if (women[c].Count == 0 || men[c].Count == 0) continue;
            var couples = Match(w, women[c], men[c], () => kin ??= pop.Kin, when, seasonal, day);
            if (couples.Count == 0) continue;
            couples = couples.OrderBy(x => Math.Abs(x.AgeGap - 3)).ThenBy(x => x.Man).ThenBy(x => x.Woman).ToList();
            List<(int S, int L)>? vacancies = null; int next = 0;
            foreach (var (woman, man, _) in couples)
            {
                if (TryWed(w, woman, man, c, ref vacancies, ref next, when, ob)) weddings++;
                else if (seasonal)
                {
                    if (pt.MarriageWait[woman] < rates.MarriageWaitCap) pt.MarriageWait[woman]++;
                    if (pt.MarriageWait[man] < rates.MarriageWaitCap) pt.MarriageWait[man]++;
                    ob.Count("marriage_waiting");
                }
            }
        }
        return weddings;
    }

    private static int ClusterOf(Population pop, int settlement)
    {
        int f = pop.Region.Settlements[settlement].FiefIndex;
        return f >= 0 && f < pop.Region.Fiefs.Count ? f : pop.Region.Fiefs.Count;
    }

    private readonly record struct Couple(int Woman, int Man, int AgeGap);

    private static List<Couple> Match(SimWorld w, List<int> women, List<int> men, Func<KinGraph> kin, GameTime when, bool seasonal, int day)
    {
        var pt = w.People; var rates = w.Rates;
        var free = new List<int>(men); // ascending
        var res = new List<Couple>();
        foreach (int wo in women)
        {
            bool proposed = pt.MarriageWait[wo] > 0
                || (seasonal && VitalMath.Roll(w.Seed, when.TotalHours, pt.IdOf(wo).Value, VitalStreams.Marriage, 0, rates.MarryChancePctPerSeason * 10_000));
            if (!proposed || free.Count == 0) continue;
            var banned = Kin2(kin(), wo);
            int wAge = w.AgeOn(wo, day), best = -1, bestKey1 = int.MaxValue, bestKey2 = int.MaxValue;
            foreach (int m in free)
            {
                if (banned.Contains(m)) continue;
                int k1 = Math.Abs(w.AgeOn(m, day) - wAge - 3), k2 = Math.Abs((int)pt.Class[m] - (int)pt.Class[wo]);
                if (k1 < bestKey1 || (k1 == bestKey1 && k2 < bestKey2)) { best = m; bestKey1 = k1; bestKey2 = k2; } // ascending scan: the lower index wins ties
            }
            if (best < 0) continue;
            free.Remove(best);
            res.Add(new Couple(wo, best, w.AgeOn(best, day) - wAge));
        }
        return res;
    }

    /// <summary>Parents, siblings, children and first cousins of a person (kin within degree 2 that may not marry).</summary>
    private static HashSet<int> Kin2(KinGraph kin, int p)
    {
        var s = new HashSet<int>();
        foreach (int par in kin.Parents(p))
        {
            s.Add(par);
            foreach (int aunt in kin.Siblings(par)) foreach (int cousin in kin.Children(aunt)) s.Add(cousin);
        }
        foreach (int sib in kin.Siblings(p)) s.Add(sib);
        foreach (int ch in kin.Children(p)) s.Add(ch);
        return s;
    }

    /// <summary>A vacant dwelling with at least <paramref name="beds"/> beds: the household's own village first, else any village of its fief (the first in ascending order); (-1,-1) when none.</summary>
    public static (int S, int L) FreeDwelling(SimWorld w, int settlement, int beds)
    {
        var pop = w.Population; int cluster = ClusterOf(pop, settlement);
        for (int pass = 0; pass < 2; pass++)
            for (int s = pass == 0 ? settlement : 0; s < (pass == 0 ? settlement + 1 : pop.Region.Settlements.Count); s++)
            {
                if (pass == 1 && (s == settlement || ClusterOf(pop, s) != cluster)) continue;
                var lots = pop.Region.Settlements[s].Plan.Lots;
                for (int l = 0; l < lots.Count; l++)
                    if (pop.LotBeds[s][l] >= Math.Max(2, beds) && pop.LotHousehold[s][l] < 0 && lots[l].IsDwelling && lots[l].Dwelling != DwellingType.Manor) return (s, l);
            }
        return (-1, -1);
    }

    private static List<(int S, int L)> Vacancies(SimWorld w, int cluster)
    {
        var pop = w.Population; var res = new List<(int, int)>();
        for (int s = 0; s < pop.Region.Settlements.Count; s++)
        {
            if (ClusterOf(pop, s) != cluster) continue;
            var lots = pop.Region.Settlements[s].Plan.Lots;
            for (int l = 0; l < lots.Count; l++)
                if (pop.LotBeds[s][l] >= 2 && pop.LotHousehold[s][l] < 0 && lots[l].IsDwelling && lots[l].Dwelling != DwellingType.Manor) res.Add((s, l));
        }
        return res;
    }

    private static bool TryWed(SimWorld w, int woman, int man, int cluster, ref List<(int S, int L)>? vacancies, ref int next, GameTime when, RegionOutbox ob)
    {
        int day = (int)when.TotalDays;
        var pt = w.People; var hht = w.Households;
        int wh = pt.Household[woman], mh = pt.Household[man];
        bool wHead = hht.Head[wh] == woman, mHead = hht.Head[mh] == man;
        bool wLone = wHead && hht.MemberCount[wh] == 1, mLone = mHead && hht.MemberCount[mh] == 1;
        if (wh == mh) { Link(w, woman, man, when, ob); return true; }               // housemates: no move needed
        bool wMovable = !wHead || wLone, mMovable = !mHead || mLone;
        // (a) the groom heads a household with a free bed (a widower, a young head): the bride moves in; (b) the other way round
        if (mHead && wMovable && hht.MemberCount[mh] < hht.Beds[mh]) { JoinHousehold(w, woman, wh, mh, wLone, when); Link(w, woman, man, when, ob); return true; }
        if (wHead && mMovable && hht.MemberCount[wh] < hht.Beds[wh]) { JoinHousehold(w, man, mh, wh, mLone, when); Link(w, woman, man, when, ob); return true; }
        // (c) neither heads a household: a vacant dwelling of the cluster (the couple sets up its own hearth)
        if (!wHead && !mHead)
        {
            vacancies ??= Vacancies(w, cluster);
            while (next < vacancies.Count && w.Population.LotHousehold[vacancies[next].S][vacancies[next].L] >= 0) next++;
            if (next < vacancies.Count)
            {
                var (s, l) = vacancies[next++];
                NewHousehold(w, woman, man, wh, mh, s, l, when);
                Link(w, woman, man, when, ob);
                return true;
            }
        }
        // (d) no dwelling free: the couple lives with the groom's family, else the bride's, when a bed is free (the stem family, decision V5)
        if (!mHead && wMovable && (hht.MemberCount[mh] + 1) * 100 <= hht.Beds[mh] * w.Rates.StemJoinMaxOccupancyPct && !HasFertileCouple(w, mh, day)) { JoinHousehold(w, woman, wh, mh, wLone, when); Link(w, woman, man, when, ob); return true; }
        if (!wHead && mMovable && (hht.MemberCount[wh] + 1) * 100 <= hht.Beds[wh] * w.Rates.StemJoinMaxOccupancyPct && !HasFertileCouple(w, wh, day)) { JoinHousehold(w, man, mh, wh, mLone, when); Link(w, woman, man, when, ob); return true; }
        return false;
    }

    /// <summary>True when a married woman of fertile age already lives in the household: a stem family has one young couple at a time (decision V5).</summary>
    private static bool HasFertileCouple(SimWorld w, int hh, int day)
    {
        var pt = w.People; var r = w.Rates;
        foreach (int m in w.Households.Members(hh))
            if (pt.Sex[m] == Sex.Female && pt.Spouse[m] >= 0 && w.AgeOn(m, day) >= r.FertilityAgeMin && w.AgeOn(m, day) <= r.FertilityAgeMax) return true;
        return false;
    }

    private static void Link(SimWorld w, int woman, int man, GameTime when, RegionOutbox ob)
    {
        var pt = w.People;
        pt.Spouse[woman] = man; pt.Spouse[man] = woman; pt.MarriageWait[woman] = 0; pt.MarriageWait[man] = 0;
        var v = w.Vital; v.Marriages++; v.Year(when.Year).Marriages++;
        ob.Count("marriages");
        bool notable = pt.Notable[woman] != EntityId.None || pt.Notable[man] != EntityId.None;
        if (w.Rates.ChronicleEveryEvent || notable)
        {
            int hh = pt.Household[man];
            ob.Chronicle(when, "marriage", [pt.IdOf(woman), pt.IdOf(man)], [new Cause(CauseKind.Other, null, "a wedding: the couple found a home")],
                ("bride", pt.FullName(woman)), ("groom", pt.FullName(man)), ("settlement", w.Population.Region.Settlements[w.Households.Settlement[hh]].Name));
        }
    }

    /// <summary>A person moves into another household (with a dowry in coin when a bride leaves her parents' roof); a lone head's whole household is absorbed.</summary>
    private static void JoinHousehold(SimWorld w, int mover, int from, int into, bool absorb, GameTime when)
    {
        var pt = w.People; var hht = w.Households; var e = w.Economy;
        if (absorb)
        {
            Demography.MovePerson(w, mover, from, into);
            hht.Head[from] = -1;
            Demography.TransferAssets(w, from, into, TransferReason.Inheritance);
            Demography.RelocateParcels(w, from, into);
            Demography.VacateDwelling(w, from);
        }
        else
        {
            Dowry(w, mover, from, into);
            Demography.MovePerson(w, mover, from, into);
        }
        pt.Class[mover] = hht.Class[into];
        _ = e; _ = when;
    }

    private static void Dowry(SimWorld w, int bride, int parents, int target)
    {
        if (w.People.Sex[bride] != Sex.Female) return;
        var e = w.Economy;
        long pct = w.Rates.DowryPct, mites = e.Ledger.Balance(e.HouseholdPurse[parents]).Mites * pct / 100;
        if (mites > 0 && parents != target) e.Ledger.Transfer(e.HouseholdPurse[parents], e.HouseholdPurse[target], Coin.FromMites(mites), TransferReason.Gift);
    }

    private static void NewHousehold(SimWorld w, int woman, int man, int wh, int mh, int s, int l, GameTime when)
    {
        var pt = w.People; var hht = w.Households; var pop = w.Population; var e = w.Economy;
        var lot = pop.Region.Settlements[s].Plan.Lots[l];
        hht.RemoveMember(wh, woman); hht.RemoveMember(mh, man);
        w.Ids.EnsureNext(IdKind.Household, hht.BaseCounter + (ulong)hht.Count);
        var id = w.Ids.Allocate(IdKind.Household);
        int hh = hht.Add(s, l, lot.Dwelling, pop.LotBeds[s][l]);
        if (hht.IdOf(hh) != id) throw new InvalidOperationException("household id out of step with the table");
        hht.Head[hh] = man; hht.Class[hh] = pt.Class[man]; hht.Deed[hh] = DeedKind.Rented; hht.Name[hh] = pt.Family[man];
        hht.AddMember(hh, man); hht.AddMember(hh, woman);
        foreach (int p in new[] { man, woman }) { pt.Household[p] = hh; pt.HomeSettlement[p] = s; pt.HomeLot[p] = (short)l; pt.Bed[p] = -1; pt.Class[p] = hht.Class[hh]; }
        pop.LotHousehold[s][l] = hh;
        e.AddHousehold(hht, hh);
        // dowry in coin from the bride's parents, and a tenth of each good from both families to start a hearth (decision V4)
        Dowry(w, woman, wh, hh);
        int pct = w.Rates.SettleInGoodsPct;
        foreach (int from in new[] { mh, wh })
        {
            var src = e.HouseholdStore[from];
            foreach (var (g, q) in src.Rows()) { long give = q * pct / 100; if (give > 0) Store.Move(src, e.HouseholdStore[hh], g, give); }
        }
        _ = when;
    }
}
