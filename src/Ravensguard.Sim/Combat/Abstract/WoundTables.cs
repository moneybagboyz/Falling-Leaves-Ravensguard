using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>One recorded blow's wounds (a template of the library, 4.1 e): the records as the resolver made them and the momentum that reached the skull.</summary>
public sealed record WoundTemplate(WoundV2[] Wounds, long SkullMilli);

/// <summary>
/// The wound tables (abstract-battles-v1 3.2), loaded once per content database from the generated rows (<c>abstract/wound_tables.json</c>, <c>abstract/wound_templates.json</c>):
/// per (weapon class, armour class, band, shield) the cumulative permille of the seven outcome classes of one attack action, and per (weapon class, armour class, outcome class) the
/// recorded wounds a hit of that class copies. Integer only. <see cref="ContentHash"/> is the hash of every content row the builder's fights read; the shipped tables carry the hash
/// they were built from, and a stale table fails test AB-1.
/// </summary>
public sealed class WoundTables
{
    private const int W = AbstractClasses.WeaponClasses, A = AbstractClasses.ArmourClasses, B = AbstractClasses.Bands, O = AbstractClasses.Outcomes;
    /// <summary>Cumulative permille by [w][a][band][shield][outcome] (the last is 1000); a cell never built is all miss.</summary>
    private readonly int[] _cum = new int[W * A * B * 2 * O];
    private readonly bool[] _built = new bool[W * A * B * 2];
    private readonly int[] _apm = new int[W * A * B * 2];
    private readonly WoundTemplate[][] _templates = new WoundTemplate[W * AbstractClasses.Groups * O][];

    public string ContentHashBuilt { get; }
    public int BlowsPerCell { get; }
    public int CellsBuilt { get; private set; }
    public int TemplateCount { get; private set; }

    private static int Cell(int w, int a, int band, int shield) => ((w * A + a) * B + band) * 2 + shield;

    public WoundTables(IEnumerable<WoundTableDef> rows, IEnumerable<WoundTemplatesDef> templates, string hash, int blows)
    {
        ContentHashBuilt = hash; BlowsPerCell = blows;
        for (int i = 0; i < _built.Length; i++) _cum[i * O + O - 1] = 1000;
        for (int i = 0; i < _cum.Length; i++) if (i % O != O - 1) _cum[i] = 1000;          // all miss: every cumulative is 1000 from the miss class on
        foreach (var r in rows)
        {
            if (!TryClass(r.Weapon, out WeaponClass wc) || !TryClass(r.Armour, out ArmourClass ac)) continue;
            foreach (var c in r.Cells)
            {
                if (c.Count < WoundTableDef.CellFields || c[0] < 0 || c[0] >= B) continue;
                int cell = Cell((int)wc, (int)ac, c[0], c[1] != 0 ? 1 : 0);
                _apm[cell] = c[3];
                int acc = 0;
                for (int o = 0; o < O; o++) { acc += c[4 + o]; _cum[cell * O + o] = Math.Min(1000, acc); }
                _cum[cell * O + O - 1] = 1000;
                if (!_built[cell]) CellsBuilt++;
                _built[cell] = true;
            }
        }
        foreach (var t in templates)
        {
            int ag = Enumerable.Range(0, AbstractClasses.Groups).FirstOrDefault(g => AbstractClasses.GroupName(g) == t.Armour, -1);
            if (!TryClass(t.Weapon, out WeaponClass wc) || ag < 0) continue;
            foreach (var (name, list) in t.ByClass)
            {
                if (!TryClass(name, out OutcomeClass oc)) continue;
                var arr = list.Select(Decode).Where(x => x != null).Select(x => x!).ToArray();
                _templates[((int)wc * AbstractClasses.Groups + ag) * O + (int)oc] = arr;
                TemplateCount += arr.Length;
            }
        }
    }

