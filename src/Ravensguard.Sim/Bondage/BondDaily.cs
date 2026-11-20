using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Society;

namespace Ravensguard.Sim.Bondage;

/// <summary>What a day's pass did (for tests and the daily trail).</summary>
public sealed record DayTally(int Ended, int Fed, int Fled, int Caught, int Voided, int Inherited, int Bought, int Revolts, int Seen, int Born);

/// <summary>
/// The day's pass over every bond (slavery 4.2 to 4.5, the H4 and H5 slices folded into the first hour of a day): owners who died pass their bonds on, terms end, treatment feeds and wears morale, tips build the
/// peculium and a slave buys himself free, the owner's trust frees a loyal slave, a free-walking unhappy slave runs (stream 0x632) and the catchers find the runaway, the law looks at slaves carried into a realm that
/// forbids them (stream 0x631, draws from 100), newborns of bonded mothers in a chattel law are bonded, and a holding with enough hungry or beaten bonded people may rise (stream 0x633). Bonds are walked in person id
/// order; every draw is keyed on the person (or holding) and the day, never on a tick.
/// </summary>
public static class BondDaily
{
    public static DayTally Run(BondWorld bw, BondBook book, CrimeBook? crimes, int day)
    {
        long sec = day * 86_400L + 60;
        int ended = 0, fed = 0, fled = 0, caught = 0, voided = 0, inherited = 0, bought = 0, seen = 0, born = 0;
        var flogs = new Dictionary<ulong, ulong>();                              // owner -> this season's flogging entry
        bool seasonDay = day % TimeConst.DaysPerSeason == 0;
        foreach (var b0 in book.Bonds.Values.ToArray())
        {
            if (!book.Bonds.TryGetValue(b0.Person, out var b) || !ReferenceEquals(b, b0)) continue;
            var person = new EntityId(b.Person); var owner = new EntityId(b.Owner);
            int prow = bw.RowOf(person);
            if (prow >= 0 && bw.People.Status[prow] == PersonStatus.Dead) { book.Bonds.Remove(b.Person); continue; }   // the dead are not slaves; the person table keeps them
            if (b.InPen) continue;
            // ---- the owner is dead: the bond passes with the estate, or the will frees it (11)
            if (OwnerGone(bw, owner))
            {
                if (bw.WillFrees(owner)) { Slavery.Free(bw, book, person, Slavery.Ending.Will, day, sec); ended++; continue; }
                if (Inherit(bw, book, b, owner, sec)) inherited++;
                owner = new EntityId(b.Owner);
            }
            // ---- a term that has run out (debt, sentence)
            if (b.TermEndDay > 0 && day >= b.TermEndDay) { Slavery.Free(bw, book, person, Slavery.Ending.TermEnded, day, sec); ended++; continue; }
            if (b.OnRun) { var r = RunawayDay(bw, book, crimes, b, day, sec); if (r == 1) caught++; else if (r == 2) voided++; continue; }
            // ---- the owner's place and the law of it (4.3)
            int here = bw.PlaceOfOwner(owner);
            if (owner != bw.W.HeroId && here >= 0) b.Place = here;                          // the hero's slaves are where he told the world he stands (BondDesk 'here')
            if (b.Place >= 0 && bw.LawAt(b.Place) == SlaveLaw.None && owner.Kind is IdKind.Person or IdKind.Household && Observe(bw, book, crimes, b, day, sec)) { seen++; continue; }
            // ---- feeding and handling by treatment (4.2)
            Feed(bw, b, owner, day);
            if (b.Treatment == Treatment.Harsh && seasonDay)
            {
                if (!flogs.TryGetValue(b.Owner, out var fe)) flogs[b.Owner] = fe = Flog(bw, b, owner, day, sec);
                b.FlogDay = day; b.FlogEntry = fe;
            }
            fed++;
            // ---- the peculium (4.2): a Fair or Kind owner lets him keep a tip
            int tip = b.Treatment switch { Treatment.Kind => bw.D.KindTipMites, Treatment.Fair => bw.D.FairTipMites, _ => 0 };
            if (tip > 0 && b.Kind != BondKind.Convict && bw.PurseOf(owner) is { } op && bw.Ledger.Balance(op).Mites >= tip)
                bw.Ledger.Transfer(op, bw.PeculiumPurse(person), new Coin(tip), TransferReason.Gift);
            if (b.PriceMites > 0 && bw.Peculium(person) >= b.PriceMites && Slavery.SelfPurchase(bw, book, person, day, sec).Ok) { bought++; ended++; continue; }
            // ---- trust: an owner who has come to trust him frees him after a year (4.4)
            if (b.Treatment != Treatment.Harsh && b.Kind != BondKind.Convict && day - b.Since >= bw.D.TrustFreeDays && owner.Kind != IdKind.Other && bw.W.OpinionPm(bw.OwnerPerson(owner), person) >= bw.D.TrustFreePm)
            { Slavery.Free(bw, book, person, Slavery.Ending.Trust, day, sec); ended++; continue; }
            // ---- the unhappy who are not chained run (4.4, stream 0x632)
            if (b.Chain == Restraint.None && b.Morale < bw.D.FleeMorale && b.Kind != BondKind.Convict && b.Place >= 0)
            {
                int p = (bw.D.FleeMorale - b.Morale) * bw.D.RunawayPmPerPoint;
                if (DetRng.Range(bw.Seed, day, b.Person, RngStreams.RunawayRoll, 0, 0, 1000) < p) { Flee(bw, book, crimes, b, day, sec); fled++; }
            }
        }
        born += Births(bw, book, day, sec);
        int revolts = Revolts.Daily(bw, book, day, sec);
        book.LastDay = day;
        return new DayTally(ended, fed, fled, caught, voided, inherited, bought, revolts, seen, born);
    }

