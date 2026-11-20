using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.People.Vital;

/// <summary>
/// The state changes of population spec 3: births, deaths and what a death sets in motion (head succession, guardianship, inheritance, escheat). All of it
/// goes through the person and household tables, the <see cref="Ledger"/> (coin) and <see cref="Store.Move"/> (goods), so coin and goods are conserved by
/// construction. Single-writer: called from the worker's hour step or from the command pump at a barrier.
/// </summary>
public static class Demography
{
    // ------------------------------------------------------------------ deaths

    /// <summary>Kills a living person: marks the row, widows the spouse, takes the person out of the household and runs succession (spec 3.1, 3.8).</summary>
    public static void Kill(SimWorld w, int p, DeathCause cause, GameTime when, RegionOutbox ob, string note = "")
    {
        var pt = w.People;
        if (pt.Status[p] != PersonStatus.Alive) return;
        long day = when.TotalDays;
        int age = PersonTable.AgeYearsOf(pt.BirthDay[p], day, pt.AgingPace);
        int hh = pt.Household[p];
        int settlement = hh >= 0 ? w.Households.Settlement[hh] : pt.HomeSettlement[p];
        pt.Status[p] = PersonStatus.Dead; pt.DeathDay[p] = (int)day; pt.DeathCause[p] = cause; pt.PregnantUntilDay[p] = -1; pt.FatherOfUnborn[p] = -1;
        var v = w.Vital;
        v.Alive--; v.DeathsByCause[(int)cause]++; v.Year(when.Year).Deaths++;
        if (cause == DeathCause.Hunger && hh >= 0) v.HungerBySegment[(age < 5 ? 0 : age < 15 ? 1 : age < 50 ? 2 : 3) * 8 + (int)w.Households.Class[hh] * 2 + (w.Households.ParcelCount[hh] > 0 ? 1 : 0)]++;
        ob.Count("deaths");
        int sp = pt.Spouse[p];
        if (sp >= 0 && pt.Spouse[sp] == p) pt.Spouse[sp] = -1;           // the partner is widowed; the dead keep the link as a record
        ChronicleDeath(w, p, cause, age, settlement, when, ob, note);
        ReleaseOffice(w, p);
        if (hh >= 0 && w.Households.RemoveMember(hh, p)) MemberLeft(w, hh, p, when, ob);
    }

    private static void ChronicleDeath(SimWorld w, int p, DeathCause cause, int age, int settlement, GameTime when, RegionOutbox ob, string note)
    {
        var pt = w.People; var v = w.Vital; var rates = w.Rates;
        bool notable = pt.Notable[p] != EntityId.None;
        bool severe = cause is DeathCause.Hunger or DeathCause.Cold or DeathCause.Violence or DeathCause.Disease or DeathCause.BloodLoss or DeathCause.Organ or DeathCause.Brain or DeathCause.Infection or DeathCause.Suffocation or DeathCause.Ischaemia;
        if (!(rates.ChronicleEveryEvent || notable || severe)) return;
        if (!(rates.ChronicleEveryEvent || notable))
        {
            long day = when.TotalDays;
            if (v.ChronicleDay != day) { v.ChronicleDay = day; v.ChronicleCount = 0; }
            if (v.ChronicleCount >= rates.ChronicleDeathsPerDay) return;
            v.ChronicleCount++;
        }
        EntityId? ev = null;
        if (settlement >= 0)
        {
            if (cause == DeathCause.Hunger && v.FamineEntry[settlement] != 0) ev = new EntityId(v.FamineEntry[settlement]);
            if (cause == DeathCause.Disease && v.OutbreakEntry[settlement] != 0) ev = new EntityId(v.OutbreakEntry[settlement]);
        }
        Cause c = cause switch
        {
            DeathCause.Age => new Cause(CauseKind.Death, null, "old age"),
            DeathCause.Hunger => new Cause(CauseKind.Famine, ev, "starvation: the nutrition reserve ran out"),
            DeathCause.Disease => new Cause(CauseKind.Disease, ev, ev.HasValue ? "outbreak" : "fever and flux"),
            DeathCause.Cold => new Cause(CauseKind.Weather, null, "cold: no firewood in a freezing spell"),
            DeathCause.Violence => new Cause(CauseKind.Wound, null, note.Length > 0 ? note : "violence"),
            DeathCause.BloodLoss => new Cause(CauseKind.Wound, null, note.Length > 0 ? note : "bled to death"),
            DeathCause.Organ => new Cause(CauseKind.Wound, null, note.Length > 0 ? note : "a wound to a vital organ"),
            DeathCause.Brain => new Cause(CauseKind.Wound, null, note.Length > 0 ? note : "a wound to the brain"),
            DeathCause.Infection => new Cause(CauseKind.Wound, null, note.Length > 0 ? note : "sepsis from a wound"),
            DeathCause.Suffocation => new Cause(CauseKind.Wound, null, note.Length > 0 ? note : "could not breathe"),
            DeathCause.Ischaemia => new Cause(CauseKind.Wound, null, note.Length > 0 ? note : "a limb lost to a tourniquet kept on"),
            _ => new Cause(CauseKind.Death, null, note.Length > 0 ? note : "illness or misadventure"),
        };
        ob.Chronicle(when, "death", [pt.IdOf(p)], [c], ("name", pt.FullName(p)), ("age", age.ToString()), ("cause", cause.ToString()),
            ("settlement", settlement >= 0 ? w.Population.Region.Settlements[settlement].Name : ""));
    }