    private static bool TryClass<T>(string name, out T value) where T : struct, Enum
    {
        foreach (var v in Enum.GetValues<T>()) if (Flavour.FlavourBook.Snake(v.ToString()) == name) { value = v; return true; }
        value = default; return false;
    }

    public bool Built(WeaponClass w, ArmourClass a, int band, bool shield) => _built[Cell((int)w, (int)a, band, shield ? 1 : 0)];

    /// <summary>The attacker's attack actions per minute x10 in a matchup (4.1.2 b: the live fight's own rate for that weapon, armour, shield and skill delta), interpolated between bands.</summary>
    public int ApmX10(WeaponClass w, ArmourClass a, int delta, bool shield)
    {
        int d = Math.Clamp(delta, -60, 60) + 60, b0 = Math.Min(B - 1, d / 20), b1 = Math.Min(B - 1, b0 + 1), frac = d % 20 * 50, s = shield ? 1 : 0;
        return (_apm[Cell((int)w, (int)a, b0, s)] * (1000 - frac) + _apm[Cell((int)w, (int)a, b1, s)] * frac) / 1000;
    }

    /// <summary>A shooter's shots per minute x10 at a range (the cell's measured rate), 0 when the cell was never built.</summary>
    public int ShotApmX10(WeaponClass w, ArmourClass a, int rangeM, bool shield) => _apm[Cell((int)w, (int)a, AbstractClasses.RangeBand(rangeM), shield ? 1 : 0)];

    /// <summary>The cumulative permille of outcome <paramref name="o"/> in a cell (the share of outcomes at or below it).</summary>
    public int Cum(WeaponClass w, ArmourClass a, int band, bool shield, OutcomeClass o) => _cum[Cell((int)w, (int)a, band, shield ? 1 : 0) * O + (int)o];

    /// <summary>The share (permille) of outcome <paramref name="o"/> in a cell.</summary>
    public int Share(WeaponClass w, ArmourClass a, int band, bool shield, OutcomeClass o) =>
        Cum(w, a, band, shield, o) - (o == 0 ? 0 : Cum(w, a, band, shield, o - 1));

    /// <summary>
    /// The outcome of one attack action for a draw 0..999 (4.1 e): melee rows interpolate linearly between the two skill-delta bands around <paramref name="deltaOrRange"/> (a skill
    /// delta, -60..+60); shooting rows (<paramref name="shot"/>) take the range band of <paramref name="deltaOrRange"/> metres. <paramref name="hitPm"/> scales the landed share
    /// (uphill, rain, night): the scaled-away share becomes a miss.
    /// </summary>
    public OutcomeClass Draw(WeaponClass w, ArmourClass a, int deltaOrRange, bool shield, int roll, bool shot = false, int hitPm = 1000)
    {
        int b0, b1, frac;
        if (shot) { b0 = b1 = AbstractClasses.RangeBand(deltaOrRange); frac = 0; }
        else
        {
            int d = Math.Clamp(deltaOrRange, -60, 60) + 60;
            b0 = Math.Min(B - 1, d / 20); b1 = Math.Min(B - 1, b0 + 1); frac = d % 20 * 50;
        }
        int s = shield ? 1 : 0;
        int c0 = Cell((int)w, (int)a, b0, s) * O, c1 = Cell((int)w, (int)a, b1, s) * O;
        int miss = (_cum[c0] * (1000 - frac) + _cum[c1] * frac) / 1000;
        int landed = 1000 - miss;
        int scaledLanded = (int)Math.Min(1000L, (long)landed * hitPm / 1000);
        int missScaled = 1000 - scaledLanded;
        if (roll < missScaled) return OutcomeClass.Miss;
        // the landed part: the same class mix as the row, over the scaled landed share
        long r = landed <= 0 ? 0 : (long)(roll - missScaled) * landed / Math.Max(1, scaledLanded) + miss;
        for (int o = 1; o < O; o++)
        {
            int cum = (_cum[c0 + o] * (1000 - frac) + _cum[c1 + o] * frac) / 1000;
            if (r < cum) return (OutcomeClass)o;
        }
        return OutcomeClass.Instant;
    }

