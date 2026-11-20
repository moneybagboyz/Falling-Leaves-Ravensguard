using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Society;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// Slander (contracts-and-rumours-v2 4.5): a rival with a ready tongue may tell a <b>false</b> rumour about another (a fabricated deed of a kind the standing table knows, never a fire or a death that did not
/// happen) with 5 percent a season (stream 0x620, key author and season); the hero can do it too (Speak ill of). The lie is a chronicle entry of its own type, <c>slander_told</c>, so it never passes for a real crime;
/// its rumour carries the false flag, spreads like any other and moves its victim's standing while believed (the deed rows read it as the crime it claims). It is <b>exposed</b> when a hearer also holds a Witnessed
/// hearing that contradicts it, or when the victim answers it in court (defamation): the victim's standing is given back and the author's falls twice what the lie cost.
/// </summary>
public static class Slander
{
    /// <summary>The deeds a lie may fabricate: crimes the standing table prices, none that would leave a body or a burnt house behind.</summary>
    public static readonly string[] Kinds = ["theft", "burglary", "poaching", "assault", "smuggling"];
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tells the lie to a listener: the chronicle entry (its subjects the victim, whose deed it claims, then the author; the two hearers named in <c>seen</c>), and the slander row. The rumour is made by the next hour's seeder from the entry.</summary>
    public static ChronicleEntry? Tell(ContractWorld w, ContractBook book, EntityId author, EntityId victim, EntityId listener, string kind, int settlement, long sec, bool byHero)
    {
        if (Array.IndexOf(Kinds, kind) < 0 || author == victim) return null;
        var ps = new List<ChronParam> { ChronParam.Person("author", author), ChronParam.Person("victim", victim), ChronParam.TextKey("kind", "ch_slander_kind_" + kind) };
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "slander_told", [victim, author, listener],
            [new Cause(CauseKind.Crime, null, NoteKey: byHero ? "ch_note_slander_hero" : "ch_note_slander_rival")],
            new Dictionary<string, string> { ["kind"] = kind, ["doer"] = w.Name(victim), ["false"] = "1", ["seen"] = N((long)author.Value) + "," + N((long)listener.Value), ["settlement"] = w.PlaceName(settlement), ["author"] = w.Name(author) },
            TextKey: "ch_slander_told", Params: ps);
        w.Chronicle.Add(e);
        book.Slanders.Add(new SlanderRow { Rumour = e.Id.Value, Author = author.Value, Victim = victim.Value, Day = (int)(sec / 86_400) });
        return e;
    }

    /// <summary>
    /// The season's first day (hour 0): for every pair the talk book marks as rivals, in key order and each way round, the one with a Speech of 30 or more tells a lie with the stream's 5 percent
    /// (key the author and the season), to a random adult of his own place. Returns the lies told.
    /// </summary>
    public static List<ChronicleEntry> Season(ContractWorld w, ContractBook book, TalkState ts, int day)
    {
        var told = new List<ChronicleEntry>();
        if (day % TimeConst.DaysPerSeason != 0) return told;
        int season = day / TimeConst.DaysPerSeason;
        if (book.LastSlanderSeason >= season) return told;
        book.LastSlanderSeason = season;
        var pt = w.People;
        foreach (var ((a, b), rel) in ts.Relations.Rows)
        {
            if ((rel.Flags & RelationFlags.Rival) == 0) continue;
            foreach (var (x, y, dir) in new[] { (a, b, 0u), (b, a, 1u) })
            {
                if (x < 0 || y < 0 || x >= pt.Count || y >= pt.Count || !pt.IsAlive(x) || !pt.IsAlive(y)) continue;
                var author = pt.IdOf(x); var victim = pt.IdOf(y);
                if (w.SpeechOf(author) < w.S.SlanderSpeech) continue;
                if (DetRng.Range(w.Seed, season, author.Value, RngStreams.SlanderRoll, dir, 0, 100) >= w.S.SlanderSeasonPct) continue;
                int home = pt.HomeSettlement[x];
                var hearers = new List<int>();
                for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.HomeSettlement[i] == home && i != x && i != y && pt.AgeYears(i, day) >= RumourSeeder.AdultAge) hearers.Add(i);
                if (hearers.Count == 0) continue;
                var listener = pt.IdOf(hearers[DetRng.Range(w.Seed, season, author.Value, RngStreams.SlanderRoll, 2 + dir, 0, hearers.Count)]);
                string kind = Kinds[DetRng.Range(w.Seed, season, author.Value, RngStreams.SlanderRoll, 4 + dir, 0, Kinds.Length)];
                if (Tell(w, book, author, victim, listener, kind, home, day * 86_400L, false) is { } e) told.Add(e);
            }
        }
        return told;
    }

    /// <summary>The hero speaks ill of a person to a listener: a Speech check against the listener's trust in the victim (the skill x 10 and a draw of stream 0x620 keyed on the hero and the season against the trust, permille).</summary>
    public static bool SpeakIll(ContractWorld w, ContractBook book, EntityId victim, EntityId listener, string kind, int settlement, long sec)
    {
        int season = (int)(sec / 86_400 / TimeConst.DaysPerSeason);
        int draw = DetRng.Range(w.Seed, season, w.HeroId.Value, RngStreams.SlanderRoll, 8, 0, 500);
        if (w.SpeechOf(w.HeroId) * 10 + draw <= w.TrustPm(listener, victim)) return false;
        return Tell(w, book, w.HeroId, victim, listener, kind, settlement, sec, true) != null;
    }

    // ------------------------------------------------------------------ exposure

    /// <summary>The slander row of a rumour, or null.</summary>
    public static SlanderRow? RowOf(ContractBook book, EntityId rumour) => book.Slanders.FirstOrDefault(s => s.Rumour == rumour.Value);

    /// <summary>
    /// Looks for lies shown false (hour 0 of each day): a hearer of the lie who also holds a <b>Witnessed</b> hearing, born within a day of the lie, of a true rumour about the victim of another kind, knows
    /// better. Returns the rows exposed. <paramref name="standing"/> and <paramref name="recompute"/> give the victim his standing back at once.
    /// </summary>
    public static List<SlanderRow> Scan(ContractWorld w, ContractBook book, TalkState ts, StandingBook? standing, Action<EntityId>? recompute, int day)
    {
        var shown = new List<SlanderRow>();
        foreach (var row in book.Slanders)
        {
            if (row.Exposed) continue;
            var lie = ts.Rumours.Get(new EntityId(row.Rumour)); if (lie == null) continue;
            bool contradicted = false;
            foreach (var h in ts.Rumours.HearingsOf(lie.Id))
            {
                if (h.Listener < 0) continue;
                foreach (var other in ts.Rumours.HeldBy(h.Listener))
                {
                    if (other == lie.Id) continue;
                    var r2 = ts.Rumours.Get(other);
                    if (r2 == null || r2.False || r2.TypeId == lie.TypeId || !r2.Subjects.Contains(new EntityId(row.Victim)) || Math.Abs(r2.BornDay - row.Day) > 1) continue;
                    if (ts.Rumours.Find(other, h.Listener) is { How: HearHow.Witnessed }) { contradicted = true; break; }
                }
                if (contradicted) break;
            }
            if (contradicted) { Expose(w, book, ts, row, "witness", standing, recompute, day); shown.Add(row); }
        }
        return shown;
    }

    /// <summary>The victim answers the lie in court (defamation, a crime in Valeholt and Seaboard): when the law holds it a crime the author pays the fine to the victim. Either way the lie is shown false.</summary>
    public static bool Answer(ContractWorld w, ContractBook book, TalkState ts, EntityId rumour, bool defamationIsCrime, StandingBook? standing, Action<EntityId>? recompute, int day)
    {
        var row = RowOf(book, rumour); if (row == null || row.Exposed) return false;
        if (defamationIsCrime && w.PurseOf(new EntityId(row.Author)) is { } from && w.PurseOf(new EntityId(row.Victim)) is { } to && from != to)
        {
            long fine = Math.Min(w.S.DefamationFineMites, w.Ledger.Balance(from).Mites);
            if (fine > 0) w.Ledger.Transfer(from, to, new Coin(fine), TransferReason.Restitution);
        }
        Expose(w, book, ts, row, "court", standing, recompute, day);
        return true;
    }

    private static void Expose(ContractWorld w, ContractBook book, TalkState ts, SlanderRow row, string how, StandingBook? standing, Action<EntityId>? recompute, int day)
    {
        row.Exposed = true; row.ExposedDay = day;
        // the victim has his standing back: the deed counts for nothing
        if (standing?.Deed(new EntityId(row.Rumour)) is { } rec) { rec.WeightPm = 0; recompute?.Invoke(new EntityId(row.Victim)); }
        var author = new EntityId(row.Author); var victim = new EntityId(row.Victim);
        var ps = new List<ChronParam> { ChronParam.Person("author", author), ChronParam.Person("victim", victim) };
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L + 3600), "slander_exposed", [author, victim], [new Cause(CauseKind.Crime, new EntityId(row.Rumour))],
            new Dictionary<string, string> { ["how"] = how, ["author"] = w.Name(author), ["victim"] = w.Name(victim), ["doer"] = w.Name(author) }, TextKey: "ch_slander_exposed", Params: ps);
        w.Chronicle.Add(e);
    }

    /// <summary>"A lie, told by Alda": the Journal card's label of an exposed false rumour (the author's name), or empty when the rumour is not shown false.</summary>
    public static string LabelOf(ContractWorld w, ContractBook book, EntityId rumour) => RowOf(book, rumour) is { Exposed: true } r ? w.Name(new EntityId(r.Author)) : "";
}

/// <summary>
/// The age of what is known (spec 4.5): every use of a rumour (an AI lord's knowledge, a contract's posting, standing) reads its age in game days; a fact older than two seasons weighs half. Pure functions, so
/// every reader weighs it the same way (<c>diplomacy-and-ai-lords.md</c> 4.2 reads these).
/// </summary>
public static class RumourAge
{
    public static int AgeDays(Rumour r, int day) => Math.Max(0, day - r.BornDay);
    public static int AgeDays(int bornDay, int day) => Math.Max(0, day - bornDay);
    /// <summary>The weight of a fact of this age, permille: full for two seasons, half after.</summary>
    public static int WeightPm(int ageDays, int staleDays = 2 * TimeConst.DaysPerSeason) => ageDays > staleDays ? 500 : 1000;
    public static int WeightPm(Rumour r, int day, int staleDays = 2 * TimeConst.DaysPerSeason) => WeightPm(AgeDays(r, day), staleDays);
}