    /// <summary>A dead keeper of a workplace or lord of a fief leaves the post; the heir takes a fief with the household (set by succession).</summary>
    private static void ReleaseOffice(SimWorld w, int p)
    {
        var pt = w.People; var pop = w.Population;
        int ws = pt.WorkSettlement[p], wl = pt.WorkLot[p];
        if (ws >= 0 && wl >= 0 && ws < pop.LotKeeper.Length && wl < pop.LotKeeper[ws].Length && pop.LotKeeper[ws][wl] == p) pop.LotKeeper[ws][wl] = -1;
    }

    private static void MemberLeft(SimWorld w, int hh, int person, GameTime when, RegionOutbox ob)
    {
        var hht = w.Households;
        if (hht.MemberCount[hh] == 0) { Dissolve(w, hh, person, when, ob); return; }
        if (hht.Head[hh] == person) SucceedHead(w, hh, person, when, ob);
    }

    // ------------------------------------------------------------------ succession

    /// <summary>
    /// The head left (died). A living adult (16+) takes over: eldest adult son, else eldest adult daughter (of the old head), else the spouse, else the eldest
    /// adult member; household, dwelling, parcels, stock and purse stay as they are. With only minors left they go to kin (guardianship). Spec 3.8.
    /// </summary>
    public static void SucceedHead(SimWorld w, int hh, int oldHead, GameTime when, RegionOutbox ob)
    {
        var pt = w.People; var hht = w.Households; int day = (int)when.TotalDays;
        int bestSon = -1, bestDaughter = -1, spouse = -1, eldest = -1, minors = 0;
        int sp = pt.Spouse[oldHead];
        foreach (int m in hht.Members(hh))
        {
            if (w.AgeOn(m, day) < 16) { minors++; continue; }
            bool child = pt.Father[m] == oldHead || pt.Mother[m] == oldHead;
            if (child && pt.Sex[m] == Sex.Male && Older(pt, m, bestSon)) bestSon = m;
            if (child && pt.Sex[m] == Sex.Female && Older(pt, m, bestDaughter)) bestDaughter = m;
            if (m == sp) spouse = m;
            if (Older(pt, m, eldest)) eldest = m;
        }
        int heir = bestSon >= 0 ? bestSon : bestDaughter >= 0 ? bestDaughter : spouse >= 0 ? spouse : eldest;
        if (heir >= 0)
        {
            hht.Head[hh] = heir;
            for (int f = 0; f < w.Population.FiefLord.Length; f++) if (w.Population.FiefLord[f] == oldHead) w.Population.FiefLord[f] = heir;
            return;
        }
        for (int f = 0; f < w.Population.FiefLord.Length; f++) if (w.Population.FiefLord[f] == oldHead) w.Population.FiefLord[f] = -1;
        PlaceOrphans(w, hh, oldHead, when, ob);
    }

    private static bool Older(PersonTable pt, int a, int b) => b < 0 || pt.BirthDay[a] < pt.BirthDay[b] || (pt.BirthDay[a] == pt.BirthDay[b] && a < b);

    private static int FiefOf(SimWorld w, int settlement) => settlement >= 0 && settlement < w.Population.Region.Settlements.Count ? w.Population.Region.Settlements[settlement].FiefIndex : -1;

    /// <summary>
    /// Only minors remain: they go with the household's coin and stock to the nearest adult kin household in the same fief (grandparent, then aunt or uncle,
    /// then adult sibling), beds permitting; with no kin they become wards of the parish (the priest's household, else the lord's) and the assets escheat to
    /// the lord's treasury. The dwelling is vacated and the parcels pass to the receiving household. Spec 3.8 rules 2 and 3.
    /// </summary>
    private static void PlaceOrphans(SimWorld w, int hh, int oldHead, GameTime when, RegionOutbox ob)
    {
        var hht = w.Households; var pt = w.People;
        var kids = hht.Members(hh).OrderBy(m => m).ToArray();
        int fief = FiefOf(w, hht.Settlement[hh]);
        int guardian = FindKinHousehold(w, hh, kids, fief, (int)when.TotalDays);
        bool kin = guardian >= 0;
        if (!kin) guardian = FindParishHousehold(w, hh, kids.Length, fief);
        if (guardian < 0)
        {
            // nobody can take them: the eldest child heads what is left, the children are vagrants (they migrate in a later season)
            int eld = kids[0]; foreach (int k in kids) if (Older(pt, k, eld)) eld = k;
            hht.Head[hh] = eld;
            foreach (int k in kids) pt.Homeless[k] = HomelessReason.Vagrant;
            return;
        }
        foreach (int k in kids) MovePerson(w, k, hh, guardian);
        if (kin) TransferAssets(w, hh, guardian, TransferReason.Inheritance);
        else EscheatAssets(w, hh, fief, TransferReason.Escheat);
        hht.Head[hh] = -1;
        RelocateParcels(w, hh, kin ? guardian : LordHousehold(w, fief, guardian, exclude: hh));
        VacateDwelling(w, hh);
        ob.Count(kin ? "orphans_to_kin" : "orphans_to_parish", kids.Length);
        if (!kin || w.Rates.ChronicleEveryEvent)
            ob.Chronicle(when, kin ? "guardianship" : "escheat", [hht.IdOf(hh), hht.IdOf(guardian)],
                [new Cause(CauseKind.Death, null, "the head died and only minors remained")], ("household", hht.Name[hh]), ("minors", kids.Length.ToString()));
    }

