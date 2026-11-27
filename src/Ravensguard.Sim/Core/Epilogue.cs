using System.Globalization;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Endings;

/// <summary>
/// One paragraph of an epilogue, as it is saved (endings spec 4): a kind, the variant chosen for it, and only ids, keys and integers. The words are rebuilt from these in whatever language is set, so
/// an ended save reads the same twice and reads in the language of the day. <see cref="Entries"/> are the chronicle entries the paragraph links to (never empty); <see cref="Persons"/> are person ids;
/// <see cref="Ids"/> are content ids or small state words (an ambition, an origin, <c>dead</c>); <see cref="Nums"/> are plain integers (a day, a count, a rung).
/// </summary>
public sealed class EpilogueBeat
{
    public string Kind { get; set; } = "";
    public int Variant { get; set; }
    public ulong[] Entries { get; set; } = [];
    public ulong[] Persons { get; set; } = [];
    public string[] Ids { get; set; } = [];
    public long[] Nums { get; set; } = [];
}

/// <summary>A companion's end for the epilogue: alive (still with the hero), dead or parted, and the chronicle entry that says why (0 none).</summary>
public sealed record CompanionFact(ulong Person, string State, ulong Cause);

/// <summary>What the epilogue is built from (endings spec 3.3): the chronicle and the state at the end. Nothing is projected forward.</summary>
public sealed class EpilogueInput
{
    public required ulong Seed { get; init; }
    public required ulong Hero { get; init; }
    public required string EndingId { get; init; }
    public required int Day { get; init; }
    public string OriginId { get; init; } = "";
    public string FirstAmbition { get; init; } = "";
    public IReadOnlyList<(string Id, int Day)> Ambitions { get; init; } = [];
    public required Chronicle Chronicle { get; init; }
    public IReadOnlyList<CompanionFact> Companions { get; init; } = [];
    /// <summary>Living children of the hero: person id and age in years, in id order.</summary>
    public IReadOnlyList<(ulong Person, int Age)> Heirs { get; init; } = [];
    public int Holdings { get; init; }
    public long CoinMites { get; init; }
    /// <summary>The unrest rung (0..4) of the hero's home place.</summary>
    public int UnrestRung { get; init; }
    /// <summary>The hero's best and worst standing with any group, in per mille (0 when there are none).</summary>
    public int BestStandingPm { get; init; }
    public int WorstStandingPm { get; init; }
    /// <summary>The entry written for taking the ending (the fallback link of every paragraph).</summary>
    public required ulong Anchor { get; init; }
}

/// <summary>What the renderer needs to turn beats into words.</summary>
public sealed class EpilogueText
{
    public required Func<string, string> Tr { get; init; }
    public required Func<ChronParamKind, long, string> NameOf { get; init; }
    public ChronGrammar Grammar { get; init; } = ChronGrammar.English;
    public Func<long, char>? GenderOf { get; init; }
    /// <summary>The name of a content row: "ambition:found_a_dynasty", "origin:tanner". Default: the id with spaces.</summary>
    public Func<string, string>? ContentName { get; init; }
    public Chronicle? Chronicle { get; init; }
}

/// <summary>
/// The epilogue (endings spec 3.3): <see cref="Build"/> picks the beats from the chronicle and the end state, in a fixed order, a variant for each by stream 0x810 keyed on the run and the beat index;
/// <see cref="Render"/> writes a beat's paragraph from its keys. Integer math only, ties by entry id.
/// </summary>
public static class Epilogue
{
    public const int Variants = 3, MinDeeds = 3, MaxDeeds = 6;

