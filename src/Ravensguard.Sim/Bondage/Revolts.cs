using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Bondage;

/// <summary>
/// The slave side of revolts (slavery 4.5): a holding with ten or more bonded people has a grievance share (the hungry, the Harsh-treated, the flogged in the last fifteen days, those parted from kin by a sale); when it is
/// above half, the guards (free armed adults) are fewer than a fifth of the bonded, and a bonded person with Leadership of 40 or more lives there, a revolt starts with P a day of <c>(share - 50) / 10</c> permille
/// (stream 0x633, key holding and day). The bonded break their chains, the survivors go as a free band led by that person, and the chronicle's causes name the floggings and the hunger. The riot machinery itself
/// (guards fighting in the bubble, abstract battle outside, the band as a bandit group) is unrest-and-revolt's (E1): here the hook is <see cref="RebelBand"/> and the entry <c>slave_revolt</c>.
/// </summary>
public static class Revolts
{
    /// <summary>The grievance share of a holding's bonded people in percent and the people it counted (hungry, harsh, flogged lately, separated lately).</summary>
    public sealed record Grievance(int Bonded, int Aggrieved, int Hungry, int Harsh, int Flogged, int Separated)
    {
        public int SharePct => Bonded == 0 ? 0 : Aggrieved * 100 / Bonded;
    }

    public static Grievance Share(BondWorld bw, IReadOnlyList<Bond> held, int day)
    {
        int aggrieved = 0, hungry = 0, harsh = 0, flogged = 0, separated = 0;
        foreach (var b in held)
        {
            bool h = b.HungryDays >= bw.D.HungryDaysGrievance, ha = b.Treatment == Treatment.Harsh, f = day - b.FlogDay <= bw.D.GrievanceDays, s = day - b.SeparatedDay <= bw.D.GrievanceDays;
            if (h) hungry++; if (ha) harsh++; if (f) flogged++; if (s) separated++;
            if (h || ha || f || s) aggrieved++;
        }
        return new Grievance(held.Count, aggrieved, hungry, harsh, flogged, separated);
    }

    /// <summary>The permille-of-ten-thousand chance a day of a share (percent): <c>(share - 50) / 10</c> permille, i.e. share - 50 in ten-thousandths; 0 at or under half.</summary>
    public static int DailyChance(int sharePct, int thresholdPct = 50) => Math.Max(0, sharePct - thresholdPct);

    /// <summary>The bonds a holding (a settlement) keeps at hand: not in a pen, not on the run, not owned by a band.</summary>
    public static IEnumerable<IGrouping<int, Bond>> Holdings(BondBook book) =>
        book.Bonds.Values.Where(b => !b.InPen && !b.OnRun && b.Place >= 0 && new EntityId(b.Owner).Kind != IdKind.Other).GroupBy(b => b.Place).OrderBy(g => g.Key);

    /// <summary>The day's revolt checks (H5 hour 0): returns how many holdings rose.</summary>
    public static int Daily(BondWorld bw, BondBook book, int day, long sec)
    {
        int rose = 0;
        foreach (var g in Holdings(book).ToList())
        {
            var held = g.ToList();
            if (held.Count < bw.D.RevoltMinBonded) continue;
            var share = Share(bw, held, day);
            if (share.SharePct <= bw.D.RevoltSharePct) continue;
            if (bw.GuardsAt(g.Key) * bw.D.RevoltGuardDiv >= held.Count) continue;
            var leader = held.Where(b => bw.LeadershipOf(new EntityId(b.Person)) >= bw.D.RevoltLeaderLeadership).OrderByDescending(b => bw.LeadershipOf(new EntityId(b.Person))).ThenBy(b => b.Person).FirstOrDefault();
            if (leader == null) continue;
            ulong key = bw.W.PlaceId(g.Key).Value != 0 ? bw.W.PlaceId(g.Key).Value : (ulong)(uint)g.Key;
            if (DetRng.Range(bw.Seed, day, key, RngStreams.SlaveRevoltRoll, 0, 0, 10_000) >= DailyChance(share.SharePct, bw.D.RevoltSharePct)) continue;
            Rise(bw, book, g.Key, held, leader, share, day, sec);
            rose++;
        }
        return rose;
    }

    /// <summary>The revolt: the bonded break their chains and go free as a band under the leader; the entry names the floggings and the hunger as its causes.</summary>
    public static void Rise(BondWorld bw, BondBook book, int place, IReadOnlyList<Bond> held, Bond leader, Grievance share, int day, long sec)
    {
        var lid = new EntityId(leader.Person);
        var causes = new List<Cause>();
        foreach (ulong fe in held.Where(b => b.FlogEntry != 0 && day - b.FlogDay <= bw.D.GrievanceDays).Select(b => b.FlogEntry).Distinct().Order().Take(3)) causes.Add(new Cause(CauseKind.Slavery, new EntityId(fe), NoteKey: "ch_note_slave_floggings"));
        if (share.Hungry > 0) causes.Add(new Cause(CauseKind.Famine, null, NoteKey: "ch_note_slave_hungry", NoteParams: [ChronParam.Count("count", share.Hungry)]));
        if (share.Separated > 0) causes.Add(new Cause(CauseKind.Slavery, null, NoteKey: "ch_note_slave_parted", NoteParams: [ChronParam.Count("count", share.Separated)]));
        if (causes.Count == 0) causes.Add(new Cause(CauseKind.Slavery, null, NoteKey: "ch_note_slave_harsh"));
        var ps = new List<ChronParam> { ChronParam.Person("leader", lid), ChronParam.Count("count", held.Count), ChronParam.Count("guards", bw.GuardsAt(place)) };
        ps.AddRange(bw.PlaceParams(place));
        var d = new Dictionary<string, string> { ["count"] = Slavery.N(held.Count), ["place"] = bw.W.PlaceName(place), ["leader"] = bw.W.Name(lid) };
        var e = new ChronicleEntry(bw.W.Ids.Allocate(IdKind.Event), new GameTime(sec), "slave_revolt", [lid], causes, d, TextKey: "ch_slavery_revolt", Params: [.. ps]);
        bw.Chronicle.Add(e);
        foreach (var b in held)
        {
            book.Bonds.Remove(b.Person);
            if (b.Person == bw.W.HeroId.Value) bw.W.Marks()?.SetEnslaved(bw.W.HeroId, false);
        }
        book.FreedTotal += held.Count;
        book.Rebels.Add(new RebelBand { Leader = leader.Person, Place = place, Day = day, Members = held.Count, Entry = e.Id.Value });
    }
}