    private static int FindKinHousehold(SimWorld w, int hh, int[] kids, int fief, int day)
    {
        var pt = w.People; var hht = w.Households; var ch = w.Children;
        bool Ok(int person, bool adult = true)
            => person >= 0 && pt.Status[person] == PersonStatus.Alive && (!adult || w.AgeOn(person, day) >= 16) && pt.Household[person] >= 0 && pt.Household[person] != hh
               && FiefOf(w, hht.Settlement[pt.Household[person]]) == fief;
        var tiers = new List<int>[3] { [], [], [] };
        var parents = new SortedSet<int>(); var grandparents = new SortedSet<int>();
        foreach (int k in kids) { if (pt.Father[k] >= 0) parents.Add(pt.Father[k]); if (pt.Mother[k] >= 0) parents.Add(pt.Mother[k]); }
        foreach (int par in parents) { if (pt.Father[par] >= 0) grandparents.Add(pt.Father[par]); if (pt.Mother[par] >= 0) grandparents.Add(pt.Mother[par]); }
        foreach (int g in grandparents) if (Ok(g)) tiers[0].Add(g);
        foreach (int g in grandparents) foreach (int uncle in ch.Of(g).OrderBy(x => x)) if (!parents.Contains(uncle) && Ok(uncle)) tiers[1].Add(uncle);
        foreach (int par in parents) foreach (int sib in ch.Of(par).OrderBy(x => x)) if (Ok(sib)) tiers[2].Add(sib);
        int first = -1;
        foreach (var tier in tiers)
            foreach (int cand in tier)
            {
                int g = pt.Household[cand];
                if (first < 0) first = g;
                if (hht.MemberCount[g] + kids.Length <= hht.Beds[g]) return g;
            }
        return first;
    }

    private static int FindParishHousehold(SimWorld w, int hh, int minors, int fief)
    {
        var pop = w.Population; var hht = w.Households;
        for (int s = 0; s < pop.PriestHousehold.Length; s++)
        {
            int g = pop.PriestHousehold[s];
            if (g >= 0 && g != hh && hht.MemberCount[g] > 0 && FiefOf(w, s) == fief) return g;
        }
        return LordHousehold(w, fief, -1, exclude: hh);
    }

    /// <summary>The household of the lord of a fief if it still has people (and is not <paramref name="exclude"/>), else <paramref name="fallback"/>.</summary>
    private static int LordHousehold(SimWorld w, int fief, int fallback, int exclude = -1)
    {
        var pop = w.Population;
        if (fief >= 0 && fief < pop.FiefLordHousehold.Length)
        {
            int g = pop.FiefLordHousehold[fief];
            if (g >= 0 && g != exclude && w.Households.MemberCount[g] > 0) return g;
        }
        return fallback;
    }

    /// <summary>The household emptied: coin, stock and parcels go to the eldest living child's household (sons first), else the surviving spouse's, else the lord. Spec 3.8.</summary>
    private static void Dissolve(SimWorld w, int hh, int lastMember, GameTime when, RegionOutbox ob)
    {
        var pt = w.People; var hht = w.Households; int day = (int)when.TotalDays;
        int fief = FiefOf(w, hht.Settlement[hh]);
        int head = hht.Head[hh] >= 0 ? hht.Head[hh] : lastMember;
        int heirHh = -1;
        foreach (var sex in new[] { Sex.Male, Sex.Female })
        {
            int best = -1;
            foreach (int c in w.Children.Of(head))
                if (pt.Status[c] == PersonStatus.Alive && pt.Sex[c] == sex && pt.Household[c] >= 0 && pt.Household[c] != hh && Older(pt, c, best)) best = c;
            if (best >= 0) { heirHh = pt.Household[best]; break; }
        }
        if (heirHh < 0 && pt.Spouse[head] >= 0 && pt.Status[pt.Spouse[head]] == PersonStatus.Alive && pt.Household[pt.Spouse[head]] != hh) heirHh = pt.Household[pt.Spouse[head]];
        hht.Head[hh] = -1;
        if (heirHh >= 0)
        {
            TransferAssets(w, hh, heirHh, TransferReason.Inheritance);
            RelocateParcels(w, hh, heirHh);
            ob.Count("inheritance");
        }
        else
        {
            EscheatAssets(w, hh, fief, TransferReason.Escheat);
            RelocateParcels(w, hh, LordHousehold(w, fief, -1, exclude: hh));
            ob.Count("escheat");
            ob.Chronicle(when, "escheat", [hht.IdOf(hh)], [new Cause(CauseKind.Death, null, "no heir: the estate fell to the lord")], ("household", hht.Name[hh]),
                ("settlement", w.Population.Region.Settlements[hht.Settlement[hh]].Name));
        }
        VacateDwelling(w, hh);
        _ = day;
    }

