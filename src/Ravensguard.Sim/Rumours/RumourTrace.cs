using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Rumours;

/// <summary>Walking a rumour back to where it began (dialogue spec 4.7): the chain of tellers behind a person's hearing, and the checks that make a chain valid.</summary>
public static class RumourTrace
{
    /// <summary>The person's own step first, then their teller's, and so on to the root (a person with no teller). Empty when the person holds no hearing.</summary>
    public static List<TraceStep> Walk(RumourTable t, EntityId rumour, int person)
    {
        var res = new List<TraceStep>(); int cur = person;
        for (int guard = 0; guard < 300 && cur >= 0; guard++)
        {
            var h = t.Find(rumour, cur);
            if (h == null) break;
            res.Add(new TraceStep(cur, h.Value.Hour, h.Value.Hop, h.Value.How, h.Value.Teller));
            cur = h.Value.Teller;
        }
        return res;
    }

    /// <summary>
    /// The trace invariants: hops strictly decrease along the chain, each teller held it in an earlier hour than the listener, the chain ends at a root on the rumour's witness list, and the source
    /// entry is in the chronicle. A violation is described in <paramref name="why"/>.
    /// </summary>
    public static bool Valid(RumourTable t, Chronicle chron, EntityId rumour, IReadOnlyList<TraceStep> chain, out string why)
    {
        why = "";
        var r = t.Get(rumour);
        if (r == null) { why = "no such rumour"; return false; }
        if (chain.Count == 0) { why = "empty chain"; return false; }
        if (!r.Authored && chron.Get(r.Source) == null) { why = "source entry missing from the chronicle"; return false; }
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            if (chain[i + 1].Hop >= chain[i].Hop) { why = $"hop does not decrease at link {i}"; return false; }
            if (chain[i + 1].Hour >= chain[i].Hour) { why = $"teller held it no earlier at link {i}"; return false; }
            if (chain[i].Teller != chain[i + 1].Person) { why = $"broken link {i}"; return false; }
        }
        var root = chain[^1];
        if (root.Teller != -1) { why = "chain does not end at a root"; return false; }
        if (!r.Authored && Array.IndexOf(r.Witnesses, root.Person) < 0) { why = "root is not on the witness list"; return false; }
        return true;
    }
}

/// <summary>The words of a rumour as one person tells it (flags change what is said, never the truth kept in the <see cref="Rumour"/>).</summary>
public static class RumourText
{
    public static string Render(Rumour r, Hearing? h, IReadOnlyList<string> settlementNames, Ravensguard.Sim.Dialogue.ITextSource text)
        => Render(r.Key, r.Place, r.Who, r.About, r.Number, h?.Flags ?? Distortion.None, h?.PlaceAlt ?? -1, settlementNames, text);

    /// <summary>The sentence of a rumour under a teller's distortion: vague names no place, exaggerated raises the number by a quarter (rounded up), misattributed names a neighbouring settlement.</summary>
    public static string Render(string key, string place, string who, string about, int number, Distortion flags, short placeAlt, IReadOnlyList<string> settlementNames, Ravensguard.Sim.Dialogue.ITextSource text)
    {
        if ((flags & Distortion.Misattributed) != 0 && placeAlt >= 0 && placeAlt < settlementNames.Count) place = settlementNames[placeAlt];
        else if ((flags & Distortion.Vague) != 0) place = text.T("R_VAGUE_PLACE");
        else if (place.Length == 0) place = text.T("R_UNKNOWN_PLACE");
        if ((flags & Distortion.Exaggerated) != 0) number = (number * 125 + 99) / 100;
        var bag = new Dictionary<string, string> { ["place"] = place, ["number"] = number.ToString(), ["who"] = who, ["about"] = about };
        return Ravensguard.Sim.Dialogue.ParamResolver.Format(text.T(key), bag);
    }

    /// <summary>The confidence label (spec 4.7): first-hand, reliable (hop 1 or less and no flags), hearsay (hop 2 and more) or garbled (flags).</summary>
    public static string Confidence(HeroHearing h) => h.Flags != Distortion.None ? "garbled" : h.How == HearHow.Witnessed ? "witness" : h.Hop <= 1 ? "reliable" : "hearsay";

    /// <summary>Whether what was told differs from the truth (the Codex's "partly true").</summary>
    public static bool PartlyTrue(Distortion flags) => flags != Distortion.None;
}
