using Ravensguard.Content;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Bondage;

/// <summary>
/// The bondage hook on the running world's hours (slavery 4.6): at the first hour of each day the captives whose hold ended in a sale go to the pens, the bonds are walked (terms, feeding, tips, trust, runaways, the law's eye,
/// revolts), and on a season's first day the faith counts the slaves held against its teaching, a slaver raid may fall on a weak village (0x630), and the households buy from the pens (0x631). Runs on the tick worker after the
/// hour's own passes. Nothing runs, and no book is made, while nobody is bonded, no captive was sold and no raid falls: a world that never meets slavery keeps its bytes.
/// </summary>
public sealed class BondLink
{
    public WorldRuntime Runtime { get; }
    private BondLink(WorldRuntime rt) { Runtime = rt; }

    public static BondLink Attach(WorldRuntime rt)
    {
        var l = new BondLink(rt);
        rt.AfterHour += l.OnHour;
        return l;
    }

    private void OnHour(long hour)
    {
        if (hour % 24 != 0) return;
        var st = Runtime.State;
        if (st.Population.People.Count == 0 || st.Economy == null || st.Content == null) return;
        Daily(st, st.Content, (int)(hour / 24));
    }

    /// <summary>The day's slavery (the first hour of a day): see the class. Public so a test can run a world's day.</summary>
    public static void Daily(WorldState st, ContentDatabase db, int day)
    {
        bool seasonDay = day > 0 && day % TimeConst.DaysPerSeason == 0;
        var book = st.Bonds;
        bool sold = st.Captivity?.Captives.Values.Any(c => c.State == CaptiveState.Released && c.Note == "sold" && !(book?.Swept.Contains(c.Person.Value) ?? false)) == true;
        if ((book == null || book.IsEmpty) && !sold && !seasonDay) return;
        var bw = BondWiring.World(st, db);
        long sec = day * 86_400L + 60;
        var made = book ?? new BondBook();
        var caps = AftermathDesk.BookOf(st, db);
        if (seasonDay && Raids.Happens(st.Seed, st.HomeRegion.Id, day / TimeConst.DaysPerSeason, st.Difficulty.BanditPct, bw.Rules))
            Raids.Raid(bw, made, caps, caps.Rules, st.HomeRegion.Id, day / TimeConst.DaysPerSeason, day, sec, p => PartyHousehold(st, p));
        if (sold) Raids.Sweep(bw, made, caps, st.Population.Region.Settlements.Count, day, sec);
        if (made.IsEmpty && !seasonDay) { st.Bonds = book; return; }
        if (made.Bonds.Count > 0)
        {
            var crimes = st.Law ?? new CrimeBook();
            BondDaily.Run(bw, made, crimes, day);
            if (!crimes.IsEmpty) st.Law = crimes;
            if (seasonDay)
            {
                Raids.Market(bw, made, day, sec);
                FaithSeason(st, db, bw, made, sec);
                made.LastSeason = day / TimeConst.DaysPerSeason;
            }
        }
        st.Bonds = made.IsEmpty ? null : made;
    }

    /// <summary>Whether a person is of the hero's household (a raid leaves the hero's own people alone: they are the player's to lose in the street, not in the abstract).</summary>
    private static bool PartyHousehold(WorldState st, EntityId p)
    {
        var pt = st.Population.People; int row = pt.IndexOf(p); var hero = st.Party?.Hero ?? EntityId.None;
        int hr = hero == EntityId.None ? -1 : pt.IndexOf(hero);
        return row >= 0 && hr >= 0 && pt.Household[row] >= 0 && pt.Household[row] == pt.Household[hr];
    }

