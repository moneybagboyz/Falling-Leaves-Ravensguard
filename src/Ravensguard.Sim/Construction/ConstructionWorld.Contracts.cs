using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;

namespace Ravensguard.Sim.Construction;

/// <summary>
/// P5-D: building contracts (spec 4.7). A site's owner commissions a builder (a carpentry or masonry business of the region) at a bid: materials at the local ask, labour (piece-hours x the
/// skilled wage x the 1,450 pm margin), the haul. A third goes into the contract's escrow at signing (it advances the builder the materials and the haul), a third at half the piece-hours (the rest of the first released to the builder), the rest at
/// handover; a missed stage stops the builder, who keeps the stage paid, and the half-built house stays the client's. The builder buys the materials at the market (real orders) and hauls
/// them to the pile through haul jobs his crew takes; his crew (his owner and staff, hired hands to make up the number) does the same ghost work by the same rules, never instant, at his
/// day's pace (0x511). The lord's planner posts civic work on the notice board with the whole price in escrow: the hero can take it as a job and build it by hand, or a builder of the
/// region takes it after the notice days.
/// </summary>
public sealed partial class ConstructionWorld
{
    /// <summary>The region's builders (carpentry and masonry businesses: their owner and staff), for bids and posted work. Null: none.</summary>
    public Func<IReadOnlyList<BuilderInfo>>? Builders { get; set; }
    /// <summary>The market book of a settlement (materials at the ask, a builder's orders). Null: no market.</summary>
    public Func<int, MarketBook?>? Market { get; set; }
    /// <summary>Background growth made visible on the home region (null in a world without the economy's growth).</summary>
    public GrowthSites? GrowthSites { get; set; }

    private ContractRulesDef? _contractRules;
    public ContractRulesDef ContractRules => _contractRules ??= Content?.All<ContractRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ContractRulesDef { Id = "contracts" };

    public Contract? ContractOf(EntityId siteId)
    {
        foreach (var c in Book.Contracts.Values) if (c.Site == siteId && c.Live) return c;
        return null;
    }

    /// <summary>The work still to do on a site's build ghosts (standard worker units).</summary>
    public long LabourUnits(ConstructionSite s)
    {
        long u = 0;
        foreach (var g in SetOf(s.Id)) if (g.Work == GhostWork.Build) u += Math.Max(0, g.WorkNeed - g.WorkDone);
        return u;
    }

    /// <summary>Labour of a bid: piece-hours x the skilled wage of a 10-hour day x the margin (BC-16: 250.5 h x 350 x 1.45 / 10 = 12,712).</summary>
    public long LabourMites(long units) => (long)((Int128)units * Rules.WageSkilledMitesPerDay * ContractRules.MarginPm / (3_600_000_000L * Rules.DayHours * 1000L));

    /// <summary>A builder's bid for a site (spec 4.7): materials still missing from the pile at the ask of his market, the labour, the haul from his market to the pile; crew and days.</summary>
    public ContractBid Bid(ConstructionSite s, BuilderInfo b, int crew = 0, bool materials = true)
    {
        long units = LabourUnits(s);
        long mats = 0, haul = 0;
        if (materials)
        {
            var pile = PileOf(s);
            var book = Market?.Invoke(b.Settlement);
            var jt = Jobs?.Invoke();
            long pathMm = (long)Math.Sqrt((double)(b.X - s.PileX) * (b.X - s.PileX) + (double)(b.Y - s.PileY) * (b.Y - s.PileY)) * 1000;
            foreach (var (good, need) in Needed(s))
            {
                long miss = Math.Max(0, need - pile.Qty(good));
                if (miss == 0) continue;
                if (book != null) mats += AskFor(book, good, miss);
                if (jt is { } j)
                {
                    long per = Econ.Goods.IsPiece(good) ? Math.Max(1, HaulLoadG / Math.Max(1, Econ.Goods.MassG(good, 1))) : HaulLoadG;
                    for (long left = miss; left > 0; left -= per) haul += Math.Max(1, JobPay.Haul(j.Env.Tuning, j.Env.UnskilledWage, pathMm, Econ.Goods.MassG(good, Math.Min(per, left))));
                }
            }
        }
        if (crew <= 0) crew = ContractRules.CrewDefault;
        crew = Math.Clamp(crew, 1, Rules.CrewMax);
        // days at about 90% of the working day (the pace jitter's mean)
        long dayUnits = (long)crew * Rules.DayHours * 3_600_000_000L * (1000 - ContractRules.PaceJitterPm) / 1000;
        int days10 = dayUnits <= 0 ? 0 : (int)((units * 10 + dayUnits - 1) / dayUnits);
        return new ContractBid(b, mats, LabourMites(units), haul, units / 3_600_000, crew, days10);
    }

