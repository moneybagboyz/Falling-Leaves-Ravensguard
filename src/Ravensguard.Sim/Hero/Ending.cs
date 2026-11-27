using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Endings;

/// <summary>Rules of the offer (endings spec 3.1).</summary>
public static class EndingRules
{
    /// <summary>An offer the conditions stop holding for this many game days ends (a lost crown).</summary>
    public const int LapseDays = 30;

    /// <summary>The six endings, one per great ambition (D1).</summary>
    public static IReadOnlyList<string> All => AmbitionTree.Endings;
}

/// <summary>The ended run, kept in the world (endings spec 4, the optional <c>ending</c> section): which ending, the day, the hero's person id and the epilogue's beats. Only keys and ids.</summary>
public sealed class EndingBook
{
    public string EndingId { get; set; } = "";
    public int Day { get; set; }
    public ulong HeroPerson { get; set; }
    public List<EpilogueBeat> Beats { get; } = [];
}

/// <summary>
/// The ending's desk (endings spec 3.1, 3.2): the offer and its 30-day lapse, and taking an ending. Taking one writes the <c>ending_taken</c> entry (caused by the great ambition's), builds the epilogue,
/// hands the hero's person to the NPC AI (his row reads Alive; his property, offices, family and debts are the person's, untouched), lets the companions go as ordinary people and stops the hero's autopilot.
/// </summary>
public static class EndingDesk
{
    /// <summary>Offers the ending of a great ambition; false when it is already on offer, the run is over, or the id is not an ending.</summary>
    public static bool Offer(HeroRecord h, string endingId)
    {
        if (h.EndedAt != null || !EndingRules.All.Contains(endingId) || h.EndingOffers.Exists(o => o.Id == endingId)) return false;
        h.EndingOffers.Add((endingId, -1));
        return true;
    }

    /// <summary>
    /// Daily bookkeeping of the offers: one whose great ambition still holds is kept (its lapse clock is cleared), one that stopped holding starts the clock, and after
    /// <see cref="EndingRules.LapseDays"/> days it ends. Returns the ids that ended.
    /// </summary>
    public static List<string> CheckOffers(HeroRecord h, AmbitionTree tree, AmbitionFacts facts, int day)
    {
        var lapsed = new List<string>();
        for (int i = 0; i < h.EndingOffers.Count; i++)
        {
            var (id, since) = h.EndingOffers[i];
            var amb = tree.All.FirstOrDefault(a => a.Ending == id);
            bool holds = amb != null && amb.Steps.Count > 0 && amb.Steps.All(s => AmbitionRules.StepDone(s, facts));
            if (holds) { if (since != -1) h.EndingOffers[i] = (id, -1); continue; }
            if (since < 0) { h.EndingOffers[i] = (id, day); continue; }
            if (day - since >= EndingRules.LapseDays) { lapsed.Add(id); h.EndingOffers.RemoveAt(i--); }
        }
        return lapsed;
    }

    /// <summary>Whether the run has been ended.</summary>
    public static bool IsEnded(WorldState w) => w.Ending != null || w.Hero?.EndedAt != null;

    /// <summary>
    /// Takes an offered ending at <paramref name="sec"/>. Call it at a tick barrier. Returns the book, or null when the ending is not on offer (or the run has already ended).
    /// Coin, mass and people are not touched: the hero's purse, pack and property stay with his person, who is now simply run by the NPC AI.
    /// </summary>
    public static EndingBook? Take(WorldState w, string endingId, long sec)
    {
        var h = w.Hero;
        if (h == null || w.Ending != null || h.EndedAt != null || !h.EndingOffers.Exists(o => o.Id == endingId)) return null;
        int day = (int)(sec / TimeConst.SecondsPerDay);
        var heroId = w.Party?.Hero ?? EconomyState.HeroOwner;
        var pt = w.Population.People;
        int row = h.PersonIndex >= 0 && h.PersonIndex < pt.Count ? h.PersonIndex : -1;

        // the entry that says it: caused by the great ambition's own
        var great = w.Chronicle.All.Where(e => e.TypeId == "great_ambition_done" && e.Subjects.Contains(heroId)).OrderBy(e => e.Id.Value).LastOrDefault();
        var causes = great != null ? new List<Cause> { new(CauseKind.Office, great.Id) } : [];
        var entry = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "ending_taken", [heroId], causes,
            new Dictionary<string, string> { ["ending"] = endingId }, "ch_p8_ending_taken", [ChronParam.Person("person", heroId)]);
        w.Chronicle.Add(entry);