    /// <summary>The chronicle weight of an entry that names the hero, by type (0: never an epilogue deed). Unlisted types weigh 10.</summary>
    public static readonly IReadOnlyDictionary<string, int> Weights = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["battle_hero_led"] = 90, ["battle_fought"] = 60, ["battle"] = 60, ["siege_stormed"] = 70, ["fief_granted"] = 80, ["manor_granted"] = 70, ["fief_inherited"] = 70, ["knight_dubbed"] = 80, ["bishop_elected"] = 80,
        ["official_appointed"] = 60, ["charter_granted"] = 60, ["hero_wedding"] = 70, ["marriage"] = 60, ["hero_child_born"] = 50, ["heir_born"] = 50, ["heir_died"] = 60, ["hero_succession"] = 40,
        ["tournament_won"] = 60, ["monster_slain"] = 80, ["site_monster_slain"] = 80, ["relic_taken"] = 70, ["works_founded"] = 60, ["works_upgraded"] = 40, ["business_founded"] = 50, ["faction_founded"] = 80,
        ["fealty_sworn"] = 50, ["outlawed"] = 60, ["murder"] = 70, ["treason"] = 70, ["sentence_hanged"] = 70, ["crime_arrested"] = 40, ["crime_witnessed"] = 30, ["ransom_paid"] = 40, ["lord_ransomed"] = 40,
        ["pilgrimage_done"] = 40, ["miracle_cure"] = 40, ["land_bought"] = 30, ["contract_done"] = 15, ["work_done"] = 12,
        ["ambition_chosen"] = 0, ["ambition_done"] = 0, ["great_ambition_done"] = 0, ["ending_taken"] = 0, ["ending_lapsed"] = 0, ["difficulty_changed"] = 0, ["hero_ageing"] = 0, ["old_age_warning"] = 0,
        ["autopilot_arrived"] = 0, ["autopilot_attacked"] = 0, ["autopilot_bigchoice"] = 0, ["autopilot_deal"] = 0, ["autopilot_injured"] = 0, ["autopilot_job_paid"] = 0, ["autopilot_start"] = 0, ["autopilot_stop"] = 0,
        ["chronicle_summary"] = 0,
    };

    /// <summary>The weight of an entry for the hero: 0 when it does not name him or is never told.</summary>
    public static int WeightOf(ChronicleEntry e, ulong hero)
    {
        if (!Names(e, hero)) return 0;
        return Weights.TryGetValue(e.TypeId, out var w) ? w : e.TypeId.StartsWith("autopilot_", StringComparison.Ordinal) ? 0 : 10;
    }

    private static bool Names(ChronicleEntry e, ulong hero)
    {
        foreach (var s in e.Subjects) if (s.Value == hero) return true;
        if (e.Params != null) foreach (var p in e.Params) if (p.Kind == ChronParamKind.Person && (ulong)p.Value == hero) return true;
        return false;
    }

    // ------------------------------------------------------------------ building

    /// <summary>The beats, in order: origin; each done ambition; the 3 to 6 heaviest deeds (in the order they happened); the companions; the heirs; the holdings; what became of the realm; the ending's own beat.</summary>
    public static List<EpilogueBeat> Build(EpilogueInput x)
    {
        var beats = new List<EpilogueBeat>();
        ulong anchor = x.Anchor;
        var mine = x.Chronicle.All.Where(e => Names(e, x.Hero)).ToList();
        ulong first = mine.Where(e => e.TypeId != "ending_taken").OrderBy(e => e.When.Seconds).ThenBy(e => e.Id.Value).Select(e => e.Id.Value).FirstOrDefault();
        void Add(string kind, ulong[]? entries = null, ulong[]? persons = null, string[]? ids = null, long[]? nums = null)
        {
            var es = entries is { Length: > 0 } ? entries.Where(v => v != 0).ToArray() : [];
            if (es.Length == 0) es = [anchor];
            beats.Add(new EpilogueBeat { Kind = kind, Variant = VariantOf(x, beats.Count), Entries = es, Persons = persons ?? [], Ids = ids ?? [], Nums = nums ?? [] });
        }

        // 1. origin and first ambition
        Add("origin", [first], [x.Hero], [x.OriginId, x.FirstAmbition]);

        // 2. each done ambition with its date and the cause of its last step
        foreach (var (id, day) in x.Ambitions)
        {
            var done = x.Chronicle.All.Where(e => e.TypeId is "ambition_done" or "great_ambition_done" && e.Data.GetValueOrDefault("ambition") == id && Names(e, x.Hero)).OrderBy(e => e.Id.Value).LastOrDefault();
            ulong cause = done?.Causes.Select(c => c.EventId?.Value ?? 0).FirstOrDefault(v => v != 0) ?? 0;
            Add("ambition", cause != 0 ? [done!.Id.Value, cause] : [done?.Id.Value ?? 0], [x.Hero], [id], [day, cause != 0 ? 1 : 0]);
        }

        // 3. the heaviest entries that name the hero (ties by id), told in the order they happened
        var deeds = mine.Select(e => (E: e, W: WeightOf(e, x.Hero))).Where(t => t.W > 0)
            .OrderByDescending(t => t.W).ThenBy(t => t.E.Id.Value).Take(MaxDeeds).Select(t => t.E)
            .OrderBy(e => e.When.Seconds).ThenBy(e => e.Id.Value).ToList();
        foreach (var e in deeds) Add("deed", [e.Id.Value], [x.Hero], [e.TypeId], [e.When.Year]);

        // 4. companions: alive, dead or parted, with the cause
        if (x.Companions.Count == 0) Add("alone", [], [x.Hero]);
        foreach (var c in x.Companions.OrderBy(c => c.Person)) Add("companion", c.Cause != 0 ? [c.Cause] : [], [x.Hero, c.Person], [c.State], [(long)c.Cause]);

        // 5. the heirs and holdings at the end
        Add("heirs", [], [.. x.Heirs.Select(h => h.Person)], [], [x.Heirs.Count, x.Heirs.Count == 0 ? -1 : x.Heirs.Max(h => h.Age)]);
        Add("holdings", [], [x.Hero], [], [x.Holdings, x.CoinMites]);

        // what became of: the realm's unrest rung, the heir's age, the standing
        Add("became", [], [x.Hero], [], [Math.Clamp(x.UnrestRung, 0, 4), x.Heirs.Count == 0 ? -1 : x.Heirs.Max(h => h.Age), x.BestStandingPm, x.WorstStandingPm]);

        // 6. the ending's own beat
        var great = x.Chronicle.All.Where(e => e.TypeId == "great_ambition_done" && Names(e, x.Hero)).OrderBy(e => e.Id.Value).LastOrDefault();
        Add("end", [x.Anchor, great?.Id.Value ?? 0], [x.Hero], [x.EndingId, great?.Data.GetValueOrDefault("ambition") ?? ""], [x.Day / TimeConst.DaysPerYear, x.Heirs.Count]);
        return beats;
    }

    /// <summary>The variant (0 .. <see cref="Variants"/>-1) of beat number <paramref name="index"/>: stream 0x810 keyed on the run (the hero's id) and the beat index.</summary>
    public static int VariantOf(EpilogueInput x, int index) => VariantOf(x.Seed, x.Hero, index);

    public static int VariantOf(ulong seed, ulong run, int index) => new Ravensguard.Sim.People.Rnd(seed, unchecked(run * 1000UL + (ulong)index), RngStreams.EndingEpilogueVariant).Range(0, Variants);

    // ------------------------------------------------------------------ rendering

    /// <summary>The text keys a beat uses (the validator and the language test read these).</summary>
    public static IEnumerable<string> KeysOf(EpilogueBeat b)
    {
        int v = b.Variant + 1;
        switch (b.Kind)
        {
            case "origin": case "deed": case "alone": case "heirs" when b.Nums.Length > 0 && b.Nums[0] > 0: case "holdings": yield return $"ep_{b.Kind}_{v}"; break;
            case "heirs": yield return $"ep_heirs_none_{v}"; break;
            case "ambition": yield return $"ep_ambition_{v}"; if (b.Nums.Length > 1 && b.Nums[1] != 0) yield return $"ep_why_{v}"; break;
            case "companion": yield return $"ep_companion_{(b.Ids.Length > 0 ? b.Ids[0] : "alive")}_{v}"; if (b.Nums.Length > 0 && b.Nums[0] != 0) yield return $"ep_why_{v}"; break;
            case "became":
                yield return $"ep_became_{v}"; yield return $"ep_realm_{Math.Clamp(b.Nums.Length > 0 ? (int)b.Nums[0] : 0, 0, 4)}";
                yield return HeirKey(b); yield return StandKey(b); break;
            case "end": yield return $"ep_end_{(b.Ids.Length > 0 ? b.Ids[0] : "")}_{v}"; break;
        }
    }

    private static string HeirKey(EpilogueBeat b)
    {
        int age = b.Nums.Length > 1 ? (int)b.Nums[1] : -1;
        return age < 0 ? "ep_heir_none" : age < 16 ? "ep_heir_child" : age < 50 ? "ep_heir_grown" : "ep_heir_old";
    }

    private static string StandKey(EpilogueBeat b)
    {
        int best = b.Nums.Length > 2 ? (int)b.Nums[2] : 0, worst = b.Nums.Length > 3 ? (int)b.Nums[3] : 0;
        return worst <= -300 ? "ep_stand_hated" : best >= 300 ? "ep_stand_honoured" : "ep_stand_plain";
    }

    /// <summary>One paragraph, from the beat's keys, ids and integers.</summary>
    public static string Render(EpilogueBeat b, EpilogueText t)
    {
        var ps = new List<ChronParam>();
        var lits = new List<(string Name, string Value, long Id)>();
        string Content(string kind, string id) => id.Length == 0 ? "" : t.ContentName?.Invoke(kind + ":" + id) ?? id.Replace('_', ' ');
        string EntryText(ulong id)
        {
            var e = t.Chronicle?.Get(new EntityId(id));
            return e == null ? t.Tr("ep_lost_entry") : ChronicleTypes.Format(e, t.Tr, t.NameOf, t.Grammar, t.GenderOf);
        }
        ulong hero = b.Persons.Length > 0 ? b.Persons[0] : 0;
        if (b.Persons.Length > 0) ps.Add(ChronParam.Person("person", new EntityId(hero)));
        long N(int i) => i < b.Nums.Length ? b.Nums[i] : 0;
        string id0 = b.Ids.Length > 0 ? b.Ids[0] : "";
        string key = KeysOf(b).First();
        string why = "";
        switch (b.Kind)
        {
            case "origin": lits.Add(("origin", Content("origin", id0), 0)); lits.Add(("ambition", Content("ambition", b.Ids.Length > 1 ? b.Ids[1] : ""), 0)); break;
            case "ambition":
                ps.Add(ChronParam.Count("year", N(0) / TimeConst.DaysPerYear)); lits.Add(("ambition", Content("ambition", id0), 0));
                if (N(1) != 0 && b.Entries.Length > 1) why = Fill($"ep_why_{b.Variant + 1}", [("cause", EntryText(b.Entries[1]), 0)]);
                break;
            case "deed": ps.Add(ChronParam.Count("year", N(0))); lits.Add(("entry", EntryText(b.Entries[0]), 0)); break;
            case "companion":
                if (b.Persons.Length > 1) ps.Add(ChronParam.Person("companion", new EntityId(b.Persons[1])));
                if (N(0) != 0) why = Fill($"ep_why_{b.Variant + 1}", [("cause", EntryText((ulong)N(0)), 0)]);
                break;
            case "heirs":
                ps.Add(ChronParam.Count("n", N(0)));
                lits.Add(("names", string.Join(", ", b.Persons.Select(p => t.NameOf(ChronParamKind.Person, (long)p))), 0)); break;
            case "holdings": ps.Add(ChronParam.Count("n", N(0))); ps.Add(ChronParam.Mites("coin", N(1))); break;
            case "became":
                ps.Add(ChronParam.TextKey("realm", $"ep_realm_{Math.Clamp((int)N(0), 0, 4)}")); ps.Add(ChronParam.TextKey("heir", HeirKey(b))); ps.Add(ChronParam.TextKey("standing", StandKey(b)));
                ps.Add(ChronParam.Count("age", Math.Max(0, N(1)))); break;
            case "end":
                ps.Add(ChronParam.Count("year", N(0))); ps.Add(ChronParam.Count("n", N(1)));
                lits.Add(("ambition", Content("ambition", b.Ids.Length > 1 ? b.Ids[1] : ""), 0)); break;
        }
        string text = Fill(key, lits, ps);
        return why.Length > 0 ? text + " " + why : text;

        string Fill(string k, List<(string, string, long)> l, List<ChronParam>? extra = null)
        {
            var all = extra == null ? ps : [.. ps, .. extra];
            string s = ChronicleTypes.Fill(t.Tr(k), all, t.Tr, t.NameOf, t.Grammar, t.GenderOf);
            foreach (var (name, value, _) in l) s = Literal(s, name, value, t.Grammar);
            return s;
        }
    }

    /// <summary>Replaces <c>{name}</c> and <c>{name.gen}</c> with a literal text.</summary>
    private static string Literal(string s, string name, string value, ChronGrammar g) =>
        s.Replace("{" + name + ".gen}", g.Form("gen", value, '\0'), StringComparison.Ordinal).Replace("{" + name + "}", value, StringComparison.Ordinal);

    /// <summary>All the paragraphs of an epilogue, in order.</summary>
    public static List<string> Paragraphs(IEnumerable<EpilogueBeat> beats, EpilogueText t) => beats.Select(b => Render(b, t)).ToList();

    /// <summary>The year number of a game day (for the text export and the screen).</summary>
    public static string Year(int day) => (day / TimeConst.DaysPerYear).ToString(CultureInfo.InvariantCulture);
}