    // ------------------------------------------------------------------ moving people and property

    /// <summary>Moves a living person to another household (marriage, guardianship): member lists, home bindings and class follow.</summary>
    public static void MovePerson(SimWorld w, int p, int from, int to)
    {
        var pt = w.People; var hht = w.Households;
        if (from >= 0) hht.RemoveMember(from, p);
        hht.AddMemberLater(to, p);
        pt.Household[p] = to; pt.HomeSettlement[p] = hht.Settlement[to]; pt.HomeLot[p] = hht.Lot[to]; pt.Bed[p] = -1;
        if (pt.Homeless[p] != HomelessReason.None) pt.Homeless[p] = HomelessReason.None;
    }

    /// <summary>All the coin and goods of household <paramref name="from"/> to household <paramref name="to"/> (reason Inheritance), conserving both.</summary>
    public static void TransferAssets(SimWorld w, int from, int to, TransferReason why)
    {
        var e = w.Economy;
        var bal = e.Ledger.Balance(e.HouseholdPurse[from]);
        if (bal.Mites > 0 && from != to) e.Ledger.Transfer(e.HouseholdPurse[from], e.HouseholdPurse[to], bal, why);
        var src = e.HouseholdStore[from]; var dst = e.HouseholdStore[to];
        foreach (var (g, q) in src.Rows()) Store.Move(src, dst, g, q);
    }

    /// <summary>Coin to the fief's treasury purse and goods to the fief's chest, both counted as escheat.</summary>
    public static void EscheatAssets(SimWorld w, int household, int fief, TransferReason why)
    {
        var e = w.Economy; var regionFiefs = w.Population.Region.Fiefs;
        if (regionFiefs.Count == 0) return;
        var f = regionFiefs[fief >= 0 && fief < regionFiefs.Count ? fief : 0];
        var treasury = e.Ledger.Open(PurseKind.Treasury, f.Id);
        var bal = e.Ledger.Balance(e.HouseholdPurse[household]);
        if (bal.Mites > 0) e.Ledger.Transfer(e.HouseholdPurse[household], treasury, bal, why);
        var chest = e.OpenStore(StoreKind.Chest, f.Id, 0, 0, enforce: false);
        var src = e.HouseholdStore[household];
        foreach (var (g, q) in src.Rows()) Store.Move(src, chest, g, q);
    }

    /// <summary>Parcels held by a household go to another holder (the lord's household for an escheat); <c>-1</c> leaves them unheld for the farming system to re-let.</summary>
    public static void RelocateParcels(SimWorld w, int from, int to)
    {
        var hht = w.Households; var holders = w.Population.ParcelHolder;
        if (hht.ParcelCount[from] == 0) return;
        for (int i = 0; i < holders.Length; i++)
            if (holders[i] == from)
            {
                holders[i] = to;
                if (to >= 0) hht.ParcelCount[to]++;
            }
        hht.ParcelCount[from] = 0;
    }

    /// <summary>The dwelling of a household that no longer lives there becomes a vacancy for the marriage brake.</summary>
    public static void VacateDwelling(SimWorld w, int hh)
    {
        var hht = w.Households; var lh = w.Population.LotHousehold;
        int s = hht.Settlement[hh], l = hht.Lot[hh];
        if (s >= 0 && s < lh.Length && l >= 0 && l < lh[s].Length && lh[s][l] == hh) lh[s][l] = -1;
    }

    // ------------------------------------------------------------------ emigration

    /// <summary>Owner of the purse and chest that hold what emigrants carry out of the region (and nothing else): coin and goods stay in the closed audit.</summary>
    public static readonly EntityId ContinentOwner = EntityId.Make(IdKind.Other, 0);

