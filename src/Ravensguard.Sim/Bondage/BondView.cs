using Ravensguard.Content;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Bondage;

/// <summary>One bonded person as a screen shows him: who, age, his best skill, how he came, how he is kept, what he holds and what he asks or cost, how long he has left.</summary>
public sealed record BondRow(ulong Person, string Name, int Age, int Skill, BondKind Kind, BondOrigin Origin, Treatment Treatment, Restraint Chain, BondWork Work, int Morale, long PriceMites, long AskMites, long PeculiumMites,
    int TermDaysLeft, int GrievancePct, bool OnRun, int Place, string PlaceName, ulong Entry);

/// <summary>One pen: the market town, its law and its lots.</summary>
public sealed record PenRow(int Settlement, string Name, SlaveLaw Law, IReadOnlyList<BondRow> Lots);

/// <summary>The hero's own bond when he is enslaved: whose he is, what he works at, the chains, the peculium and the ways out the screen lists (4.4).</summary>
public sealed record HeroBond(BondRow Row, ulong Owner, string OwnerName, bool Night, int LockPm, long PeculiumMites, long PriceMites, bool OwnerMayRefuse);

/// <summary>The bondage screens' snapshot, built at the barrier (immutable: the screen reads, never writes): the hero's slaves (the Ledger's People tab), the pens of the region, and his own bond.</summary>
public sealed record BondView(IReadOnlyList<BondRow> Owned, IReadOnlyList<PenRow> Pens, HeroBond? Hero, int Day, int Freed)
{
    public bool IsEmpty => Owned.Count == 0 && Pens.Count == 0 && Hero == null;

    /// <summary>The view of a world, null when nobody is bonded and no pen stands (a world without slavery shows nothing).</summary>
    public static BondView? Of(WorldState st, ContentDatabase? db, int day)
    {
        var book = st.Bonds; if (book == null || book.IsEmpty) return null;
        var bw = BondWiring.World(st, db); var hero = st.Party?.Hero ?? Slavery.Nobody; var region = st.Population.Region;
        BondRow Row(Bond b)
        {
            var id = new EntityId(b.Person); int row = bw.RowOf(id);
            string name = bw.W.Name(id);
            int age = id == hero ? 30 : bw.AgeOf(id, day);
            var held = new[] { b }; var g = Revolts.Share(bw, held, day);
            return new BondRow(b.Person, name, age, bw.BestSkill(id), b.Kind, b.Origin, b.Treatment, b.Chain, b.Work, b.Morale, b.PriceMites, b.Ask, bw.Peculium(id), b.TermEndDay > 0 ? Math.Max(0, b.TermEndDay - day) : -1,
                g.Aggrieved > 0 ? 100 : 0, b.OnRun, b.Place, bw.W.PlaceName(b.Place), b.Entry);
        }
        var owned = new List<BondRow>();
        if (hero != Slavery.Nobody)
            foreach (var b in book.Bonds.Values.Where(b => !b.InPen && b.Owner == hero.Value && b.Person != hero.Value)) owned.Add(Row(b));
        var pens = new List<PenRow>();
        foreach (var g in book.Bonds.Values.Where(b => b.InPen && b.Place >= 0).GroupBy(b => b.Place).OrderBy(g => g.Key))
            pens.Add(new PenRow(g.Key, g.Key < region.Settlements.Count ? region.Settlements[g.Key].Name : "", bw.LawAt(g.Key), [.. g.OrderBy(b => b.Person).Select(Row)]));
        HeroBond? hb = null;
        if (hero != Slavery.Nobody && book.Of(hero) is { InPen: false } hbond)
        {
            var rules = st.Captivity?.Rules ?? AftermathRules.Default;
            var (_, _, lockp, _, _) = bw.W.SkillsOf(hero);
            long sec = day * 86_400L + 12 * 3600;
            var owner = new EntityId(hbond.Owner);
            hb = new HeroBond(Row(hbond), hbond.Owner, bw.W.Name(bw.OwnerPerson(owner)), EscapeTable.Dark(day * 86_400L + 22 * 3600), EscapeTable.LockPm(rules, lockp, CaptorKind.Slavers, false), bw.Peculium(hero), hbond.PriceMites,
                bw.LawAt(hbond.Place) is SlaveLaw.Chattel or SlaveLaw.WarCaptives);
            _ = sec;
        }
        return new BondView(owned, pens, hb, day, book.FreedTotal);
    }
}