    /// <summary>The probability (permille) that one attack action lands a wound of serious or worse: the table's part of the Lanchester coefficient (4.2).</summary>
    public int OutPm(WeaponClass w, ArmourClass a, int delta, bool shield, int hitPm = 1000)
    {
        int d = Math.Clamp(delta, -60, 60) + 60, b0 = Math.Min(B - 1, d / 20), b1 = Math.Min(B - 1, b0 + 1), frac = d % 20 * 50, s = shield ? 1 : 0;
        int c0 = Cell((int)w, (int)a, b0, s) * O, c1 = Cell((int)w, (int)a, b1, s) * O;
        int light = (_cum[c0 + (int)OutcomeClass.Light] * (1000 - frac) + _cum[c1 + (int)OutcomeClass.Light] * frac) / 1000;
        int miss = (_cum[c0] * (1000 - frac) + _cum[c1] * frac) / 1000, landed = 1000 - miss;
        if (landed <= 0) return 0;
        long scaled = Math.Min(1000L, (long)landed * hitPm / 1000);
        return (int)((1000 - light) * scaled / landed);                 // the serious-or-worse share of the landed blows, over the landed share the press scales
    }

    /// <summary>The recorded wounds of a class (empty when none were recorded: the hit is then counted with no wound, as armour holding).</summary>
    public WoundTemplate[] Templates(WeaponClass w, ArmourClass a, OutcomeClass o)
    {
        int g = AbstractClasses.Group(a);
        var t = _templates[((int)w * AbstractClasses.Groups + g) * O + (int)o];
        if (t is { Length: > 0 }) return t;
        // fall back to the nearest armour group that has some (a rare class in a thin group)
        for (int d = 1; d < AbstractClasses.Groups; d++)
            foreach (int g2 in new[] { g - d, g + d })
                if (g2 >= 0 && g2 < AbstractClasses.Groups && _templates[((int)w * AbstractClasses.Groups + g2) * O + (int)o] is { Length: > 0 } x) return x;
        return [];
    }

    // ------------------------------------------------------------------ templates as numbers

    public const int WoundFields = 11;

    public static List<long> Encode(ReadOnlySpan<WoundV2> wounds, long skull)
    {
        var l = new List<long>(wounds.Length * WoundFields + 2) { wounds.Length };
        foreach (var w in wounds) l.AddRange([w.Part, w.Tissue, w.Structure, w.Organ, (long)w.Kind, w.DepthPm, w.Grade, w.Count, w.Residual, w.BleedX10, (long)w.Flags]);
        l.Add(skull);
        return l;
    }

    public static WoundTemplate? Decode(List<long> l)
    {
        if (l.Count < 2) return null;
        int n = (int)l[0];
        if (l.Count != n * WoundFields + 2) return null;
        var w = new WoundV2[n];
        for (int i = 0; i < n; i++)
        {
            int b = 1 + i * WoundFields;
            w[i] = new WoundV2
            {
                Part = (short)l[b], Tissue = (short)l[b + 1], Structure = (short)l[b + 2], Organ = (short)l[b + 3], Kind = (WoundKind)l[b + 4], DepthPm = (int)l[b + 5], Grade = (byte)l[b + 6],
                Count = (byte)l[b + 7], Residual = (byte)l[b + 8], BleedX10 = (int)l[b + 9], Flags = (WoundFlags)l[b + 10],
            };
        }
        return new WoundTemplate(w, l[^1]);
    }

    // ------------------------------------------------------------------ loading and the content hash

    private static readonly ConditionalWeakTable<ContentDatabase, WoundTables> Cache = new();