    /// <summary>
    /// Materials at the local ask: what buying them from the book would cost now when it holds them, else the ask at the book's reference price (the builder gets them from the
    /// settlement's sources or the next delivery: an empty book's curve price is not what anyone pays).
    /// </summary>
    public static long AskFor(MarketBook book, ushort good, long qty)
    {
        if (qty <= 0) return 0;
        var gb = book.Goods[good];
        if (gb.StockUnits >= qty) return book.QuoteBuy(good, qty);
        return (long)((Int128)qty * gb.RefMicro * (1000 + book.Money.SpreadHalfPermille) / 1000 / PriceCurve.Micro);
    }

    /// <summary>Every builder's bid for a site, cheapest first (then id).</summary>
    public IReadOnlyList<ContractBid> Bids(EntityId siteId, int crew = 0)
    {
        if (Site(siteId) is not { Open: true } s || Builders?.Invoke() is not { } bs) return [];
        return [.. bs.Select(b => Bid(s, b, crew)).OrderBy(x => x.TotalMites).ThenBy(x => x.Builder.Id)];
    }

    private PurseId? PurseOf(PurseKind k, EntityId id) => Econ.Ledger.TryFind(k, id, out var p) ? p : null;
    private PurseId EscrowOf(Contract c) => Econ.Ledger.Open(PurseKind.Chest, c.Escrow);
    private BuilderInfo? Builder(EntityId id) => Builders?.Invoke().FirstOrDefault(b => b.Id == id);

    /// <summary>
    /// The owner of a site hires a builder at his bid (spec 4.7, BC-16): a third of the price into the contract's escrow, the builder pays his crew's wages (the site's payer while the
    /// contract runs), buys the missing materials and posts their haul to the pile.
    /// </summary>
    public ConstructionResult Commission(EntityId by, PurseKind byKind, EntityId siteId, EntityId builderId, int crew, long nowSec)
    {
        var s = Site(siteId);
        if (s == null || !s.Open) return ConstructionResult.No("cn_no_site");
        if (s.Owner != by) return ConstructionResult.No("cn_not_your_site");
        if (ContractOf(siteId) != null) return ConstructionResult.No("cn_contract_exists");
        if (Builder(builderId) is not { } b) return ConstructionResult.No("cn_no_builder");
        if (Book.Contracts.Values.Any(c => c.State == ContractState.Signed && c.Contractor == b.Id)) return ConstructionResult.No("cn_builder_busy", b.Name);
        var bid = Bid(s, b, crew);
        long third = bid.TotalMites / 3;
        if (PurseOf(byKind, by) is not { } cp || Econ.Ledger.Balance(cp).Mites < third) return ConstructionResult.No("cn_cannot_pay_stage", third);
        var c = new Contract
        {
            Id = ConstructionIds.Make(RegionId, ConstructionIds.ContractSlot, Book.Take()), Kind = ContractKind.Commission, State = ContractState.Signed, Site = s.Id, Client = by, ClientKind = byKind,
            Contractor = b.Id, ContractorKind = b.Kind, Blueprint = s.Blueprint, Settlement = b.Settlement, MaterialsMites = bid.MaterialsMites, LabourMites = bid.LabourMites, HaulMites = bid.HaulMites,
            WorkUnits = LabourUnits(s), Crew = bid.Crew, PostedSec = nowSec, SignedSec = nowSec,
        };
        Book.Contracts[c.Id] = c;
        Econ.Ledger.Transfer(cp, EscrowOf(c), new Coin(third), JobReasons.Escrow);
        c.Stages = 1;
        // the escrow advances the builder what the materials and their haul cost (at most the third): he buys and carries before he is paid for his work
        Release(c, EscrowOf(c), PurseOf(b.Kind, b.Id) ?? Econ.Ledger.Open(b.Kind, b.Id), Math.Min(third, bid.MaterialsMites + bid.HaulMites));
        StartBuilder(c, s, b, nowSec);
        Chron(nowSec, "contract_signed", [by, b.Id, s.Id], [new Cause(CauseKind.Other, null, "a builder hired at his bid")], ("total", c.TotalMites.ToString()), ("crew", c.Crew.ToString()));
        Publish(nowSec);
        return new ConstructionResult(true, "cn_contract_signed", [b.Name, c.TotalMites, bid.ExpectedDaysX10 / 10, bid.ExpectedDaysX10 % 10], third, c.Id);
    }