    private static bool OwnerGone(BondWorld bw, EntityId owner)
    {
        if (owner.Kind == IdKind.Person) { int r = bw.RowOf(owner); return r >= 0 && bw.People.Status[r] == PersonStatus.Dead; }
        if (owner.Kind == IdKind.Household) { int h = bw.HouseholdOf(owner); return h >= 0 && bw.W.Households is { } t && t.MemberCount[h] <= 0; }
        return false;
    }

    /// <summary>The owner died (11): the bond passes with the estate (as a holding does): to the household he led, else to his fief (escheat). Returns whether it changed hands.</summary>
    private static bool Inherit(BondWorld bw, BondBook book, Bond b, EntityId owner, long sec)
    {
        EntityId heir = EntityId.None;
        int hh = owner.Kind == IdKind.Person ? bw.HouseholdOf(owner) : -1;
        if (hh >= 0 && bw.W.Households is { } t && t.MemberCount[hh] > 0) heir = t.IdOf(hh);
        else
        {
            int s = b.Place >= 0 ? b.Place : bw.W.HomeOf(new EntityId(b.Person));
            int f = bw.W.FiefOf(s);
            if (f >= 0 && f < bw.W.FiefIds.Length) heir = bw.W.FiefIds[f];
        }
        if (heir == EntityId.None || heir.Value == b.Owner) return false;
        b.Owner = heir.Value; b.Since = Math.Max(b.Since, (int)(sec / 86_400));
        var who = bw.OwnerPerson(heir); var person = new EntityId(b.Person);
        var ps = new List<ChronParam> { ChronParam.Person("person", person) };
        if (who != EntityId.None) ps.Insert(0, ChronParam.Person("doer", who));
        var e = Slavery.Write(bw, sec, "slave_inherited", "ch_slavery_inherited", who != EntityId.None ? [who, person] : [person], Slavery.Note("ch_note_slave_estate", b.Entry), ps);
        b.Entry = e.Id.Value;
        if (heir.Kind == IdKind.Household && person != bw.W.HeroId) bw.Move(person, bw.HouseholdOf(heir));
        return true;
    }