        var input = InputOf(w, h, heroId, row, endingId, day, entry.Id.Value);
        var book = new EndingBook { EndingId = endingId, Day = day, HeroPerson = heroId.Value };
        book.Beats.AddRange(Epilogue.Build(input));

        // the hero passes to the world: an ordinary person on the NPC AI's books
        if (row >= 0 && pt.Status[row] == PersonStatus.Hero) pt.Status[row] = PersonStatus.Alive;
        if (w.Party != null) foreach (var m in w.Party.Squad.Members) if (m.Active) m.State = HireState.Ended;
        if (w.Autopilot?.Get(heroId) is { } st) { st.Enabled = false; st.Paused = false; }
        h.EndedAt = day; h.EndingOffers.Clear();
        w.Ending = book;
        return book;
    }

    private static EpilogueInput InputOf(WorldState w, HeroRecord h, EntityId heroId, int row, string endingId, int day, ulong anchor)
    {
        var pt = w.Population.People;
        var comps = new List<CompanionFact>();
        if (w.Party != null)
            foreach (var m in w.Party.Squad.Members.OrderBy(m => m.Person.Value))
            {
                int pr = m.PersonIndex >= 0 && m.PersonIndex < pt.Count && pt.IdOf(m.PersonIndex) == m.Person ? m.PersonIndex : pt.IndexOf(m.Person);
                bool dead = pr >= 0 && pr < pt.Count && pt.Status[pr] == PersonStatus.Dead;
                string state = dead ? "dead" : m.State == HireState.Active ? "alive" : "parted";
                comps.Add(new CompanionFact(m.Person.Value, state, CauseOf(w.Chronicle, m.Person, state)));
            }
        var heirs = new List<(ulong Person, int Age)>();
        if (row >= 0)
            for (int i = 0; i < pt.Count; i++)
                if ((pt.Father[i] == row || pt.Mother[i] == row) && pt.Status[i] is PersonStatus.Alive or PersonStatus.Hero) heirs.Add((pt.IdOf(i).Value, pt.AgeYears(i, day)));
        heirs.Sort((a, b) => a.Person.CompareTo(b.Person));
        int holdings = row < 0 || w.Production == null ? 0 : w.Production.Businesses.Count(b => b.Owner == row && b.State == BusinessState.Open);
        long coin = 0;
        if (w.Economy != null && w.Economy.Ledger.TryFind(PurseKind.Person, heroId, out var purse)) coin = w.Economy.Ledger.Balance(purse).Mites;
        int rung = row >= 0 && w.Unrest != null ? w.Unrest.RungOf(pt.HomeSettlement[row]) : 0;
        int best = 0, worst = 0;
        if (w.Standing != null) foreach (var (k, v) in w.Standing.Rows) if (k.Person == heroId.Value) { best = Math.Max(best, v.ValuePm); worst = Math.Min(worst, v.ValuePm); }
        var done = h.AmbitionsDone.OrderBy(d => d.Day).ToList();
        return new EpilogueInput
        {
            Seed = w.Seed, Hero = heroId.Value, EndingId = endingId, Day = day, OriginId = h.Origin, FirstAmbition = done.Count > 0 ? done[0].Id : h.AmbitionId, Ambitions = done,
            Chronicle = w.Chronicle, Companions = comps, Heirs = heirs, Holdings = holdings, CoinMites = coin, UnrestRung = rung, BestStandingPm = best, WorstStandingPm = worst, Anchor = anchor,
        };
    }

    private static ulong CauseOf(Chronicle c, EntityId person, string state)
    {
        if (state == "alive") return 0;
        bool Match(string t) => state == "dead" ? t.Contains("death") || t.Contains("died") || t.Contains("killed") || t.Contains("slain") : t is "squad_member_quit" or "kin_left" or "hired";
        return c.All.Where(e => e.Subjects.Contains(person) && Match(e.TypeId)).OrderBy(e => e.Id.Value).Select(e => e.Id.Value).LastOrDefault();
    }
}
