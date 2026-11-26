using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>What the sea raids need of the world around the armies (P7-SH-C): the naval book, the ride book (the ships), the captive book, the sea edges, the history and the home region. Set by <see cref="SeaRaidWiring"/>.</summary>
public sealed class SeaRaidHost
{
    public Func<NavalBook?> Peek { get; init; } = () => null;
    public Func<NavalBook> Ensure { get; init; } = () => new NavalBook();
    public Func<RideBook?> Rides { get; init; } = () => null;
    public Func<CaptiveBook?> Captives { get; init; } = () => null;
    public Func<IReadOnlyList<SeaEdge>> Edges { get; init; } = () => [];
    public Func<WorldHistory?> History { get; init; } = () => null;
    public int HomeRegion { get; init; }
    public SeaRules Rules { get; init; } = SeaRules.Default;
}

public sealed partial class ArmyWorld
{
    /// <summary>The sea raids' world (null: none can land here).</summary>
    public SeaRaidHost? Sea { get; set; }

    private static EntityId ShipOwner(int realm) => EntityId.None;

    // ------------------------------------------------------------------ the season: raids planned (4.4)

    /// <summary>
    /// The season's planning (spring and summer): each realm of a raider culture that has ships and a sea edge to this region weighs the coastal villages of the region by the raid rule, and one raid is put in the book (with the victim's grievance).
    /// The raid sails; the landing is its <see cref="SeaRaid.LandHour"/>. Realms in index order. Returns the raids planned.
    /// </summary>
    public int PlanSeaRaids(ArmyEnv e, PoliticsCtx? pc = null)
    {
        var sea = Sea; var hist = sea?.History(); var pb = Politics; if (sea == null || hist == null || Content == null) return 0;
        var season = e.Time.Season; if (season is not (Season.Spring or Season.Summer)) return 0;
        var rules = sea.Rules; var edges = sea.Edges(); var book = sea.Ensure(); int planned = 0;
        var targets = CoastalTargets(e, rules);
        if (targets.Count == 0) { if (book.IsEmpty) { } return 0; }
        foreach (var realm in hist.Realms.OrderBy(r => r.Index))
        {
            if (!realm.Alive || realm.Capital < 0 || realm.Capital >= hist.Settlements.Count) continue;
            if (book.Raids.Values.Any(r => !r.Over && r.Realm == realm.Index)) continue;
            string cid = Ravensguard.Sim.Faith.ReligionBook.CultureOf(Content, realm.CultureId)?.Id ?? "";
            if (!SeaRaids.RaidsBySea(rules, cid)) continue;
            int from = RegionCoord.OfCell(hist.Settlements[realm.Capital].Cell).Id;
            var edge = SeaEdges.Find(edges, from, sea.HomeRegion); if (edge == null) continue;
            int coastal = hist.Settlements.Count(s => !s.IsRuin && s.Realm == realm.Index && edges.Any(x => x.A == RegionCoord.OfCell(s.Cell).Id || x.B == RegionCoord.OfCell(s.Cell).Id));
            int fleet = Math.Min(rules.D.RaidShipsMax, 1 + coastal / 3);
            var rr = new RaiderRealm(realm.Index, realm.CultureId, cid, fleet, from);
            var mine = targets.Select(t => t with { SailKm = edge.LengthKm }).ToList();
            var raid = SeaRaids.Plan(W.Seed, e.SeasonNo, rr, mine, v => pb?.WarOf(realm.Index, v) != null, rules, e.Hour, book.Next);
            if (raid == null) continue;
            Launch(e, book, raid, pc);
            planned++;
        }
        return planned;
    }