    /// <summary>The tables of a content database (built once from its rows), or null when the database has none (a test fixture without the generated content).</summary>
    public static WoundTables? For(ContentDatabase db)
    {
        if (!db.All<WoundTableDef>().Any()) return null;
        return Cache.GetValue(db, d =>
        {
            var meta = d.All<WoundTableMetaDef>().OrderBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();
            return new WoundTables(d.All<WoundTableDef>().OrderBy(r => r.Id, StringComparer.Ordinal), d.All<WoundTemplatesDef>().OrderBy(r => r.Id, StringComparer.Ordinal), meta?.ContentHash ?? "", meta?.BlowsPerCell ?? 0);
        });
    }

    /// <summary>The builder's version: a change to how cells are fought or classified bumps it (and the hash with it).</summary>
    public const int BuilderVersion = 3;

    /// <summary>
    /// The hash of everything the table builder's fights read (3.2: weapon shapes, armour shapes, materials, body plans, calibration constants): the class members' weapons, the
    /// representative armour, the shields, launchers and ammunition, the human and horse plans, the courser, the melee, morale, mounted and resolver tuning, the stances, and every
    /// material any of those name. Rows nobody here reads (a new site's furniture) do not change it. FNV-1a, 16 hex digits.
    /// </summary>
    public static string ContentHash(ContentDatabase db)
    {
        var keys = new SortedSet<(string, string)>();
        void Add(string type, string id) { if (db.Contains(type, id)) keys.Add((type, id)); }
        for (int w = 0; w < AbstractClasses.WeaponClasses; w++) foreach (var m in AbstractClasses.Members((WeaponClass)w)) Add("weapon_shape", m.ShapeId);
        Add("weapon_shape", "longbow"); Add("weapon_shape", "crossbow"); Add("weapon_shape", "fist");
        for (int a = 0; a < AbstractClasses.ArmourClasses; a++) foreach (var k in AbstractClasses.Kit((ArmourClass)a)) Add("armour_shape", k.ShapeId);
        foreach (var k in FightCalibration.Padded.Worn) Add("armour_shape", k.ShapeId);
        foreach (var k in FightCalibration.Mailed.Worn) Add("armour_shape", k.ShapeId);
        Add("shield_shape", "round_shield"); Add("shield_shape", "heater");
        foreach (var id in db.Ids("launcher")) Add("launcher", id);
        foreach (var id in new[] { "bodkin_arrow", "broadhead_arrow", "quarrel" }) Add("ammo", id);
        Add("body_plan", "human"); Add("body_plan", "horse"); Add("species", "warhorse_courser");
        foreach (var t in new[] { "melee_tuning", "morale_rules", "mounted_tuning", "combat_calibration", "combat_stance" }) foreach (var id in db.Ids(t)) Add(t, id);
        // every material named by those rows (and the materials' own references), to a fixed point
        var mats = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new List<(string, string)>(keys);
        for (int qi = 0; qi < queue.Count; qi++)
        {
            var e = db.GetEntry(queue[qi].Item1, queue[qi].Item2);
            if (e == null) continue;
            foreach (var s in Strings(e.Json))
                if (db.Contains("material", s) && mats.Add(s)) { keys.Add(("material", s)); queue.Add(("material", s)); }
        }
        foreach (var w in new[] { "iron", "steel", "oak", "ash", "bone", "linen", "wool", "leather" }) if (db.Contains("material", w)) keys.Add(("material", w));
        ulong h = 14695981039346656037UL;
        void Mix(string s) { foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; } h ^= 0xFF; h *= 1099511628211UL; }
        Mix("builder:" + BuilderVersion);
        foreach (var (t, id) in keys) { Mix(t); Mix(id); Mix(db.GetEntry(t, id)!.Json.ToJsonString()); }
        return h.ToString("X16");
    }

    private static IEnumerable<string> Strings(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o: foreach (var (_, v) in o) foreach (var s in Strings(v)) yield return s; break;
            case JsonArray a: foreach (var v in a) foreach (var s in Strings(v)) yield return s; break;
            case JsonValue v when v.TryGetValue<string>(out var str): yield return str; break;
        }
    }
}
