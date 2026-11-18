using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Crafting;

/// <summary>One alternative of a component slot, resolved: a good and a quantity (grams for kg and litre goods, a count for pieces).</summary>
public sealed record CompAlt(ushort Good, long Qty, bool Keep, string? NeedsStep);

/// <summary>A component slot: the first alternative the crafter can supply is used.</summary>
public sealed class CompSlot { public CompAlt[] Alts = []; }

public sealed record ToolNeed(string Quality, int Level);

public sealed class CStep
{
    public string Id = "";
    public long Seconds;
    public bool Attended = true, Critical, Outdoor, Conditional;
    public long DeadlineSec, GraceSec;
    public string? Container;
    public ushort RuinedInto = ItemTable.NoGood;
    public ushort FuelGood = ItemTable.NoGood;
    public long FuelQty;
}

public sealed class CResult
{
    public ushort Good = ItemTable.NoGood;
    /// <summary>Item shape and its material when the result is an item instance.</summary>
    public string? Shape, Material;
    /// <summary>Quantity of the good (grams or pieces) for a goods result; for an item, the count made per batch unit is 1.</summary>
    public long Qty;
    /// <summary>Mass in grams (of the good's quantity, or of the item).</summary>
    public long MassG;
    public bool IsItem => Shape != null;
}

public sealed record CByproduct(ushort Good, long Qty, int ChancePm, long MassG);
public sealed record ProfUse(ushort Prof, int TimeMultPm, int SkillPenalty);

/// <summary>A recipe resolved against the good catalogue and the people skill list: dense ids, integer quantities, seconds.</summary>
public sealed class CraftRecipe
{
    public ushort Index;
    public string Id = "", Name = "", Category = "", Effort = "light", Culture = "any", Secret = "none", Note = "", Effect = "";
    public Skill Skill;
    public int Difficulty, MinLight, Morale, BatchMax = 1, TimeScalePm = 800;
    public CompSlot[] Slots = [];
    public ToolNeed[] Tools = [];
    public string[] Using = [];
    public string? Workstation;
    public SortedDictionary<string, int> MinAttr = [];
    public CStep[] Steps = [];
    public CResult[] Results = [];
    public CByproduct[] Byproducts = [];
    public ProfUse[] Profs = [];
    public long AddsG;
    public bool KnownByAll, Decomp, MasterTeaches = true;
    public Skill AutoSkill; public int AutoLevel = -1;
    public int WatchHoursX10; public string? Manuscript;
    public bool IsPractice; public int PracticeDx100, PracticeCap = 100;
    public CraftRepairDef? Repair;
    public bool IsRepair => Repair != null;
    public long AttendedSeconds() { long s = 0; foreach (var st in Steps) if (st.Attended && !st.Conditional) s += st.Seconds; return s; }
    public bool HasUnattended { get { foreach (var s in Steps) if (!s.Attended) return true; return false; } }
}

/// <summary>
/// The recipe database (basic-crafting spec 3.1 to 3.4): every <c>craft_recipe</c> of the content with its goods, qualities, requirement sets and proficiencies resolved and checked at load.
/// Recipes get dense ids by ordinal string order. A bad file fails with its file, line and recipe id, and every problem is listed (test K-1), including a cycle in the proficiency graph.
/// </summary>
public sealed class RecipeDb
{
    public static readonly string[] Categories = ["fire", "food", "medical", "cloth", "craft", "smith", "practice"];
    public static readonly string[] Efforts = ["light", "moderate", "heavy"];

    public GoodCatalog Goods { get; }
    public IReadOnlyList<CraftRecipe> Recipes { get; }
    public IReadOnlyList<ProficiencyDef> Proficiencies { get; }
    public IReadOnlyDictionary<string, RequirementSetDef> Sets { get; }
    public IReadOnlyDictionary<string, CraftStationDef> Stations { get; }
    public IReadOnlyDictionary<string, CraftToolDef> Tools { get; }
    public IReadOnlyList<ManuscriptDef> Manuscripts { get; }
    public IReadOnlyCollection<string> KnownQualities { get; }
    private readonly Dictionary<string, CraftRecipe> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ushort> _profIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ushort> _manuscriptIndex = new(StringComparer.Ordinal);