    /// <summary>The coastal villages of the region as raid targets: those with a sand beach within 2 km, what their households hold (grain at the rule's price, and coin), and the armed men known near them.</summary>
    public List<RaidTarget> CoastalTargets(ArmyEnv e, SeaRules rules)
    {
        var list = new List<RaidTarget>(); var region = Region; var all = Beaches.Of(region, rules);
        if (all.Count == 0 || Sea == null) return list;
        var ht = Households; var eco = W.Economy;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            var st = region.Settlements[s]; if (st.Class == SettlementClass.Town || st.Realm < 0) continue;
            if (Beaches.Near(all, st.CenterX, st.CenterY, 2000).Count == 0) continue;
            long value = 0;
            for (int h = 0; h < ht.Count; h++)
            {
                if (ht.MemberCount[h] <= 0 || ht.Settlement[h] != s) continue;
                value += eco.HouseholdCoin(h).Mites;
                foreach (var g in GrainGoods()) value += eco.HouseholdStore[h].Qty(g) * Math.Max(1, eco.Goods.MassG(g, 1)) / 1000 * rules.D.GrainMitesPerKg;
            }
            int guards = GuardsNear(s);
            list.Add(new RaidTarget(Sea.HomeRegion, s, st.Realm, value, guards, 0));
        }
        return list;
    }

    /// <summary>The place a warband is counted at: where it stands, or its seat while it is on the road.</summary>
    private static int Posted(Warband wb) => wb.Here >= 0 ? wb.Here : wb.Seat;

    private int GuardsNear(int settlement)
    {
        int n = 0; var pt = People;
        for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.HomeSettlement[i] == settlement && pt.Occupation[i] == Occupation.Guard) n++;
        foreach (var wb in Book.Warbands.Values) if (DistM(Posted(wb), settlement) <= 24_000) n += wb.Members.Count;
        return n;
    }

    /// <summary>Puts a planned raid in the book: its number, the beach it will run onto (the sand nearest the village), and the victim's grievance (a <c>raid</c> casus belli).</summary>
    public SeaRaid? Launch(ArmyEnv e, NavalBook book, SeaRaid raid, PoliticsCtx? pc = null)
    {
        var rules = Sea?.Rules ?? SeaRules.Default; var region = Region;
        if ((uint)raid.Settlement >= (uint)region.Settlements.Count) return null;
        var st = region.Settlements[raid.Settlement];
        var beach = Beaches.Near(Beaches.Of(region, rules), st.CenterX, st.CenterY, 2000);
        if (beach.Count == 0) return null;
        int S = RegionData.Size, T = RegionData.TileM;
        raid.BeachXmm = ((beach[0].Tile % S) * (long)T + T / 2) * 1000; raid.BeachYmm = ((beach[0].Tile / S) * (long)T + T / 2) * 1000;
        return Enlist(e, book, raid, pc);
    }

    /// <summary>The book's side of <see cref="Launch"/> once the beach is known (a test may name its own beach): the raid's number, its war's declaration, the grievance.</summary>
    public SeaRaid Enlist(ArmyEnv e, NavalBook book, SeaRaid raid, PoliticsCtx? pc = null)
    {
        raid.Id = book.Next++; raid.State = RaidState.Sailing;
        if (raid.War && Politics?.WarOf(raid.Realm, raid.Victim) is { } war) raid.DeclEntry = war.DeclEntry;
        book.Raids[raid.Id] = raid; book.Touch();
        if (pc != null) SeaRaids.GrantCb(pc, raid);
        return raid;
    }

    // ------------------------------------------------------------------ the hour: sail, land, raid, withdraw

    /// <summary>One game hour of every raid in the book, in id order: the sailing ones land at their dawn, the landed ones run to the village and take what they came for, the raiding ones withdraw (or are met on the beach) and sail.</summary>
    public void SeaRaidsHour(ArmyEnv e)
    {
        var book = Sea?.Peek(); if (book == null || book.Raids.Count == 0) return;
        foreach (var raid in book.Raids.Values.ToList())
        {
            switch (raid.State)
            {
                case RaidState.Sailing when e.Hour >= raid.LandHour: Land(e, raid); break;
                case RaidState.Landed when e.Hour >= raid.LandHour + Sea!.Rules.D.RunHours: Loot(e, raid); break;
                case RaidState.Raiding when e.Hour >= raid.WithdrawHour: raid.State = RaidState.Withdrawing; book.Touch(); break;
            }
            if (raid.State == RaidState.Withdrawing && e.Hour >= raid.WithdrawHour + Sea!.Rules.D.RunHours) Embark(e, raid);
        }
    }

    /// <summary>
    /// The landing (4.5): the raid's longships run onto the sand at dawn, each a real vehicle row on the beach with its crew made as people (stream 0x7D4, key the raid and the man: grown men, no household, a soldier's trade), the
    /// guards of each ship (6 to 10) stay by her and the rest form on the beach. The men are the plan's count, shared out over the ships.
    /// </summary>
    public bool Land(ArmyEnv e, SeaRaid raid)
    {
        var sea = Sea; var rides = sea?.Rides(); if (sea == null || rides == null) return false;
        var d = sea.Rules.D; var bp = rides.Templates.Get("longship");
        int index = 0;
        for (int i = 0; i < raid.PlanShips; i++)
        {
            var ship = rides.Vehicles.Create(bp, ShipOwner(raid.Realm), raid.BeachXmm + i * 12_000L, raid.BeachYmm + i * 12_000L);
            ship.Water = Ravensguard.Sim.Carts.WaterState.Aground;
            raid.Ships.Add(ship.Id.Value);
            int men = raid.PlanMen / raid.PlanShips + (i < raid.PlanMen % raid.PlanShips ? 1 : 0);
            int guards = Math.Min(men - 1, d.GuardsMin + DetRng.Range(W.Seed, raid.Id, (ulong)i, RngStreams.CrewFromOutside, 0, 0, Math.Max(1, d.GuardsMax - d.GuardsMin + 1)));
            for (int k = 0; k < men; k++)
            {
                ulong id = MakeRaider(e, raid, index++);
                (k < guards ? raid.Guards : raid.Raiders).Add(id);
            }
        }
        raid.Raiders.Sort(); raid.Guards.Sort();
        raid.LandHour = e.Hour; raid.State = RaidState.Landed; Book.Touch(); Sea?.Ensure().Touch();
        return true;
    }

    private ulong MakeRaider(ArmyEnv e, SeaRaid raid, int index)
    {
        var w = W; var pt = People; long day = e.Day;
        ulong key = ((ulong)(uint)raid.Id << 32) | (uint)index;
        w.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
        var id = w.Ids.Allocate(IdKind.Person);
        int p = pt.Add();
        if (pt.IdOf(p) != id) throw new InvalidOperationException("person id out of step with the table");
        var pool = w.Names?.For(raid.Culture);
        var rg = new Rnd(w.Seed, key, RngStreams.CrewFromOutside); var rf = new Rnd(w.Seed, key ^ (1UL << 63), RngStreams.CrewFromOutside);
        int age = 18 + new Rnd(w.Seed, key ^ (1UL << 62), RngStreams.CrewFromOutside).Range(0, 22);
        pt.Sex[p] = Sex.Male; pt.BirthDay[p] = PersonTable.BirthDayFor(day, age, new Rnd(w.Seed, key ^ (3UL << 61), RngStreams.CrewFromOutside).Range(0, 60), pt.AgingPace);
        pt.Culture[p] = (byte)raid.Culture; pt.Class[p] = SocialClass.Peasant;
        pt.Family[p] = pool != null ? pool.FamilyName(rf) : "Raider"; pt.Given[p] = pool != null ? pool.Given(false, rg) : "Sea-wolf";
        pt.Status[p] = PersonStatus.Alive; pt.Occupation[p] = Occupation.Soldier; pt.Household[p] = -1;
        var rs = new RngStream(w.Seed, 0, key, RngStreams.CrewFromOutside, 8);
        int Roll() => 35 + rs.Range(0, 21) + rs.Range(0, 21) + rs.Range(0, 21);
        int b = p * PersonTable.AttrCount;
        byte K(int v) => (byte)Math.Clamp(v, 1, 100);
        pt.Attr[b] = K(Roll()); pt.Attr[b + 1] = K(Roll()); pt.Attr[b + 2] = K(Roll()); pt.Attr[b + 3] = K(Roll()); pt.Attr[b + 4] = K(Roll());
        w.Economy.AddPerson(p);
        w.Vital.Alive++; w.Vital.Immigrants++;
        w.NoteBirth(p);
        return id.Value;
    }

    /// <summary>
    /// The raid on the village (4.5): the raiders take grain from the households they rob (a real move from their stores into the ships' holds), a share of each one's coin (a ledger transfer into the ship's strongbox), captives where
    /// their culture's law allows them (the captive book), and put a torch to a thatched building (the fire world). The bell rings: the lords hear of the raid, and the fief's warband (or the nearest) is on its way. The raiders mean to be gone
    /// at the plan's hour, or an hour before the riders come.
    /// </summary>
    public void Loot(ArmyEnv e, SeaRaid raid)
    {
        var sea = Sea!; var d = sea.Rules.D; var rides = sea.Rides()!; var eco = W.Economy;
        var holds = raid.Ships.Select(s => rides.Vehicles.Find(new EntityId(s))).Where(v => v != null).Select(v => v!).ToList();
        raid.State = RaidState.Raiding; raid.LootHour = e.Hour; raid.BellHour = e.Hour;
        var ht = Households; var hhs = new List<int>();
        for (int h = 0; h < ht.Count; h++) if (ht.MemberCount[h] > 0 && ht.Settlement[h] == raid.Settlement) hhs.Add(h);
        int robbed = hhs.Count == 0 ? 0 : Math.Max(1, hhs.Count * d.LootHouseholdPct / 100);
        int start = hhs.Count == 0 ? 0 : (int)(DetRng.Next(W.Seed, raid.Id, (ulong)raid.Settlement, RngStreams.SeaRaidTies, 20) % (ulong)hhs.Count);
        var box = holds.Count > 0 ? eco.Ledger.Open(PurseKind.Chest, new EntityId(holds[0].Id.Value)) : default;
        int n = 0;
        for (int k = 0; k < hhs.Count && n < robbed && holds.Count > 0; k++)
        {
            int h = hhs[(start + k) % hhs.Count];
            long got = TakeGrain(h, holds[n % holds.Count].Cargo, d.LootGrainKg);
            long coin = eco.HouseholdCoin(h).Mites * d.LootCoinPct / 100;
            if (coin > 0 && eco.Ledger.Transfer(eco.HouseholdPurse[h], box, new Coin(coin), TransferReason.Plunder)) { raid.TakenCoin += coin; got++; }
            if (got > 0) { raid.TakenG += got; n++; }
        }
        raid.Households = n;
        TakeCaptives(e, raid);
        Burn(e, raid);
        Book.Hear((int)e.Day, raid.Settlement, 0, raid.Realm, raid.Raiders.Count);
        // the riders: the fief's warband, or the nearest, one ride away
        raid.Defender = -1; raid.FightHour = -1;
        int fi = Region.Settlements[raid.Settlement].FiefIndex;
        var wb = (fi >= 0 ? Book.WarbandOfFief(fi) : null) ?? Book.Warbands.Values.OrderBy(x => DistM(Posted(x), raid.Settlement)).ThenBy(x => x.Id).FirstOrDefault();
        long plan = e.Hour + d.RaidHours;
        if (wb != null && wb.Members.Count > 0)
        {
            int dist = DistM(Posted(wb), raid.Settlement);
            raid.Defender = wb.Id; raid.FightHour = e.Hour + Math.Max(1, (dist + d.RideMPerHour - 1) / d.RideMPerHour);
            plan = Math.Min(plan, Math.Max(e.Hour, raid.FightHour - d.RunHours));          // the alarm cuts the stay short: they break off as the riders come
        }
        raid.WithdrawHour = plan;
        sea.Ensure().Touch();
    }

    private void TakeCaptives(ArmyEnv e, SeaRaid raid)
    {
        var sea = Sea!; var d = sea.Rules.D; var caps = sea.Captives(); if (caps == null || Content == null) return;
        if (BondLaw(raid.Culture) is not (Ravensguard.Sim.Bondage.SlaveLaw.WarCaptives or Ravensguard.Sim.Bondage.SlaveLaw.Chattel)) return;
        var pt = People; var pool = new List<int>(); int day = (int)e.Day;
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i) || pt.HomeSettlement[i] != raid.Settlement || pt.Occupation[i] == Occupation.Guard) continue;
            int age = pt.AgeYears(i, day); if (age < 16 || age > 50) continue;
            var who = pt.IdOf(i); if (IsHero(who.Value) || caps.Find(who) is { Held: true }) continue;
            pool.Add(i);
        }
        int want = Math.Min(pool.Count, d.CaptivesMax > 0 ? 1 + DetRng.Range(W.Seed, raid.Id, (ulong)raid.Settlement, RngStreams.SeaRaidTies, 21, 0, d.CaptivesMax) : 0);
        var captor = new EntityId(raid.Ships.Count > 0 ? raid.Ships[0] : 0); long sec = e.Hour * 3600;
        for (int k = 0; k < want; k++)
        {
            int at = DetRng.Range(W.Seed, raid.Id, (ulong)raid.Settlement, RngStreams.SeaRaidTies, (uint)(30 + k), 0, pool.Count);
            int row = pool[at]; pool.RemoveAt(at);
            var who = pt.IdOf(row);
            caps.Capture(caps.Rules, CaptorKind.Slavers, who, false, captor, new PurseRef(PurseKind.Household, captor), sec, CaptiveRank.Levy, 0, PayerKind.None, [], [], (0, 0, 0, 0, 0), 0);
            raid.Captives.Add(who.Value);
        }
    }

    private Ravensguard.Sim.Bondage.SlaveLaw BondLaw(int culture)
        => Content == null ? Ravensguard.Sim.Bondage.SlaveLaw.None : Ravensguard.Sim.Bondage.BondRules.Parse(Ravensguard.Sim.Faith.ReligionBook.CultureOf(Content, culture)?.Slavery);

    /// <summary>A torch to the thatched building nearest the village's heart that is not already burning (the raiders' leader's flame); nothing without a fire world.</summary>
    private void Burn(ArmyEnv e, SeaRaid raid)
    {
        var d = Sea!.Rules.D; var fw = W.Get<FireWorld>(); if (fw == null || e.Ob == null || raid.Raiders.Count == 0) return;
        var st = Region.Settlements[raid.Settlement]; var ob = e.Ob; var prev = fw.Chronicle;
        fw.Chronicle = (when, type, key, ps, subj, causes, data) => ob.Chronicle(when, type, key, ps, subj, causes, data);
        try
        {
            var cause = FireCause.Person(new EntityId(raid.Raiders[0]), "torch");
            for (int b = 0; b < d.BurnMax; b++)
            {
                int pick = -1; long bestD = long.MaxValue; bool thatch = false;
                foreach (var p in fw.Profiles())
                {
                    if (p.FlammableCells == 0 || fw.Book.Buildings.ContainsKey(p.Structure)) continue;
                    long dx = p.CentreX - st.CenterX, dy = p.CentreY - st.CenterY, dd = dx * dx + dy * dy;
                    if (dd > 600L * 600) continue;
                    bool t = p.ThatchRoof;
                    if ((t && !thatch) || (t == thatch && dd < bestD)) { thatch = t; bestD = dd; pick = p.Structure; }
                }
                if (pick >= 0 && fw.IgniteBuilding(pick, e.Hour, cause)) raid.Burned++;
            }
        }
        finally { fw.Chronicle = prev; }
    }

    /// <summary>
    /// The withdrawal (4.5): the raiders run back to the beach with their loot. If the riders have come by then (the raid's fight hour is not after the embarking hour) there is a fight on the beach, the warband against the raiders as the
    /// Phase 7 band rule fights them; with every raider down the raid is caught and the ships lie on the sand with their holds. Else, or with raiders left, they push off (the ships float free and put to sea), the men leave the region,
    /// and the loot goes with them. The chronicle is written once.
    /// </summary>
    public void Embark(ArmyEnv e, SeaRaid raid)
    {
        var sea = Sea!; var rides = sea.Rides()!; var d = sea.Rules.D;
        long embark = raid.WithdrawHour + d.RunHours;
        if (raid.Defender >= 0 && !raid.Fought && raid.FightHour >= 0 && raid.FightHour <= embark && Book.Warbands.TryGetValue(raid.Defender, out var wb))
        {
            BeachFight(e, raid, wb); raid.Fought = true;
        }
        var pt = People;
        raid.Raiders.RemoveAll(m => { int r = RowOf(m); return r < 0 || pt.Status[r] != PersonStatus.Alive; });
        if (raid.Raiders.Count == 0 && raid.Fought)
        {
            raid.State = RaidState.Caught; raid.EndHour = e.Hour;
            foreach (ulong s in raid.Ships) if (rides.Vehicles.Find(new EntityId(s)) is { } v) v.Water = Ravensguard.Sim.Carts.WaterState.Aground;
            WriteRaid(e, raid, caught: true);
        }
        else
        {
            // push off: the afloat ships go out to sea, the men with them (they are no longer in the region)
            var st = Region.Settlements[raid.Settlement];
            long vx = raid.BeachXmm - st.CenterX * 1000L, vy = raid.BeachYmm - st.CenterY * 1000L, len = Math.Max(1, (long)Math.Sqrt((double)vx * vx + (double)vy * vy));
            int k = 0;
            foreach (ulong s in raid.Ships)
            {
                if (rides.Vehicles.Find(new EntityId(s)) is not { } v) continue;
                v.Water = Ravensguard.Sim.Carts.WaterState.Afloat; v.XMm = raid.BeachXmm + vx * 10_000_000L / len + k * 12_000L; v.YMm = raid.BeachYmm + vy * 10_000_000L / len; k++;
            }
            foreach (ulong m in raid.Raiders.Concat(raid.Guards).OrderBy(x => x))
            {
                int r = RowOf(m); if (r < 0 || pt.Status[r] != PersonStatus.Alive) continue;
                pt.Status[r] = PersonStatus.Departed; W.Vital.Alive--; W.Vital.Emigrants++;
            }
            raid.State = RaidState.Gone; raid.EndHour = e.Hour;
            WriteRaid(e, raid, caught: false);
        }
        sea.Ensure().Touch();
    }

    /// <summary>The warband against the raiders on the beach: a transient band of the raiders (the Phase 7 band fight, abstract); the raiders' dead are counted off the raid.</summary>
    private void BeachFight(ArmyEnv e, SeaRaid raid, Warband wb)
    {
        var pt = People;
        var band = new BanditBand { Id = -raid.Id, Lair = raid.Settlement, Origin = BandOrigin.Deserters, FormedDay = (int)e.Day };
        foreach (ulong m in raid.Raiders)
        {
            int r = RowOf(m); if (r < 0 || pt.Status[r] != PersonStatus.Alive) continue;
            band.Members.Add(m); band.Prev[m] = (byte)pt.Occupation[r]; pt.Occupation[r] = Occupation.Bandit;      // a man who yields has his trade back (ReleaseBandit); one still in the band is still fighting
        }
        if (band.Members.Count == 0) return;
        band.Members.Sort(); band.Leader = band.Members.OrderByDescending(m => MeleeOf(RowOf(m))).ThenBy(m => m).First();
        var res = FightBand(e, wb, band);
        raid.Dead += res.BandDead;
        // the raiders left are those still in the fight; the ones who yielded, and the dead, are off the beach
        foreach (ulong m in raid.Raiders.ToList())
        {
            int r = RowOf(m);
            if (r >= 0 && pt.Status[r] == PersonStatus.Alive && pt.Occupation[r] == Occupation.Bandit) pt.Occupation[r] = Occupation.Soldier;
            else raid.Raiders.Remove(m);
        }
    }

    private void WriteRaid(ArmyEnv e, SeaRaid raid, bool caught)
    {
        var rules = Sea!.Rules; var pt = People;
        raid.Dead = raid.Raiders.Count == 0 && caught ? raid.Dead : raid.Dead;
        Cause cause = raid.War
            ? new Cause(CauseKind.War, raid.DeclEntry == 0 ? null : new EntityId(raid.DeclEntry), "", "ch_note_am_war_raid")
            : new Cause(CauseKind.Raid, null, "", "ch_note_sh_raid_culture");
        var leader = new EntityId(raid.Raiders.Count > 0 ? raid.Raiders[0] : raid.Guards.Count > 0 ? raid.Guards[0] : 0);
        var entry = Chron(e, "sea_raid", "ch_sh_raid",
            [ChronParam.Person("leader", leader), ChronParam.Place("place", PlaceId(raid.Settlement)), ChronParam.Count("ships", raid.Ships.Count), ChronParam.Count("kg", (int)(raid.TakenG / 1000)),
             ChronParam.Count("captives", raid.Captives.Count), ChronParam.Count("burned", raid.Burned), ChronParam.Count("dead", raid.Dead)],
            [leader, PlaceId(raid.Settlement)], cause);
        raid.Entry = entry.Value;
        if (!caught) return;
        Chron(e, "raiders_caught", "ch_sh_caught",
            [ChronParam.Place("place", PlaceId(raid.Settlement)), ChronParam.Count("dead", raid.Dead), ChronParam.Count("ships", raid.Ships.Count)],
            [PlaceId(raid.Settlement)], new Cause(CauseKind.Raid, entry, "", "ch_note_sh_caught"));
    }
}

