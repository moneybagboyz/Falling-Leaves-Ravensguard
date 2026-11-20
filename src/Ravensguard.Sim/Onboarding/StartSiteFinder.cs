using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Onboarding;

/// <summary>Where a new game starts and who waits there (onboarding spec 4.1).</summary>
public sealed record StartSite(int Settlement, int X, int Y, string Kind, int MentorPerson, int MentorX, int MentorY, int ErrandSettlement, bool Relaxed, string Note)
{
    public bool Found => Settlement >= 0;
}

/// <summary>
/// Chooses the start of a new game. The hard rule (plan 3f, the only softening of the game): the start point keeps <c>WarSafeM</c> from an active war and <c>LairSafeM</c> from a monster lair. Beyond it the start must be a good place
/// to learn: a job board or inn within 400 m, a mentor within 120 m, a barn or holder of animals within 800 m, a settlement 2 to 8 km away for the first ride, fresh water within 300 m and a bed for the first night. Candidates
/// are taken in ascending settlement order; those that pass everything are ranked by what else they offer, ties broken by stream <c>0x370</c>. Nothing passes: another kind of place, then any safe settlement with the soft rules relaxed
/// (reported in <c>Relaxed</c>), then no start with a message.
/// </summary>
public static class StartSiteFinder
{
    public const int BoardM = 400, MentorM = 120, AnimalsM = 800, WaterM = 300, RideMinM = 2000, RideMaxM = 8000;

    /// <summary>Which person kinds can be the mentor of a given mentor id, best first.</summary>
    public static Occupation[] MentorTrades(string mentor) => mentor switch
    {
        "old_knight" => [Occupation.Guard, Occupation.Lord, Occupation.Steward],
        "caravan_master" => [Occupation.Merchant, Occupation.Innkeeper],
        "free_farmer" => [Occupation.Farmer, Occupation.Ploughman, Occupation.Shepherd],
        "old_sergeant" => [Occupation.Guard],
        "prior" => [Occupation.Priest],
        "steward" => [Occupation.Steward, Occupation.Reeve],
        "fence" => [Occupation.Innkeeper, Occupation.Merchant, Occupation.Woodcutter, Occupation.CharcoalBurner],
        "journeyman" => [Occupation.Tanner, Occupation.Cobbler, Occupation.Butcher, Occupation.Merchant],   // P5-H: the tanner's widow's mentor, her husband's old journeyman
        _ => [],
    };

    static int Dist(int ax, int ay, int bx, int by) => (int)Math.Min(int.MaxValue, RegionData.Isqrt(RegionData.Dist2(ax - bx, ay - by)));

    static string[] FallbackKinds(string kind) => kind switch
    {
        "wilds" => ["village"], "manor" => ["town", "village"], "monastery" => ["village", "town"], "road" => ["village", "town"], "town" => ["village"], _ => ["town"],
    };

    /// <summary>Candidate settlements of a kind, in ascending index order.</summary>
    static IEnumerable<int> Of(RegionData r, string kind)
    {
        for (int i = 0; i < r.Settlements.Count; i++)
        {
            var s = r.Settlements[i];
            bool ok = kind switch
            {
                "town" => s.Class == SettlementClass.Town,
                "village" => s.Class == SettlementClass.Village,
                "manor" => s.Plan.ManorLot >= 0,
                "monastery" => s.Plan.ChurchLot >= 0 && s.Class != SettlementClass.Farmstead,
                "road" => s.Class != SettlementClass.Farmstead,
                "wilds" => s.Class == SettlementClass.Farmstead,
                _ => true,
            };
            if (ok) yield return i;
        }
    }

    static (int X, int Y) LotCentre(RegionSettlement s, int lot) => lot >= 0 && lot < s.Plan.Lots.Count ? (s.Plan.Lots[lot].Rect.CenterX, s.Plan.Lots[lot].Rect.CenterY) : (s.CenterX, s.CenterY);

    static bool WaterNear(RegionData r, RegionSettlement s, int x, int y)
    {
        if (s.OnRiver || s.NearWaterway >= 0) return true;
        int t = RegionSettings.TileM, n = WaterM / t + 1, cx = x / t, cy = y / t;
        for (int dy = -n; dy <= n; dy++)
            for (int dx = -n; dx <= n; dx++)
            {
                int tx = cx + dx, ty = cy + dy;
                if (!RegionData.InBounds(tx, ty) || r.Water[RegionData.Index(tx, ty)] == 0) continue;
                if (Dist(x, y, tx * t + t / 2, ty * t + t / 2) <= WaterM) return true;
            }
        return false;
    }

    /// <summary>The adult who will be the mentor: the best trade first, in the settlement, lowest index; none when nobody fits.</summary>
    static int PickMentor(Population pop, int settlement, string mentor, ulong seed)
    {
        var people = pop.People; var trades = MentorTrades(mentor);
        var adults = new List<int>();
        for (int i = 0; i < people.Count; i++)
            if (people.IsAlive(i) && people.HomeSettlement[i] == settlement && pop.Age(i) >= 25 && pop.Age(i) <= 70) adults.Add(i);
        foreach (var t in trades)
        {
            var m = adults.Where(i => people.Occupation[i] == t).ToList();
            if (m.Count > 0) return m[DetRng.Range(seed, 0, (ulong)settlement, RngStreams.OnboardingSite, (uint)t, 0, m.Count)];
        }
        return adults.Count == 0 ? -1 : adults[DetRng.Range(seed, 0, (ulong)settlement, RngStreams.OnboardingSite, 99, 0, adults.Count)];
    }

