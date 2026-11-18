using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production;

/// <summary>A recipe resolved against the good catalogue: integer quantities (grams, litre goods as grams of mass, piece counts) and dense ids.</summary>
public sealed class Recipe
{
    public ushort Index;
    public string Id = "";
    public string Station = "";
    public Skill Skill;
    public int WorkerHours, MaxWorkers, DurationDays, MinBatchPermille, WearPpmPerBatch;
    public (ushort Good, long Qty)[] Inputs = [];
    public (ushort Good, long Qty)[] Outputs = [];
    public long AddsG;
    public bool HasTool;
    public bool Quality;
    public WageClass Wage;
    public long InputMassG(GoodCatalog c) { long s = 0; foreach (var (g, q) in Inputs) s += c.MassG(g, q); return s; }
    public long OutputMassG(GoodCatalog c) { long s = 0; foreach (var (g, q) in Outputs) s += c.MassG(g, q); return s; }
}

public enum WageClass : byte { Unskilled, Skilled, Master }

/// <summary>
/// The recipes of the loaded content (production spec 2.2, 2.3). Loading checks the mass rule <c>outputs &lt;= inputs + adds</c> and that every good and station
/// kind exists, so a recipe like the legacy "2 wool -> 6 cloth" is rejected with a readable message.
/// </summary>
public sealed class RecipeBook
{
    /// <summary>Station kinds recipes may name (production spec 2.2 plus the work bench, candle table and bloomery family the Phase 2 chains need).</summary>
    public static readonly string[] StationKinds =
    [
        "oven", "mill_stones", "malt_floor", "brew_vat", "butcher_block", "cheese_press", "salting_tub", "spinning_wheel", "loom", "fulling_tub", "dye_vat",
        "tailor_bench", "tan_pit", "cobbler_bench", "saddle_tree", "charcoal_clamp", "bloomery", "forge", "anvil", "cupel_hearth", "saw_pit", "brick_clamp", "kiln",
        "lime_kiln", "mason_yard", "barrel_bench", "wheelwright_bay", "work_bench", "candle_table",
    ];

    public const int WageUnskilledPerHour = ItemMath.WageUnskilled, WageSkilledPerHour = ItemMath.WageSkilled, WageMasterPerHour = ItemMath.WageMaster;
    public static int WagePerHour(WageClass c) => c switch { WageClass.Unskilled => WageUnskilledPerHour, WageClass.Skilled => WageSkilledPerHour, _ => WageMasterPerHour };
    /// <summary>Daily wage in mites for a full 10-hour day (150 / 350 / 500, production spec 3.3).</summary>
    public static int WagePerDay(WageClass c) => c switch { WageClass.Unskilled => 150, WageClass.Skilled => 350, _ => 500 };

    public IReadOnlyList<Recipe> Recipes { get; }
    private readonly Dictionary<string, Recipe> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Recipe>> _byStation = new(StringComparer.Ordinal);
    public GoodCatalog Goods { get; }

    private RecipeBook(GoodCatalog goods, List<Recipe> recipes)
    {
        Goods = goods; Recipes = recipes;
        foreach (var r in recipes)
        {
            _byId[r.Id] = r;
            if (!_byStation.TryGetValue(r.Station, out var l)) _byStation[r.Station] = l = [];
            l.Add(r);
        }
    }

