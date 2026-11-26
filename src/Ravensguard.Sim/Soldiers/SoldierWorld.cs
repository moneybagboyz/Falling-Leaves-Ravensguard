using System.Globalization;
using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Soldiers;

/// <summary>The sizes of the world's records for the filling rule: a piece's size from the crafting state's fit book (70,000 cm3 when unknown), a soldier's body from the person table (a man 72,000, a woman 62,000, less or more by up to 4,000 on a stable hash of his id).</summary>
public sealed class StateKitFit(WorldState state, Func<PersonTable?> people) : IKitFit
{
    public const int DefaultCm3 = 70_000;
    public int FitOf(ulong item) => state.Crafting?.Fits.TryGetValue(new EntityId(item), out var f) == true ? f : DefaultCm3;

    public bool SetFit(ulong item, int cm3)
    {
        if (state.Crafting == null) return false;
        state.Crafting.Fits[new EntityId(item)] = cm3;
        return true;
    }

    public int BodyOf(ulong person)
    {
        var p = people(); int row = p?.IndexOf(new EntityId(person)) ?? -1;
        bool female = row >= 0 && p!.Sex[row] == Sex.Female;
        return BodyCm3(person, female);
    }

    /// <summary>A body size by id and sex: no draw, an integer hash (the same on every machine).</summary>
    public static int BodyCm3(ulong person, bool female)
    {
        ulong h = person * 0x9E3779B97F4A7C15UL; h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
        return (female ? 62_000 : 72_000) + (int)(h % 8001UL) - 4000;
    }
}

/// <summary>
/// P7-SC-A: the soldiers' runtime over a world state: the roles and templates, the book, the filling rule over the works' armouries and the sizes of the world's records. Built once per state (the desk's
/// <see cref="SoldierWiring.For"/>). Holds no state of its own beyond the book.
/// </summary>
public sealed class SoldierWorld
{
    public SoldierBook Book { get; }
    public TroopRoles Roles { get; }
    public Armouries Armouries { get; }
    public ArmouryFill Fill { get; }
    public LevyGear Levies { get; }
    public IKitFit Fit { get; }
    public Func<PersonTable?> People { get; }
    /// <summary>How many armourers could take a refit job at an armoury (the works' count; 0 when the world has no works).</summary>
    public Func<long, int> ArmourersFree { get; set; } = _ => 0;
    /// <summary>The economy (for the wage roll's purses).</summary>
    public EconomyState Econ { get; }
    /// <summary>Whether a man owns a war horse (SC-B dubbing; horses are not item rows, so the world supplies this; default: none).</summary>
    public Func<ulong, bool> HasWarHorse { get; set; } = _ => false;
    /// <summary>The holding id of a lord (manor, knights_fee, baron, count, ruler) for the retinue cap; empty: no holding known, no cap enforced.</summary>
    public Func<long, string> HoldingOf { get; set; } = _ => "";
    /// <summary>Who of a lord's people are his squad's companions (not counted against the retinue cap).</summary>
    public Func<long, ISet<ulong>> CompanionsOf { get; set; } = _ => new HashSet<ulong>();

    /// <summary>A world (or a test) that keeps its soldiers' purses elsewhere than the household table answers here first; null: the household's.</summary>
    public Func<ulong, PurseId?>? PurseHook { get; set; }
    /// <summary>P7-SC-C: what the world tells the companies' daily choices.</summary>
    public CompanyHooks CompanyHooks { get; } = new();
    /// <summary>P7-SC-D: what the world tells the pens (feeding, sale where legal, a lord's cession, a killing, a broken parole).</summary>
    public PenHooks PenHooks { get; } = new();

