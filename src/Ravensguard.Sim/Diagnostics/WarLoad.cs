using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>What the scripted host is made of (economy-audit 5.2, army-economy 4.5): the economy's numbers only, no soldiers.</summary>
public sealed record WarLoadSpec(int Levies, int Spearmen, int Archers, int ManAtArms, int Knights, int Mercenaries, int Followers, int Horses, int Carts)
{
    /// <summary>The audit's 2,000 men: 1,040 region levies, 560 retinue (300 spearmen, 160 archers, 60 men-at-arms, 40 knights), 400 mercenaries from outside, 100 followers, 300 horses, 60 carts.</summary>
    public static WarLoadSpec Army2000 => new(1040, 300, 160, 60, 40, 400, 100, 300, 60);
    /// <summary>The small test host of EA-11: 200 men, 30 horses, 6 carts.</summary>
    public static WarLoadSpec Small200 => new(100, 40, 20, 10, 0, 30, 0, 30, 6);
    public int Men => Levies + Spearmen + Archers + ManAtArms + Knights + Mercenaries;
}

/// <summary>The war table's running numbers of the load generator.</summary>
public sealed class WarTally
{
    public long ManDaysFed, ManDaysHungry, ManDaysPaid, ManDaysUnpaid, Deserted, BoughtStapleG, BaggageStapleG, PaidMites, BoughtMites, ShortStapleG;
    /// <summary>Coin the realm sent to the pay chest at the muster, and the arrears the realm still owed when the host disbanded.</summary>
    public long FundedMites, ArrearsMites, AidMites, TallageMites;
    public int Mustered, Days;
    public void Reset() { ManDaysFed = ManDaysHungry = ManDaysPaid = ManDaysUnpaid = Deserted = BoughtStapleG = BaggageStapleG = PaidMites = BoughtMites = ShortStapleG = FundedMites = ArrearsMites = AidMites = TallageMites = 0; Mustered = Days = 0; }
}

/// <summary>
/// The scripted load generator of the war-load soak (economy-audit 5.1): a host with no soldiers and no battles that is a plain client of <see cref="HostSupply"/>. It registers a host with a pay chest (funded from the realm's
/// treasury) and a baggage store, musters its levies from the region's households (real people: their households lose their labour while they serve; the day of the muster is jittered +-2 days on stream 0x700, key war and
/// season), walks a fixed route at the column's pace, and each day <see cref="Before"/> the host slot updates the host's place and <see cref="After"/> it reads the day's <see cref="DayResult"/>: men fed or hungry, paid or in
/// arrears, and the stand-in desertion (1 man per 100 man-days unpaid or unfed; the military's rule replaces it in P7-AM-D) that returns deserters to their households or, from outside, to the continent. A diagnostic harness: it
/// lives under Diagnostics, the only place the 0x700 stream may be named, and nothing in a game started from the menu constructs it.
/// </summary>
public sealed class WarLoad
{
    private readonly HostSupply _hs;
    private readonly EconomyWorld _ew;
    private readonly ulong _seed;
    public WarLoadSpec Spec { get; }
    public int WarId { get; }
    public HostId Host { get; private set; } = HostId.None;
    public bool Active => !Host.IsNone;
    private long _carryManDays;
    private int[] _route = [];
    private int _routeStep;

    /// <summary>The tallies the war table prints (cumulative; a soak shares one across its campaigns and resets it each year).</summary>
    public WarTally Tally { get; }
    public int Mustered { get => Tally.Mustered; private set => Tally.Mustered = value; }
    /// <summary>The last day's result (null before the first slot).</summary>
    public DayResult? LastResult { get; private set; }

    /// <summary>The realm raises the coin of the campaign before the muster (aid from the fiefs, then a tallage: <see cref="WarFunding"/>, the audit's decision EA_F7). Off by default so a test that funds the chest itself stays as it was.</summary>
    public bool RealmRaises { get; set; }

    public WarLoad(HostSupply hs, WarLoadSpec spec, ulong seed, int warId, WarTally? tally = null) { _hs = hs; _ew = hs.Economy; Spec = spec; _seed = seed; WarId = warId; Tally = tally ?? new WarTally(); }

    /// <summary>The day a muster ordered for <paramref name="baseDay"/> happens: +-2 days, stream 0x700, key the war id and the season.</summary>
    public static int MusterDay(ulong seed, int baseDay, int warId, int season) => baseDay + DetRng.Range(seed, season, (ulong)warId, RngStreams.WarLoadMusterJitter, 0, -2, 3);

