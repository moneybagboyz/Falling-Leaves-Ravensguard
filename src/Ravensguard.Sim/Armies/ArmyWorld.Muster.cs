using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>
/// A ruler's call of his levy (armies-on-the-map 4.2, 4.3): which realm, where the host gathers and what the rest of the campaign is worth to him (mites; 0 for a call with no campaign behind it, so the levy goes home for the harvest).
/// <see cref="Commander"/> null is the realm's ruler; <see cref="MaxLevies"/> caps the men asked for in all; <see cref="Raise"/> lets the realm borrow and tax to fund the chest (the hero's call, a campaign brain's), where the AI's
/// call funds it from the treasury or not at all.
/// </summary>
public sealed record MusterCall(int Realm, int Point, long Worth = 0, ulong? Commander = null, int MaxLevies = int.MaxValue, bool Raise = false, int FundDays = 0, bool FollowsWar = true);

/// <summary>
/// P7-AM-B, levies and the muster (armies-on-the-map 4.2, 4.3, 4.10): a ruler's summons goes out by rider, each fief of the realm draws its levy the day after it hears (real men taken from their households' work by
/// <see cref="HostSupply.Muster"/>, the village draw of overview 10d, with the household's gear by its wealth), the columns walk to the muster point, the host forms when four fifths of the expected men are there or three days after
/// the first company, the levies serve ten days unpaid and then are paid or sent home, the ruler weighs the harvest at the day 25 and the winter sends the rest home. Vassals serve with their warbands or pay scutage
/// (<see cref="HostSupply.CallWarService"/>). Nothing here makes a man, a coin or a grain: the men are persons of the world, the pay is the host's chest, the food the host supply's ladder.
/// </summary>
public sealed partial class ArmyWorld
{
    /// <summary>The army economy's service of the running home region, made on first use (the wiring sets it; null in a bare world).</summary>
    public Func<HostSupply>? SupplyProvider { get; set; }

    private UnrestCtx? _uctx; private int _uctxDay = int.MinValue;

    /// <summary>The host supply, with the unrest rule's levy evasion bound to it (the households that hide their man, stream 0x682).</summary>
    public HostSupply? Supply
    {
        get
        {
            var hs = SupplyProvider?.Invoke() ?? W.Get<HostSupply>();
            if (hs != null && hs.Evades == null) hs.Evades = EvadesLevy;
            return hs;
        }
    }

    private bool EvadesLevy(int settlement, int household, int day)
    {
        var ub = W.Get<UnrestBook>(); var rb = Rule; if (ub == null || rb == null) return false;
        if (_uctx == null || _uctxDay != day) { _uctx = UnrestWiring.Context(W, ub, rb, null, day); _uctxDay = day; }
        return UnrestEngine.EvadesLevy(_uctx, settlement, household);
    }

    // ------------------------------------------------------------------ what a ruler asks

    /// <summary>The men a fief owes in a muster (3.3, taxes 3.9): one in ten households in peace, one in five in war.</summary>
    public int QuotaOf(int fief, bool atWar)
    {
        var ew = Ew; if (ew == null) return 0;
        return ew.Taxes.LevyQuota(HouseholdsOfFief(fief), atWar);
    }

    /// <summary>Households with people in them in a fief's settlements.</summary>
    public int HouseholdsOfFief(int fief)
    {
        var ht = Households; int n = 0;
        for (int i = 0; i < ht.Count; i++) { int s = ht.Settlement[i]; if (ht.MemberCount[i] > 0 && s >= 0 && s < Region.Settlements.Count && Region.Settlements[s].FiefIndex == fief) n++; }
        return n;
    }

    /// <summary>The man who leads a realm's host: its ruler where the realm has one among the people, else the holder of the fief that holds its seat, else the holder of its first fief; null when the realm has no lord here.</summary>
    public ulong? CommanderOf(int realm)
    {
        var rb = Rule; if (rb == null) return null;
        if (rb.Rulers.TryGetValue(realm, out long p) && p >= 0 && RowOf((ulong)p) is var r && r >= 0 && People.IsAlive(r)) return (ulong)p;
        if (Politics?.RealmIfAny(realm) is { Ruler: not 0 } row && RowOf(row.Ruler) is var r2 && r2 >= 0 && People.IsAlive(r2)) return row.Ruler;
        if (Ravensguard.Sim.Production.SeatBuildings.Seats(Region).TryGetValue(realm, out int seat) && (uint)seat < (uint)Region.Settlements.Count && rb.FiefOf(Region.Settlements[seat].FiefIndex) is { Holder: >= 0 } sf) return (ulong)sf.Holder;
        foreach (var f in rb.Fiefs.Values) if (f.Realm == realm && f.Holder >= 0 && !f.Forfeited) return (ulong)f.Holder;
        return null;
    }