    public SoldierWorld(SoldierBook book, ContentDatabase db, EconomyState econ, WorksBook works, LargeBuildingRulesDef rules, IKitFit fit, Func<PersonTable?> people)
    {
        Book = book; Fit = fit; People = people; Econ = econ;
        book.Skills.Base = (p, sk) => { var t = people(); int row = t?.IndexOf(p) ?? -1; return row >= 0 ? t!.SkillLevelOf(row, sk) : 0; };
        Roles = new TroopRoles(db);
        Armouries = new Armouries(works, econ.Items, econ.Goods, db, rules);
        Fill = new ArmouryFill(Roles, Armouries, econ.Items);
        Levies = new LevyGear(Roles, Fill, econ.Items);
    }

    /// <summary>A person's level in a skill (0 for one the table does not know).</summary>
    public int SkillOf(ulong person, Skill s) => Book.Skills.Level(new EntityId(person), s);

    /// <summary>The purse of a soldier's household (none when he has no household).</summary>
    public PurseId PurseOf(ulong person)
    {
        if (PurseHook?.Invoke(person) is { } hooked) return hooked;
        var p = People(); int row = p?.IndexOf(new EntityId(person)) ?? -1;
        if (row < 0) return PurseId.None;
        int hh = p!.Household[row];
        return hh >= 0 && hh < Econ.HouseholdPurse.Length ? Econ.HouseholdPurse[hh] : PurseId.None;
    }

    /// <summary>The skill that orders a role's soldiers in a fill: his best skill among the role's weapon families (the role's own requirement), else Polearms for spearmen and Archery for bowmen.</summary>
    public int SkillFor(ulong person, string role)
    {
        var r = Roles.Role(role); if (r == null) return 0;
        int best = 0;
        foreach (var k in r.Requires.Keys.Concat(r.RequiresAny.Keys))
            if (Enum.TryParse<Skill>(k, out var s) && s is Skill.Blades or Skill.Axes or Skill.Hafted or Skill.Polearms or Skill.Archery or Skill.Throwing) best = Math.Max(best, SkillOf(person, s));
        return best;
    }

    /// <summary>The soldiers of a lord as the filling rule takes them (every row whose lord is <paramref name="lord"/>), by person id.</summary>
    public List<FillSoldier> SoldiersOf(long lord, Func<SoldierRow, bool>? only = null) =>
        [.. Book.Rows.Values.Where(r => r.Lord == lord && (only == null || only(r))).Select(r => new FillSoldier(r.Person, r.Role, r.Template, SkillFor(r.Person, r.Role)))];

    /// <summary>Plans the fill of a lord's soldiers from an armoury (nothing moves).</summary>
    public FillPlan Plan(long armoury, IReadOnlyList<FillSoldier> soldiers) => Fill.Plan(armoury, soldiers, ArmourersFree(armoury), Fit, Book);

    /// <summary>Plans and carries out the fill.</summary>
    public FillResult FillFrom(long armoury, IReadOnlyList<FillSoldier> soldiers, int day, long lord)
    {
        var plan = Plan(armoury, soldiers);
        return Fill.Apply(armoury, plan, Book, Fit, day, lord);
    }
}

/// <summary>Builds and finds the soldiers' runtime of a world state (as <c>IndustryWiring.For</c> does for industries).</summary>
public static class SoldierWiring
{
    private static readonly ConditionalWeakTable<WorldState, SoldierWorld> Loose = new();

    public static SoldierWorld For(WorldState state, ContentDatabase content) => Loose.GetValue(state, s =>
    {
        var econ = s.Economy ?? throw new InvalidOperationException("soldiers need the world's economy");
        var book = s.Soldiers ??= new SoldierBook();
        var works = s.Works ??= new WorksBook();
        var rules = content.All<LargeBuildingRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new LargeBuildingRulesDef { Id = "large_buildings" };
        PersonTable? People() => (s.HomeRecords?.Economy?.Production ?? s.Production)?.Pop.People;
        var sw = new SoldierWorld(book, content, econ, works, rules, new StateKitFit(s, People), People);
        var ww = s.HomeRecords?.Sim.Get<WorksWorld>();
        if (ww != null) sw.ArmourersFree = ww.ArmourersOf;
        return sw;
    });
}