    /// <summary>
    /// A person leaves the region (migration push, population spec 3.7): the row becomes <see cref="PersonStatus.Departed"/> (kept, ids never dangle), the spouse
    /// is widowed only when they stay behind, and the household loses the member (succession when the head goes). Carries nothing.
    /// </summary>
    public static void Depart(SimWorld w, int p, GameTime when, RegionOutbox ob)
    {
        var pt = w.People; var hht = w.Households;
        if (pt.Status[p] != PersonStatus.Alive) return;
        int hh = pt.Household[p];
        pt.Status[p] = PersonStatus.Departed; pt.PregnantUntilDay[p] = -1; pt.FatherOfUnborn[p] = -1;
        var v = w.Vital; v.Alive--; v.Emigrants++; v.Year(when.Year).Emigrants++;
        ob.Count("emigrants");
        if (w.Get<Ravensguard.Sim.Economy.Markets.ContinentLink>() is { } lk) { var rl = lk.Mine; rl.EmigrantPersons++; rl.TotalLeft++; }
        int sp = pt.Spouse[p];
        if (sp >= 0 && pt.Spouse[sp] == p) pt.Spouse[sp] = -1;
        ReleaseOffice(w, p);
        if (hh >= 0 && hht.RemoveMember(hh, p)) MemberLeft(w, hh, p, when, ob);
    }

    /// <summary>A whole household leaves the region with its coin and stock (which move to the continent's purse and chest); the dwelling is vacated and the parcels pass to the lord.</summary>
    public static void DepartHousehold(SimWorld w, int hh, GameTime when, RegionOutbox ob)
    {
        var pt = w.People; var hht = w.Households; var e = w.Economy;
        var members = hht.Members(hh).OrderBy(m => m).ToArray();
        var v = w.Vital;
        foreach (int p in members)
        {
            pt.Status[p] = PersonStatus.Departed; pt.PregnantUntilDay[p] = -1; pt.FatherOfUnborn[p] = -1;
            v.Alive--; v.Emigrants++; v.Year(when.Year).Emigrants++;
            ReleaseOffice(w, p);
            int sp = pt.Spouse[p];
            if (sp >= 0 && pt.Spouse[sp] == p && pt.Household[sp] != hh) pt.Spouse[sp] = -1;
            hht.RemoveMember(hh, p);
        }
        ob.Count("emigrants", members.Length);
        var link = w.Get<Ravensguard.Sim.Economy.Markets.ContinentLink>();
        var purse = link == null ? e.Ledger.Open(PurseKind.Treasury, ContinentOwner) : PurseId.None;
        var bal = e.Ledger.Balance(e.HouseholdPurse[hh]);
        // chevage (integration decision I11): a household that leaves its lord's land pays him a share of its coin, so a region that people leave does not lose its money for good
        int fiefOut = FiefOf(w, hht.Settlement[hh]);
        if (bal.Mites > 0 && fiefOut >= 0 && fiefOut < w.Population.Region.Fiefs.Count && w.Rates.ChevagePct > 0)
        {
            var due = new Coin(bal.Mites * w.Rates.ChevagePct / 100);
            if (due.Mites > 0 && e.Ledger.Transfer(e.HouseholdPurse[hh], e.Ledger.Open(PurseKind.Treasury, w.Population.Region.Fiefs[fiefOut].Id), due, TransferReason.Escheat)) bal -= due;
        }
        var src = e.HouseholdStore[hh];
        if (link != null)
        {
            // continent tick v1: what the household takes leaves the region's books (emigrated coin, exported goods) and arrives in the neighbour's summary at the next tick
            var rl = link.Mine;
            if (bal.Mites > 0 && e.Ledger.Sink(e.HouseholdPurse[hh], bal, SinkReason.Emigrate)) rl.EmigrantCoin += bal.Mites;
            long grainG = 0;
            foreach (var (g, q) in src.Rows())
            {
                if (e.Food.IsGrain(g)) grainG += e.Goods.MassG(g, q);
                int tk = Array.IndexOf(Ravensguard.Sim.Continental.ContinentRules.TrackedGoods, e.Goods.StringId(g)); if (tk >= 0) rl.EmigrantG[tk] += e.Goods.MassG(g, q);
                src.Destroy(g, q, DestroyReason.Export);
            }
            rl.EmigrantMeasures += grainG * Ravensguard.Sim.Continental.ContinentRules.MeasuresPerKgX1000 / 1_000_000;
            rl.EmigrantPersons += members.Length; rl.EmigrantHouseholds++; rl.TotalLeft += members.Length;
        }
        else
        {
            if (bal.Mites > 0) e.Ledger.Transfer(e.HouseholdPurse[hh], purse, bal, TransferReason.Gift);
            var chest = e.OpenStore(StoreKind.Chest, ContinentOwner, 0, 0, enforce: false);
            foreach (var (g, q) in src.Rows()) Store.Move(src, chest, g, q);
        }
        int fief = FiefOf(w, hht.Settlement[hh]);
        hht.Head[hh] = -1;
        RelocateParcels(w, hh, LordHousehold(w, fief, -1, exclude: hh));
        VacateDwelling(w, hh);
        if (w.Rates.ChronicleEveryEvent)
            ob.Chronicle(when, "emigration", [hht.IdOf(hh)], [new Cause(CauseKind.Migration, null, "the household left a crowded dwelling")], ("household", hht.Name[hh]), ("members", members.Length.ToString()));
    }

    // ------------------------------------------------------------------ immigration