    public Recipe this[string id] => _byId.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"unknown recipe '{id}'");
    public bool TryGet(string id, out Recipe r) => _byId.TryGetValue(id, out r!);
    public IReadOnlyList<Recipe> ForStation(string kind) => _byStation.TryGetValue(kind, out var l) ? l : [];

    /// <summary>Resolves one definition; returns the readable problems found (empty when valid). The mass rule is checked in integer grams.</summary>
    public static List<string> Validate(RecipeDef d, GoodCatalog cat, out Recipe? recipe)
    {
        var errs = new List<string>(); recipe = null;
        string at = $"recipe '{d.Id}'";
        if (Array.IndexOf(StationKinds, d.Station) < 0) errs.Add($"{at}: unknown station kind '{d.Station}'");
        if (d.WorkerHours <= 0) errs.Add($"{at}: worker_hours must be positive");
        if (d.MaxWorkers <= 0) errs.Add($"{at}: max_workers must be positive");
        if (d.Inputs.Count == 0 || d.Outputs.Count == 0) errs.Add($"{at}: needs inputs and outputs");
        if (d.MinBatchPermille is < 1 or > 1000) errs.Add($"{at}: min_batch_permille must be 1..1000");
        if (!Enum.TryParse<WageClass>(d.WageClass, true, out var wage)) { errs.Add($"{at}: unknown wage_class '{d.WageClass}'"); wage = WageClass.Skilled; }
        var skill = Skill.None;
        if (d.Skill != "None" && !Enum.TryParse(d.Skill, out skill)) errs.Add($"{at}: unknown skill '{d.Skill}'");
        var ins = Resolve(d.Inputs, cat, at, "input", errs); var outs = Resolve(d.Outputs, cat, at, "output", errs);
        if (errs.Count > 0) return errs;
        var r = new Recipe
        {
            Id = d.Id, Station = d.Station, Skill = skill, WorkerHours = d.WorkerHours, MaxWorkers = d.MaxWorkers, DurationDays = d.DurationDays, MinBatchPermille = d.MinBatchPermille,
            WearPpmPerBatch = d.Tool?.WearPpmPerBatch ?? 0, HasTool = d.Tool != null, Quality = d.Quality, Wage = wage, Inputs = ins, Outputs = outs, AddsG = (long)Math.Round(d.AddsKg * 1000),
        };
        long inG = r.InputMassG(cat), outG = r.OutputMassG(cat);
        if (outG > inG + r.AddsG)
            errs.Add($"{at}: outputs weigh {outG / 1000.0:0.###} kg but inputs weigh {inG / 1000.0:0.###} kg and adds_kg allows {r.AddsG / 1000.0:0.###} kg: matter cannot appear from nowhere");
        else recipe = r;
        return errs;
    }

    private static (ushort, long)[] Resolve(List<RecipeIoDef> io, GoodCatalog cat, string at, string what, List<string> errs)
    {
        var res = new List<(ushort, long)>(); var seen = new HashSet<ushort>();
        foreach (var x in io)
        {
            if (!cat.TryIdOf(x.Good, out var g)) { errs.Add($"{at}: unknown {what} good '{x.Good}'"); continue; }
            if (!seen.Add(g)) { errs.Add($"{at}: good '{x.Good}' listed twice as {what}"); continue; }
            var def = cat.Def(g); long qty;
            switch (def.Unit)
            {
                case GoodUnit.Piece:
                    if (x.Pieces <= 0 || x.Kg != 0 || x.Litres != 0) { errs.Add($"{at}: {what} '{x.Good}' is a counted good, give pieces only"); continue; }
                    qty = x.Pieces; break;
                case GoodUnit.Litre:
                    if (x.Litres > 0) qty = (long)Math.Round(x.Litres * def.LitreMassG);
                    else if (x.Kg > 0) qty = (long)Math.Round(x.Kg * 1000);
                    else { errs.Add($"{at}: {what} '{x.Good}' needs litres or kg"); continue; }
                    break;
                default:
                    if (x.Kg <= 0 || x.Litres != 0 || x.Pieces != 0) { errs.Add($"{at}: {what} '{x.Good}' is a kilogram good, give kg only"); continue; }
                    qty = (long)Math.Round(x.Kg * 1000); break;
            }
            res.Add((g, qty));
        }
        return [.. res];
    }

    /// <summary>Builds the book from the content; throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static RecipeBook Build(ContentDatabase db, GoodCatalog cat)
    {
        var list = new List<Recipe>(); var errs = new List<string>();
        foreach (var d in db.All<RecipeDef>().OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            errs.AddRange(Validate(d, cat, out var r));
            if (r != null) { r.Index = (ushort)list.Count; list.Add(r); }
        }
        if (errs.Count > 0) throw new InvalidDataException(string.Join("\n", errs));
        return new RecipeBook(cat, list);
    }

    /// <summary>Builds a book from explicit definitions (tests).</summary>
    public static RecipeBook Build(IEnumerable<RecipeDef> defs, GoodCatalog cat)
    {
        var list = new List<Recipe>(); var errs = new List<string>();
        foreach (var d in defs.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            errs.AddRange(Validate(d, cat, out var r));
            if (r != null) { r.Index = (ushort)list.Count; list.Add(r); }
        }
        if (errs.Count > 0) throw new InvalidDataException(string.Join("\n", errs));
        return new RecipeBook(cat, list);
    }
}