    /// <summary>The builder takes over the site: he pays its hired hands, his owner and staff build (their business pays them), hired hands make up the crew; he buys and hauls the materials.</summary>
    private void StartBuilder(Contract c, ConstructionSite s, BuilderInfo b, long nowSec)
    {
        s.Payer = b.Id; s.PayerKind = b.Kind;
        if (s.State == SiteState.Paused && s.Pause == PauseReason.Owner) { s.State = SiteState.Active; s.Pause = PauseReason.None; }
        var taken = new HashSet<EntityId>(Book.Sites.Values.Where(o => o.Open).SelectMany(o => o.Crew.Select(m => m.Person)));
        int n = 0;
        if (Pop != null)
            foreach (int row in b.People)
            {
                if (n >= c.Crew || s.Crew.Count >= Rules.CrewMax) break;
                if (row < 0 || row >= Pop.People.Count || !Pop.People.IsAlive(row)) continue;
                var id = Pop.People.IdOf(row);
                if (taken.Contains(id)) continue;
                s.Crew.Add(new CrewMember { Person = id, PersonIndex = row, Class = CrewClass.Follower });
                n++;
            }
        if (n < c.Crew) Hire(b.Id, -1, s.Id, c.Crew - n, false, nowSec);
        if (Book.AdvancedSec < 0) Book.AdvancedSec = nowSec / Rules.QuantumSeconds * Rules.QuantumSeconds;
        if (c.Kind == ContractKind.Commission) BuyForSite(c, s, b, nowSec);           // posted work: the materials are the client's
        Refresh(s);
    }

    /// <summary>
    /// The builder buys what the site still lacks (beyond the pile and his store) from the settlement's sources and its market (real orders: the demand registers in the book), and his
    /// cart brings what he holds to the pile, one working hour at a time: a cart's load per round trip between his yard and the pile (<see cref="ContractRulesDef.CartKg"/>).
    /// </summary>
    private void BuyForSite(Contract c, ConstructionSite s, BuilderInfo b, long nowSec, bool buy = true)
    {
        if (b.Store == null) return;
        var pile = PileOf(s);
        var book = Market?.Invoke(b.Settlement);
        var cr = ContractRules;
        long dist = (long)Math.Sqrt((double)(b.X - s.PileX) * (b.X - s.PileX) + (double)(b.Y - s.PileY) * (b.Y - s.PileY));
        long tripSec = 2 * dist * 60 / Math.Max(1, cr.CartMetresPerMinute) + cr.CartLoadMinutes * 60L;
        long budgetG = cr.CartKg * 1000L * 3600 / Math.Max(60, tripSec);
        bool moved = false;
        foreach (var (good, need) in Needed(s))
        {
            long miss = need - pile.Qty(good);
            if (miss <= 0) continue;
            long have = b.Store.Qty(good);
            if (buy && have < miss && b.Party != null)
            {
                // the builder's sources are the settlement's (poles, clay, the commons' straw for thatch), then the market
                if (GrowthSites != null) GrowthSites.Source(b.Settlement, good, miss - have, b.Store, b.Party, 0, (int)(nowSec / 86400));
                else book?.Buy(b.Party, good, miss - have, 3000, (int)(nowSec / 86400));
            }
            long room = Econ.Goods.IsPiece(good) ? budgetG / Math.Max(1, Econ.Goods.MassG(good, 1)) : budgetG;      // what the cart's load still takes of it
            long send = Math.Min(Math.Min(miss, b.Store.Qty(good)), room);
            if (send <= 0) continue;
            long got = Store.MoveUpTo(b.Store, pile, good, send);
            budgetG -= Econ.Goods.MassG(good, got);
            moved |= got > 0;
            if (budgetG <= 0) break;
        }
        if (moved) Refresh(s);
    }