    private static void Feed(BondWorld bw, Bond b, EntityId owner, int day)
    {
        int hh = bw.HouseholdOf(owner);
        bool hungry = hh >= 0 && bw.HouseholdHungry(hh);
        switch (b.Treatment)
        {
            case Treatment.Harsh: b.HungryDays++; b.Morale = Math.Max(0, b.Morale - bw.D.HarshMoraleDay); break;      // the basket at 70%: health and morale fall
            case Treatment.Fair: b.HungryDays = hungry ? b.HungryDays + 1 : 0; if (hungry) b.Morale = Math.Max(0, b.Morale - 1); else if (b.Morale < bw.D.MoraleStart + 10) b.Morale++; break;
            default: b.HungryDays = hungry ? b.HungryDays + 1 : 0; if (hungry) b.Morale = Math.Max(0, b.Morale - 1); else b.Morale = Math.Min(bw.D.KindMoraleCap, b.Morale + bw.D.KindMoraleDay); break;
        }
    }

    /// <summary>The season's floggings of an owner's Harsh bonds: one entry, the cause a revolt later names.</summary>
    private static ulong Flog(BondWorld bw, Bond b, EntityId owner, int day, long sec)
    {
        var who = bw.OwnerPerson(owner); int n = bw.W.People.Count > 0 ? 1 : 1;
        var ps = new List<ChronParam> { ChronParam.Count("count", n) };
        if (who != EntityId.None) ps.Insert(0, ChronParam.Person("doer", who));
        ps.AddRange(bw.PlaceParams(b.Place));
        return Slavery.Write(bw, sec, "slave_flogged", "ch_slavery_flogged", who != EntityId.None ? [who, new EntityId(b.Person)] : [new EntityId(b.Person)], Slavery.Note("ch_note_slave_harsh", b.Entry), ps).Id.Value;
    }

    // ------------------------------------------------------------------ the law looks (4.3)

    /// <summary>
    /// A slave held in a realm whose law is none (carried there, bought and not yet sold): with the draw (stream 0x631, draw 100 and up) a resident sees it; then the crime <c>slaving</c> is on the books (the
    /// doer the owner, the slave the victim), the lord's men free the slave and take the fine of twice the price from the owner's purse to the fief's treasury (what he cannot pay is the court's), and a second
    /// conviction is outlawry in the realm. Returns whether the bond was ended.
    /// </summary>
    public static bool Observe(BondWorld bw, BondBook book, CrimeBook? crimes, Bond b, int day, long sec)
    {
        if (crimes == null) return false;
        var owner = new EntityId(b.Owner); var person = new EntityId(b.Person); var doer = bw.OwnerPerson(owner);
        if (doer == EntityId.None) return false;
        if (DetRng.Range(bw.Seed, day, b.Person, RngStreams.SlaveMarketTies, 100, 0, 1000) >= 500) return false;     // nobody looked today
        int witness = -1; var pt = bw.People;
        for (int i = 0; i < pt.Count && witness < 0; i++)
            if (pt.IsAlive(i) && pt.HomeSettlement[i] == b.Place && pt.AgeYears(i, day) >= 16 && pt.IdOf(i) != doer && pt.IdOf(i) != person && !book.Bonds.ContainsKey(pt.IdOf(i).Value)) witness = i;
        if (witness < 0) return false;
        long price = b.PriceMites > 0 ? b.PriceMites : bw.PriceOf(person, day);
        var rec = CrimeSystem.Commit(bw.W, crimes, new ActSpec("slaving", doer, person, b.Place, sec, price, 0, "ch_note_crime_greed"), [new WitnessRow(pt.IdOf(witness).Value, WitnessRow.KnewIt)], ActorLook.None);
        long fine = price * bw.D.SlavingFineX;
        int fief = bw.W.FiefOf(b.Place);
        if (bw.PurseOf(owner) is { } from && bw.W.TreasuryOf?.Invoke(b.Place) is { } to)
            bw.Ledger.Transfer(from, to, new Coin(Math.Min(fine, bw.Ledger.Balance(from).Mites)), TransferReason.Fine);
        int priors = crimes.Crimes.Values.Count(c => c.Kind == "slaving" && c.Doer == doer.Value && c.Id != rec.Id);
        if (priors >= 1) bw.W.Marks()?.Outlaw(doer, bw.W.RealmOf(b.Place));
        _ = fief;
        Slavery.Free(bw, book, person, Slavery.Ending.Law, day, sec, rec.Entry);
        return true;
    }

