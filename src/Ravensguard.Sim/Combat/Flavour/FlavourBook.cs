using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>What a template is matched against: the blow or event reduced to the words the content's <c>when</c> clauses use.</summary>
public struct FlavourCtx
{
    public string Attack, Family, Material, MaterialCat, ArmourOutcome, ArmourFamily, Tissue, Grade, PartGroup, Part, Facing, Target, Style, Event, Cause;
    public int Severity;
    public WoundFlags Flags;
    public bool Called, Chained;

    public static FlavourCtx Blank() => new()
    {
        Attack = "", Family = "", Material = "", MaterialCat = "", ArmourOutcome = "none", ArmourFamily = "", Tissue = "none", Grade = "", PartGroup = "body", Part = "",
        Facing = "front", Target = "human", Style = "normal", Event = "", Cause = "",
    };
}

/// <summary>A template with its condition lists lowered to what the matcher reads.</summary>
public sealed class CompiledFlavour
{
    public required FlavourDef Def { get; init; }
    public int Index { get; init; }
    public string Id => Def.Id;
    public int Weight => Math.Max(1, Def.Weight);
    public bool High => Def.Gore == "high";
    internal WoundFlags FlagsAny;
    internal string[] Attack = [], Family = [], Material = [], ArmourOutcome = [], ArmourFamily = [], Tissue = [], Grade = [], PartGroup = [], Part = [], Facing = [], Target = [], Style = [], Event = [], Cause = [];
    internal int[] Severity = [];

    private static bool Has(string[] list, string v) { for (int i = 0; i < list.Length; i++) if (string.Equals(list[i], v, StringComparison.Ordinal)) return true; return false; }

    public bool Matches(in FlavourCtx c)
    {
        if (Attack.Length > 0 && !Has(Attack, c.Attack)) return false;
        if (Family.Length > 0 && !Has(Family, c.Family)) return false;
        if (Material.Length > 0 && !Has(Material, c.Material) && !Has(Material, c.MaterialCat)) return false;
        if (ArmourOutcome.Length > 0 && !Has(ArmourOutcome, c.ArmourOutcome)) return false;
        if (ArmourFamily.Length > 0 && !Has(ArmourFamily, c.ArmourFamily)) return false;
        if (Tissue.Length > 0 && !Has(Tissue, c.Tissue)) return false;
        if (Grade.Length > 0 && !Has(Grade, c.Grade)) return false;
        if (Severity.Length > 0 && Array.IndexOf(Severity, c.Severity) < 0) return false;
        if (PartGroup.Length > 0 && !Has(PartGroup, c.PartGroup)) return false;
        if (Part.Length > 0 && !Has(Part, c.Part)) return false;
        if (Facing.Length > 0 && !Has(Facing, c.Facing)) return false;
        if (Target.Length > 0 && !Has(Target, c.Target)) return false;
        if (Style.Length > 0 && !Has(Style, c.Style)) return false;
        if (Event.Length > 0 && !Has(Event, c.Event)) return false;
        if (Cause.Length > 0 && !Has(Cause, c.Cause)) return false;
        if (FlagsAny != WoundFlags.None && (FlagsAny & c.Flags) == 0) return false;
        if (Def.When.Called is { } call && call != c.Called) return false;
        if (Def.When.Chained is { } ch && ch != c.Chained) return false;
        return true;
    }
}

/// <summary>
/// Every flavour template of the loaded content, by slot (feedback-flavour-log 3.2, 3.3). Built once from the content database; the formatter picks from it. The English text of a
/// template is the game's <c>tr()</c> of its key.
/// </summary>
public sealed class FlavourBook
{
    /// <summary>The parameters a template may name (test FL-2).</summary>
    public static readonly string[] KnownParams =
    [
        "attacker", "target", "weapon", "weapon_full", "verb", "armour_piece", "armour_material", "armour", "part", "part_base", "side", "tissue", "structure", "organ", "bone", "grade",
        "he", "his", "him", "He", "His", "tgt_he", "tgt_his", "tgt_him", "tgt_He", "tgt_His", "who", "other", "count", "n", "minutes", "place", "trigger", "cause", "name", "number", "weapon_a", "line",
    ];

    public static readonly string[] Slots = ["open", "armour", "tissue", "blow", "consequence", "after", "defence", "shot", "morale", "report"];

    public IReadOnlyList<CompiledFlavour> All { get; }
    private readonly Dictionary<string, List<CompiledFlavour>> _bySlot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompiledFlavour> _byId = new(StringComparer.Ordinal);

    public FlavourBook(ContentDatabase db) : this(db.All<FlavourDef>()) { }

    public FlavourBook(IEnumerable<FlavourDef> defs)
    {
        var list = new List<CompiledFlavour>();
        foreach (var d in defs.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var w = d.When;
            var c = new CompiledFlavour
            {
                Def = d, Index = list.Count,
                Attack = [.. w.Attack], Family = [.. w.Family], Material = [.. w.Material], ArmourOutcome = [.. w.ArmourOutcome], ArmourFamily = [.. w.ArmourFamily], Tissue = [.. w.Tissue],
                Grade = [.. w.Grade], PartGroup = [.. w.PartGroup], Part = [.. w.Part], Facing = [.. w.Facing], Target = [.. w.Target], Style = [.. w.Style], Event = [.. w.Event], Cause = [.. w.Cause],
                Severity = [.. w.Severity], FlagsAny = FlagsOf(w.Flags),
            };
            list.Add(c); _byId[d.Id] = c;
            if (!_bySlot.TryGetValue(d.Slot, out var sl)) _bySlot[d.Slot] = sl = [];
            sl.Add(c);
        }
        All = list;
    }

    public static readonly string[] FlagNames = Enum.GetValues<WoundFlags>().Where(f => f != WoundFlags.None).Select(f => Snake(f.ToString())).ToArray();

    public static string Snake(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++) { if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_'); sb.Append(char.ToLowerInvariant(pascal[i])); }
        return sb.ToString();
    }

    public static WoundFlags FlagsOf(IEnumerable<string> names)
    {
        var f = WoundFlags.None;
        foreach (var n in names)
            foreach (var v in Enum.GetValues<WoundFlags>())
                if (v != WoundFlags.None && Snake(v.ToString()) == n) f |= v;
        return f;
    }

    public CompiledFlavour? ById(string id) => _byId.GetValueOrDefault(id);
    public IReadOnlyList<CompiledFlavour> Slot(string slot) => _bySlot.TryGetValue(slot, out var l) ? l : [];

    /// <summary>The templates of a slot that match, in index order (<paramref name="reduceBlood"/> leaves out the pieces marked high gore).</summary>
    public void Candidates(string slot, in FlavourCtx ctx, List<CompiledFlavour> into, bool reduceBlood = false)
    {
        into.Clear();
        if (!_bySlot.TryGetValue(slot, out var l)) return;
        for (int i = 0; i < l.Count; i++)
        {
            var t = l[i];
            if (reduceBlood && t.High) continue;
            if (t.Matches(ctx)) into.Add(t);
        }
    }

    public int CountMatches(string slot, in FlavourCtx ctx) { var l = new List<CompiledFlavour>(); Candidates(slot, ctx, l); return l.Count; }
}