/// <summary>
/// Fixes every derived good's price from its recipe (production spec 3.6, B2): <c>price_out = (sum inputs + 1.45 x labour) / outputs</c>. Raw goods (no recipe makes
/// them) keep their content price; a good made by several recipes is priced by the lowest recipe id, and co-products share the recipe's cost by mass.
/// At these prices every recipe earns exactly 45% of its wage cost for an owner-operator.
/// </summary>
public static class PriceSolver
{
    public const double LabourMarkup = 1.45;

    /// <summary>Wage cost in mites of one batch: worker-hours x the class wage per hour.</summary>
    public static double Labour(Recipe r) => r.WorkerHours * (double)RecipeBook.WagePerHour(r.Wage);

    /// <summary>Solved price per good in mites per kg (per piece for counted goods). <paramref name="rawOverride"/> replaces content prices of raw goods; <paramref name="pinned"/> stay as in content.</summary>
    public static double[] Solve(RecipeBook book, IReadOnlyDictionary<string, double>? rawOverride = null, IReadOnlySet<string>? pinned = null)
    {
        var cat = book.Goods; int n = cat.Count;
        var owner = new Recipe?[n];
        foreach (var r in book.Recipes) foreach (var (g, _) in r.Outputs) owner[g] ??= r;      // recipes are sorted by id: lowest id owns the good
        var price = new double[n]; var state = new byte[n];
        double Price(ushort g)
        {
            if (state[g] == 2) return price[g];
            if (state[g] == 1) throw new InvalidDataException($"recipe cycle through good '{cat.StringId(g)}'");
            state[g] = 1;
            string id = cat.StringId(g);
            var o = owner[g];
            if (o == null || (pinned != null && pinned.Contains(id)) || (rawOverride != null && rawOverride.ContainsKey(id)))
                price[g] = rawOverride != null && rawOverride.TryGetValue(id, out var ov) ? ov : cat.Def(g).BasePriceMitesPerKg;
            else
            {
                double cost = Labour(o) * LabourMarkup;
                foreach (var (ig, q) in o.Inputs) cost += UnitsOf(cat, ig, q) * Price(ig);
                double massAll = o.OutputMassG(cat);
                // the good's share of the cost by mass, spread over its units
                foreach (var (og, oq) in o.Outputs)
                {
                    double share = cat.MassG(og, oq) / massAll;
                    if (owner[og] == o && !(pinned?.Contains(cat.StringId(og)) ?? false) && !(rawOverride?.ContainsKey(cat.StringId(og)) ?? false))
                    { price[og] = cost * share / UnitsOf(cat, og, oq); state[og] = 2; }
                }
                state[g] = 2;
            }
            state[g] = 2; return price[g];
        }
        for (ushort g = 0; g < n; g++) Price(g);
        return price;
    }

    /// <summary>Units for pricing: kilograms for kg and litre goods, pieces for counted goods.</summary>
    public static double UnitsOf(GoodCatalog cat, ushort good, long qty) => cat.IsPiece(good) ? qty : qty / 1000.0;

    /// <summary>Profit of one full batch at the given prices (mites per kg or piece): revenue - inputs - wages.</summary>
    public static (double Revenue, double Inputs, double Wages, double Profit) BatchEconomics(RecipeBook book, Recipe r, double[] price)
    {
        var cat = book.Goods; double rev = 0, inp = 0;
        foreach (var (g, q) in r.Outputs) rev += UnitsOf(cat, g, q) * price[g];
        foreach (var (g, q) in r.Inputs) inp += UnitsOf(cat, g, q) * price[g];
        double wages = Labour(r);
        return (rev, inp, wages, rev - inp - wages);
    }
}
