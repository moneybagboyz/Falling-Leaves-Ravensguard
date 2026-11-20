using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Law;

/// <summary>One of the ways out of a cell (4.5, 10a): its text key, whether it can be done now, and what it would cost.</summary>
public sealed record WayOut(string Key, bool Available, long Mites = 0);

/// <summary>One charge as the cell shows it: the crime, the sentence the court would give now (with every reason), the proof the court has and where it stands.</summary>
public sealed record ChargeView(int Crime, string Kind, long ValueMites, Expected Expected, int Witnesses, int Attending, bool InAct, bool Confessed, bool Tried, bool Acquitted);

/// <summary>A sentence being served or waiting for its morning.</summary>
public sealed record ServingView(int Crime, string Kind, string Key, long Mites, int Days, int Lashes, int DueDay, int EndDay, SentenceState State);

/// <summary>The cell as the hero sees it (crime-and-justice 6, 10a): his charges with the sentence he can expect, what is being served, when the court sits, the days he has been fed, and every way out.</summary>
public sealed record CellView(int Fief, int Settlement, int SinceDay, int FedDays, int CourtInDays, bool Chained, IReadOnlyList<ChargeView> Charges, IReadOnlyList<ServingView> Serving, IReadOnlyList<WayOut> WaysOut);

/// <summary>One case on the hero's docket: who, what, the proof, the culture's own sentence and every table he may choose in its place.</summary>
public sealed record CaseView(int Crime, ulong Accused, string AccusedName, string Kind, long ValueMites, int Witnesses, int Attending, bool InAct, bool Confessed, bool Proved, Expected Expected, IReadOnlyList<Expected> Alternatives);

/// <summary>The hero's court (4.7): the cases waiting for his judgment on a court day, in crime id order, and the fief they belong to.</summary>
public sealed record CourtView(int Fief, int Day, IReadOnlyList<CaseView> Cases);

/// <summary>Builds the views the screens read, at a barrier (nothing here writes).</summary>
public static class JusticeView
{
    /// <summary>The days until the court next sits (0: today).</summary>
    public static int CourtIn(LawWorld w, int day) { int n = 0; while (n < 1000 && !Court.IsCourtDay(w, day + n)) n++; return n; }

    /// <summary>The hero's cell (null when he is not held).</summary>
    public static CellView? Cell(LawWorld w, CrimeBook cb, JusticeBook jb, EntityId person, int day)
    {
        if (!jb.Gaol.TryGetValue(person.Value, out var row)) return null;
        var charges = new List<ChargeView>();
        foreach (int id in row.Crimes)
        {
            if (cb.Get(id) is not { } rec) continue;
            charges.Add(new ChargeView(rec.Id, rec.Kind, rec.ValueMites, Sentences.Expect(w, cb, jb, rec, person, row.Fief, npcLord: true), rec.Witnesses.Count(x => x.Recognised), Court.Attending(w, rec, row.Settlement).Count,
                row.InAct && rec.Cried, jb.Confessed.Contains(id), false, false));
        }
        var serving = jb.Sentences.Values.Where(s => s.Person == person.Value && s.State == SentenceState.Pending).OrderBy(s => s.Crime)
            .Select(s => new ServingView(s.Crime, s.Kind, s.Key, s.Mites, s.Days, s.Lashes, s.DueDay, s.EndDay, s.State)).ToList();
        var cap = w.Captives?.Find(person);
        var ways = new List<WayOut>();
        var purse = w.PurseOf(person); long coin = purse is { } p && w.Ledger != null ? w.Ledger.Balance(p).Mites : 0;
        var lead = charges.FirstOrDefault(c => c.Expected.Kind is "fine" or "kin_pays" or "confiscation" or "restitution" or "weregild" or "blood_price" or "penance");
        ways.Add(new WayOut("jl_way_pay", lead != null && coin >= lead.Expected.Mites, lead?.Expected.Mites ?? 0));
        long restitution = charges.Sum(c => cb.Get(c.Crime) is { } r && !jb.Restituted.Contains(r.Id) ? r.ValueMites : 0);
        ways.Add(new WayOut("jl_way_restitution", restitution > 0 && coin >= restitution, restitution));
        ways.Add(new WayOut("jl_way_mercy", charges.Any(c => c.Expected.Mercy < 0 || (row.Fief >= 0 && w.StandingWithFief(person, row.Fief) >= w.S.MercyStandingPm))));
        ways.Add(new WayOut("jl_way_clergy", w.LiteracyOf(person) >= w.S.ClergyLiteracy && w.ChurchCourts(row.Settlement)));
        long? floor = row.Gaoler != 0 ? Gaol.BribeFloor(w, w.HonestyOf(new EntityId(row.Gaoler))) : null;
        ways.Add(new WayOut("jl_way_bribe", floor != null && coin >= floor, floor ?? 0));
        ways.Add(new WayOut("jl_way_escape", cap is { Held: true }));
        ways.Add(new WayOut("jl_way_rescue", cap is { Held: true, RescueMembers: > 0 }));
        return new CellView(row.Fief, row.Settlement, row.SinceDay, row.FedDays, CourtIn(w, day), cap?.Restraint == Combat.Aftermath.Restraint.Chained, charges, serving, ways);
    }

    /// <summary>The hero's docket (null when he has none).</summary>
    public static CourtView? Docket(LawWorld w, CrimeBook cb, JusticeBook jb, int day)
    {
        if (jb.Docket.Count == 0) return null;
        var cases = new List<CaseView>(); int fief = -1;
        foreach (int id in jb.Docket)
        {
            if (cb.Get(id) is not { State: CrimeState.Held } rec || !jb.Gaol.TryGetValue((ulong)rec.Perpetrator, out var row)) continue;
            var who = new EntityId((ulong)rec.Perpetrator); fief = row.Fief;
            cases.Add(new CaseView(rec.Id, who.Value, w.Name(who), rec.Kind, rec.ValueMites, rec.Witnesses.Count(x => x.Recognised), Court.Attending(w, rec, row.Settlement).Count, row.InAct && rec.Cried, jb.Confessed.Contains(id),
                Court.Proved(w, jb, rec, row), Sentences.Expect(w, cb, jb, rec, who, row.Fief, npcLord: false), Sentences.Alternatives(w, cb, jb, rec, who)));
        }
        return cases.Count == 0 ? null : new CourtView(fief, day, cases);
    }

    /// <summary>The hero's fief law, by crime kind, for the Law panel: (kind, the culture's key, the lord's own key or empty, the keys allowed).</summary>
    public static IReadOnlyList<(string Kind, string Culture, string Own, IReadOnlyList<string> Allowed)> LawPanel(LawWorld w, JusticeBook jb, int fief)
    {
        int seat = fief >= 0 && fief < w.FiefSeat.Length ? w.FiefSeat[fief] : -1;
        var l = new List<(string, string, string, IReadOnlyList<string>)>();
        foreach (var c in w.Law.Crimes.Where(c => !c.Civil).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            string culture = w.CultureKey(seat, c.Id) ?? w.CultureKey(seat, Sentences.Alias(c.Id)) ?? "fine";
            l.Add((c.Id, culture, jb.Laws.GetValueOrDefault((fief, c.Id), ""), Sentences.Allowed(w, culture)));
        }
        return l;
    }
}
