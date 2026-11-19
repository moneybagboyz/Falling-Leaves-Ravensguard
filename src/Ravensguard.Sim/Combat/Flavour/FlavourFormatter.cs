using System.Text;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>A text source with no words: every key reads as itself (the picks do not need prose).</summary>
public sealed class NoText : ITextSource
{
    public static readonly NoText Instance = new();
    public string T(string key) => key;
    public bool Has(string key) => false;
}

/// <summary>
/// Flavour text from the log (feedback-flavour-log 4.1). Two steps: the <b>picks</b> (which template of each slot a line uses: a pure draw on stream 0x480 keyed by the line's tick
/// and serial, with a penalty on pieces used in the last 12 lines, and never the same set as any of them) are made in line order by a cursor and kept in the line, so a line reads the
/// same whoever looked first, in a replay, and after a save; the <b>text</b> is built lazily, only for lines that are shown. Pure: no wall clock, no global random, nothing the sim reads.
/// </summary>
public static class FlavourFormatter
{
    public const int RecentLines = 12;
    const int ComposedWeight = 36;
    const int Attempts = 8;

    // ------------------------------------------------------------------ picks

    /// <summary>Makes the picks of every line below <paramref name="upTo"/> that has none yet, in order.</summary>
    public static void PickUpTo(FightLog log, FlavourBook book, int upTo, bool reduceBlood = false)
    {
        upTo = Math.Min(upTo, log.Lines.Count);
        var buf = new List<CompiledFlavour>(64);
        while (log.PickCursor < upTo) { PickLine(log, book, log.PickCursor, buf, reduceBlood); log.PickCursor++; }
    }

