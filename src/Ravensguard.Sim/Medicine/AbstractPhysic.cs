using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Medicine;

/// <summary>
/// The abstract tier's physician (medicine 4.4, H4 slice): a wounded person in a settlement with a physician or barber-surgeon, whose household holds the fee, is treated within the day with
/// P = 80% (physician) / 60% (barber): the draw is stream 0x600, key the person, tick = the game day, draw 1, so it does not depend on the hour or the order. The man does the
/// <see cref="TreatAi.Next"/> sequence on the book (at most <see cref="MaxSteps"/>, each kind once, only what his sort offers), each performed at its finish second, and each fee moves from the
/// household's purse to his business box (or his own household when he has none). Persons are walked in stable-id order. Without a physician <see cref="AbstractFirstAid"/> is all there is.
/// </summary>
public static class AbstractPhysic
{
    public const int MaxSteps = 6;

    /// <summary>The man's kit: what he carries to the bedside (its price is in his fees).</summary>
    private static ItemBag Kit() => new ItemBag().Add("bandage", 8).Add("rope", 2).Add("splint", 2).Add("needle_and_thread", 1).Add("poultice", 2);

    /// <summary>The chance, percent, that a wounded person is seen within the day.</summary>
    public static int TreatPct(PhysicianKind k) => k == PhysicianKind.Physician ? MedicineRules.Current.Rules.PhysicianTreatPct : MedicineRules.Current.Rules.BarberTreatPct;

    /// <summary>Runs the hour's physic over the ledger; returns the keys of the persons a physician saw (so first aid leaves them be), or null when the region has none.</summary>
    public static HashSet<ulong>? Run(SimWorld w, WoundLedger l, long hourStartSec)
    {
        var book = w.Get<PhysicianBook>();
        if (book == null || book.IsEmpty) return null;
        var pt = w.People; var econ = w.Economy; var ew = w.Get<EconomyWorld>();
        long day = hourStartSec / TimeConst.SecondsPerDay;
        HashSet<ulong>? seen = null;
        List<ulong>? keys = null;
        foreach (var (key, b) in l.Books) { if (b.Dead || b.Records.Count == 0) continue; (keys ??= []).Add(key); }
        if (keys == null) return null;
        keys.Sort();
        foreach (ulong key in keys)
        {
            var b = l.Books[key];
            int row = pt.IndexOf(new EntityId(key));
            if (row < 0 || pt.Status[row] != PersonStatus.Alive) continue;
            int hh = pt.Household[row], s = pt.HomeSettlement[row];
            if (hh < 0 || s < 0) continue;
            var men = book.In(s); if (men.Count == 0) continue;
            var man = men[(int)(key % (ulong)men.Count)];
            if (DetRng.Range(w.Seed, day, key, MedicineEnums.PhysicianStream, 1, 0, 100) >= TreatPct(man.Kind)) continue;
            int mrow = pt.IndexOf(new EntityId(man.Person)); if (mrow < 0 || pt.Status[mrow] != PersonStatus.Alive) continue;
            var purse = econ.HouseholdPurse[hh];
            if (econ.Ledger.Balance(purse).Mites < PhysicianFees.VisitFee(man.Kind)) continue;
            b.AdvanceTo(hourStartSec);
            if (Treat(w, ew, book, b, man, mrow, key, purse, hourStartSec)) (seen ??= []).Add(key);
        }
        return seen;
    }

    private static bool Treat(SimWorld w, EconomyWorld? ew, PhysicianBook book, WoundBook b, PhysicianRow man, int mrow, ulong key, PurseId payer, long hourStartSec)
    {
        var pt = w.People; var econ = w.Economy;
        var plan = book.Plans?.Invoke(b.PlanId);
        int skill = pt.SkillLevelOf(mrow, Skill.Surgery);
        ulong actor = man.Person;
        var items = Kit();
        PurseId payee = man.Business >= 0 && ew != null && man.Business < ew.Production.Businesses.Count ? ew.Production.Businesses[man.Business].Purse : econ.HouseholdPurse[pt.Household[mrow]];
        var done = new HashSet<TreatKind>();
        long t = hourStartSec; bool any = false, visited = false;
        for (int n = 0; n < MaxSteps; n++)
        {
            var req = TreatAi.Next(b, plan, items, t, skill, actor);
            if (req is not { } r || !done.Add(r.Kind) || !PhysicianFees.Offers(man.Kind, r.Kind)) break;
            long finish = t + (long)Treatment.SimSeconds(r.Kind) * MedicineTables.TicksPerSimS;
            int fee = PhysicianFees.Fee(man.Kind, r.Kind);
            bool visit = PhysicianFees.ServiceOf(r.Kind) == "visit";
            if (visit && visited || payer == payee) fee = 0;                         // one look a visit; his own household is not charged
            if (fee > 0 && econ.Ledger.Balance(payer).Mites < fee) break;
            var res = Treatment.Perform(b, plan, r with { GameSec = finish }, items);
            if (!res.Ok) break;
            if (fee > 0) econ.Ledger.Transfer(payer, payee, new Coin(fee), MedicineEnums.FeeReason);
            if (visit) visited = true;
            any = true; t = finish;
        }
        return any;
    }
}