    sealed record Cand(int Settlement, int X, int Y, int Mentor, int MentorX, int MentorY, int Errand, int Score, bool Full);

    static Cand? Evaluate(Population pop, int si, string mentorId, int warM, int lairM, ulong seed, bool strict)
    {
        var r = pop.Region; var s = r.Settlements[si];
        int m = PickMentor(pop, si, mentorId, seed);
        // the start is beside the mentor's door (the lot of their household), else the middle of the settlement
        int mx = s.CenterX, my = s.CenterY;
        if (m >= 0)
        {
            int h = pop.People.Household[m];
            var lots = pop.LotHousehold[si];
            for (int l = 0; l < lots.Length; l++) if (lots[l] == h && h >= 0) { (mx, my) = LotCentre(s, l); break; }
        }
        // the hero starts a few metres off, on the mentor's side of the street: a jitter of up to 20 m from stream 0x370
        int jx = DetRng.Range(seed, 1, (ulong)si, RngStreams.OnboardingSite, 1, -20, 21), jy = DetRng.Range(seed, 1, (ulong)si, RngStreams.OnboardingSite, 2, -20, 21);
        int x = Math.Clamp(mx + jx, 0, RegionSettings.RegionM - 1), y = Math.Clamp(my + jy, 0, RegionSettings.RegionM - 1);
        if (!r.IsSafeStart(x, y, warM, lairM) || !r.IsSafeStart(s.CenterX, s.CenterY, warM, lairM)) return null;
        bool board = s.Class == SettlementClass.Town && s.Plan.Square.W > 0 && Dist(x, y, s.Plan.Square.CenterX, s.Plan.Square.CenterY) <= BoardM;
        int bonus = 0;
        foreach (var lot in s.Plan.Lots)
        {
            if (lot.Use == LotUse.Tavern && Dist(x, y, lot.Rect.CenterX, lot.Rect.CenterY) <= BoardM) { board = true; bonus++; }
        }
        if (!board && s.Class == SettlementClass.Village) board = Dist(x, y, s.CenterX, s.CenterY) <= BoardM; // a village's work is heard from the reeve and the innkeeper at its middle
        bool animals = false;
        foreach (var lot in s.Plan.Lots) if (lot.Use is LotUse.Barn or LotUse.Longhouse or LotUse.Manor && Dist(x, y, lot.Rect.CenterX, lot.Rect.CenterY) <= AnimalsM) { animals = true; break; }
        int errand = -1, bestD = int.MaxValue;
        for (int k = 0; k < r.Settlements.Count; k++)
        {
            if (k == si) continue;
            var o = r.Settlements[k]; int d = Dist(x, y, o.CenterX, o.CenterY);
            if (d < RideMinM || d > RideMaxM || o.Class == SettlementClass.Farmstead) continue;
            int gap = Math.Abs(d - 4000);       // the nearest to 4 km, the middle of the 2 to 6 km of the first ride
            if (gap < bestD) { bestD = gap; errand = k; }
        }
        bool water = WaterNear(r, s, x, y);
        bool bed = s.Plan.Lots.Any(l => l.Use == LotUse.Tavern) || (m >= 0 && pop.People.Household[m] >= 0);
        bool mentorNear = m >= 0 && Dist(x, y, mx, my) <= MentorM;
        bool full = board && mentorNear && animals && errand >= 0 && water && bed;
        if (strict && !full) return null;
        int score = (full ? 100 : 0) + bonus + (errand >= 0 ? 4000 - bestD : 0) / 100 + (s.Class == SettlementClass.Town ? 2 : 0);
        return new Cand(si, x, y, m, mx, my, errand, score, full);
    }

    /// <summary>Finds the start for an origin. <paramref name="kind"/> is the origin's <c>start_location</c>, <paramref name="mentor"/> the id its mentor has.</summary>
    public static StartSite Find(Population pop, string kind, string mentor, ulong seed, int warSafeM = 10_000, int lairSafeM = 3_000)
    {
        var r = pop.Region;
        foreach (var (k, relaxed) in new[] { kind }.Concat(FallbackKinds(kind)).Select((k, i) => (k, i > 0)))
        {
            var cs = Of(r, k).Select(i => Evaluate(pop, i, mentor, warSafeM, lairSafeM, seed, strict: true)).Where(c => c != null).Cast<Cand>().ToList();
            if (cs.Count == 0) continue;
            var best = cs.OrderByDescending(c => c.Score).ThenBy(c => DetRng.Next(seed, 2, (ulong)c.Settlement, RngStreams.OnboardingSite, 3)).First();
            return new StartSite(best.Settlement, best.X, best.Y, k, best.Mentor, best.MentorX, best.MentorY, best.Errand, relaxed, relaxed ? "fallback:" + k : "");
        }
        // no settlement passes the whole cluster: any safe settlement, soft rules relaxed
        var any = Enumerable.Range(0, r.Settlements.Count).Select(i => Evaluate(pop, i, mentor, warSafeM, lairSafeM, seed, strict: false)).Where(c => c != null).Cast<Cand>().OrderByDescending(c => c.Score).ThenBy(c => c.Settlement).FirstOrDefault();
        if (any != null) return new StartSite(any.Settlement, any.X, any.Y, "any", any.Mentor, any.MentorX, any.MentorY, any.Errand, true, "relaxed");
        return new StartSite(-1, 0, 0, kind, -1, 0, 0, -1, true, "No safe start of this kind in the region; choose another region.");
    }
}