    /// <summary>
    /// The lord's planner (or any client) posts a site's work on the notice board (spec 4.7): the labour at the bid's rate, all of it put into the contract's escrow now; the materials are
    /// the client's (the pile). Taken by the hero (<see cref="Take"/>) or, after the notice days, by a builder of the region.
    /// </summary>
    public ConstructionResult Post(EntityId client, PurseKind clientKind, EntityId siteId, ContractKind kind, int settlement, long nowSec, int growthSerial = -1)
    {
        var s = Site(siteId);
        if (s == null || !s.Open) return ConstructionResult.No("cn_no_site");
        if (ContractOf(siteId) != null) return ConstructionResult.No("cn_contract_exists");
        long units = LabourUnits(s), labour = LabourMites(units);
        if (PurseOf(clientKind, client) is not { } cp || Econ.Ledger.Balance(cp).Mites < labour) return ConstructionResult.No("cn_cannot_pay_stage", labour);
        var c = new Contract
        {
            Id = ConstructionIds.Make(RegionId, ConstructionIds.ContractSlot, Book.Take()), Kind = kind, State = ContractState.Open, Site = s.Id, Client = client, ClientKind = clientKind,
            Blueprint = s.Blueprint, Settlement = settlement, LabourMites = labour, WorkUnits = units, Crew = ContractRules.CrewDefault, PostedSec = nowSec, GrowthSerial = growthSerial,
        };
        Book.Contracts[c.Id] = c;
        Econ.Ledger.Transfer(cp, EscrowOf(c), new Coin(labour), JobReasons.Escrow);
        c.Stages = 3;
        Chron(nowSec, "contract_posted", [client, s.Id], [new Cause(CauseKind.Other, null, kind == ContractKind.Civic ? "the lord's planner wants it built" : "work offered on the board")], ("wages", labour.ToString()));
        Publish(nowSec);
        return new ConstructionResult(true, "cn_contract_posted", [labour], labour, c.Id);
    }

    /// <summary>The open contracts on the board (spec 4.7), oldest first.</summary>
    public IEnumerable<Contract> Board() => Book.Contracts.Values.Where(c => c.State == ContractState.Open);

    /// <summary>
    /// The hero takes a posted contract as a job (decided with the user: hands-on): he builds it by hand (and with his followers), is paid half of the escrow at half the piece-hours and the
    /// rest at handover; if it is not finished by the due day it goes back on the board and he keeps what he was paid.
    /// </summary>
    public ConstructionResult Take(EntityId hero, PurseKind heroKind, EntityId contractId, long nowSec)
    {
        if (Book.Contracts.GetValueOrDefault(contractId) is not { State: ContractState.Open } c) return ConstructionResult.No("cn_no_contract");
        if (Book.Contracts.Values.Any(o => o.State == ContractState.Signed && o.Contractor == hero)) return ConstructionResult.No("cn_one_contract");
        var s = Site(c.Site);
        if (s == null || !s.Open) return ConstructionResult.No("cn_no_site");
        c.State = ContractState.Signed; c.Contractor = hero; c.ContractorKind = heroKind; c.SignedSec = nowSec; c.WorkUnits = LabourUnits(s);
        long expectedDays = (c.WorkUnits + Rules.DayHours * 3_600_000_000L - 1) / (Rules.DayHours * 3_600_000_000L);
        c.DueSec = nowSec + Math.Max(ContractRules.MinDueDays, expectedDays * ContractRules.DueFactor) * 86400L;
        Econ.Ledger.Open(heroKind, hero);
        if (s.State == SiteState.Paused && s.Pause == PauseReason.Owner) { s.State = SiteState.Active; s.Pause = PauseReason.None; }
        Refresh(s);
        Chron(nowSec, "contract_taken", [hero, s.Id], [new Cause(CauseKind.Other, null, "taken from the notice board")], ("wages", c.LabourMites.ToString()));
        Publish(nowSec);
        return new ConstructionResult(true, "cn_contract_taken", [c.LabourMites, (c.DueSec - nowSec) / 86400], 0, c.Id);
    }

    /// <summary>The hero gives a contract up: it goes back on the board; what he was paid he keeps.</summary>
    public ConstructionResult Drop(EntityId hero, EntityId contractId, long nowSec)
    {
        if (Book.Contracts.GetValueOrDefault(contractId) is not { State: ContractState.Signed } c || c.Contractor != hero) return ConstructionResult.No("cn_no_contract");
        Repost(c, nowSec, "given up");
        Publish(nowSec);
        return ConstructionResult.Yes("cn_contract_dropped");
    }