    /// <summary>The settlement nearest the border: the one furthest from <paramref name="from"/>, ties the lowest index (the "gate with the realm at war" of the scenario).</summary>
    public static int FarthestSettlement(Ravensguard.Sim.WorldGen.Region.Region region, int from)
    {
        long best = -1; int pick = from; var f = region.Settlements[from];
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            long dx = region.Settlements[s].CenterX - f.CenterX, dy = region.Settlements[s].CenterY - f.CenterY, d = dx * dx + dy * dy;
            if (d > best) { best = d; pick = s; }
        }
        return pick;
    }

    /// <summary>The route out and back: from the start, hop to the nearest unvisited settlement that is nearer the gate, until the gate; then the same stops in reverse. Deterministic (ties by index).</summary>
    public static int[] RouteFor(Ravensguard.Sim.WorldGen.Region.Region region, int start, int gate)
    {
        var path = new List<int> { start }; int cur = start; long Dist2(int a, int b) { long dx = region.Settlements[a].CenterX - region.Settlements[b].CenterX, dy = region.Settlements[a].CenterY - region.Settlements[b].CenterY; return dx * dx + dy * dy; }
        while (cur != gate && path.Count < 12)
        {
            int next = -1; long bestD = long.MaxValue;
            for (int s = 0; s < region.Settlements.Count; s++)
            {
                if (path.Contains(s) || Dist2(s, gate) >= Dist2(cur, gate)) continue;
                long d = Dist2(cur, s); if (d < bestD) { bestD = d; next = s; }
            }
            if (next < 0) { path.Add(gate); break; }
            path.Add(next); cur = next;
        }
        if (path[^1] != gate) path.Add(gate);
        var back = new List<int>(path); back.Reverse(); back.RemoveAt(0);
        return [.. path.Concat(back)];
    }

    /// <summary>Registers the host at <paramref name="start"/>, funds its chest from the realm's treasury (up to <paramref name="fundDays"/> days of its pay and food at today's grain mid, what the treasury holds if less), and musters its levies from the fiefs by their war quota.</summary>
    public void Begin(int start, int gate, int fundDays = 30, int levyFief = -1)
    {
        var econ = _ew.World.Economy; var led = econ.Ledger;
        int n = (int)(_ew.World.State.HourDone / 24);
        var chest = led.Open(PurseKind.Chest, HostIds.Chest(WarId));
        var bag = econ.OpenStore(StoreKind.Sealed, HostIds.Baggage(WarId));
        var men = new Roster([(TroopRole.Spearman, Spec.Spearmen), (TroopRole.Archer, Spec.Archers), (TroopRole.ManAtArms, Spec.ManAtArms), (TroopRole.Knight, Spec.Knights), (TroopRole.Mercenary, Spec.Mercenaries)]);
        var treasury = RealmTreasury();
        Host = _hs.Register(new HostSpec(0, EntityId.None, chest, bag, men, Spec.Followers, new AnimalCounts(Spec.Horses, 0, 0, 0), Spec.Carts, new SupplyPolicy())
        { Treasury = treasury, OutsideMen = Spec.Mercenaries });
        // the levies: each fief gives up to its war quota (one man a household), in fief order, until the spec's count
        var region = _ew.World.Population.Region; var tax = _ew.Taxes;
        int need = Spec.Levies;
        for (int f = 0; f < region.Fiefs.Count && need > 0; f++)
        {
            if (levyFief >= 0 && f != levyFief) continue;
            int hh = 0; var hht = _ew.World.Population.Households;
            for (int i = 0; i < hht.Count; i++) if (hht.MemberCount[i] > 0 && hht.Settlement[i] >= 0 && region.Settlements[hht.Settlement[i]].FiefIndex == f) hh++;
            int quota = Math.Min(need, Math.Max(1, tax.LevyQuota(hh, true)));
            var r = _hs.Muster(Host, f, quota);
            Mustered += r.Persons.Count; need -= r.Persons.Count;
        }
        // the chest: pay and food for the days, from the realm
        if (!treasury.IsNone)
        {
            var a = _hs.Account(Host); var needs = _hs.Needs(Host, n);
            long fund = (needs.PayMites + needs.StapleG * 45 / 1000 + needs.FodderHayG * 12 / 1000 + needs.FodderOatsG * 30 / 1000) * fundDays;
            if (RealmRaises) { var (aid, tallage) = WarFunding.Raise(_ew, treasury, fund); Tally.AidMites += aid; Tally.TallageMites += tallage; }
            fund = Math.Min(fund, led.Balance(treasury).Mites);
            if (fund > 0 && led.Transfer(treasury, chest, new Coin(fund), TransferReason.Hoard)) Tally.FundedMites += fund;
            _ = a;
        }
        _route = RouteFor(region, start, gate); _routeStep = 0;
        _hs.Update(Host, _hs.PlaceAt(start), HostMode.Camp);
    }

    private PurseId RealmTreasury()
    {
        foreach (var t in _ew.Taxes.Treasuries.All) if (t.Kind == TreasuryKind.Realm) return t.Purse;
        return PurseId.None;
    }

    /// <summary>Before the host slot of the day: the host walks one stop of its route (a column's day) and the military's note of where it is goes to the economy. <paramref name="mode"/> March on a moving day, Camp where it stands.</summary>
    public void Before(bool marching, HostMode? mode = null)
    {
        if (!Active) return;
        if (marching && _routeStep < _route.Length - 1) _routeStep++;
        _hs.Update(Host, _hs.PlaceAt(_route[Math.Min(_routeStep, _route.Length - 1)]), mode ?? (marching ? HostMode.March : HostMode.Camp));
    }

    /// <summary>Walks back to the start (a returned army).</summary>
    public int CurrentSettlement => _route.Length == 0 ? -1 : _route[Math.Min(_routeStep, _route.Length - 1)];
    public bool AtGate => _routeStep >= _route.Length / 2;
    public bool Home => _routeStep >= _route.Length - 1;

    /// <summary>After the host slot: tally the day's result, and apply the stand-in desertion (1 man per 100 man-days unpaid or unfed).</summary>
    public void After(int day)
    {
        if (!Active) return;
        var res = _hs.Last(Host); if (res == null || res.Day != day) return;
        LastResult = res; Tally.Days++;
        var a = _hs.Account(Host);
        long fedMen = 0, short_ = 0;
        for (int r = 0; r < Roster.Roles; r++)
        {
            int c = a.Men[r]; if (c == 0) continue;
            bool hungry = res.HungerDays[r] > 0, unpaid = res.UnpaidDays[r] > 0;
            if (hungry) Tally.ManDaysHungry += c; else { Tally.ManDaysFed += c; fedMen += c; }
            if (unpaid) Tally.ManDaysUnpaid += c; else Tally.ManDaysPaid += c;
            if (hungry || unpaid) short_ += c;
        }
        Tally.BoughtStapleG += res.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Market]; Tally.BaggageStapleG += res.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Baggage];
        Tally.ShortStapleG += res.ShortG[(int)SupplyNeed.Staple]; Tally.PaidMites += res.PaidTotal; Tally.BoughtMites += res.BoughtMites;
        _carryManDays += short_;
        int leave = (int)(_carryManDays / 100); _carryManDays %= 100;
        if (leave > 0) Desert(a, leave);
    }

    /// <summary>The stand-in's deserters: levy persons first (they walk home, the labour returns), then the other roles in role order (counts); the economy settles each as <see cref="HostSupply.Desert"/> does.</summary>
    private void Desert(HostAccount a, int n)
    {
        var persons = _hs.Persons(Host).Reverse().Take(Math.Min(n, a.Men[(int)TroopRole.Levy])).ToList();
        if (persons.Count > 0) { _hs.Desert(Host, persons); n -= persons.Count; Tally.Deserted += persons.Count; }
        for (int r = 0; r < Roster.Roles && n > 0; r++)
        {
            if ((TroopRole)r == TroopRole.Levy) continue;
            int took = _hs.DesertCounts(Host, (TroopRole)r, n); n -= took; Tally.Deserted += took;
        }
    }

    /// <summary>The campaign is over: the host is disbanded (arrears paid as far as the chest goes, the men home, the unspent coin back to the treasury).</summary>
    public void End()
    {
        if (!Active) return;
        Tally.ArrearsMites += _hs.Account(Host).Arrears.Sum();
        _hs.Disband(Host); Host = HostId.None;
    }
}