    /// <summary>
    /// The season's sin of keeping slaves against a doctrine (slavery 4.3; faith 4.3, FC-12): for each owner, in id order, the piety lost and the <c>slave_held</c> deed through
    /// <see cref="Ravensguard.Sim.Faith.ChurchServices.SlaveHeldSeason"/> (the owner's religion against each slave's). The faith's own book answers, so a world without parishes pays nothing.
    /// </summary>
    public static void FaithSeason(WorldState st, ContentDatabase db, BondWorld bw, BondBook book, long sec)
    {
        if (st.Church == null) return;
        var religions = Ravensguard.Sim.Faith.FaithWiring.ReligionsOf(st, db);
        var pt = st.Population.People;
        foreach (var g in book.Bonds.Values.Where(b => !b.InPen && new EntityId(b.Owner).Kind is IdKind.Person or IdKind.Household).GroupBy(b => b.Owner).OrderBy(g => g.Key))
        {
            var owner = bw.OwnerPerson(new EntityId(g.Key)); if (owner == EntityId.None) continue;
            int orow = pt.IndexOf(owner); int oreg = orow >= 0 ? religions.OfCulture(pt.Culture[orow]) : -1;
            var slaves = g.OrderBy(b => b.Person).ToList();
            int RelOf(int i) { int r = pt.IndexOf(new EntityId(slaves[i].Person)); return r >= 0 ? religions.OfCulture(pt.Culture[r]) : -1; }
            Ravensguard.Sim.Faith.ChurchServices.SlaveHeldSeason(st, db, owner, oreg, slaves.Count, RelOf, bw.PlaceOfOwner(new EntityId(g.Key)), new GameTime(sec));
        }
    }
}

/// <summary>Builds the <see cref="BondWorld"/> of a running world and the one place the world's other systems ask slavery a question.</summary>
public static class BondWiring
{
    public static BondWorld World(WorldState st, ContentDatabase? db)
    {
        var region = st.Population.Region; var pop = st.Population; var pt = pop.People; var e = st.Economy;
        var w = LawWorld.From(st, db);
        var laws = new SlaveLaw[region.Settlements.Count]; var cultures = new int[region.Settlements.Count];
        for (int s = 0; s < laws.Length; s++)
        {
            cultures[s] = region.Settlements[s].Culture;
            laws[s] = db != null ? BondRules.Parse(Ravensguard.Sim.Faith.ReligionBook.CultureOf(db, cultures[s])?.Slavery) : SlaveLaw.None;
        }
        var hero = st.Party?.Hero ?? EntityId.None;
        int[]? guards = null;
        int GuardCount(int s)
        {
            if (guards == null)
            {
                guards = new int[region.Settlements.Count];
                for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.Occupation[i] == Occupation.Guard && pt.HomeSettlement[i] is var h and >= 0 && h < guards.Length) guards[h]++;
            }
            return s >= 0 && s < guards.Length ? guards[s] : 0;
        }
        return new BondWorld
        {
            W = w, Rules = BondRules.From(db),
            LawAt = s => s >= 0 && s < laws.Length ? laws[s] : SlaveLaw.None,
            LawOfCulture = c => db != null ? BondRules.Parse(Ravensguard.Sim.Faith.ReligionBook.CultureOf(db, c)?.Slavery) : SlaveLaw.None,
            CultureAt = s => s >= 0 && s < cultures.Length ? cultures[s] : -1,
            Maimed = p => st.Justice != null && st.Justice.Marks.Any(m => m.Person == p.Value && m.Kind == "hand"),
            LeadershipOf = p => p == hero ? st.Hero?.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(Skill.Leadership)) ?? 0 : pt.IndexOf(p) is var r and >= 0 ? pt.SkillLevelOf(r, Skill.Leadership) : 0,
            GuardsAt = GuardCount,
            HouseholdHungry = hh => e != null && hh >= 0 && hh < e.HouseholdCount && (e.Grievances[hh] & (Grievance.Hunger | Grievance.MissedMeal)) != 0,
            NearestFreeTown = from =>
            {
                int best = -1; long bd = long.MaxValue;
                for (int s = 0; s < laws.Length; s++) if (laws[s] == SlaveLaw.None && s != from) { long d = w.DistanceM(from, s); if (d < bd) { bd = d; best = s; } }
                return best;
            },
        };
    }

    /// <summary>Whether a person is a slave, for the systems that must skip him (the succession: a bonded kin is not a play heir, 4.4 and SL-12).</summary>
    public static bool IsBonded(WorldState st, EntityId person) => st.Bonds?.Has(person) == true;
}