    private void Repost(Contract c, long nowSec, string why)
    {
        Chron(nowSec, "contract_lapsed", [c.Contractor, c.Site], [new Cause(CauseKind.Other, null, why)]);
        c.State = ContractState.Open; c.Contractor = default; c.ContractorKind = PurseKind.Person; c.PostedSec = nowSec; c.SignedSec = 0; c.DueSec = 0;
    }

    /// <summary>Whether <paramref name="person"/> builds this site under a contract (the land rule then lets him work there).</summary>
    public bool IsContractor(ConstructionSite s, EntityId person)
    {
        foreach (var c in Book.Contracts.Values) if (c.Site == s.Id && c.State == ContractState.Signed && c.Contractor == person) return true;
        return false;
    }

    /// <summary>
    /// The game second a site's crew starts work in the day of <paramref name="t0"/>: a builder's crew under a signed contract starts 0 to twice the jitter of the day late (stream 0x511,
    /// key contract and game day), in whole quanta; 0 for every other site.
    /// </summary>
    private long LateStart(ConstructionSite s, long t0)
    {
        if (Book.Contracts.Count == 0) return 0;
        Contract? c = null;
        foreach (var o in Book.Contracts.Values) if (o.Site == s.Id && o.State == ContractState.Signed && o.ContractorKind != PurseKind.Person) { c = o; break; }
        if (c == null) return 0;
        long day = t0 / 86400, q = Math.Max(1, Rules.QuantumSeconds);
        long span = (long)Rules.DayHours * 3600 * 2 * ContractRules.PaceJitterPm / 1000 / q;            // whole quanta, 0 to twice the jitter of the day
        long late = DetRng.Range(Seed, day, c.Id.Value, RngStreams.ContractorPace, 0, 0, (int)span + 1) * q;
        return day * 86400 + Rules.DayStartHour * 3600L + late;
    }

    /// <summary>
    /// The contracts' turn at each hour's start (inside <see cref="AdvanceTo"/>, so a function of game time whatever the cadence): stages fall due at half the piece-hours and at handover;
    /// a builder buys what is missing at 06:00; posted work past its notice days goes to a builder of the region; the hero's contract past its due day goes back on the board.
    /// </summary>
    private void ContractsStep(long nowSec)
    {
        if (Book.Contracts.Count == 0) return;
        bool any = false;
        foreach (var c in Book.Contracts.Values.ToList())
        {
            var s = Site(c.Site);
            if (s == null) continue;
            if (s.Deferred > 0) Settle(s, Math.Max(1, Rules.QuantumSeconds));          // the cheap path's deferred quanta first: a stage reads the work done
            s.LinearUntil = 0;
            switch (c.State)
            {
                case ContractState.Open:
                    if (nowSec >= c.PostedSec + ContractRules.NoticeDays * 86400L && s.Open) any |= TakeByBuilder(c, s, nowSec);
                    break;
                case ContractState.Signed:
                    any |= Stage(c, s, nowSec);
                    if (c.State == ContractState.Signed && c.ContractorKind == PurseKind.Person && c.DueSec > 0 && nowSec >= c.DueSec) { Repost(c, nowSec, "not finished by the due day"); any = true; }
                    // the builder orders at 06:00 and has his loads carried every working hour (the board takes a few at a time)
                    long tod = nowSec % 86400;
                    if (c.State == ContractState.Signed && c.Kind == ContractKind.Commission && tod >= Rules.DayStartHour * 3600L && tod < (Rules.DayStartHour + Rules.DayHours) * 3600L
                        && Builder(c.Contractor) is { } b) BuyForSite(c, s, b, nowSec, buy: tod == Rules.DayStartHour * 3600L);
                    break;
                case ContractState.Done or ContractState.Stopped:
                    // the builder's crew is gone (paid at the day's end): the site is the client's again
                    if (s.Payer == c.Contractor && s.Crew.Count == 0) { s.Payer = c.Client; s.PayerKind = c.ClientKind; any = true; }
                    break;
            }
        }
        _ = any;
    }

    /// <summary>Contracts that still need the clock (open on the board or being built).</summary>
    private bool AnyLiveContract()
    {
        foreach (var c in Book.Contracts.Values) if (c.Live || (c.State is ContractState.Done or ContractState.Stopped && Site(c.Site) is { } s && s.Payer == c.Contractor)) return true;
        return false;
    }