    /// <summary>
    /// A household that left comes back (integration decision I27, the stand-in for the continent's inflow): its departed members are alive again in a vacant dwelling (its own
    /// village first), the eldest adult heads it, each member's reserve is full, and the continent's purse and chest give it a stake from what the emigrants took out
    /// (20 crowns and a fortnight of grain a head, as much as is there), so coin and goods stay in the closed audit. False when no dwelling is free.
    /// </summary>
    public static bool ReturnHousehold(SimWorld w, int hh, List<int> members, GameTime when, RegionOutbox ob, Stake? stake = null)
    {
        var pt = w.People; var hht = w.Households; var e = w.Economy; long day = when.TotalDays;
        var (s, l) = Marriage.FreeDwelling(w, hht.Settlement[hh], members.Count);
        if (s < 0) return false;
        var pop = w.Population;
        hht.Settlement[hh] = s; hht.Lot[hh] = (short)l; hht.Beds[hh] = (short)pop.LotBeds[s][l]; hht.Dwelling[hh] = pop.Region.Settlements[s].Plan.Lots[l].Dwelling; pop.LotHousehold[s][l] = hh;
        int head = -1;
        foreach (int p in members)
        {
            pt.Status[p] = PersonStatus.Alive; pt.HomeSettlement[p] = s; pt.HomeLot[p] = (short)l; pt.Bed[p] = -1; pt.Homeless[p] = HomelessReason.None;
            e.NutritionCd[p] = (short)HouseholdNeeds.MaxNutritionCd;
            hht.AddMemberLater(hh, p);
            int age = w.AgeOn(p, day);
            if (age >= 18 && (head < 0 || pt.BirthDay[p] < pt.BirthDay[head])) head = p;
            w.Vital.Alive++; w.Vital.Immigrants++; w.Vital.Year(when.Year).Immigrants++;
        }
        hht.Head[hh] = head >= 0 ? head : members[0];
        ob.Count("immigrants", members.Count);
        if (stake != null) { GiveStake(w, hh, stake.Value); return true; }
        var purse = e.Ledger.Open(PurseKind.Treasury, ContinentOwner);
        long stakeMites = Math.Min(e.Ledger.Balance(purse).Mites, members.Count * 2000L);
        if (stakeMites > 0) e.Ledger.Transfer(purse, e.HouseholdPurse[hh], new Coin(stakeMites), TransferReason.Gift);
        if (e.OtherStores.TryGetValue(ContinentOwner, out var chest))
        {
            long want = (long)members.Count * HouseholdNeeds.GramsPerMeasure * 14;
            foreach (ushort g in e.Food.Staples) { if (want <= 0) break; if (e.Food.IsGrain(g)) want -= Store.MoveUpTo(chest, e.HouseholdStore[hh], g, want); }
        }
        return true;
    }

    /// <summary>What the continent gives a household that comes to the region: coin (immigrated into its purse) and grain in measures (imported into its store).</summary>
    [ThreadStatic] public static int LastSettled;
    public readonly record struct Stake(long CoinMites, long GrainMeasures);

    private static void GiveStake(SimWorld w, int hh, Stake stake)
    {
        var e = w.Economy;
        if (stake.CoinMites > 0) e.Ledger.Mint(e.HouseholdPurse[hh], new Coin(stake.CoinMites), SourceReason.Immigrate);
        if (stake.GrainMeasures > 0 && e.Goods.TryIdOf("wheat", out ushort wheat))
            e.HouseholdStore[hh].Create(wheat, stake.GrainMeasures * 1_000_000 / Ravensguard.Sim.Continental.ContinentRules.MeasuresPerKgX1000, CreateReason.Import);
    }