    private RecipeDb(GoodCatalog goods, List<CraftRecipe> recipes, List<ProficiencyDef> profs, Dictionary<string, RequirementSetDef> sets, Dictionary<string, CraftStationDef> stations,
        Dictionary<string, CraftToolDef> tools, List<ManuscriptDef> manuscripts, SortedSet<string> qualities)
    {
        Goods = goods; Recipes = recipes; Proficiencies = profs; Sets = sets; Stations = stations; Tools = tools; Manuscripts = manuscripts; KnownQualities = qualities;
        foreach (var r in recipes) _byId[r.Id] = r;
        for (int i = 0; i < profs.Count; i++) _profIndex[profs[i].Id] = (ushort)i;
        for (int i = 0; i < manuscripts.Count; i++) _manuscriptIndex[manuscripts[i].Id] = (ushort)i;
    }

    public CraftRecipe this[string id] => _byId.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"unknown craft recipe '{id}'");
    public CraftRecipe this[ushort index] => Recipes[index];
    public bool TryGet(string id, out CraftRecipe r) => _byId.TryGetValue(id, out r!);
    public ushort ProfIndex(string id) => _profIndex[id];
    public ProficiencyDef Prof(ushort i) => Proficiencies[i];
    public ManuscriptDef? Manuscript(string id) => _manuscriptIndex.TryGetValue(id, out var i) ? Manuscripts[i] : null;

    /// <summary>The tool requirements of a recipe together with those of its requirement sets (highest level per quality).</summary>
    public ToolNeed[] AllNeeds(CraftRecipe r)
    {
        var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in r.Tools) d[t.Quality] = Math.Max(d.GetValueOrDefault(t.Quality), t.Level);
        foreach (var u in r.Using) if (Sets.TryGetValue(u, out var s)) foreach (var q in s.Qualities) d[q.Quality] = Math.Max(d.GetValueOrDefault(q.Quality), q.Level);
        return [.. d.Select(kv => new ToolNeed(kv.Key, kv.Value))];
    }

    public bool NeedsWater(CraftRecipe r) => r.Using.Any(u => Sets.TryGetValue(u, out var s) && s.Water);

    /// <summary>Mass check of spec 4.1: <c>outputs + by-products &lt;= inputs + adds</c>, in grams, with the lightest alternative of each slot.</summary>
    public long InputMassG(CraftRecipe r)
    {
        long s = 0;
        foreach (var slot in r.Slots) s += slot.Alts.Min(a => Goods.MassG(a.Good, a.Qty));
        return s;
    }

    public long OutputMassG(CraftRecipe r) => r.Results.Sum(x => x.MassG) + r.Byproducts.Sum(b => b.MassG);

    public static RecipeDb Build(ContentDatabase db, GoodCatalog cat)
    {
        var errs = new List<string>();
        var result = Build(db, cat, errs);
        if (errs.Count > 0) throw new InvalidDataException(string.Join("\n", errs));
        return result!;
    }

    /// <summary>Builds the database; every problem is added to <paramref name="errs"/> (with file and line) and the result is null when there are any.</summary>
    public static RecipeDb? Build(ContentDatabase db, GoodCatalog cat, List<string> errs)
    {
        string Loc(string type, string id) => db.GetEntry(type, id) is { } e ? $"{e.Location.File}({e.Location.Line})" : type;
        var tools = db.All<CraftToolDef>().OrderBy(t => t.Id, StringComparer.Ordinal).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var stations = db.All<CraftStationDef>().OrderBy(t => t.Id, StringComparer.Ordinal).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var sets = db.All<RequirementSetDef>().OrderBy(t => t.Id, StringComparer.Ordinal).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var profs = db.All<ProficiencyDef>().OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        var manus = db.All<ManuscriptDef>().OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        var qualities = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in tools.Values) foreach (var q in t.Qualities.Keys) qualities.Add(q);
        foreach (var s in stations.Values) foreach (var q in s.Provides.Keys) qualities.Add(q);

        // proficiency graph: every requirement exists and there is no cycle
        var pIds = profs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in profs)
            foreach (var q in p.Requires) if (!pIds.Contains(q.Id)) errs.Add($"{Loc("proficiency", p.Id)}: proficiency '{p.Id}' requires unknown proficiency '{q.Id}'");
        foreach (var cyc in FindCycle(profs)) errs.Add($"proficiency graph has a cycle: {cyc}");
        foreach (var s in sets.Values)
            foreach (var q in s.Qualities) if (!qualities.Contains(q.Quality)) errs.Add($"{Loc("requirement_set", s.Id)}: requirement set '{s.Id}' needs quality '{q.Quality}' that no tool or station provides");
        foreach (var m in manus)
            foreach (var r in m.Recipes) if (!db.Contains("craft_recipe", r)) errs.Add($"{Loc("manuscript", m.Id)}: manuscript '{m.Id}' teaches unknown recipe '{r}'");

        var list = new List<CraftRecipe>();
        foreach (var d in db.All<CraftRecipeDef>().OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var local = new List<string>();
            var r = Resolve(d, db, cat, tools, stations, sets, pIds, qualities, manus, local);
            foreach (var e in local) errs.Add($"{Loc("craft_recipe", d.Id)}: recipe '{d.Id}': {e}");
            if (r != null && local.Count == 0) { r.Index = (ushort)list.Count; list.Add(r); }
        }
        if (errs.Count > 0) return null;
        // the proficiency list order is the ordinal order (profs came sorted); recipe prof ids resolve against it
        var idx = new Dictionary<string, ushort>(StringComparer.Ordinal);
        for (int i = 0; i < profs.Count; i++) idx[profs[i].Id] = (ushort)i;
        foreach (var r in list)
        {
            var d = db.Get<CraftRecipeDef>(r.Id);
            r.Profs = [.. d.Proficiencies.Select(p => new ProfUse(idx[p.Id], p.TimeMultPm, p.SkillPenalty))];
        }
        return new RecipeDb(cat, list, profs, sets, stations, tools, manus, qualities);
    }

    private static IEnumerable<string> FindCycle(List<ProficiencyDef> profs)
    {
        var by = profs.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 visiting, 2 done
        var found = new List<string>();
        void Visit(string id, List<string> path)
        {
            if (!by.TryGetValue(id, out var p)) return;
            if (state.GetValueOrDefault(id) == 2) return;
            if (state.GetValueOrDefault(id) == 1) { found.Add(string.Join(" -> ", path.SkipWhile(x => x != id).Append(id))); return; }
            state[id] = 1; path.Add(id);
            foreach (var q in p.Requires) Visit(q.Id, path);
            path.RemoveAt(path.Count - 1); state[id] = 2;
        }
        foreach (var p in profs) Visit(p.Id, []);
        return found;
    }

    private static long Qty(CraftComponentDef x, GoodCatalog cat, ushort g, List<string> errs, string what)
    {
        var def = cat.Def(g);
        switch (def.Unit)
        {
            case GoodUnit.Piece: if (x.Pieces <= 0 || x.Kg != 0 || x.Litres != 0) { errs.Add($"{what} '{x.Good}' is a counted good, give pieces only"); return 0; } return x.Pieces;
            case GoodUnit.Litre:
                if (x.Litres > 0) return (long)Math.Round(x.Litres * def.LitreMassG);
                if (x.Kg > 0) return (long)Math.Round(x.Kg * 1000);
                errs.Add($"{what} '{x.Good}' needs litres or kg"); return 0;
            default: if (x.Kg <= 0 || x.Litres != 0 || x.Pieces != 0) { errs.Add($"{what} '{x.Good}' is a kilogram good, give kg only"); return 0; } return (long)Math.Round(x.Kg * 1000);
        }
    }

    private static CraftRecipe? Resolve(CraftRecipeDef d, ContentDatabase db, GoodCatalog cat, Dictionary<string, CraftToolDef> tools, Dictionary<string, CraftStationDef> stations,
        Dictionary<string, RequirementSetDef> sets, HashSet<string> profIds, SortedSet<string> qualities, List<ManuscriptDef> manuscripts, List<string> errs)
    {
        var r = new CraftRecipe { Id = d.Id, Name = d.Name, Category = d.Category, Effort = d.Effort, Difficulty = d.Difficulty, MinLight = d.MinLight, Morale = d.Morale, Culture = d.Culture, Secret = d.Secret, Note = d.Note, Effect = d.Effect ?? "" };
        if (Array.IndexOf(Categories, d.Category) < 0) errs.Add($"unknown category '{d.Category}'");
        if (Array.IndexOf(Efforts, d.Effort) < 0) errs.Add($"unknown effort '{d.Effort}'");
        if (d.Difficulty is < 0 or > 100) errs.Add("difficulty must be 0..100");
        if (d.MinLight is < 0 or > 255) errs.Add("min_light must be 0..255");
        r.Skill = Skill.None;
        if (d.Skill != "None" && !Enum.TryParse(d.Skill, out r.Skill)) errs.Add($"unknown skill '{d.Skill}'");
        if (d.Steps.Count == 0) errs.Add("needs at least one step");

        // components
        var slots = new List<CompSlot>(); var stepIds = d.Steps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < d.Components.Count; i++)
        {
            if (d.Components[i].Count == 0) { errs.Add($"component slot {i + 1} has no alternative"); continue; }
            var alts = new List<CompAlt>();
            foreach (var a in d.Components[i])
            {
                if (!cat.TryIdOf(a.Good, out var g)) { errs.Add($"unknown component good '{a.Good}' in slot {i + 1}"); continue; }
                long q = Qty(a, cat, g, errs, "component");
                if (a.NeedsStep != null && !stepIds.Contains(a.NeedsStep)) errs.Add($"component '{a.Good}' needs unknown step '{a.NeedsStep}'");
                alts.Add(new CompAlt(g, q, a.Keep, a.NeedsStep));
            }
            slots.Add(new CompSlot { Alts = [.. alts] });
        }
        r.Slots = [.. slots];

        // tools, sets, workstation
        r.Tools = [.. d.Tools.Select(t => new ToolNeed(t.Quality, t.Level))];
        foreach (var t in d.Tools) if (!qualities.Contains(t.Quality)) errs.Add($"unknown tool quality '{t.Quality}'");
        foreach (var u in d.Using) if (!sets.ContainsKey(u)) errs.Add($"unknown requirement set '{u}'");
        r.Using = [.. d.Using];
        if (d.Workstation != null && !stations.ContainsKey(d.Workstation)) errs.Add($"unknown workstation '{d.Workstation}'");
        r.Workstation = d.Workstation;
        foreach (var (a, v) in d.MinAttr.OrderBy(k => k.Key, StringComparer.Ordinal)) { if (a is not ("strength" or "toughness" or "dexterity" or "perception" or "wits")) errs.Add($"unknown attribute '{a}'"); r.MinAttr[a] = v; }
        foreach (var p in d.Proficiencies) if (!profIds.Contains(p.Id)) errs.Add($"unknown proficiency '{p.Id}'");

        // steps
        var seen = new HashSet<string>(StringComparer.Ordinal); var steps = new List<CStep>(); int critical = 0;
        foreach (var s in d.Steps)
        {
            if (!seen.Add(s.Id)) errs.Add($"step '{s.Id}' listed twice");
            long sec = (long)Math.Round(s.Minutes * 60.0 + s.Hours * 3600.0);
            if (sec <= 0) errs.Add($"step '{s.Id}' has no time");
            var cs = new CStep { Id = s.Id, Seconds = sec, Attended = s.Attended, Critical = s.Critical, Outdoor = s.Outdoor, Container = s.Container,
                Conditional = d.Components.Any(c => c.Any(a => a.NeedsStep == s.Id)) };
            if (s.Critical) critical++;
            if (!s.Attended)
            {
                if (s.DeadlineH <= 0 || s.GraceH <= 0) errs.Add($"unattended step '{s.Id}' needs deadline_h and grace_h above zero");
                cs.DeadlineSec = (long)Math.Round(s.DeadlineH * 3600.0); cs.GraceSec = (long)Math.Round(s.GraceH * 3600.0);
                if (s.RuinedInto != null)
                {
                    if (!cat.TryIdOf(s.RuinedInto, out var ri)) errs.Add($"step '{s.Id}' ruins into unknown good '{s.RuinedInto}'");
                    else if (cat.IsPiece(ri)) errs.Add($"step '{s.Id}' ruins into a counted good '{s.RuinedInto}'; ruined work is kg or litres");
                    else cs.RuinedInto = ri;
                }
                else if (cat.TryIdOf("waste", out var w)) cs.RuinedInto = w;
                if (s.Critical) errs.Add($"unattended step '{s.Id}' cannot be the critical step");
            }
            if (s.Fuel != null)
            {
                if (!cat.TryIdOf(s.Fuel.Good, out var fg)) errs.Add($"unknown fuel good '{s.Fuel.Good}'");
                else { cs.FuelGood = fg; cs.FuelQty = Qty(s.Fuel, cat, fg, errs, "fuel"); }
            }
            steps.Add(cs);
        }
        if (critical > 1) errs.Add("only one step may be critical");
        if (steps.Count > 0 && steps.All(s => !s.Attended)) errs.Add("needs an attended step");
        r.Steps = [.. steps];

        // results, by-products, mass
        var results = new List<CResult>();
        foreach (var x in d.Results)
        {
            if (x.Item != null)
            {
                if (!db.Contains("armour_shape", x.Item) && !db.Contains("weapon_shape", x.Item)) errs.Add($"unknown item shape '{x.Item}'");
                if (x.Material == null || !db.Contains("material", x.Material)) errs.Add($"item '{x.Item}' needs a known material (got '{x.Material}')");
                if (x.Kg <= 0) errs.Add($"item '{x.Item}' needs its mass in kg");
                results.Add(new CResult { Shape = x.Item, Material = x.Material, Qty = 1, MassG = (long)Math.Round(x.Kg * 1000) });
            }
            else if (x.Good != null && cat.TryIdOf(x.Good, out var g))
            {
                long q = Qty(new CraftComponentDef { Good = x.Good, Kg = x.Kg, Litres = x.Litres, Pieces = x.Pieces }, cat, g, errs, "result");
                results.Add(new CResult { Good = g, Qty = q, MassG = cat.MassG(g, q) });
            }
            else errs.Add($"unknown result good '{x.Good}'");
        }
        r.Results = [.. results];
        var bps = new List<CByproduct>();
        foreach (var b in d.Byproducts)
        {
            if (!cat.TryIdOf(b.Good, out var g)) { errs.Add($"unknown by-product good '{b.Good}'"); continue; }
            long q = Qty(new CraftComponentDef { Good = b.Good, Kg = b.Kg, Pieces = b.Pieces }, cat, g, errs, "by-product");
            bps.Add(new CByproduct(g, q, b.ChancePm, cat.MassG(g, q)));
        }
        r.Byproducts = [.. bps];
        r.AddsG = (long)Math.Round(d.AddsKg * 1000);
        r.BatchMax = Math.Max(1, d.Batch.Max); r.TimeScalePm = d.Batch.TimeScalePm;

        // what the finished work does
        r.IsPractice = d.Practice != null;
        if (d.Practice != null) { r.PracticeDx100 = d.Practice.GainDx100; r.PracticeCap = d.Practice.CapLevel; if (r.Results.Length > 0) errs.Add("a practice recipe gives no item"); }
        r.Repair = d.Repair;
        if (d.Repair != null && d.Repair.Target is not ("garment" or "tool" or "blade")) errs.Add($"unknown repair target '{d.Repair.Target}'");
        if (r.Results.Length == 0 && !r.IsPractice && !r.IsRepair && string.IsNullOrEmpty(r.Effect)) errs.Add("makes nothing: give results, practice, repair or effect");

        // mass: outputs + by-products <= inputs + adds (spec 4.1)
        long inG = 0; foreach (var slot in r.Slots) if (slot.Alts.Length > 0) inG += slot.Alts.Min(a => cat.MassG(a.Good, a.Qty));
        long outG = r.Results.Sum(x => x.MassG) + r.Byproducts.Sum(b => b.MassG);
        if (outG > inG + r.AddsG) errs.Add($"outputs weigh {outG / 1000.0:0.###} kg but inputs weigh {inG / 1000.0:0.###} kg and adds_kg allows {r.AddsG / 1000.0:0.###} kg: matter cannot appear from nowhere");

        // learning
        var l = d.Learn; r.KnownByAll = l.KnownByAll; r.Decomp = l.Decomp; r.MasterTeaches = l.Master;
        r.WatchHoursX10 = l.WatchHours > 0 ? (int)Math.Round(l.WatchHours * 10) : CraftMath.WatchHoursX10(d.Difficulty);
        if (l.AutoAt != null)
        {
            if (!Enum.TryParse(l.AutoAt.Skill, out Skill ask)) errs.Add($"unknown auto_at skill '{l.AutoAt.Skill}'"); else { r.AutoSkill = ask; r.AutoLevel = l.AutoAt.Level; }
        }
        if (l.Manuscript != null)
        {
            var m = manuscripts.FirstOrDefault(x => x.Id == l.Manuscript);
            if (m == null) errs.Add($"unknown manuscript '{l.Manuscript}'");
            else { r.Manuscript = l.Manuscript; if (!m.Recipes.Contains(d.Id)) errs.Add($"manuscript '{l.Manuscript}' does not list this recipe"); }
        }
        return r;
    }

    /// <summary>Plain-text dump (tests, tools).</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (var r in Recipes) sb.Append(r.Id).Append(' ').Append(r.Skill).Append(r.Difficulty).Append(' ').Append(r.Steps.Length).AppendLine();
        return sb.ToString();
    }
}