/// <summary>The sea raids' hour (overview 8.4 <c>Ships</c> = 1,120, H1 after the armies): ships sail, land, raid and withdraw. A no-op while no raid is in the book.</summary>
[AbstractSystem]
public sealed class SeaRaidHourSystem : AbstractSystem
{
    public SeaRaidHourSystem() : base("sea_raids", TickPhase.H1Caravans, SystemOrder.Ships) { }
    public override void Step(TickContext ctx)
    {
        var aw = ctx.World.Get<ArmyWorld>(); if (aw?.Sea == null) return;
        var env = new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour);
        aw.SeaRaidsHour(env);
        aw.ShipsHour(env);                                                                  // [P7-SH-D] ships that burn, take water or are taken
    }
}

/// <summary>The sea raids' season (H5 at the first hour of a season's first day): the realms of raider cultures plan their raids. A no-op outside spring and summer.</summary>
[AbstractSystem]
public sealed class SeaRaidSeasonSystem : AbstractSystem
{
    public SeaRaidSeasonSystem() : base("sea_raids_season", TickPhase.H5DailyGlobals, SystemOrder.Ships) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0 || !ctx.IsSeasonStartDay) return;
        var aw = ctx.World.Get<ArmyWorld>(); if (aw?.Sea == null) return;
        var pb = ctx.World.Get<PoliticsBook>(); var rb = ctx.World.Get<Ravensguard.Sim.Rule.RuleBook>();
        PoliticsCtx? pc = pb != null && rb != null ? PoliticsEngine.Context(ctx.World, pb, rb, Ravensguard.Sim.Rule.RuleChron.Of(ctx.Outbox), (int)ctx.Day) : null;
        aw.PlanSeaRaids(new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour), pc);
    }
}

/// <summary>Installs the sea raids on the running home region (the P7-SH-C hook in <c>WorldRuntime</c>): the host the raids read their world through. The naval book is the world state's, made at the first raid, so a world that never saw one keeps its bytes.</summary>
public static class SeaRaidWiring
{
    public static void Attach(Worlds.RegionWorld home, Save.WorldState state, ContentDatabase content)
    {
        var aw = home.Sim.Get<ArmyWorld>(); if (aw == null) return;
        aw.Sea = new SeaRaidHost
        {
            Peek = () => state.Naval, Ensure = () => state.Naval ??= new NavalBook(), Rides = () => state.Economy != null ? Ravensguard.Sim.Carts.VehicleDesk.BookOf(state, content) : null, HomeRegion = state.HomeRegion.Id,
            Captives = () => AftermathDesk.BookOf(state, content), Edges = () => state.Continent != null ? SeaEdges.Of(state.Continent) : [], History = () => state.History,
            Rules = SeaRules.From(content),
        };
    }
}