    static void PickLine(FightLog log, FlavourBook book, int index, List<CompiledFlavour> buf, bool reduce)
    {
        var line = log.Lines[index];
        if (line.Picked) return;
        var desc = FlavourDescribe.Describe(log, line, NoText.Instance);
        var picks = new ushort[5];
        if (desc.Slot.Length > 0)
        {
            // what the last lines used
            var recent = new List<int>(RecentLines * 5);
            var tuples = new List<ushort[]>(RecentLines);
            for (int i = index - 1; i >= 0 && i >= index - RecentLines; i--)
            {
                var l = log.Lines[i]; var t = new[] { l.P0, l.P1, l.P2, l.P3, l.P4 };
                tuples.Add(t);
                foreach (var p in t) if (p != 0 && book.ById(log.Str(p)) is { } c) recent.Add(c.Index);
            }
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                picks = PickSet(log, book, line, desc, attempt, recent, buf, reduce);
                bool same = false;
                foreach (var t in tuples) if (t.AsSpan().SequenceEqual(picks)) { same = true; break; }
                if (!same) break;
            }
        }
        var ln = log.Lines[index];
        ln.P0 = picks[0]; ln.P1 = picks[1]; ln.P2 = picks[2]; ln.P3 = picks[3]; ln.P4 = picks[4]; ln.Picked = true;
        log.Lines[index] = ln;
    }

    static ushort[] PickSet(FightLog log, FlavourBook book, in FightLine line, LineDesc d, int attempt, List<int> recent, List<CompiledFlavour> buf, bool reduce)
    {
        var p = new ushort[5];
        uint Draw(int slot) => (uint)(slot * 8 + attempt);
        if (d.Slot == "blow")
        {
            var ctx = d.Ctx;
            book.Candidates("blow", ctx, buf, reduce);
            var special = WeightedOrNone(log, buf, recent, line, Draw(0), ComposedWeight);
            int ci;
            if (special != null) { p[0] = log.Intern(special.Id); ci = 1; }
            else
            {
                book.Candidates("open", ctx, buf, reduce); p[0] = IdOf(log, Weighted(log, buf, recent, line, Draw(1)));
                if (ctx.ArmourOutcome != "none") { book.Candidates("armour", ctx, buf, reduce); p[1] = IdOf(log, Weighted(log, buf, recent, line, Draw(2))); }
                bool terminal = ctx.ArmourOutcome is "stopped" or "deflected";
                if (!terminal && (ctx.Tissue != "none" || line.Kind == FightEventKind.Stopped)) { book.Candidates("tissue", ctx, buf, reduce); p[2] = IdOf(log, Weighted(log, buf, recent, line, Draw(3))); }
                ci = 3;
            }
            for (int k = 0; k < d.Cons.Count && ci < 5; k++, ci++)
            {
                book.Candidates("consequence", d.Cons[k].Ctx, buf, reduce);
                p[ci] = IdOf(log, Weighted(log, buf, recent, line, Draw(4 + k)));
            }
        }
        else
        {
            book.Candidates(d.Slot, d.Ctx, buf, reduce);
            p[0] = IdOf(log, Weighted(log, buf, recent, line, Draw(1)));
        }
        return p;
    }

    static ushort IdOf(FightLog log, CompiledFlavour? c) => c == null ? (ushort)0 : log.Intern(c.Id);

    static CompiledFlavour? Weighted(FightLog log, List<CompiledFlavour> cands, List<int> recent, in FightLine line, uint draw) => WeightedOrNone(log, cands, recent, line, draw, 0);

    /// <summary>A weighted draw over the candidates (a piece used within the last 12 lines counts a quarter); <paramref name="noneWeight"/> is the share of "none of them" (returns null).</summary>
    static CompiledFlavour? WeightedOrNone(FightLog log, List<CompiledFlavour> cands, List<int> recent, in FightLine line, uint draw, int noneWeight)
    {
        if (cands.Count == 0) return null;
        int total = 0;
        for (int i = 0; i < cands.Count; i++) total += Wt(cands[i], recent);
        int span = total + noneWeight * 4;
        int r = DetRng.Range(log.Seed, line.Tick, (ulong)line.Serial + 1, RngStreams.FlavourPick, draw, 0, span);
        if (r >= total) return null;
        for (int i = 0; i < cands.Count; i++) { r -= Wt(cands[i], recent); if (r < 0) return cands[i]; }
        return cands[^1];
    }

    static int Wt(CompiledFlavour c, List<int> recent) => c.Weight * (recent.Contains(c.Index) ? 1 : 4);

    // ------------------------------------------------------------------ text

    /// <summary>The prose of line <paramref name="index"/>: the sentence of the blow and up to two consequences, or the one sentence of the event.</summary>
    public static string Format(FightLog log, int index, FlavourBook book, ITextSource text, bool reduceBlood = false)
    {
        if (index < 0 || index >= log.Lines.Count) return "";
        PickUpTo(log, book, index + 1, reduceBlood);
        var line = log.Lines[index];
        var d = FlavourDescribe.Describe(log, line, text);
        if (d.Slot.Length == 0) return "";
        var sb = new StringBuilder(160);
        string One(ushort id, Dictionary<string, string> p) => book.ById(log.Str(id)) is { } c ? Subst(text.T(c.Def.TextKey), p) : "";
        if (d.Slot == "blow")
        {
            var first = line.P0 != 0 ? book.ById(log.Str(line.P0)) : null;
            int ci;
            if (first != null && first.Def.Slot == "blow")
            {
                sb.Append(Sentence(One(line.P0, d.P)));
                ci = 1;
            }
            else
            {
                var parts = new List<string>(3);
                foreach (var id in new[] { line.P0, line.P1, line.P2 }) if (id != 0) { var s = One(id, d.P); if (s.Length > 0) parts.Add(s); }
                if (parts.Count > 0) sb.Append(Sentence(string.Join(' ', parts)));
                ci = 3;
            }
            var picks = new[] { line.P0, line.P1, line.P2, line.P3, line.P4 };
            for (int k = 0; k < d.Cons.Count && ci < 5; k++, ci++)
            {
                if (picks[ci] == 0) continue;
                var cp = FlavourDescribe.ConsParams(log, d, d.Cons[k].Wound, text);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Sentence(One(picks[ci], cp)));
            }
        }
        else sb.Append(Sentence(One(line.P0, d.P)));
        if (sb.Length == 0) sb.Append(Sentence(Subst(text.T("fl_fallback"), d.P)));
        return Tidy(sb.ToString());
    }

    /// <summary>One sentence of a slot, drawn without the recent-use rule (reports, the chronicle, the callouts): the same inputs always give the same sentence.</summary>
    public static string Say(FlavourBook book, ITextSource text, string slot, in FlavourCtx ctx, Dictionary<string, string> p, ulong seed, long tick, ulong key, uint draw = 0)
    {
        var buf = new List<CompiledFlavour>();
        book.Candidates(slot, ctx, buf);
        if (buf.Count == 0) return "";
        int total = 0; foreach (var c in buf) total += c.Weight;
        int r = DetRng.Range(seed, tick, key, RngStreams.FlavourPick, draw, 0, total);
        foreach (var c in buf) { r -= c.Weight; if (r < 0) return Tidy(Sentence(Subst(text.T(c.Def.TextKey), p))); }
        return "";
    }

    static string Sentence(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return s;
        char last = s[^1];
        if (last is not ('.' or '!' or '?' or '"')) s += ".";
        return s;
    }

    /// <summary>Capital letters at the start of each sentence; double spaces and spaces before punctuation removed.</summary>
    public static string Tidy(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool up = true;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ' && sb.Length > 0 && (sb[^1] == ' ' || i + 1 < s.Length && s[i + 1] is ',' or '.' or ';')) continue;
            sb.Append(up && char.IsLetter(c) ? char.ToUpperInvariant(c) : c);
            if (char.IsLetter(c)) up = false;
            if (c is '.' or '!' or '?' && i + 1 < s.Length && s[i + 1] == ' ') up = true;
        }
        return sb.ToString().Trim();
    }

    /// <summary>Replaces <c>{name}</c> by its value; an unknown name stays as written (test FL-2 finds it).</summary>
    public static string Subst(string template, Dictionary<string, string> p)
    {
        if (template.IndexOf('{') < 0) return template;
        var sb = new StringBuilder(template.Length + 32);
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '{')
            {
                int j = template.IndexOf('}', i + 1);
                if (j > i)
                {
                    string name = template.Substring(i + 1, j - i - 1);
                    if (p.TryGetValue(name, out var v)) { sb.Append(v); i = j; continue; }
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The names a template uses.</summary>
    public static IEnumerable<string> ParamsOf(string template)
    {
        for (int i = 0; i < template.Length; i++)
            if (template[i] == '{') { int j = template.IndexOf('}', i + 1); if (j > i) { yield return template.Substring(i + 1, j - i - 1); i = j; } }
    }

    // ------------------------------------------------------------------ the expanded line: trace table and plain wound list

    /// <summary>The layer trace of a line's blow, one row per layer ("gambeson: cut (need 16.1, momentum 146.9)"), for the expanded log row.</summary>
    public static List<string> TraceTable(FightLog log, int index, ITextSource text)
    {
        var rows = new List<string>();
        if (index < 0 || index >= log.Lines.Count) return rows;
        var b = log.BlowOf(log.Lines[index]);
        if (b == null) return rows;
        foreach (var s in b.Steps)
        {
            string layer = s.IsArmour && s.Piece >= 0 && s.Piece < b.PieceShapes.Length
                ? FlavourDescribe.V(text, "mat", log.Str(b.PieceMaterials[s.Piece])) + " " + FlavourDescribe.V(text, "armour", log.Str(b.PieceShapes[s.Piece]))
                : s.Tissue != 0 ? FlavourDescribe.V(text, "tissue", FlavourDescribe.TissueOf(log, new WoundSnap { Tissue = s.Tissue })) : text.Has("fl_ui_guard") ? text.T("fl_ui_guard") : "guard";
            string o = FlavourDescribe.V(text, "outcome", FlavourBook.Snake(s.Outcome.ToString()));
            string row = s.NeedMilli > 0 ? Subst(text.T("fl_ui_trace_row"), new Dictionary<string, string> { ["layer"] = layer, ["outcome"] = o, ["need"] = Tenths(s.NeedMilli), ["momentum"] = Tenths(s.InMilli) })
                : Subst(text.T("fl_ui_trace_row_plain"), new Dictionary<string, string> { ["layer"] = layer, ["outcome"] = o, ["momentum"] = Tenths(s.InMilli) });
            rows.Add(row);
        }
        return rows;
    }

    static string Tenths(long milli) => $"{milli / 1000}.{Math.Abs(milli % 1000) / 100}";

    /// <summary>The plain wound list of a blow (also the tooltip of the paper-doll): one line per wound.</summary>
    public static List<string> WoundList(FightLog log, int index, ITextSource text)
    {
        var rows = new List<string>();
        if (index < 0 || index >= log.Lines.Count) return rows;
        var b = log.BlowOf(log.Lines[index]);
        if (b == null) return rows;
        foreach (var w in b.Wounds) rows.Add(WoundText(log, w, text));
        return rows;
    }

    public static string WoundText(FightLog log, in WoundSnap w, ITextSource text)
    {
        string part = FlavourDescribe.PartName(text, log.Str(w.Part), out _, out _);
        string tissue = FlavourDescribe.TissueOf(log, w);
        string grade = FlavourDescribe.GradeOf(log, w);
        var bits = new List<string> { FlavourDescribe.V(text, "tissue", tissue) };
        if (grade.Length > 0) bits[0] += " " + FlavourDescribe.V(text, "grade", grade);
        else bits[0] += " " + (w.Kind == WoundKind.Cut ? FlavourDescribe.V(text, "grade", "cut") : FlavourDescribe.V(text, "grade", "bruised"));
        if (w.Structure != 0) bits.Add(FlavourDescribe.V(text, "struct", log.Str(w.Structure)));
        if (w.Organ != 0) bits.Add(FlavourDescribe.V(text, "organ", log.Str(w.Organ)));
        foreach (var f in FlavourBook.FlagNames)
        {
            var fl = FlavourBook.FlagsOf([f]);
            if ((w.Flags & fl) != 0 && fl is WoundFlags.Severed or WoundFlags.BoneBroken or WoundFlags.Compound or WoundFlags.Displaced or WoundFlags.SpineHit or WoundFlags.Concussion or WoundFlags.RibSlip)
                bits.Add(FlavourDescribe.V(text, "flag", f));
        }
        if (w.BleedX10 > 0) bits.Add(Subst(text.T("fl_ui_bleeds"), new Dictionary<string, string> { ["n"] = $"{w.BleedX10 / 10}.{w.BleedX10 % 10}" }));
        return part + ": " + string.Join(", ", bits);
    }

    // ------------------------------------------------------------------ the FlavourHash (test FL-13)

    /// <summary>FNV-1a over the formatted lines of the whole log: equal on both machines, across a save and a load mid-fight.</summary>
    public static ulong Hash(FightLog log, FlavourBook book, ITextSource text)
    {
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < log.Lines.Count; i++)
        {
            string s = Format(log, i, book, text);
            h = FightMath.Mix(h, log.Lines[i].Serial);
            foreach (char ch in s) h = FightMath.Mix(h, ch);
        }
        return h;
    }
}