    /// <summary>The stages of a signed contract: half the piece-hours built, and handover.</summary>
    private bool Stage(Contract c, ConstructionSite s, long nowSec)
    {
        long left = LabourUnits(s), done = Math.Max(0, c.WorkUnits - left);
        var escrow = EscrowOf(c);
        var to = PurseOf(c.ContractorKind, c.Contractor) ?? Econ.Ledger.Open(c.ContractorKind, c.Contractor);
        bool posted = c.Kind != ContractKind.Commission;
        if (s.State == SiteState.Finished || (left == 0 && !GhostsOf(s.Id).Any()))
        {
            // handover: the client pays what is still owed (a posted contract's escrow holds it all), the escrow pays the builder
            if (!posted && c.Stages < 3)
            {
                long owe = c.TotalMites - c.TotalMites / 3 * c.Stages;
                if (PurseOf(c.ClientKind, c.Client) is { } cp && Econ.Ledger.Balance(cp).Mites >= owe) { Econ.Ledger.Transfer(cp, escrow, new Coin(owe), JobReasons.Escrow); c.Stages = 3; }
            }
            Release(c, escrow, to, Econ.Ledger.Balance(escrow).Mites);
            c.State = ContractState.Done; c.DoneSec = nowSec;
            Chron(nowSec, "contract_done", [c.Client, c.Contractor, s.Id], [new Cause(CauseKind.Other, null, "the last piece built and handed over")], ("paid", c.ReleasedMites.ToString()));
            return true;
        }
        if (done * 1000 < c.WorkUnits * ContractRules.MidStagePm) return false;
        if (posted)
        {
            if (c.ReleasedMites > 0) return false;
            Release(c, escrow, to, Econ.Ledger.Balance(escrow).Mites / 2);
            return true;
        }
        if (c.Stages >= 2) return false;
        // the second third: the escrow pays the builder the first, the client puts up the second
        Release(c, escrow, to, Econ.Ledger.Balance(escrow).Mites);
        long third = c.TotalMites / 3;
        if (PurseOf(c.ClientKind, c.Client) is { } p && Econ.Ledger.Balance(p).Mites >= third && Econ.Ledger.Transfer(p, escrow, new Coin(third), JobReasons.Escrow)) { c.Stages = 2; return true; }
        // missed: the builder stops and keeps the stage paid; the half-built house is the client's
        c.State = ContractState.Stopped; c.DoneSec = nowSec;
        s.Pause = PauseReason.NoWages;
        foreach (var m in s.Crew) m.Ghost = default;
        s.Crew.RemoveAll(m => m.Class == CrewClass.Follower);
        Event(ConstructionEventKind.Paused, nowSec, s.PileX, s.PileY, s.PileZ, "", s.Id);
        Chron(nowSec, "contract_stopped", [c.Client, c.Contractor, s.Id], [new Cause(CauseKind.Other, null, "the client missed a stage payment")], ("owed", third.ToString()));
        return true;
    }

    private void Release(Contract c, PurseId escrow, PurseId to, long mites)
    {
        if (mites <= 0) return;
        if (Econ.Ledger.Transfer(escrow, to, new Coin(mites), JobReasons.Pay)) c.ReleasedMites += mites;
    }

    /// <summary>Posted work past its notice: the most skilled builder of the region who is free takes it (his owner and staff build, hired hands make up the crew).</summary>
    private bool TakeByBuilder(Contract c, ConstructionSite s, long nowSec)
    {
        if (Builders?.Invoke() is not { } bs) return false;
        var busy = new HashSet<EntityId>(Book.Contracts.Values.Where(o => o.State == ContractState.Signed).Select(o => o.Contractor));
        BuilderInfo? best = null;
        foreach (var b in bs.OrderByDescending(b => b.BestSkill).ThenBy(b => (long)(b.X - s.PileX) * (b.X - s.PileX) + (long)(b.Y - s.PileY) * (b.Y - s.PileY)).ThenBy(b => b.Id))
            if (!busy.Contains(b.Id)) { best = b; break; }
        if (best == null) return false;
        c.State = ContractState.Signed; c.Contractor = best.Id; c.ContractorKind = best.Kind; c.SignedSec = nowSec; c.WorkUnits = LabourUnits(s);
        Econ.Ledger.Open(best.Kind, best.Id);
        StartBuilder(c, s, best, nowSec);
        Chron(nowSec, "contract_taken", [best.Id, s.Id], [new Cause(CauseKind.Other, null, "a builder of the region took the posted work")], ("wages", c.LabourMites.ToString()));
        return true;
    }
}
