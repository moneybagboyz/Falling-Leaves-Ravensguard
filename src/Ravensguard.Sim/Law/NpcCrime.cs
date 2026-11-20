using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Law;

/// <summary>
/// NPC crime, so the law has work (crime-and-justice 4.8, stream 0x614 keyed on household and day): a desperate household (hungry or in arrears) steals from a neighbour with a daily chance, and
/// neighbours come to blows now and then. The abstract tier has no geometry, so the witness is a draw: a resident sees it with 40% by day and 15% at night, and knows the culprit with 70%; an act nobody
/// saw is found by its victim, who reports it with half a chance. Every record has its chain of chronicle entries. Nothing here moves coin: the value is the loss the bounty is measured by.
/// </summary>
public static class NpcCrime
{
    private const uint DrawHit = 0, DrawHour = 1, DrawVictim = 2, DrawValue = 3, DrawWitness = 4, DrawWho = 5, DrawKnown = 6, DrawPerp = 7, DrawVictimReports = 8, DrawAssaultHit = 9;

    public sealed record Tally(int Thefts, int Assaults, int Witnessed, int Reported);

    /// <summary>The day's rolls (the H4 slice): returns what was done. Households in index order, so the result never depends on a hash order.</summary>
    public static Tally Daily(LawWorld w, CrimeBook book, int day)
    {
        var hh = w.Households; if (hh == null || hh.Count == 0) return new Tally(0, 0, 0, 0);
        var s = w.S; var pt = w.People; int thefts = 0, assaults = 0, witnessed = 0, reported = 0;
        // adult residents per settlement, in person order
        var adults = new Dictionary<int, List<int>>();
        for (int i = 0; i < pt.Count; i++)
            if (pt.IsAlive(i) && pt.HomeSettlement[i] >= 0 && pt.AgeYears(i, day) >= 16) { if (!adults.TryGetValue(pt.HomeSettlement[i], out var l)) adults[pt.HomeSettlement[i]] = l = []; l.Add(i); }
        var bySettlement = new Dictionary<int, List<int>>();
        for (int h = 0; h < hh.Count; h++) if (hh.MemberCount[h] > 0 && hh.Settlement[h] >= 0) { if (!bySettlement.TryGetValue(hh.Settlement[h], out var l)) bySettlement[hh.Settlement[h]] = l = []; l.Add(h); }
        for (int h = 0; h < hh.Count; h++)
        {
            if (hh.MemberCount[h] == 0 || hh.Settlement[h] < 0) continue;
            int settlement = hh.Settlement[h]; ulong key = hh.IdOf(h).Value;
            bool theft = w.Desperate(h) && DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawHit, 0, 10_000) < s.NpcTheftPer10k;
            bool assault = !theft && DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawAssaultHit, 0, 10_000) < s.NpcAssaultPer10k;
            if (!theft && !assault) continue;
            if (!bySettlement.TryGetValue(settlement, out var homes) || homes.Count < 2 || !adults.TryGetValue(settlement, out var residents)) continue;
            // the culprit: an adult of the household; the victim: another household of the town (its eldest adult speaks for it)
            var culprits = residents.Where(i => pt.Household[i] == h).ToList(); if (culprits.Count == 0) continue;
            int culprit = culprits[DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawPerp, 0, culprits.Count)];
            var others0 = homes.Where(x => x != h).ToList(); int vh = others0[DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawVictim, 0, others0.Count)];
            var victims = residents.Where(i => pt.Household[i] == vh).ToList(); if (victims.Count == 0) continue;
            int victim = victims[0];
            int hour = DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawHour, 0, 24);
            bool daylight = hour is >= 6 and < 18;
            long value = theft ? 20 + DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawValue, 0, Math.Max(1, s.NpcStealMaxMites - 20 + 1)) : 0;
            string kind = theft ? "theft" : "assault";
            var doer = pt.IdOf(culprit); var vic = pt.IdOf(victim);
            var a = new ActSpec(kind, doer, vic, settlement, day * 86_400L + hour * 3600L, value, 0, theft ? "ch_note_crime_hunger" : "ch_note_crime_quarrel");
            bool seen = DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawWitness, 0, 100) < (daylight ? s.WitnessDayPct : s.WitnessNightPct);
            CrimeRecord rec;
            if (seen)
            {
                var others = residents.Where(i => pt.Household[i] != h && pt.Household[i] != vh).ToList();
                if (others.Count == 0) { rec = CommitUnseen(w, book, a, vic, key, day); }
                else
                {
                    int wi = others[DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawWho, 0, others.Count)];
                    bool knew = DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawKnown, 0, 100) < 70;
                    rec = CrimeSystem.Commit(w, book, a, [new WitnessRow(pt.IdOf(wi).Value, knew ? WitnessRow.KnewIt : (byte)0)], ActorLook.None);
                    witnessed++;
                }
            }
            else rec = CommitUnseen(w, book, a, vic, key, day);
            if (theft) thefts++; else assaults++;
            if (rec.State == CrimeState.Reported) reported++;
        }
        return new Tally(thefts, assaults, witnessed, reported);
    }

    /// <summary>Nobody saw it: the victim finds the loss and reports it with half a chance (the rest stay a grumble: Seen, with no witness).</summary>
    private static CrimeRecord CommitUnseen(LawWorld w, CrimeBook book, ActSpec a, EntityId victim, ulong key, int day)
    {
        var rec = CrimeSystem.Commit(w, book, a, [], ActorLook.None);
        if (DetRng.Range(w.Seed, day, key, RngStreams.NpcCrime, DrawVictimReports, 0, 100) < w.S.NpcVictimReportPct) CrimeSystem.Report(w, rec, victim, a.Sec);
        return rec;
    }
}
