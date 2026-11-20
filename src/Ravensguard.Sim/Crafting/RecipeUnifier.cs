using Ravensguard.Content;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// One recipe format (crafting-full 3.1, P5-E). The Phase 2 production recipes are <c>craft_recipe</c> rows with <c>business: true</c> (converted once, in content); a mod that still ships
/// Phase 2 <c>recipe</c> rows has them converted at load (<see cref="ToCraft"/>). The Phase 2 recipe book is a derived view of the business recipes (<see cref="ToBook"/>): the inputs per batch are the
/// first alternative of each slot, the worker-hours the attended steps, the calendar days the unattended steps, the station the unified station's Phase 2 kind, so
/// <see cref="Ravensguard.Sim.Production.ProductionSystem"/> runs the same arithmetic and the Phase 2 goldens do not move (CF-1).
/// </summary>
public static class RecipeUnifier
{
    /// <summary>Phase 2 station kinds whose unified station has another id (crafting-full 3.2); every other kind is its own station id.</summary>
    public static readonly IReadOnlyDictionary<string, string> StationOfKind = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["mill_stones"] = "millstone", ["malt_floor"] = "malting_floor", ["saw_pit"] = "sawpit", ["barrel_bench"] = "cooper_bench", ["wheelwright_bay"] = "wheelwright", ["work_bench"] = "workbench",
    };

    private static readonly IReadOnlyDictionary<string, string> CategoryOfKind = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["oven"] = "food", ["mill_stones"] = "food", ["malt_floor"] = "food", ["brew_vat"] = "food", ["butcher_block"] = "food", ["cheese_press"] = "food", ["salting_tub"] = "food",
        ["spinning_wheel"] = "cloth", ["loom"] = "cloth", ["fulling_tub"] = "cloth", ["dye_vat"] = "cloth", ["tailor_bench"] = "cloth",
        ["bloomery"] = "smith", ["forge"] = "smith", ["anvil"] = "smith", ["cupel_hearth"] = "smith",
        ["saw_pit"] = "build", ["work_bench"] = "build", ["charcoal_clamp"] = "build", ["brick_clamp"] = "build", ["mason_yard"] = "build", ["lime_kiln"] = "build", ["kiln"] = "build",
    };

    /// <summary>The difficulty a converted Phase 2 recipe gets by its wage class (Phase 2 had none).</summary>
    public static int DifficultyOf(string wageClass) => wageClass switch { "unskilled" => 10, "master" => 50, _ => 30 };

    /// <summary>The id the recipe book (and saves, business kinds) knows a business recipe by.</summary>
    public static string BookId(CraftRecipeDef d) => d.Migrate ?? d.Id;

    /// <summary>The Phase 2 station kind of a unified station.</summary>
    public static string KindOf(ContentDatabase db, string station) => db.TryGet<CraftStationDef>(station, out var s) ? s.Kind ?? s.Id : station;

    /// <summary>A Phase 2 <c>recipe</c> row in the one format (a mod's old rows, converted at load the way the base content was converted once).</summary>
    public static CraftRecipeDef ToCraft(RecipeDef r)
    {
        var steps = new List<CraftStepDef> { new() { Id = "work", Hours = r.WorkerHours } };
        if (r.DurationDays > 0) steps.Add(new CraftStepDef { Id = "cure", Hours = r.DurationDays * 24, Attended = false, DeadlineH = 24, GraceH = 48 });
        return new CraftRecipeDef
        {
            Id = r.Id, Name = r.Name, Category = CategoryOfKind.GetValueOrDefault(r.Station, "craft"), Skill = r.Skill, Difficulty = DifficultyOf(r.WageClass), Effort = "moderate",
            Components = [.. r.Inputs.Select(i => new List<CraftComponentDef> { new() { Good = i.Good, Kg = i.Kg, Litres = i.Litres, Pieces = i.Pieces } })],
            Tools = r.Tool == null ? [] : [new CraftToolReqDef { Quality = r.Tool.Quality, Level = r.Tool.Min, WearPpmPerBatch = r.Tool.WearPpmPerBatch }],
            Workstation = StationOfKind.GetValueOrDefault(r.Station, r.Station), Steps = steps,
            Results = [.. r.Outputs.Select(o => new CraftResultDef { Good = o.Good, Kg = o.Kg, Litres = o.Litres, Pieces = o.Pieces })],
            AddsKg = r.AddsKg, Batch = new CraftBatchDef { Max = 1, MinPm = r.MinBatchPermille }, Business = true, WorkersMax = r.MaxWorkers, WageClass = r.WageClass,
            BusinessQuality = r.Quality,
        };
    }

    /// <summary>The recipe book row of a business recipe (the derived view; CF-1 compares it field by field with the Phase 2 book).</summary>
    public static RecipeDef ToBook(ContentDatabase db, CraftRecipeDef d)
    {
        double attendedH = 0, waitH = 0;
        foreach (var s in d.Steps) { double h = s.Hours + s.Minutes / 60.0; if (s.Attended) attendedH += h; else waitH += h; }
        var tool = d.Tools.FirstOrDefault(t => t.WearPpmPerBatch > 0);
        return new RecipeDef
        {
            Id = BookId(d), Name = d.Name, Station = d.Workstation == null ? "" : KindOf(db, d.Workstation), Skill = d.Skill, WorkerHours = (int)Math.Round(attendedH), MaxWorkers = d.WorkersMax,
            Inputs = [.. d.Components.Where(c => c.Count > 0).Select(c => new RecipeIoDef { Good = c[0].Good, Kg = c[0].Kg, Litres = c[0].Litres, Pieces = c[0].Pieces })],
            Outputs = [.. d.Results.Where(x => x.Good != null).Select(x => new RecipeIoDef { Good = x.Good!, Kg = x.Kg, Litres = x.Litres, Pieces = x.Pieces })],
            AddsKg = d.AddsKg, Tool = tool == null ? null : new RecipeToolDef { Quality = tool.Quality, Min = tool.Level, WearPpmPerBatch = tool.WearPpmPerBatch },
            DurationDays = (int)(waitH / 24), Quality = d.BusinessQuality, WageClass = d.WageClass, MinBatchPermille = d.Batch.MinPm,
        };
    }

    /// <summary>Every business recipe of the content as recipe book rows, plus a mod's own Phase 2 rows (a craft row with the same book id wins).</summary>
    public static List<RecipeDef> BookDefs(ContentDatabase db)
    {
        var list = new List<RecipeDef>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in db.All<CraftRecipeDef>().Where(d => d.Business).OrderBy(d => d.Id, StringComparer.Ordinal))
            if (ids.Add(BookId(d))) list.Add(ToBook(db, d));
        foreach (var r in db.All<RecipeDef>().OrderBy(r => r.Id, StringComparer.Ordinal))
            if (ids.Add(r.Id)) list.Add(r);
        return list;
    }

    /// <summary>Every recipe in the one format: the craft rows, then a mod's Phase 2 rows converted (skipped when a craft row already has the id or book id).</summary>
    public static List<CraftRecipeDef> CraftDefs(ContentDatabase db)
    {
        var list = db.All<CraftRecipeDef>().ToList();
        var ids = new HashSet<string>(list.Select(d => d.Id).Concat(list.Select(BookId)), StringComparer.Ordinal);
        foreach (var r in db.All<RecipeDef>().OrderBy(r => r.Id, StringComparer.Ordinal))
            if (!ids.Contains(r.Id)) list.Add(ToCraft(r));
        return list;
    }
}