    /// <summary>
    /// A family from the continent settles in a vacant dwelling (continent tick v1, spec 3.7: the region instantiates synthetic households for the migrants posted to it): a father and
    /// mother in their twenties and the rest children, names from the region's own pools, the ids from this region's allocator (E9), the coin and grain the continent sent
    /// with them. Returns the household row, or -1 when no dwelling with room is free.
    /// </summary>
    /// <summary>The persons actually settled are <see cref="LastSettled"/>.</summary>
    public static int SettleFamily(SimWorld w, int persons, Func<int, Stake> stakeFor, GameTime when, RegionOutbox ob, int salt)
    {
        var pt = w.People; var hht = w.Households; var pop = w.Population; var e = w.Economy; long day = when.TotalDays;
        if (persons < 1) return -1;
        int s = -1, l = -1;
        for (; persons >= 2 && s < 0; persons--)               // a smaller family when no dwelling is big enough for the whole party
            for (int cand = 0; cand < pop.Region.Settlements.Count && s < 0; cand++) { var (cs, cl) = Marriage.FreeDwelling(w, cand, persons); if (cs >= 0) { s = cs; l = cl; } }
        persons++;
        if (s < 0) return -1;
        var lot = pop.Region.Settlements[s].Plan.Lots[l];
        w.Ids.EnsureNext(IdKind.Household, hht.BaseCounter + (ulong)hht.Count);
        var hid = w.Ids.Allocate(IdKind.Household);
        int hh = hht.Add(s, l, lot.Dwelling, pop.LotBeds[s][l]);
        if (hht.IdOf(hh) != hid) throw new InvalidOperationException("household id out of step with the table");
        int culture = pop.Region.Settlements[s].Culture;
        var pool = w.Names?.For(culture);
        string family = pool != null ? pool.FamilyName(new Rnd(w.Seed, hid.Value, VitalStreams.Name)) : "Settler";
        hht.Class[hh] = SocialClass.Peasant; hht.Deed[hh] = DeedKind.Rented; hht.Name[hh] = family;
        int head = -1;
        for (int i = 0; i < persons; i++)
        {
            w.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
            var id = w.Ids.Allocate(IdKind.Person);
            int p = pt.Add();
            if (pt.IdOf(p) != id) throw new InvalidOperationException("person id out of step with the table");
            bool adult = i < 2, female = i == 1 || (!adult && DetRng.Range(w.Seed, when.TotalHours, id.Value, VitalStreams.Conception, 2, 0, 2) == 1);
            int age = adult ? (i == 0 ? 28 : 25) : 1 + DetRng.Range(w.Seed, when.TotalHours, id.Value, VitalStreams.Conception, 3, 0, 10);
            pt.Sex[p] = female ? Sex.Female : Sex.Male; pt.BirthDay[p] = PersonTable.BirthDayFor(day, age, DetRng.Range(w.Seed, when.TotalHours, id.Value, VitalStreams.Conception, 4, 0, 59), pt.AgingPace);
            pt.Culture[p] = (byte)culture; pt.Class[p] = SocialClass.Peasant; pt.Family[p] = family;
            pt.Given[p] = pool != null ? pool.Given(female, new Rnd(w.Seed, id.Value, VitalStreams.Name)) : (female ? "Settler" : "Settler");
            pt.Household[p] = hh; pt.HomeSettlement[p] = s; pt.HomeLot[p] = (short)l; pt.Bed[p] = -1; pt.Status[p] = PersonStatus.Alive;
            pt.Occupation[p] = Occupation.None;
            RollAdultAttributes(pt, p, w.Seed, id.Value, adult ? 100 : 55);
            hht.AddMember(hh, p);
            e.AddPerson(p);
            if (i == 0) head = p;
            else if (i == 1) { pt.Spouse[p] = head; pt.Spouse[head] = p; }
            else { pt.Mother[p] = head + 0 >= 0 && pt.Spouse[head] >= 0 ? pt.Spouse[head] : -1; pt.Father[p] = head; }
            w.Vital.Alive++; w.Vital.Immigrants++; w.Vital.Year(when.Year).Immigrants++;
            w.NoteBirth(p);
        }
        hht.Head[hh] = head;
        pop.LotHousehold[s][l] = hh;
        e.AddHousehold(hht, hh);
        for (int i = 0; i < hht.MemberCount[hh]; i++) { }
        foreach (int p in hht.Members(hh)) e.NutritionCd[p] = (short)HouseholdNeeds.MaxNutritionCd;
        pop.ResetKin();
        GiveStake(w, hh, stakeFor(persons));
        ob.Count("immigrants", persons); LastSettled = persons;
        if (w.Rates.ChronicleEveryEvent) ob.Chronicle(when, "immigration", [hid], [new Cause(CauseKind.Migration, null, "a family came over the border looking for land")], ("household", family), ("members", persons.ToString()), ("settlement", pop.Region.Settlements[s].Name));
        _ = salt;
        return hh;
    }

    private static void RollAdultAttributes(PersonTable pt, int c, ulong seed, ulong entity, int fPct)
    {
        var s = new RngStream(seed, 0, entity, VitalStreams.Attributes);
        int Roll() => 20 + s.Range(0, 21) + s.Range(0, 21) + s.Range(0, 21);
        int str = Roll(), tou = Roll(), dex = Roll(), per = Roll(), wit = Roll();
        if (pt.Sex[c] == Sex.Female) { str -= 6; dex += 2; }
        str = str * fPct / 100; tou = tou * fPct / 100; dex = dex * (fPct + (fPct < 100 ? 10 : 0)) / 100;
        int b = c * PersonTable.AttrCount;
        byte K(int v) => (byte)Math.Clamp(v, 1, 100);
        pt.Attr[b] = K(str); pt.Attr[b + 1] = K(tou); pt.Attr[b + 2] = K(dex); pt.Attr[b + 3] = K(per); pt.Attr[b + 4] = K(wit);
    }

