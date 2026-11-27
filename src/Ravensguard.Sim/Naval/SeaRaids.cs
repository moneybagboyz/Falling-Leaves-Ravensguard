using Ravensguard.Sim.Core;
using Ravensguard.Sim.Politics;

namespace Ravensguard.Sim.Naval;

/// <summary>Where a sea raid stands (4.4, 4.5): sailing to the beach, landed on it, raiding the village, withdrawing to the ships, gone with its loot, or caught on the beach with every raider down.</summary>
public enum RaidState : byte { Sailing, Landed, Raiding, Withdrawing, Gone, Caught }

/// <summary>
/// A sea raid (3.4 <c>SeaRaid</c>): who comes, whom they come for and what they have done. Hours are absolute game hours. The ships are real vehicle rows and the men real persons once the raid has landed
/// (<see cref="Ships"/>, <see cref="Raiders"/>, <see cref="Guards"/>); the loot lies in the ships' holds and strongboxes and the captives are in the captive book, so nothing here is a number that stands for a thing.
/// </summary>
public sealed class SeaRaid
{
    public int Id, Realm, Victim, Region, Settlement, Culture;
    public bool War;
    /// <summary>The declaration of the war that gave leave (0: the culture's own raiding in peace).</summary>
    public ulong DeclEntry;
    public int PlanShips, PlanMen;
    /// <summary>The beach, in region millimetres.</summary>
    public long BeachXmm, BeachYmm;
    public long PlannedHour, LandHour = -1, LootHour = -1, BellHour = -1, WithdrawHour = -1, EndHour = -1;
    public RaidState State;
    public List<ulong> Ships { get; } = [];
    public List<ulong> Raiders { get; } = [];
    public List<ulong> Guards { get; } = [];
    public List<ulong> Captives { get; } = [];
    public long TakenG, TakenCoin;
    public int Burned, Dead, Households;
    /// <summary>The chronicle entry of the raid (0 until it is written).</summary>
    public ulong Entry;
    public bool Fought;
    /// <summary>The warband the bell brought (-1 none) and the hour it reaches the beach.</summary>
    public int Defender = -1;
    public long FightHour = -1;
    /// <summary>P8-LW-2: the game hour a live beach fight opened (-1: none open); the abstract fight is the fallback after a day.</summary>
    public long LiveSince = -1;
    public bool Over => State is RaidState.Gone or RaidState.Caught;
}

/// <summary>The sea raids of a world (optional <c>naval</c> section; absent while none was ever planned).</summary>
public sealed class NavalBook
{
    public SortedDictionary<int, SeaRaid> Raids { get; } = [];
    public int Next = 1;
    /// <summary>P7-SH-D: the ships' fight rows (a ship that burns, is holed, is taken or sunk), by vehicle id.</summary>
    public SortedDictionary<ulong, ShipCombatRow> Ships { get; } = [];
    /// <summary>P7-SH-D: ships bound by grapples, by (lower id, higher id).</summary>
    public List<GrappleBind> Binds { get; } = [];
    /// <summary>P7-SH-D: ships at sea and in harbour as frames (placement, motion, riders), by vehicle id.</summary>
    public SortedDictionary<ulong, ShipFrameState> Frames { get; } = [];
    /// <summary>
    /// P8-LW-4: the running sea (fishing boats, ferries, barges and the season's catch), as (kind, key) to value: kind 0 a village's fishing boat (key the settlement, value the vehicle id), 1 a ford's ferry (key the crossing), 2 a waterway's barge,
    /// 3 the grams a village's water has given this season, 4 the season the catch counts are of (key 0). Hashed and saved only while any is held.
    /// </summary>
    public SortedDictionary<(byte Kind, int Key), long> Trade { get; } = [];
    public bool IsEmpty => Raids.Count == 0 && Next <= 1 && Ships.Count == 0 && Binds.Count == 0 && Frames.Count == 0 && Trade.Count == 0;
    public int Touched;
    public void Touch() => Touched++;

    /// <summary>FNV-1a over every raid in id order (the raids' share of the <c>NavalHash</c>).</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        void M(long v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; } }
        M(Next);
        foreach (var r in Raids.Values)
        {
            M(r.Id); M(r.Realm); M(r.Victim); M(r.Region); M(r.Settlement); M(r.Culture); M(r.War ? 1 : 0); M((long)r.DeclEntry); M(r.PlanShips); M(r.PlanMen); M(r.BeachXmm); M(r.BeachYmm);
            M(r.PlannedHour); M(r.LandHour); M(r.LootHour); M(r.BellHour); M(r.WithdrawHour); M(r.EndHour); M((long)r.State); M(r.TakenG); M(r.TakenCoin); M(r.Burned); M(r.Dead); M(r.Households);
            M((long)r.Entry); M(r.Fought ? 1 : 0); M(r.Defender); M(r.FightHour);
            if (r.LiveSince >= 0) M(r.LiveSince);                  // P8-LW-2: only while a live fight is open, so every other raid hashes as before
            foreach (var l in new[] { r.Ships, r.Raiders, r.Guards, r.Captives }) { M(l.Count); foreach (var v in l) M((long)v); }
        }
        if (Trade.Count > 0) { M(-5); foreach (var ((k, key), v) in Trade) { M(k); M(key); M(v); } }                 // P8-LW-4: only while the running sea holds anything
        if (Ships.Count + Binds.Count + Frames.Count == 0) return h;                // a world with no naval fights hashes as it did in SH-C
        foreach (var x in Ships.Values)
        {
            M((long)x.Ship); M((long)x.State); M((long)x.Captor.Value); M((long)x.Entry); M(x.Serial); M(x.BurnCarry); M(x.SailDown ? 1 : 0); M(x.Bailers); M(x.Burning.Count); foreach (int i in x.Burning) M(i);
        }
        foreach (var b in Binds) { M((long)b.A); M((long)b.B); M(b.Lines); M(b.HaulMen); M(b.HaulS); M(b.Joined ? 1 : 0); M(b.OffLx); M(b.OffLy); M(b.OffHeading); M(b.Throws); }
        foreach (var f in Frames.Values) { h = NavalHash.Mix(h, f); }
        return h;
    }
}