    private bool LordsAlive(long holder) => holder >= 0 && RowOf((ulong)holder) is var r && r >= 0 && People.IsAlive(r);

    /// <summary>The fiefs a muster asks, ascending: the realm's, held by a living lord and not forfeited.</summary>
    public List<FiefRow> FiefsOfRealm(int realm)
    {
        var rb = Rule; var res = new List<FiefRow>(); if (rb == null) return res;
        foreach (var f in rb.Fiefs.Values) if (f.Realm == realm && !f.Forfeited && LordsAlive(f.Holder) && (uint)f.Fief < (uint)Region.Fiefs.Count) res.Add(f);
        return res;
    }

    /// <summary>
    /// The town of the realm nearest the enemy's land (armies-on-the-map 4.3: "the town nearest the objective on his side"): the realm's towns by their straight distance to the nearest settlement of the enemy realm, ties by index; the realm's
    /// seat when it has no town near; -1 when the realm has no settlement here.
    /// </summary>
    public int MusterPointFor(int realm, int enemy)
    {
        var hs = Supply; if (hs == null) return -1;
        int best = -1; long bd = long.MaxValue;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            if (hs.RealmOf(s) != realm || Region.Settlements[s].Class != SettlementClass.Town) continue;
            long d = long.MaxValue;
            for (int t = 0; t < Region.Settlements.Count; t++)
            {
                if (hs.RealmOf(t) != enemy) continue;
                long dx = Region.Settlements[s].CenterX - Region.Settlements[t].CenterX, dy = Region.Settlements[s].CenterY - Region.Settlements[t].CenterY;
                d = Math.Min(d, dx * dx + dy * dy);
            }
            if (d < bd) { bd = d; best = s; }
        }
        if (best >= 0) return best;
        if (Ravensguard.Sim.Production.SeatBuildings.Seats(Region).TryGetValue(realm, out int seat)) return seat;
        foreach (var f in FiefsOfRealm(realm)) return Region.Fiefs[f.Fief].SeatSettlement;
        return -1;
    }

    /// <summary>Days a column takes to walk from one settlement to another along the road graph at the pace of 1.3 m/s, 16 hours a day (3.7 km a day); a settlement itself is no walk.</summary>
    public int WalkDays(int from, int to)
    {
        if (from == to || from < 0 || to < 0) return 0;
        long m = PathOf(from, to) is { } p ? p.LengthMm / 1000 : DistM(from, to) * 14 / 10;
        long perDay = (long)Math.Max(1, Rules.MarchMPerHour) * Math.Max(1, Rules.MarchHoursPerDay);
        return (int)((m + perDay - 1) / perDay);
    }

    // ------------------------------------------------------------------ the summons

    /// <summary>
    /// The ruler calls his levy (4.3): the host is made (state <c>Summoned</c>), every fief of the realm is told by rider (it hears within the hour, or the day, the road to its seat takes at 12 m/s in stages), the vassals are called to serve or
    /// pay scutage (<see cref="HostSupply.CallWarService"/>), the ruler's own warband and the serving vassals' ride to the point, and <c>muster_called</c> is written (its cause the war's declaration, else the lord's will). Returns the host,
    /// or null when the realm already has one, has no lord or no man to ask, or the call cannot be made.
    /// </summary>
    public ArmyHost? CallMuster(ArmyEnv e, MusterCall call)
    {
        var rb = Rule; var hs = Supply; if (rb == null || hs == null) return null;
        if (Book.HostOfRealm(call.Realm) != null || (uint)call.Point >= (uint)Region.Settlements.Count) return null;
        ulong? cmd = call.Commander ?? CommanderOf(call.Realm); if (cmd == null) return null;
        var war = Politics?.WarOf(call.Realm); bool atWar = war != null; int day = (int)e.Day;
        var fiefs = FiefsOfRealm(call.Realm); int left = call.MaxLevies, total = 0;
        var host = new ArmyHost { Id = Book.NextHost, Realm = call.Realm, War = call.FollowsWar ? war?.Serial ?? -1 : -1, Point = call.Point, SummonsDay = day, Commander = cmd.Value, Worth = call.Worth, FundDays = call.FundDays > 0 ? call.FundDays : Rules.FundDays, Raises = call.Raise, State = HostState.Summoned };
        int fromSeat = FiefOfLord(cmd.Value) is var cf && cf >= 0 ? Region.Fiefs[cf].SeatSettlement : call.Point;
        foreach (var f in fiefs)
        {
            if (left <= 0) break;
            int q = Math.Min(QuotaOf(f.Fief, atWar), left); if (q <= 0) continue;
            int seat = Region.Fiefs[f.Fief].SeatSettlement;
            int hours = Math.Max(1, (int)((DistM(fromSeat, seat) + Rules.SummonsMPerHour - 1) / Math.Max(1, Rules.SummonsMPerHour)));
            host.Companies.Add(new LevyCompany { Fief = f.Fief, Seat = seat, Quota = q, Heard = day + (e.HourOfDay + hours) / 24 });
            left -= q; total += q;
        }
        host.Expected = total;
        Book.NextHost++; Book.Hosts[host.Id] = host;
        var cause = war != null
            ? new Cause(CauseKind.War, war.DeclEntry != 0 ? new EntityId(war.DeclEntry) : null, "", "ch_note_am_muster_war")
            : new Cause(CauseKind.Campaign, null, "", "ch_note_am_muster_called");
        var entry = Chron(e, "muster_called", "ch_am_muster", [ChronParam.Person("ruler", new EntityId(cmd.Value)), ChronParam.Place("place", PlaceId(call.Point)), ChronParam.Count("men", total)], [new EntityId(cmd.Value), PlaceId(call.Point)], cause);
        host.Entry = entry.Value;
        // the vassals: each serves with his men or pays scutage in their place; those who serve bring their warbands, the ruler brings his own
        hs.Chron = e.Chronicler;
        foreach (var sc in hs.CallWarService(call.Realm, day))
            if (sc.Served && Book.WarbandOfFief(sc.Fief) is { } vw && vw.Lord == (ulong)sc.Vassal && Book.HostOfWarband(vw.Id) == null) Join(e, host, vw, ContingentKind.Vassal);
        if (Book.WarbandOfLord(cmd.Value) is { } own && Book.HostOfWarband(own.Id) == null) Join(e, host, own, ContingentKind.Warband);
        return host;
    }

    private void Join(ArmyEnv e, ArmyHost host, Warband wb, ContingentKind kind)
    {
        if (wb.Escorting != 0 && Book.Errands.TryGetValue(wb.Escorting, out var er) && er.Escort == wb.Id) { er.Escort = 0; if (er.State == ErrandState.Moving) er.State = ErrandState.Waiting; }
        wb.Escorting = 0;
        SetTask(e, wb, new WarbandOption(WarbandTask.JoinHost, 1000, host.Point, (ulong)host.Id, "ch_note_am_join_host", wb.Here == host.Point ? [] : [host.Point]));
        host.Warbands.Add(new WarbandJoin(wb.Id, kind, wb.Members.Count)); host.Expected += wb.Members.Count;
    }

    // ------------------------------------------------------------------ the AI's call (S11War, the first day of summer)

    /// <summary>
    /// A realm at war whose ruler is not the hero calls its levy on the first day of summer, the day after the spring sowing, when its treasury can carry the levy for the days it funds (4.3). The muster point is the realm's town nearest the enemy.
    /// What the host does after it forms is the campaign brain's (AM-E): with no campaign behind the call it is worth nothing and the levy goes home for the harvest.
    /// </summary>
    private void AutoMuster(ArmyEnv e)
    {
        var rb = Rule; var pb = Politics; if (rb == null || pb == null || !pb.Wars.Any(w => w.Alive)) return;                  // no war, no call, and no host supply made for nothing
        var hs = Supply; if (hs == null) return;
        foreach (int realm in rb.Fiefs.Values.Where(f => f.Realm >= 0 && !f.Forfeited && f.Holder >= 0).Select(f => f.Realm).Distinct().OrderBy(r => r))
        {
            var war = pb.WarOf(realm); if (war == null || Book.HostOfRealm(realm) != null) continue;
            ulong? cmd = CommanderOf(realm); if (cmd == null || IsHero(cmd.Value) || (rb.Rulers.TryGetValue(realm, out long ruler) && IsHero((ulong)ruler))) continue;
            int point = MusterPointFor(realm, war.Attacker == realm ? war.Defender : war.Attacker); if (point < 0) continue;
            long asked = 0; foreach (var f in FiefsOfRealm(realm)) asked += QuotaOf(f.Fief, true);
            if (asked <= 0) continue;
            var b = hs.Budget(realm, new Roster([(TroopRole.Levy, (int)Math.Min(asked, 100_000))]), Rules.FundDays);
            if (b.TreasuryMites < b.DailyCostMites * Rules.FundDays) continue;
            CallMuster(e, new MusterCall(realm, point));
        }
    }

    // ------------------------------------------------------------------ the daily step (hour 18, after the host supply's day)

    /// <summary>
    /// A day of the hosts (ArmiesDaily, after <see cref="HostSupply.RunDay"/>): the fiefs that have heard draw their men, the columns arrive, the host forms, the owed days end, the ruler weighs the harvest on day 25 and winter sends the
    /// levies home; the released walk home and are back on their fields on arrival; the dead are struck off.
    /// </summary>
    public void MusterDay(ArmyEnv e)
    {
        if (Book.Hosts.Count == 0) return;
        var hs = Supply; if (hs == null) return;
        hs.Chron = e.Chronicler;
        int day = (int)e.Day, year = day / TimeConst.DaysPerYear, doy = e.DayOfYear;
        foreach (var host in Book.Hosts.Values.ToList())
        {
            if (host.State != HostState.Disbanding)
            {
                Prune(hs, host);
                if (host.State == HostState.Summoned) Draw(e, hs, host, day);
                if (host.State != HostState.Disbanding && host.State != HostState.Summoned)
                {
                    Gather(e, hs, host, day);
                    Decide(e, hs, host, day, doy, year);
                }
            }
            WalkHome(host, day);
            if (host.State == HostState.Disbanding && host.Companies.Count == 0) { Book.Hosts.Remove(host.Id); Book.Touch(); }
        }
    }

    private void Prune(HostSupply hs, ArmyHost host)
    {
        foreach (var c in host.Companies)
        {
            if (c.Walking) continue;
            for (int i = c.Men.Count - 1; i >= 0; i--)
            {
                ulong m = c.Men[i]; int r = RowOf(m);
                if (r >= 0 && People.IsAlive(r)) continue;
                c.Men.RemoveAt(i); Soldiers?.Book.Rows.Remove(m);
                if (r >= 0 && host.Supply != 0) hs.Release(new HostId(host.Supply), [r]);
                if (m == c.Captain) c.Captain = BestOf(c);
            }
        }
        for (int i = host.Warbands.Count - 1; i >= 0; i--) if (!Book.Warbands.ContainsKey(host.Warbands[i].Warband)) host.Warbands.RemoveAt(i);
    }

    private ulong BestOf(LevyCompany c)
    {
        ulong best = 0; int bs = -1;
        foreach (ulong m in c.Men) { int r = RowOf(m); if (r < 0) continue; int s = MeleeOf(r); if (s > bs || (s == bs && m < best)) { bs = s; best = m; } }
        return best;
    }

    // ------------------------------------------------------------------ the draw

    private void Register(ArmyEnv e, HostSupply hs, ArmyHost host)
    {
        var econ = W.Economy; var led = econ.Ledger; int n = 1_000_000 + host.Id;
        var chest = led.Open(PurseKind.Chest, HostIds.Chest(n));
        var bag = econ.OpenStore(StoreKind.Sealed, HostIds.Baggage(n));
        var id = hs.Register(new HostSpec(host.Realm, new EntityId(host.Commander), chest, bag, new Roster(), 0, new AnimalCounts(0, 0, 0, 0), 0, new SupplyPolicy())
        { Treasury = hs.RealmTreasury(host.Realm), Campaign = new EntityId(host.Entry) });
        host.Supply = id.Value;
        hs.Update(id, hs.PlaceAt(host.Point), HostMode.Camp);
    }

    /// <summary>The fiefs that have heard, in fief order, draw their men: the village draw by the host supply, the men's gear by their households' wealth, the captain, the march to the point; then the chest is funded.</summary>
    private void Draw(ArmyEnv e, HostSupply hs, ArmyHost host, int day)
    {
        var due = host.Companies.Where(c => !c.IsDrawn && day >= c.Heard + Rules.GatherDays).OrderBy(c => c.Fief).ToList();
        if (due.Count == 0) return;
        if (host.Supply == 0) Register(e, hs, host);
        var hid = new HostId(host.Supply); var rc = RuleCtx(e);
        foreach (var c in due)
        {
            var r = hs.Muster(hid, c.Fief, c.Quota);
            c.Drawn = day; c.Asked = r.HouseholdsAsked; c.Evaded = r.Evaded;
            foreach (int row in r.Persons) c.Men.Add(People.IdOf(row).Value);
            c.Men.Sort(); c.Captain = BestOf(c);
            c.Arrives = day + WalkDays(c.Seat, host.Point);
            Arm(e, host, c, rc);
            host.Expected += c.Men.Count - c.Quota;                                                // the call counted the quota: now it counts the men who came (evaders and the shortfall are not owed)
            Chron(e, "levy_drawn", "ch_am_levy", [ChronParam.Place("place", PlaceId(c.Seat)), ChronParam.Count("men", c.Men.Count), ChronParam.Count("evaded", c.Evaded)], [new EntityId(host.Commander), PlaceId(c.Seat)],
                new Cause(CauseKind.Campaign, new EntityId(host.Entry), "", "ch_note_am_levy_called"));
        }
        host.Expected = Math.Max(0, host.Expected);
        if (host.Companies.All(c => c.IsDrawn))
        {
            int men = host.Companies.Sum(c => c.Men.Count);
            if (men == 0 && host.Warbands.Count == 0) { Release(e, hs, host, "ch_note_am_no_men"); return; }
            host.State = HostState.Mustering;
            FundChest(e, hs, host, day);
        }
    }

    /// <summary>Each levy brings what his household's wealth lists (SC-A's <c>LevyGear</c>): real items of the household, moved to him, none made; he is a soldier of the commander's, role levy.</summary>
    private void Arm(ArmyEnv e, ArmyHost host, LevyCompany c, RuleCtx? rc)
    {
        if (Soldiers == null || rc == null) return;
        var items = W.Economy.Items; var ht = Households; var want = new Dictionary<ulong, List<EntityId>>();
        var hhOf = new Dictionary<ulong, int>();
        foreach (ulong m in c.Men) { int row = RowOf(m); if (row < 0) continue; int hh = People.Household[row]; if (hh >= 0) hhOf[m] = hh; }
        foreach (int hh in hhOf.Values.Distinct()) want[ht.IdOf(hh).Value] = [];
        foreach (int row in items.LiveRows())
        {
            ref var it = ref items.At(row);
            if (it.Parent < 0 && it.Owner.Kind == IdKind.Household && want.TryGetValue(it.Owner.Value, out var list)) list.Add(it.Id);
        }
        foreach (ulong m in c.Men)
        {
            if (!hhOf.TryGetValue(m, out int hh)) continue;
            var wealth = Soldiers.Roles.WealthOf(RuleCouncil.Movables(rc, hh));
            Soldiers.Levies.Bring(Soldiers.Book, m, wealth, want[ht.IdOf(hh).Value].Where(id => items.IsLive(id) && items.Get(id).Owner.Kind == IdKind.Household), (long)host.Commander, (int)e.Day);
        }
    }

    /// <summary>The realm funds the chest for the days it means to keep the host (the treasury's coin, the whole of it a ledger transfer); a call that may raise coin (the hero's, a brain's) borrows and taxes by the rule as well.</summary>
    private void FundChest(ArmyEnv e, HostSupply hs, ArmyHost host, int day)
    {
        var hid = new HostId(host.Supply); var led = W.Economy.Ledger;
        var a = hs.Account(hid); var plan = a.Roster; int days = Math.Max(1, host.FundDays);
        var bud = hs.Budget(host.Realm, plan, days); long cost = bud.DailyCostMites * days;
        var purse = hs.RealmTreasury(host.Realm);
        if (purse.IsNone) return;
        long have = led.Balance(purse).Mites;
        if (have >= cost || RaiseAllowed(host)) { hs.Fund(hid, days, day); return; }
        long move = Math.Min(have, cost - led.Balance(hs.ChestOf(hid)).Mites);
        if (move > 0) led.Transfer(purse, hs.ChestOf(hid), new Coin(move), TransferReason.Hoard);
    }

    private static bool RaiseAllowed(ArmyHost host) => host.Raises;

    // ------------------------------------------------------------------ gathering

    private int Arrived(ArmyHost host, int day)
    {
        int n = 0;
        foreach (var c in host.Companies) if (c.IsDrawn && !c.Walking && day >= c.Arrives) n += c.Men.Count;
        foreach (var j in host.Warbands) if (Book.Warbands.TryGetValue(j.Warband, out var wb) && wb.Here == host.Point && wb.Stops.Count == 0) n += wb.Members.Count;
        return n;
    }

    /// <summary>The host forms (4.3) when four fifths of the expected men are at the point or three days after the first of them came; the levies' kit is filled from the armoury at the point, gaps shown; the host is fed and paid where it stands.</summary>
    public void Gather(ArmyEnv e, HostSupply hs, ArmyHost host, int day)
    {
        if (host.State != HostState.Mustering) return;
        int arrived = Arrived(host, day);
        if (host.FirstArrival < 0 && arrived > 0) host.FirstArrival = day;
        if (host.Expected <= 0) return;
        if (arrived * 100 < host.Expected * Rules.FormPct && !(host.FirstArrival >= 0 && day >= host.FirstArrival + Rules.FormWaitDays)) return;
        host.State = HostState.Camped; host.FormedDay = day;
        EquipHost(e, host);
        Chron(e, "host_formed", "ch_am_host_formed", [ChronParam.Person("ruler", new EntityId(host.Commander)), ChronParam.Place("place", PlaceId(host.Point)), ChronParam.Count("men", Math.Max(arrived, 1))],
            [new EntityId(host.Commander), PlaceId(host.Point)], new Cause(CauseKind.Campaign, new EntityId(host.Entry), "", "ch_note_am_host_gathered"));
    }

    /// <summary>The armoury at the muster point fills the levies' spears and shields by SC-A's rule (the lord's default fill for a levy), best piece first and gaps shown; nothing is made.</summary>
    private void EquipHost(ArmyEnv e, ArmyHost host)
    {
        if (Soldiers == null) return;
        long? key = ArmouryOf(host.Point); if (key == null) return;
        var mine = new HashSet<ulong>(); foreach (var c in host.Companies) foreach (ulong m in c.Men) mine.Add(m);
        var men = Soldiers.SoldiersOf((long)host.Commander, r => mine.Contains(r.Person));
        if (men.Count > 0) Soldiers.FillFrom(key.Value, men, (int)e.Day, (long)host.Commander);
    }

    // ------------------------------------------------------------------ the ruler's decisions

    private void Decide(ArmyEnv e, HostSupply hs, ArmyHost host, int day, int doy, int year)
    {
        if (host.State == HostState.Besieging) return;                                                // a host besieging stays (4.7)
        if (host.War >= 0 && Politics is { } pb && !pb.Wars.Any(w => w.Serial == host.War && w.Alive)) { Release(e, hs, host, "ch_note_am_war_over"); return; }
        int levies = host.Levies;
        if (doy >= Rules.WinterReleaseDoy && host.SummonsDay < day - (doy - Rules.WinterReleaseDoy)) { Release(e, hs, host, "ch_note_am_winter_release"); return; }       // winter, and the call was made before it
        if (host.Supply == 0 || levies == 0) { if (host.State == HostState.Camped && levies == 0 && host.Warbands.Count == 0) Release(e, hs, host, "ch_note_am_service_over"); return; }
        var hid = new HostId(host.Supply); var acc = hs.Account(hid);
        // the owed days are done: the lord pays the levy on, or sends him home (Q1)
        if (!host.Paying && acc.ServiceDays >= SupplyConfig.LevyFreeDays)
        {
            var (chest, tomorrow) = hs.PayState(hid, day + 1);
            if (host.Worth > 0 && chest >= tomorrow * Rules.KeepPayDays) host.Paying = true;
            else { Release(e, hs, host, "ch_note_am_service_over"); return; }
        }
        // day 25: the campaign against the grain the levies' households would leave standing in the reap window (4.10)
        if (doy == Rules.HarvestDecisionDoy && host.DecidedYear != year)
        {
            host.DecidedYear = year; long kg = 0, mites = 0;
            foreach (var c in host.Companies) if (!c.Walking && c.Men.Count > 0) { var (k, m) = hs.HarvestRiskPriced(c.Fief, c.Men.Count, Rules.ReapWindowDays); kg += k; mites += m; }
            if (host.Worth > mites)
                Chron(e, "host_held_for_harvest", "ch_am_hold", [ChronParam.Person("ruler", new EntityId(host.Commander)), ChronParam.Place("place", PlaceId(host.Point)), ChronParam.Count("men", levies), ChronParam.Count("kg", kg), ChronParam.Mites("shortfall", mites)],
                    [new EntityId(host.Commander), PlaceId(host.Point)], new Cause(CauseKind.Campaign, new EntityId(host.Entry), "", "ch_note_am_hold_worth"));
            else { Release(e, hs, host, "ch_note_am_harvest"); return; }
        }
    }

    // ------------------------------------------------------------------ release

    /// <summary>
    /// Sends the host home (4.2, 4.10, 4.11): the levies' arrears are paid as far as the chest goes, they leave the host's books (the household's labour returns when they are home: they walk, by the road, from the point), their kit goes back
    /// to their households and issued pieces to the armoury, the warbands ride home to their seats, the chest's coin goes back to the realm's treasury. Writes <c>levy_released</c> with the reason as its note.
    /// </summary>
    public void Release(ArmyEnv e, HostSupply hs, ArmyHost host, string noteKey)
    {
        if (host.State == HostState.Disbanding) return;
        int day = (int)e.Day, men = 0;
        if (host.Supply != 0)
        {
            var hid = new HostId(host.Supply);
            if (hs.Exists(hid)) hs.PayDownArrears(hid);
            foreach (var c in host.Companies)
            {
                if (c.Walking || !c.IsDrawn) continue;
                var rows = new List<int>(); foreach (ulong m in c.Men) { int r = RowOf(m); if (r >= 0) rows.Add(r); }
                if (hs.Exists(hid)) hs.Release(hid, rows);
                GiveKitBack(c);
                men += c.Men.Count;
                c.Walking = true; c.HomeDay = day + (day >= c.Arrives ? WalkDays(host.Point, c.Seat) : Math.Max(0, day - c.Drawn));
            }
            if (hs.Exists(hid)) hs.Disband(hid);
        }
        host.Companies.RemoveAll(c => !c.IsDrawn);
        foreach (var j in host.Warbands.ToList())
        {
            if (!Book.Warbands.TryGetValue(j.Warband, out var wb) || wb.Task != WarbandTask.JoinHost) continue;
            SetTask(e, wb, new WarbandOption(WarbandTask.Idle, 0, -1, 0, "ch_note_am_host_released", wb.Here == wb.Seat ? [] : [wb.Seat]));
        }
        host.Warbands.Clear();
        host.State = HostState.Disbanding; host.ReleasedDay = day; Book.Touch();
        Chron(e, "levy_released", "ch_am_levy_released", [ChronParam.Person("ruler", new EntityId(host.Commander)), ChronParam.Place("place", PlaceId(host.Point)), ChronParam.Count("men", men)],
            [new EntityId(host.Commander), PlaceId(host.Point)], new Cause(CauseKind.Campaign, new EntityId(host.Entry), "", noteKey));
    }

    /// <summary>A levy's own kit goes back to his household's things and the pieces issued from an armoury to it; his soldier's row is gone.</summary>
    private void GiveKitBack(LevyCompany c)
    {
        if (Soldiers == null) return;
        var items = W.Economy.Items;
        foreach (ulong m in c.Men)
        {
            if (Soldiers.Book.Of(m) is { } row)
            {
                var issued = new HashSet<ulong>(row.Issued.Select(i => i.Item)); int r = RowOf(m); int hh = r >= 0 ? People.Household[r] : -1;
                if (hh >= 0) foreach (var (_, item) in row.Worn) if (!issued.Contains(item) && items.IsLive(new EntityId(item)) && items.Get(new EntityId(item)).Owner.Value == m) items.Get(new EntityId(item)).Owner = Households.IdOf(hh);
            }
            Soldiers.Fill.Discharge(Soldiers.Book, m);
        }
    }

    /// <summary>The released men who have walked home are back at their fields: the company is struck off and their labour is theirs again.</summary>
    private void WalkHome(ArmyHost host, int day)
    {
        bool any = false;
        for (int i = host.Companies.Count - 1; i >= 0; i--)
            if (host.Companies[i].Walking && day >= host.Companies[i].HomeDay) { host.Companies.RemoveAt(i); any = true; }
        if (any) Book.Touch();
    }
}