    /// <summary>A person who left alone comes back to the household that still stands (a bed free), with a stake of 20 crowns from the continent's purse.</summary>
    public static bool ReturnPerson(SimWorld w, int p, int hh, GameTime when, RegionOutbox ob, Stake? given = null)
    {
        var pt = w.People; var hht = w.Households; var e = w.Economy;
        pt.Status[p] = PersonStatus.Alive; pt.PregnantUntilDay[p] = -1; pt.HomeSettlement[p] = hht.Settlement[hh]; pt.HomeLot[p] = hht.Lot[hh]; pt.Bed[p] = -1; pt.Homeless[p] = HomelessReason.None;
        e.NutritionCd[p] = (short)HouseholdNeeds.MaxNutritionCd;
        hht.AddMemberLater(hh, p);
        w.Vital.Alive++; w.Vital.Immigrants++; w.Vital.Year(when.Year).Immigrants++;
        ob.Count("immigrants");
        if (given != null) { GiveStake(w, hh, given.Value); return true; }
        var purse = e.Ledger.Open(PurseKind.Treasury, ContinentOwner);
        long stake = Math.Min(e.Ledger.Balance(purse).Mites, 2000L);
        if (stake > 0) e.Ledger.Transfer(purse, e.HouseholdPurse[hh], new Coin(stake), TransferReason.Gift);
        return true;
    }

    // ------------------------------------------------------------------ births

    /// <summary>
    /// The pregnancy of <paramref name="mother"/> comes to term: a new person row (id from the allocator, attributes rolled from the child template), kin links to
    /// both parents, member of the mother's household, with a full nutrition reserve. Returns the child's index.
    /// </summary>
    public static int Birth(SimWorld w, int mother, GameTime when, RegionOutbox ob)
    {
        var pt = w.People; var hht = w.Households; long day = when.TotalDays;
        int father = pt.FatherOfUnborn[mother], hh = pt.Household[mother];
        w.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
        var id = w.Ids.Allocate(IdKind.Person);
        int c = pt.Add();
        if (pt.IdOf(c) != id) throw new InvalidOperationException($"person id out of step with the table: {pt.IdOf(c)} vs {id}");
        var motherId = pt.IdOf(mother);
        bool female = DetRng.Range(w.Seed, when.TotalHours, motherId.Value, VitalStreams.Conception, 1, 0, 2) == 1;
        pt.Sex[c] = female ? Sex.Female : Sex.Male;
        pt.Mother[c] = mother; pt.Father[c] = father >= 0 && father < pt.Count ? father : -1;
        pt.Culture[c] = pt.Culture[mother]; pt.Class[c] = hht.Class[hh]; pt.BirthDay[c] = (int)day;
        pt.Family[c] = pt.Father[c] >= 0 ? pt.Family[pt.Father[c]] : pt.Family[mother];
        pt.Given[c] = w.Names != null ? w.Names.For(pt.Culture[c]).Given(female, new Rnd(w.Seed, id.Value, VitalStreams.Name)) : (female ? "Daughter" : "Son");
        pt.Dynasty[c] = pt.Father[c] >= 0 && pt.Dynasty[pt.Father[c]] != EntityId.None ? pt.Dynasty[pt.Father[c]] : pt.Dynasty[mother];
        pt.HomeSettlement[c] = hht.Settlement[hh]; pt.HomeLot[c] = hht.Lot[hh]; pt.Household[c] = hh;
        RollChildAttributes(pt, c, w.Seed, id.Value);
        hht.AddMemberLater(hh, c);
        w.Economy.AddPerson(c);
        w.Population.ResetKin();
        w.NoteBirth(c);
        pt.PregnantUntilDay[mother] = -1; pt.FatherOfUnborn[mother] = -1; pt.LastBirthDay[mother] = (int)day;
        var v = w.Vital; v.Alive++; v.Births++; v.Year(when.Year).Births++;
        ob.Count("births");
        bool notable = pt.Notable[mother] != EntityId.None || (pt.Father[c] >= 0 && pt.Notable[pt.Father[c]] != EntityId.None);
        if (w.Rates.ChronicleEveryEvent || notable)
            ob.Chronicle(when, "birth", pt.Father[c] >= 0 ? [id, motherId, pt.IdOf(pt.Father[c])] : [id, motherId], [new Cause(CauseKind.Other, null, "a child is born")],
                ("name", pt.FullName(c)), ("sex", female ? "female" : "male"), ("settlement", w.Population.Region.Settlements[hht.Settlement[hh]].Name));
        return c;
    }

    private static void RollChildAttributes(PersonTable pt, int c, ulong seed, ulong entity)
    {
        var s = new RngStream(seed, 0, entity, VitalStreams.Attributes);
        int Roll() => 20 + s.Range(0, 21) + s.Range(0, 21) + s.Range(0, 21);
        int str = Roll(), tou = Roll(), dex = Roll(), per = Roll(), wit = Roll();
        if (pt.Sex[c] == Sex.Female) { str -= 6; dex += 2; }
        int f = 55; // the Phase 1 child template at age 0
        str = str * f / 100; tou = tou * f / 100; dex = dex * (f + 10) / 100;
        int b = c * PersonTable.AttrCount;
        byte K(int v) => (byte)Math.Clamp(v, 1, 100);
        pt.Attr[b] = K(str); pt.Attr[b + 1] = K(tou); pt.Attr[b + 2] = K(dex); pt.Attr[b + 3] = K(per); pt.Attr[b + 4] = K(wit);
    }
}