/// <summary>A realm that might raid by sea: its culture, the longships it can send and the region they sail from.</summary>
public sealed record RaiderRealm(int Realm, int Culture, string CultureId, int Ships, int FromRegion);

/// <summary>A coastal village a raid might fall on: what it is thought to hold (stores and coin, by rumour), the armed men it is known to have within a day, and the sail from the raiders' region in kilometres.</summary>
public sealed record RaidTarget(int Region, int Settlement, int Realm, long ValueMites, int Guards, int SailKm);

/// <summary>
/// Sea raid planning (4.4, SH-10), once a season in spring and summer for each realm of a raider culture: the target is the richest weakly guarded coastal village known, by <c>value / sail days - risk</c>; the raid goes at war with the
/// victim, or in peace by culture with a draw of <c>raid_in_peace_pm</c> (overview 10a Q7); 1 to 4 longships of 30 to 60 men each. Draws are the 0x7D3 stream keyed on the realm and the season. Pure: the same inputs, the same raid.
/// </summary>
public static class SeaRaids
{
    public static bool RaidsBySea(SeaRules rules, string cultureId) => rules.D.RaidCultures.Contains(cultureId);

    public static int SailDays(SeaRules rules, int km) => Math.Max(1, (km + rules.D.SailKmPerDay - 1) / rules.D.SailKmPerDay);

    /// <summary>The target's score: what it holds over the days to sail there, less the risk of the armed men known near it.</summary>
    public static long Score(SeaRules rules, RaidTarget t) => t.ValueMites / SailDays(rules, t.SailKm) - (long)t.Guards * rules.D.RiskMitesPerGuard;

    /// <summary>
    /// The raid a realm plans this season, or null. <paramref name="atWar"/> says whether the realm is at war with another; <paramref name="nowHour"/> is the game hour of the planning. Ties on the score are broken by a draw on 0x7D3.
    /// </summary>
    public static SeaRaid? Plan(ulong seed, int season, RaiderRealm realm, IReadOnlyList<RaidTarget> targets, Func<int, bool> atWar, SeaRules rules, long nowHour, int id)
    {
        var d = rules.D;
        if (!RaidsBySea(rules, realm.CultureId) || realm.Ships <= 0) return null;
        var cand = targets.Where(t => t.Realm >= 0 && t.Realm != realm.Realm && t.Guards <= d.WeakGuards && t.ValueMites > 0).ToList();
        if (cand.Count == 0) return null;
        ulong key = (ulong)(uint)realm.Realm;
        // war with the victim gives leave; in peace the culture's raiding is a draw of raid_in_peace_pm a thousand
        var best = cand.Max(t => Score(rules, t));
        var top = cand.Where(t => Score(rules, t) == best).OrderBy(t => t.Region).ThenBy(t => t.Settlement).ToList();
        var pick = top[top.Count == 1 ? 0 : DetRng.Range(seed, season, key, RngStreams.SeaRaidTies, 0, 0, top.Count)];
        bool war = atWar(pick.Realm);
        if (!war && DetRng.Range(seed, season, key, RngStreams.SeaRaidTies, 1, 0, 1000) >= d.RaidInPeacePm) return null;
        int ships = Math.Min(realm.Ships, 1 + DetRng.Range(seed, season, key, RngStreams.SeaRaidTies, 2, 0, Math.Max(1, d.RaidShipsMax)));
        int men = 0;
        for (int i = 0; i < ships; i++) men += d.RaidMenMin + DetRng.Range(seed, season, key, RngStreams.SeaRaidTies, (uint)(3 + i), 0, Math.Max(1, d.RaidMenMax - d.RaidMenMin + 1));
        long landDay = nowHour / 24 + SailDays(rules, pick.SailKm);
        return new SeaRaid
        {
            Id = id, Realm = realm.Realm, Victim = pick.Realm, Region = pick.Region, Settlement = pick.Settlement, Culture = realm.Culture, War = war, PlanShips = ships, PlanMen = men,
            PlannedHour = nowHour, LandHour = landDay * 24 + d.LandHourOfDay,
        };
    }

    /// <summary>The victim's grievance: a <see cref="CbKind.Raid"/> against the raiders' realm, once per raid in play (one unused already held covers it). Returns the grievance, or null when one stood.</summary>
    public static CasusBelli? GrantCb(PoliticsCtx c, SeaRaid raid)
    {
        foreach (var x in c.B.Cbs) if (!x.Used && x.Holder == raid.Victim && x.Against == raid.Realm && x.Kind == CbKind.Raid) return null;
        return Diplomacy.AddCb(c, raid.Victim, raid.Realm, CbKind.Raid);
    }

    /// <summary>Whether a point (region metres) is within the bell's carry of the village (4.5: 3 km).</summary>
    public static bool BellHeard(SeaRules rules, long bellXmm, long bellYmm, long xMm, long yMm)
    {
        long dx = bellXmm - xMm, dy = bellYmm - yMm, r = rules.D.BellM * 1000L;
        return dx * dx + dy * dy <= r * r;
    }
}