    // ------------------------------------------------------------------ runaways (4.4)

    /// <summary>A bonded person runs (0x632): the bond carries (he stays on the books, on the run), the owner posts the runaway's bounty (crime <c>runaway</c>, a lord's bounty job slave catchers take), a chronicle entry says so.</summary>
    public static void Flee(BondWorld bw, BondBook book, CrimeBook? crimes, Bond b, int day, long sec)
    {
        var person = new EntityId(b.Person); var owner = new EntityId(b.Owner); var who = bw.OwnerPerson(owner); int from = b.Place;
        b.RunawayDay = Math.Max(1, day); b.Chain = Restraint.None;
        var ps = new List<ChronParam> { ChronParam.Person("person", person) };
        ps.AddRange(bw.PlaceParams(from));
        var e = Slavery.Write(bw, sec, "slave_runaway", "ch_slavery_runaway", who != EntityId.None ? [person, who] : [person], Slavery.Note(b.Treatment == Treatment.Harsh ? "ch_note_slave_harsh" : "ch_note_slave_unhappy", b.Entry), ps);
        b.Entry = e.Id.Value;
        if (crimes != null && who != EntityId.None)
        {
            long price = Math.Max(1, b.PriceMites);
            var rec = CrimeSystem.Commit(bw.W, crimes, new ActSpec("runaway", person, who, from, sec, price, 0, "ch_note_crime_greed"), [new WitnessRow(who.Value, WitnessRow.KnewIt)], ActorLook.None);
            CrimeSystem.Report(bw.W, rec, who, sec);
            b.RunawayCrime = rec.Id; b.BountyJob = rec.BountyJob;
        }
        b.Place = -1;
    }

    /// <summary>
    /// The runaway's day: the catchers find him after three days with a daily chance (0x632 draw 1): the bounty goes to the fief's first guard, the slave is back with his owner, chained and (a Harsh owner) branded as a
    /// runaway; or he reaches a realm whose law is none (draw 2, from the sixth day) and is free there: the bond is void in its courts and no bounty can be collected. Returns 0 nothing, 1 caught, 2 voided.
    /// </summary>
    public static int RunawayDay(BondWorld bw, BondBook book, CrimeBook? crimes, Bond b, int day, long sec)
    {
        int out0 = day - b.RunawayDay;
        var person = new EntityId(b.Person); var owner = new EntityId(b.Owner);
        int home = bw.PlaceOfOwner(owner);
        if (out0 >= 6 && bw.NearestFreeTown(home) is var free and >= 0 && DetRng.Range(bw.Seed, day, b.Person, RngStreams.RunawayRoll, 2, 0, 1000) < bw.D.CatchPm)
        { Arrive(bw, book, person, free, day, sec); return 2; }
        if (out0 >= bw.D.CatchAfterDays && DetRng.Range(bw.Seed, day, b.Person, RngStreams.RunawayRoll, 1, 0, 1000) < bw.D.CatchPm) { Caught(bw, book, crimes, b, home, day, sec); return 1; }
        return 0;
    }

    /// <summary>He is brought back (the catchers' bounty is paid, the owner chains him, a Harsh owner brands him a runaway).</summary>
    public static void Caught(BondWorld bw, BondBook book, CrimeBook? crimes, Bond b, int home, int day, long sec)
    {
        var person = new EntityId(b.Person);
        int seat = home >= 0 ? Gaol.SeatOf(bw.W, bw.W.FiefOf(home), home) : -1;
        EntityId collector = EntityId.None; long paid = 0;
        var guards = seat >= 0 ? Guards.Of(bw.People, seat) : [];
        if (guards.Count > 0) collector = bw.People.IdOf(guards[0]);
        if (b.BountyJob != 0 && crimes != null && bw.W.Jobs != null)
        {
            if (collector != EntityId.None && bw.W.PurseOf(collector) is { } pay) paid = Bounties.Collect(bw.W, crimes, new EntityId(b.BountyJob), collector, pay, true, sec);
            else if (bw.W.JobEnv is { } env) bw.W.Jobs.Cancel(env, new EntityId(b.BountyJob), sec);
            if (crimes.Get(b.RunawayCrime) is { } rec) rec.State = CrimeState.Served;
        }
        b.BountyJob = 0; b.RunawayCrime = 0; b.RunawayDay = 0; b.Chain = Restraint.Chained; b.Place = home; b.Morale = bw.D.MoraleStart;
        if (b.Treatment == Treatment.Harsh) { b.FlogDay = day; bw.W.Marks()?.AddBrand(person, Brand.Runaway); }
        var ps = new List<ChronParam> { ChronParam.Person("person", person) };
        if (collector != EntityId.None) ps.Add(ChronParam.Person("collector", collector));
        ps.AddRange(bw.PlaceParams(home));
        var e = Slavery.Write(bw, sec, "slave_caught", "ch_slavery_caught", collector != EntityId.None ? [collector, person] : [person], Slavery.Note("ch_note_slave_caught", b.Entry), ps, ("paid", Slavery.N(paid)));
        b.Entry = e.Id.Value;
    }

    /// <summary>
    /// A person stands in a settlement (the hero walked in, a runaway arrived): a runaway who reaches a settlement whose law is none is free there (4.4): the courts do not return slaves, the owner's bounty is
    /// cancelled and the bond is void. A person who is not a runaway is not touched here (the law looks at owners: <see cref="Observe"/>). Returns whether the bond was voided.
    /// </summary>
    public static bool Arrive(BondWorld bw, BondBook book, EntityId person, int settlement, int day, long sec)
    {
        if (book.Of(person) is not { OnRun: true } b || bw.LawAt(settlement) != SlaveLaw.None) return false;
        b.Place = settlement;
        Slavery.Free(bw, book, person, Slavery.Ending.Voided, day, sec);
        return true;
    }

    // ------------------------------------------------------------------ birth

    /// <summary>Children born to a bonded mother in a chattel law are bonded to her owner (4.1 rule 4); in the other laws they are free (war captives: "children free"). Returns how many.</summary>
    private static int Births(BondWorld bw, BondBook book, int day, long sec)
    {
        if (book.Bonds.Count == 0) return 0;
        var pt = bw.People; int made = 0;
        for (int i = pt.Count - 1, seen = 0; i >= 0 && seen < 400; i--, seen++)
        {
            if (pt.BirthDay[i] < day - 1 || !pt.IsAlive(i) || pt.Mother[i] < 0 || pt.Mother[i] >= pt.Count) continue;
            var mother = pt.IdOf(pt.Mother[i]); var child = pt.IdOf(i);
            if (book.Of(mother) is not { InPen: false, Kind: BondKind.Chattel } mb || book.Has(child) || bw.LawAt(mb.Place) != SlaveLaw.Chattel) continue;
            var e = Slavery.Write(bw, sec, "slave_born", "ch_slavery_born", [child, mother], Slavery.Note("ch_note_slave_mother", mb.Entry), [ChronParam.Person("person", child), ChronParam.Person("mother", mother)]);
            Slavery.Make(bw, book, child, new EntityId(mb.Owner), BondKind.Chattel, BondOrigin.Born, day, mb.Place, bw.D.BasePriceMites * bw.D.ChildPm / 1000, 0, e.Id.Value);
            made++;
        }
        return made;
    }
}
