using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Succession;

/// <summary>
/// Who receives a holding: a person row (-1 none), the person's id, the holder id and purse kind holdings are written with (a household for an NPC, the hero's own id for the play heir, the lord's
/// household or treasury), whether it is the play heir (the hero's id stays: nothing moves), whether it is the lord (escheat), and a name for the screens.
/// </summary>
public readonly record struct Recipient(int Row, EntityId Person, EntityId Holder, PurseKind Kind, bool Hero, bool Lord, string Name);

/// <summary>One line of the succession screen's estate summary: what, its value, who gets it, whether it escheats, whether it is the play heir's.</summary>
public sealed record EstateLineView(EstateThing Kind, EntityId Id, string Name, long ValueMites, string To, bool Escheat, bool ToHeir);

/// <summary>
/// What the succession screen shows before the estate settles (inheritance 4.3, 6): the coin, the funeral, the debts and what the coin pays of them, what the play heir receives, what goes to other kin
/// and what goes to the lord, under which law and will. A pure function of the tables (<see cref="EstateWorld.Summary"/>): the same before and after a save (IN-11).
/// </summary>
public sealed record EstateSummary(string Dead, string Heir, string Law, bool Will, long CoinMites, long FuneralMites, long DebtsMites, long DebtsFromCoinMites, long CoinToHeirMites,
    IReadOnlyList<EstateLineView> Lines, IReadOnlyList<(string Creditor, DebtKind Kind, long Mites)> Debts)
{
    public IEnumerable<EstateLineView> YouInherit => Lines.Where(l => l.ToHeir);
    public IEnumerable<EstateLineView> ToTheLord => Lines.Where(l => l.Escheat);
    public IEnumerable<EstateLineView> ToOthers => Lines.Where(l => !l.ToHeir && !l.Escheat);
    public static readonly EstateSummary Empty = new("", "", "", false, 0, 0, 0, 0, 0, [], []);
}

/// <summary>
/// Estates settled (inheritance 4.1-4.6; P5-H): <b>one</b> rule for the hero and for every person who dies holding something. <see cref="Gather"/> finds what the dead held (deeds, leases, stalls,
/// claims, charters, businesses, shares, building sites and contracts, vehicles, animals, commissions, apprenticeships, the home chest, a person purse) by holder, never by a search of the world;
/// the plan gives each a recipient (a will first, then the culture's law: primogeniture or partible, then the lord), pays the funeral and the debts in the fixed order (wages by person index, rent
/// and dues by creditor, commission balances, from the coin, then the chest, then goods sold at bid to the creditor directly) and transfers the rest: coin through the ledger, goods by store moves,
/// ownership by rewriting the holder, so coin, mass and deeds are conserved by construction. What a debt still lacks is claimed at the next season's start (S6) in the order stalls, shares,
/// vehicles and animals, businesses, deeds, lowest value first. Pure in the tables (no draw).
/// </summary>
public sealed class EstateWorld
{
    public WorldState State { get; }
    public ContentDatabase Content { get; }
    /// <summary>Phase 6 hook (relief, wardship): called for each deed an heir inherits; Phase 5 charges nothing.</summary>
    public Action<Deed, Recipient>? OnDeedInherited { get; set; }
    /// <summary>Phase 6 hook: a dead lord's fief (a <see cref="HoldingKind.Fief"/> deed or the fief lord's row): recorded only in Phase 5.</summary>
    public Action<int, int>? OnFiefPassed { get; set; }

    public EstateWorld(WorldState state, ContentDatabase content) { State = state; Content = content; }

    private EconomyState Econ => State.Economy!;
    private PersonTable Pt => State.Population.People;
    private Population Pop => State.Population;
    private LandWorld? Land => State.Economy != null && State.Land != null ? LandWiring.For(State, Content) : null;
    private OwnershipWorld? Own => State.Production != null ? OwnershipWiring.For(State, Content) : null;

    // ------------------------------------------------------------------ what the dead held

    internal sealed class Held
    {
        public EstateThing Kind;
        public EntityId Id;
        public long Value;
        public int Settlement = -1;
        public bool Real;
        public bool Carried;
        public object? Ref;
    }

    private long DeedValue(Deed d)
    {
        if (d.Kind == HoldingKind.Lot && !d.IsLease && Land is { } lw) return lw.ValueOf(d);
        if (d.Kind == HoldingKind.Stall) return Land?.Rules.StallRentMitesPerSeason ?? d.DuesMitesPerSeason;
        return Math.Max(0, d.DuesMitesPerSeason);
    }

    private int SettlementOf(Deed d)
    {
        if (d.Settlement >= 0) return d.Settlement;
        if (d.Kind == HoldingKind.Parcel && (uint)d.Subject < (uint)Pop.Region.Parcels.Count) return Pop.Region.Parcels[(int)d.Subject].Settlement;
        if (d.Lord >= 0 && d.Lord < Pop.Region.Fiefs.Count) return Pop.Region.Fiefs[d.Lord].SeatSettlement;
        return -1;
    }

    private long BusinessValue(Business b) => Own is { } ow ? ow.ValueOf(b) : Econ.Ledger.Balance(b.Purse).Mites;

    /// <summary>
    /// Every holding of the books by its holder (one pass over each book): deeds, leases and stalls, open claims, granted charters, shares, open building sites, live contracts, vehicles, animals,
    /// open commissions and apprenticeships (both sides). <paramref name="only"/> restricts it to one holder. The hero's business rows and chest are added by <see cref="Gather"/>.
    /// </summary>
    private Dictionary<EntityId, List<Held>> Index(EntityId? only = null)
    {
        var idx = new Dictionary<EntityId, List<Held>>();
        void Add(EntityId holder, Held h) { if (only is { } o && holder != o) return; if (!idx.TryGetValue(holder, out var l)) idx[holder] = l = []; l.Add(h); }
        if (State.Land is { } land)
        {
            foreach (var d in land.Deeds.Values)
            {
                if (only is { } o && d.Holder != o) continue;
                var k = d.Kind == HoldingKind.Stall ? EstateThing.Stall : d.IsLease ? EstateThing.Lease : EstateThing.Deed;
                Add(d.Holder, new Held { Kind = k, Id = d.Id, Value = DeedValue(d), Settlement = SettlementOf(d), Real = k != EstateThing.Stall, Ref = d });
            }
            int reg = Land?.RegionId ?? 0;
            foreach (var c in land.Claims) if (c.State == ClaimState.Open) Add(c.Claimant, new Held { Kind = EstateThing.Claim, Id = LandIds.Serial(reg, c.Serial), Settlement = -1, Real = true, Ref = c });
            foreach (var c in land.Charters) if (c.State == CharterState.Granted) Add(c.Grantee, new Held { Kind = EstateThing.Charter, Id = LandIds.Serial(reg, c.Serial), Settlement = c.Anchor, Real = true, Ref = c });
        }
        if (State.Ownership is { } ob && State.Production is { } prod2)
            foreach (var h in ob.Holdings.Values)
                foreach (var s in h.Shares)
                    if ((only == null || s.Holder == only) && prod2.ById(h.Business) is { } b)
                        Add(s.Holder, new Held { Kind = EstateThing.Share, Id = h.Business, Value = Valuation.SharePrice(BusinessValue(b), s.Pm), Settlement = b.Settlement, Ref = h });
        if (State.Construction is { } cb)
        {
            foreach (var s in cb.Sites.Values) if (s.Open || s.State == SiteState.Abandoned) Add(s.Owner, new Held { Kind = EstateThing.Site, Id = s.Id, Value = s.PaidMites, Real = true, Ref = s });
            // a contract in progress: the client's side is an asset (what his escrow still holds for the builder; the materials chest and the yard are the builder's account and stay with the
            // contract), the hero's own building job (a posted contract he took) is personal labour and goes back on the board
            foreach (var c in cb.Contracts.Values)
            {
                if (!c.Live) continue;
                long escrow = Econ.Ledger.TryFind(PurseKind.Chest, c.Escrow, out var ep) ? Econ.Ledger.Balance(ep).Mites : 0;
                Add(c.Client, new Held { Kind = EstateThing.Contract, Id = c.Id, Value = escrow, Settlement = c.Settlement, Ref = c });
                if (c.State == ContractState.Signed && c.ContractorKind == PurseKind.Person && c.Contractor != c.Client)
                    Add(c.Contractor, new Held { Kind = EstateThing.Contract, Id = c.Id, Settlement = c.Settlement, Ref = new ContractorJob(c) });
            }
        }
        if (State.Mounts is { } rb)
        {
            foreach (var v in rb.Vehicles.Rows) Add(v.Owner, new Held { Kind = EstateThing.Vehicle, Id = v.Id, Ref = v, Carried = rb.Driving == v.Id });
            foreach (var a in rb.Animals.Rows)
                if (a.State != AnimalState.Dead) Add(a.Owner, new Held { Kind = EstateThing.Animal, Id = a.Id, Ref = a, Carried = rb.Mounted == a.Id || a.State is AnimalState.Ridden or AnimalState.Following or AnimalState.Hitched });
        }
        if (State.Crafting is { } cs)
        {
            foreach (var c in cs.Commissions.Values) if (c.Open) Add(c.Client, new Held { Kind = EstateThing.Commission, Id = c.Id, Value = c.Escrow, Ref = c });
            foreach (var a in cs.Apprenticeships.Values)
                if (!a.Ended) { Add(a.Apprentice, new Held { Kind = EstateThing.Apprenticeship, Id = a.Id, Ref = a }); if (a.Master != a.Apprentice) Add(a.Master, new Held { Kind = EstateThing.Apprenticeship, Id = a.Id, Ref = a }); }
        }
        return idx;
    }

    /// <summary>The dead's own building job under a contract he took (the contractor's side).</summary>
    private sealed record ContractorJob(Contract C);

    /// <summary>The index of the day's settling (built once by <see cref="Daily"/>), else null: a single estate scans for its holder.</summary>
    private Dictionary<EntityId, List<Held>>? _index;

    /// <summary>Everything <paramref name="holder"/> (row <paramref name="row"/>) holds, in a fixed order (kind, then id). <paramref name="hero"/> adds his businesses and the home chest.</summary>
    internal List<Held> Gather(EntityId holder, int row, bool hero)
    {
        var src = _index ?? Index(holder);
        var list = src.TryGetValue(holder, out var l) ? [.. l] : new List<Held>();
        foreach (var h in list) { if (!hero) h.Carried = false; if (h.Ref is Deed { Kind: HoldingKind.Fief } fd) OnFiefPassed?.Invoke(fd.Lord, row); }
        // a business: the hero's here; an NPC's passes by the labour market's succession (the same law, EstateSettler.Heirs, and the will through LabourMarket.WillHeir)
        if (hero && State.Production is { } prod && row >= 0)
            foreach (var b in prod.Businesses)
                if (b.Owner == row && b.State == BusinessState.Open) list.Add(new Held { Kind = EstateThing.Business, Id = b.Id, Value = BusinessValue(b), Settlement = b.Settlement, Real = true, Ref = b });
        if (hero && Econ.Ledger.TryFind(PurseKind.Chest, OwnershipWorld.HeroChestId, out var chest) && Econ.Ledger.Balance(chest).Mites > 0)
            list.Add(new Held { Kind = EstateThing.Chest, Id = OwnershipWorld.HeroChestId, Value = Econ.Ledger.Balance(chest).Mites, Ref = chest });
        list.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Id.CompareTo(b.Id));
        return list;
    }

    // ------------------------------------------------------------------ recipients

    private Recipient Npc(int row)
    {
        int hh = Pt.Household[row];
        return hh >= 0 && hh < Pop.Households.Count
            ? new Recipient(row, Pt.IdOf(row), Pop.Households.IdOf(hh), PurseKind.Household, false, false, Pt.FullName(row))
            : new Recipient(row, Pt.IdOf(row), Pt.IdOf(row), PurseKind.Person, false, false, Pt.FullName(row));
    }

    /// <summary>The lord of a place as a recipient (escheat): his household, else the fief's treasury, else the town's chest; his row when the fief has a living lord.</summary>
    public Recipient LordAt(int settlement)
    {
        var r = Pop.Region;
        if ((uint)settlement >= (uint)r.Settlements.Count) settlement = r.Settlements.Count > 0 ? 0 : -1;
        if (settlement < 0) return new Recipient(-1, EntityId.None, EntityId.None, PurseKind.Person, false, true, "");
        var (id, kind) = LandRegistry.LordOf(Pop, settlement);
        int fief = r.Settlements[settlement].FiefIndex;
        int lord = fief >= 0 && fief < Pop.FiefLord.Length ? Pop.FiefLord[fief] : -1;
        if (lord >= 0 && (lord >= Pt.Count || Pt.Status[lord] != PersonStatus.Alive)) lord = -1;
        string name = fief >= 0 && fief < r.Fiefs.Count ? r.Fiefs[fief].Name : r.Settlements[settlement].Name;
        return new Recipient(lord, lord >= 0 ? Pt.IdOf(lord) : id, id, kind, false, true, name);
    }

    private PurseId PurseOf(Recipient to)
    {
        if (to.Kind == PurseKind.Household && Pop.Households.IndexOf(to.Holder) is int h && h >= 0 && h < Econ.HouseholdCount) return Econ.HouseholdPurse[h];
        return Econ.Ledger.Open(to.Kind, to.Holder);
    }

    private Store StoreOf(Recipient to)
    {
        if (to.Kind == PurseKind.Household && Pop.Households.IndexOf(to.Holder) is int h && h >= 0 && h < Econ.HouseholdStore.Length) return Econ.HouseholdStore[h];
        return Econ.OpenStore(StoreKind.Chest, to.Holder, 0, 0, false);
    }

    private Recipient OfHolder(EntityId holder, PurseKind kind)
    {
        if (kind == PurseKind.Household && Pop.Households.IndexOf(holder) is int h && h >= 0)
        {
            int head = Pop.Households.Head[h];
            return new Recipient(head, head >= 0 && head < Pt.Count ? Pt.IdOf(head) : holder, holder, kind, false, false, Pop.Households.Name[h]);
        }
        int row = Pt.IndexOf(holder);
        return new Recipient(row, holder, holder, kind, false, false, row >= 0 ? Pt.FullName(row) : "");
    }

    // ------------------------------------------------------------------ the plan

    internal sealed class Plan
    {
        public EntityId Dead;
        public int Row = -1;
        public bool Hero;
        public string DeadName = "";
        public Recipient? PlayHeir;
        public bool PlayHeirKin;
        public List<PropertyHeir> Heirs = [];
        public InheritanceRule Law;
        public Will? Will;
        public List<(Held H, Recipient To)> Lines = [];
        public List<EstateDebt> Debts = [];
        public PurseId Purse;
        public bool HasPurse;
        public int Home = -1;
    }

    private Recipient? OfPerson(EntityId person, Plan p)
    {
        if (person == EntityId.None) return null;
        if (p.PlayHeir is { } ph && (ph.Person == person || ph.Row >= 0 && Pt.IndexOf(person) == ph.Row)) return ph;
        int row = Pt.IndexOf(person);
        if (row < 0 || row >= Pt.Count || Pt.Status[row] is not (PersonStatus.Alive or PersonStatus.Hero)) return null;
        return Npc(row);
    }

    private Recipient OfHeir(PropertyHeir h, Plan p) => p.PlayHeir is { } ph && ph.Row >= 0 && ph.Row == h.Row ? ph : Npc(h.Row);

    /// <summary>The recipient of one holding (4.1): the will (everything, or this thing), then a companion keeps what he carries, then the law (partible sons round robin, else the first heir), else the lord.</summary>
    private Recipient To(Held h, Plan p, int sonIndex)
    {
        if (p.Will is { } w)
        {
            if (OfPerson(w.AllTo, p) is { } all) return all;
            if (OfPerson(w.ThingTo(h.Id), p) is { } thing) return thing;
        }
        if (p.PlayHeir is { } ph && !p.PlayHeirKin && h.Carried) return ph;
        if (p.Hero && p.Row < 0) return p.PlayHeir ?? LordAt(h.Settlement >= 0 ? h.Settlement : p.Home);   // a hero without a person row: the Phase 4 rule (the play heir takes all)
        var sons = p.Law == InheritanceRule.Partible ? EstateSettler.PartibleSons(p.Heirs) : [];
        if (sons.Count > 1 && sonIndex >= 0) return OfHeir(sons[sonIndex % sons.Count], p);
        if (p.Heirs.Count > 0) return OfHeir(p.Heirs[0], p);
        return LordAt(h.Settlement >= 0 ? h.Settlement : p.Home);
    }

    internal Plan MakePlan(EntityId dead, int row, bool hero, Recipient? playHeir, bool playHeirKin, PurseId? purse, int day, Func<int, bool>? unavailable = null)
    {
        var p = new Plan { Dead = dead, Row = row, Hero = hero, PlayHeir = playHeir, PlayHeirKin = playHeirKin, Will = State.Wills?.Of(dead) };
        if (purse is { } pu) { p.Purse = pu; p.HasPurse = true; }
        else if (Econ.Ledger.TryFind(PurseKind.Person, dead, out var pp)) { p.Purse = pp; p.HasPurse = true; }
        if (row >= 0 && row < Pt.Count)
        {
            p.DeadName = Pt.FullName(row);
            p.Home = Pt.HomeSettlement[row];
            p.Heirs = EstateSettler.Heirs(Pt, row, day, null, unavailable, Pop.Kin);
            p.Law = EstateSettler.LawOf(Content, Pt.Culture[row]);
        }
        if (p.Home < 0 && State.Ownership is { HeroAt: >= 0 } ob && hero) p.Home = ob.HeroAt;
        var held = Gather(dead, row, hero);
        // partible: real property dealt round robin among the sons by value (each holding whole); movables likewise (each whole), coin equally
        var sons = p.Law == InheritanceRule.Partible ? EstateSettler.PartibleSons(p.Heirs) : [];
        int[] deal = sons.Count > 1 ? EstateSettler.RoundRobin([.. held.Select(h => (h.Id, h.Value))], sons.Count) : new int[held.Count];
        if (sons.Count > 1)
        {
            // real property and movables are dealt separately, each eldest first by value (4.2 rule 4)
            var real = held.Select((h, i) => (h, i)).Where(x => x.h.Real).ToList(); var mov = held.Select((h, i) => (h, i)).Where(x => !x.h.Real).ToList();
            var dr = EstateSettler.RoundRobin([.. real.Select(x => (x.h.Id, x.h.Value))], sons.Count); var dm = EstateSettler.RoundRobin([.. mov.Select(x => (x.h.Id, x.h.Value))], sons.Count);
            for (int k = 0; k < real.Count; k++) deal[real[k].i] = dr[k];
            for (int k = 0; k < mov.Count; k++) deal[mov[k].i] = dm[k];
        }
        for (int i = 0; i < held.Count; i++) p.Lines.Add((held[i], To(held[i], p, sons.Count > 1 ? deal[i] : -1)));
        // a contract follows its site: whoever gets the half-built house is the client who pays its stages
        var siteTo = p.Lines.Where(l => l.H.Ref is ConstructionSite).ToDictionary(l => l.H.Id, l => l.To);
        for (int i = 0; i < p.Lines.Count; i++)
            if (p.Lines[i].H.Ref is Contract c && siteTo.TryGetValue(c.Site, out var st)) p.Lines[i] = (p.Lines[i].H, st);
        p.Debts = DebtsOf(held);
        return p;
    }

    /// <summary>The debts of the holdings (4.2 rule 2), in the fixed order: owed wages and steward wages by person index, rent and dues arrears by creditor id, commission balances by creditor id.</summary>
    private List<EstateDebt> DebtsOf(List<Held> held)
    {
        var wages = new List<(int Row, EstateDebt D)>(); var rent = new List<EstateDebt>(); var comm = new List<EstateDebt>();
        foreach (var h in held)
        {
            if (h.Ref is Business b)
            {
                for (int i = 0; i < b.Staff.Count; i++)
                    if (b.Owed[i] > 0 && Pt.Household[b.Staff[i]] is int hh && hh >= 0)
                        wages.Add((b.Staff[i], new EstateDebt { Kind = DebtKind.Wage, Creditor = Pop.Households.IdOf(hh), CreditorKind = PurseKind.Household, OwedMites = b.Owed[i], Subject = b.Id }));
                if (State.Ownership?.Of(b.Id) is { Steward: >= 0, StewardOwed: > 0 } sh && Pt.Household[sh.Steward] is int shh && shh >= 0)
                    wages.Add((sh.Steward, new EstateDebt { Kind = DebtKind.Steward, Creditor = Pop.Households.IdOf(shh), CreditorKind = PurseKind.Household, OwedMites = sh.StewardOwed, Subject = b.Id }));
            }
            else if (h.Ref is Deed d && d.OwedMites > 0 && Land is { } lw)
            {
                var (to, tk) = lw.Payee(d);
                if (to != EntityId.None) rent.Add(new EstateDebt { Kind = DebtKind.Rent, Creditor = to, CreditorKind = tk, OwedMites = d.OwedMites, Subject = d.Id });
            }
            else if (h.Ref is Commission c && c.State == CommissionState.Ready && c.PriceMites - c.Escrow > 0)
                comm.Add(new EstateDebt { Kind = DebtKind.Commission, Creditor = c.Maker, CreditorKind = PurseKind.Person, OwedMites = c.PriceMites - c.Escrow, Subject = c.Id });
        }
        var res = new List<EstateDebt>();
        res.AddRange(wages.OrderBy(x => x.Row).ThenBy(x => x.D.Subject).Select(x => x.D));
        res.AddRange(rent.OrderBy(x => x.Creditor).ThenBy(x => x.Subject));
        res.AddRange(comm.OrderBy(x => x.Creditor).ThenBy(x => x.Subject));
        return res;
    }

    // ------------------------------------------------------------------ the summary for the screen

    private string NameOf(Held h)
    {
        switch (h.Ref)
        {
            case Deed d:
            {
                int s = SettlementOf(d); string place = s >= 0 && s < Pop.Region.Settlements.Count ? Pop.Region.Settlements[s].Name : "";
                return (d.Kind == HoldingKind.Stall ? "stall" : d.IsLease ? d.Kind == HoldingKind.Parcel ? "field lease" : "lease" : d.Kind.ToString().ToLowerInvariant()) + (place.Length > 0 ? ", " + place : "");
            }
            case Business b: return Own?.NameOf(b) ?? "business";
            case BusinessHolding bh: return "share of " + (State.Production?.ById(bh.Business) is { } sb && Own is { } ow ? ow.NameOf(sb) : "a business");
            case ConstructionSite s: return "building site" + (s.Blueprint.Length > 0 ? " (" + s.Blueprint + ")" : "");
            case Contract c: return "contract " + c.Blueprint;
            case ContractorJob j: return "building job " + j.C.Blueprint;
            case VehicleRow v: return v.Design.Length > 0 ? v.Design : "vehicle";
            case Animal: return "animal";
            case Commission: return "commission";
            case Apprenticeship: return "apprenticeship";
            case Claim: return "claim";
            case Charter: return "founding charter";
            default: return h.Kind == EstateThing.Chest ? "home chest" : h.Kind.ToString().ToLowerInvariant();
        }
    }

    /// <summary>The estate summary of the hero's death (4.3: before it runs): what the play heir would receive, what goes to kin and to the lord, the coin after funeral and debts.</summary>
    public EstateSummary Summary(EntityId dead, int row, bool hero, Recipient? playHeir, bool playHeirKin, PurseId? purse, int day, long funeralPm = SuccessionOrder.FuneralPm, Func<int, bool>? unavailable = null)
    {
        var p = MakePlan(dead, row, hero, playHeir, playHeirKin, purse, day, unavailable);
        long coin = p.HasPurse ? Econ.Ledger.Balance(p.Purse).Mites : 0;
        long funeral = playHeir != null ? coin * funeralPm / 1000 : 0;                          // as Phase 4: no funeral is paid when the line ends
        long debts = p.Debts.Sum(d => d.OwedMites);
        long chest = p.Lines.Where(l => l.H.Kind == EstateThing.Chest).Sum(l => l.H.Value);
        long fromCoin = Math.Min(debts, coin - funeral + chest);
        long purseAfter = Math.Max(0, coin - funeral - Math.Min(debts, coin - funeral));
        long toHeir = playHeir == null ? 0 : CoinShares(p, purseAfter).Where(x => x.To.Hero).Sum(x => x.Mites);
        var lines = p.Lines.Where(l => l.H.Kind != EstateThing.Apprenticeship).Select(l => new EstateLineView(l.H.Kind, l.H.Id, NameOf(l.H), l.H.Value, l.To.Name, l.To.Lord, l.To.Hero)).ToList();
        var dl = p.Debts.Select(d => (OfHolder(d.Creditor, d.CreditorKind).Name, d.Kind, d.OwedMites)).ToList();
        return new EstateSummary(p.DeadName, playHeir?.Name ?? "", p.Law == InheritanceRule.Partible ? "partible" : "primogeniture", p.Will != null, coin, funeral, debts, fromCoin, toHeir, lines, dl);
    }

    // ------------------------------------------------------------------ settling

    /// <summary>How the purse coin left after the funeral and the debts is shared (4.1, 4.2): a companion play heir keeps what he carries; a will's coin shares; else the law (partible: equally among sons).</summary>
    private List<(Recipient To, long Mites)> CoinShares(Plan p, long coin)
    {
        var res = new List<(Recipient, long)>();
        if (coin <= 0) return res;
        if (p.PlayHeir is { } ph && (!p.PlayHeirKin || p.Hero && p.Row < 0) && !(p.Will is { } w0 && OfPerson(w0.AllTo, p) is { } a0 && !a0.Hero))
        {
            if (p.Will is { } w1 && OfPerson(w1.AllTo, p) is { Hero: true }) { res.Add((ph, coin)); return res; }
            // the companion keeps the purse (what he carries), less the will's coin lines to others
            long given = 0;
            if (p.Will is { } w2) foreach (var b in w2.Bequests.Where(b => b.What == BequestWhat.Coin)) if (OfPerson(b.Person, p) is { } r && !r.Hero) { long m = coin * b.CoinPm / 1000; res.Add((r, m)); given += m; }
            res.Add((ph, coin - given));
            return res;
        }
        if (p.Will is { } w && OfPerson(w.AllTo, p) is { } all) { res.Add((all, coin)); return res; }
        long left = coin;
        if (p.Will is { } w3)
            foreach (var b in w3.Bequests.Where(b => b.What == BequestWhat.Coin))
                if (OfPerson(b.Person, p) is { } r) { long m = coin * b.CoinPm / 1000; if (m > 0) { res.Add((r, m)); left -= m; } }
        if (left <= 0) return res;
        var sons = p.Law == InheritanceRule.Partible ? EstateSettler.PartibleSons(p.Heirs) : [];
        if (sons.Count > 1) { var parts = EstateSettler.Equally(left, sons.Count); for (int i = 0; i < sons.Count; i++) res.Add((OfHeir(sons[i], p), parts[i])); return res; }
        if (p.Heirs.Count > 0) res.Add((OfHeir(p.Heirs[0], p), left));
        else res.Add((LordAt(p.Home), left));
        return res;
    }

    private void Pay(Plan p, EstateDebt d, PurseId from, long mites)
    {
        if (mites <= 0) return;
        PurseId to;
        if (d.Kind == DebtKind.Commission) to = Econ.Ledger.Open(PurseKind.Chest, d.Subject);       // into the commission's escrow: the balance is the maker's at collection
        else to = PurseOf(OfHolder(d.Creditor, d.CreditorKind));
        if (to == from || !Econ.Ledger.Transfer(from, to, new Coin(mites), d.Kind == DebtKind.Rent ? TransferReason.Rent : d.Kind == DebtKind.Commission ? TransferReason.Trade : TransferReason.Wage)) return;
        Settled(p, d, mites);
    }

    /// <summary>A debt paid by <paramref name="mites"/> (in coin or goods): the books it came from are cleared by as much.</summary>
    private void Settled(Plan p, EstateDebt d, long mites)
    {
        d.PaidMites += mites;
        switch (d.Kind)
        {
            case DebtKind.Wage when State.Production?.ById(d.Subject) is { } b:
            {
                int hh = Pop.Households.IndexOf(d.Creditor);
                for (int i = 0; i < b.Staff.Count && mites > 0; i++)
                    if (Pt.Household[b.Staff[i]] == hh && b.Owed[i] > 0) { long x = Math.Min(mites, b.Owed[i]); b.Owed[i] -= x; mites -= x; }
                break;
            }
            case DebtKind.Steward when State.Ownership?.Of(d.Subject) is { } h: h.StewardOwed = Math.Max(0, h.StewardOwed - mites); break;
            case DebtKind.Rent when State.Land?.Deeds.TryGetValue(d.Subject, out var deed) == true:
                deed!.OwedMites = Math.Max(0, deed.OwedMites - mites);
                if (deed.OwedMites == 0) { deed.ArrearsSeasons = 0; deed.InDebt = false; }
                break;
            case DebtKind.Commission when State.Crafting?.Commissions.TryGetValue(d.Subject, out var c) == true: c!.Escrow += mites; break;
        }
    }

    /// <summary>Goods of the estate's stores sold at bid to a creditor directly (4.2 rule 2): moved to his store, valued at the market's bid, set against what is owed.</summary>
    private void PayInGoods(Plan p, EstateDebt d, IReadOnlyList<Store> stores)
    {
        if (d.Left <= 0 || State.Markets is not { } mk || Pop.Region.Settlements.Count == 0) return;
        var book = mk.BookForSettlement(Math.Clamp(p.Home, 0, Pop.Region.Settlements.Count - 1));
        var to = d.Kind == DebtKind.Commission && Pt.IndexOf(d.Creditor) is int mr && mr >= 0 ? Npc(mr) : OfHolder(d.Creditor, d.CreditorKind);
        var dest = StoreOf(to);
        foreach (var s in stores)
            foreach (var (g, q) in s.Rows().ToList())
            {
                if (d.Left <= 0) return;
                long bid = book.BidMicro(g); if (bid <= 0 || q <= 0) continue;
                long need = (long)(((Int128)d.Left * PriceCurve.Micro + bid - 1) / bid);
                long moved = Store.MoveUpTo(s, dest, g, Math.Min(q, need));
                if (moved <= 0) continue;
                long value = (long)((Int128)bid * moved / PriceCurve.Micro);
                if (d.Kind == DebtKind.Commission) d.PaidMites += Math.Min(d.Left, value);       // the maker takes the goods for the balance (the escrow is not touched)
                else Settled(p, d, Math.Min(d.Left, value));
            }
    }

    /// <summary>
    /// Settles an estate (4.2) into <paramref name="est"/>: the funeral is the caller's (the hero's is Phase 4's), then the debts, then each holding to its recipient, the purse coin shared, the lines
    /// and the debts recorded on the estate. <paramref name="goods"/> are the stores whose goods may be sold to creditors (the body's estate chest, stall stores). Returns the deed ids the play heir holds.
    /// </summary>
    internal List<string> Apply(Plan p, Estate est, int day, IReadOnlyList<Store> goods, int heroRowNow = -1, EntityId guardian = default)
    {
        // debts: the coin first, then the home chest, then goods at bid; what is left waits for the creditors' claims at the next season's start
        var chestLine = p.Lines.FirstOrDefault(l => l.H.Kind == EstateThing.Chest);
        foreach (var d in p.Debts)
        {
            if (p.HasPurse) Pay(p, d, p.Purse, Math.Min(d.Left, Econ.Ledger.Balance(p.Purse).Mites));
            if (d.Left > 0 && chestLine.H?.Ref is PurseId ch) Pay(p, d, ch, Math.Min(d.Left, Econ.Ledger.Balance(ch).Mites));
        }
        var stores = new List<Store>(goods);
        foreach (var (h, _) in p.Lines) if (h.Ref is Deed sd && sd.Kind == HoldingKind.Stall && State.Ownership?.Stalls.GetValueOrDefault(sd.Subject) is { } sh && Econ.OtherStores.TryGetValue(sh.StoreId, out var ss)) stores.Add(ss);
        if (!p.HasPurse || p.Debts.Any(d => d.Left > 0)) foreach (var d in p.Debts) PayInGoods(p, d, stores);
        // the holdings
        var deeds = new List<string>();
        foreach (var (h, to) in p.Lines)
        {
            Transfer(h, to, day, heroRowNow, guardian, p);
            est.Holdings.Add(new EstateEntry { Kind = h.Kind, Id = h.Id, ValueMites = h.Value, To = to.Holder, ToKind = to.Kind, ToRow = to.Row, Escheat = to.Lord });
            if (to.Hero && h.Kind is EstateThing.Deed or EstateThing.Lease or EstateThing.Stall) deeds.Add(h.Id.Value.ToString(CultureInfo.InvariantCulture));
        }
        // the coin left in the purse
        if (p.HasPurse)
        {
            long left = Econ.Ledger.Balance(p.Purse).Mites;
            foreach (var (to, m) in CoinShares(p, left))
            {
                if (m <= 0 || to.Hero) continue;
                var dest = PurseOf(to);
                if (dest != p.Purse && Econ.Ledger.Transfer(p.Purse, dest, new Coin(m), to.Lord ? TransferReason.Escheat : TransferReason.Inheritance))
                    est.Holdings.Add(new EstateEntry { Kind = EstateThing.Coin, Id = p.Dead, ValueMites = m, To = to.Holder, ToKind = to.Kind, ToRow = to.Row, Escheat = to.Lord });
            }
        }
        est.Debts.AddRange(p.Debts);
        return deeds;
    }

    // ------------------------------------------------------------------ one holding to its recipient (also the creditors' claims)

    private void Transfer(Held h, Recipient to, int day, int heroRowNow, EntityId guardian, Plan? p)
    {
        switch (h.Ref)
        {
            case Deed d: PassDeed(d, to, day); break;
            case Claim c:
                if (to.Hero) break;
                if (to.Lord) { c.State = ClaimState.Lapsed; c.EndDay = day; }
                else { c.Claimant = to.Holder; c.ClaimantPurse = to.Kind; }
                break;
            case Charter c:
                if (to.Hero) break;
                if (to.Lord) { c.State = CharterState.Lapsed; c.LapseDay = day; }
                else { c.Grantee = to.Holder; c.GranteePurse = to.Kind; }
                break;
            case Business b: PassBusiness(b, to, day, heroRowNow, guardian); break;
            case BusinessHolding bh: PassShare(bh, h, to); break;
            case ConstructionSite s:
            {
                // a builder under a signed contract pays his own crew and goes on building (the heir, as client, pays the stages); a site of the dead's own hands pauses until the heir says
                bool contracted = State.Construction?.Contracts.Values.Any(c => c.Site == s.Id && c.State == ContractState.Signed && c.Contractor != _shareFrom) == true;
                if (!to.Hero)
                {
                    s.Owner = to.Holder; s.OwnerKind = to.Kind;
                    if (s.Payer == _shareFrom || !contracted) { s.Payer = to.Holder; s.PayerKind = to.Kind; }
                }
                if (s.Open && !contracted) { s.Pause = PauseReason.Owner; s.State = SiteState.Paused; foreach (var c in s.Crew) c.Ghost = default; }   // the heir says when it resumes (4.2 rule 3)
                break;
            }
            case Contract c: if (!to.Hero) { c.Client = to.Holder; c.ClientKind = to.Kind; } break;   // the escrow, the materials chest and the yard stay with the contract
            case ContractorJob j:
                // his own job: given up, back on the board (he keeps what he was paid); with no construction world loaded the contract is left to its due day
                if (State.Construction != null && State.Economy != null) ConstructionWiring.For(State, Content).Drop(j.C.Contractor, j.C.Id, (long)day * 86400);
                break;
            case VehicleRow v: if (!to.Hero) v.Owner = to.Holder; break;
            case Animal a: if (!to.Hero) a.Owner = to.Holder; break;
            case Commission c: if (!to.Hero) c.Client = to.Row >= 0 ? to.Person : to.Holder; break;
            case Apprenticeship ap: ap.Ended = true; break;                                     // the dead neither learn nor teach
            case PurseId chest:
                if (!to.Hero && Econ.Ledger.Balance(chest).Mites is long m && m > 0) Econ.Ledger.Transfer(chest, PurseOf(to), new Coin(m), to.Lord ? TransferReason.Escheat : TransferReason.Inheritance);
                break;
        }
    }

    private void PassDeed(Deed d, Recipient to, int day)
    {
        var book = State.Land!;
        if (to.Hero)
        {
            d.Cause = DeedCause.Inherited; d.AcquiredDay = day;
            book.Changes.Add(new DeedChange(day, d.Id, d.Holder, d.Tenure, d.DuesMitesPerSeason, d.LeaseEndDay, DeedCause.Inherited));
            OnDeedInherited?.Invoke(d, to);
            return;
        }
        if (to.Lord && (d.IsLease || d.Kind == HoldingKind.Stall))
        {
            // a lease or a stall goes back to its lessor: the lease ends; what lay in a leased house goes to the lord with it
            if (Econ.OtherStores.TryGetValue(d.Id, out var hs)) { var dest = StoreOf(to); foreach (var (g, q) in hs.Rows().ToList()) Store.Move(hs, dest, g, q); }
            if (d.Kind == HoldingKind.Stall) DropStall(d, to);
            book.Deeds.Remove(d.Id);
            book.Changes.Add(new DeedChange(day, d.Id, d.Lessor, Tenure.Freehold, 0, -1, DeedCause.LeaseEnded));
            return;
        }
        if (d.Kind == HoldingKind.Stall) DropStall(d, to);
        d.Holder = to.Holder; d.HolderPurse = to.Kind; d.AcquiredDay = day; d.AskMites = 0;
        d.Cause = to.Lord ? DeedCause.Escheated : DeedCause.Inherited;
        book.Changes.Add(new DeedChange(day, d.Id, d.Holder, d.Tenure, d.DuesMitesPerSeason, d.LeaseEndDay, d.Cause));
        foreach (var c in book.Claims) if (c.DeedId == d.Id && c.State == ClaimState.Deeded) { c.Claimant = to.Holder; c.ClaimantPurse = to.Kind; }
        if (!to.Lord) OnDeedInherited?.Invoke(d, to);
    }

    /// <summary>A stall of the hero's book leaves it (to anyone but the play heir): its goods go to the new holder's store.</summary>
    private void DropStall(Deed d, Recipient to)
    {
        if (State.Ownership is not { } ob || !ob.Stalls.TryGetValue(d.Subject, out var sh)) return;
        if (Econ.OtherStores.TryGetValue(sh.StoreId, out var ss)) { var dest = StoreOf(to); foreach (var (g, q) in ss.Rows().ToList()) Store.Move(ss, dest, g, q); }
        ob.Stalls.Remove(d.Subject);
    }

    private void PassBusiness(Business b, Recipient to, int day, int heroRowNow, EntityId guardian)
    {
        var prod = State.Production!;
        var hold = State.Ownership?.Of(b.Id);
        if (to.Hero)
        {
            int row = heroRowNow >= 0 ? heroRowNow : to.Row;
            if (row >= 0 && row < Pt.Count) { b.Owner = row; b.OwnerHousehold = Pt.Household[row]; }
            if (hold != null)
            {
                hold.InheritedDay = day;
                // a minor heir (4.4): the guardian runs it as steward with the default directives until the heir is 16
                int g = guardian != EntityId.None ? Pt.IndexOf(guardian) : -1;
                if (row >= 0 && row < Pt.Count && Pt.AgeYears(row, day) < EstateSettler.AdultAge && g >= 0 && g < Pt.Count && hold.Steward < 0)
                {
                    hold.Steward = g; hold.HandsOn = false; hold.Orders = new Directives { SendEveryDays = hold.Orders.SendEveryDays, KeepMites = hold.Orders.KeepMites };
                    hold.StewardWage = State.Production != null && Own is { } ow ? ow.Rules.StewardWageMitesPerDay : hold.StewardWage;
                }
            }
            return;
        }
        if (to.Row >= 0 && to.Row < Pt.Count) prod.Inherit(b, to.Row);
        else { b.Owner = -1; b.OwnerHousehold = to.Kind == PurseKind.Household ? Pop.Households.IndexOf(to.Holder) : -1; }   // nobody to work it: the labour market finds an owner at its next day
        if (to.Lord) b.OwnerWorks = false;
        if (hold != null && hold.Shares.Count == 0) State.Ownership!.Holdings.Remove(b.Id);
        else if (hold != null) { hold.Steward = -1; hold.HandsOn = true; }
    }

    private void PassShare(BusinessHolding bh, Held h, Recipient to)
    {
        if (to.Hero) return;
        int i = bh.Shares.FindIndex(s => s.Holder == _shareFrom);
        if (i < 0) return;
        var row = bh.Shares[i]; bh.Shares.RemoveAt(i);
        var biz = State.Production?.ById(bh.Business);
        bool owner = biz != null && biz.OwnerHousehold >= 0 && to.Kind == PurseKind.Household && Pop.Households.IdOf(biz.OwnerHousehold) == to.Holder;
        if (!owner)
        {
            int j = bh.Shares.FindIndex(s => s.Holder == to.Holder);
            if (j >= 0) bh.Shares[j] = new ShareRow(to.Holder, bh.Shares[j].Pm + row.Pm); else bh.Shares.Add(new ShareRow(to.Holder, row.Pm));
            bh.Shares.Sort((a, b) => a.Holder.CompareTo(b.Holder));
        }
        if (biz != null && Own is { } ow && !ow.IsHeroOwner(biz) && bh.Shares.Count == 0) State.Ownership!.Holdings.Remove(bh.Business);   // nobody but the owner holds it any more
    }

    /// <summary>Whose share rows a transfer rewrites (the dead's id, or the holder a creditor claims from).</summary>
    private EntityId _shareFrom;

    // ------------------------------------------------------------------ the hero's death (called by SuccessionDesk)

    /// <summary>
    /// Settles the hero's estate after Phase 4's funeral and record (<see cref="SuccessionOrder.Settle"/>): debts, every holding to its recipient, the coin left in the purse shared by the will and
    /// the law (the play heir takes up the purse; a companion keeps it), recorded on <paramref name="est"/> and in the chronicle (<c>estate_settled</c>). <paramref name="heirRowNow"/> is the play
    /// heir's row once it is the hero's; <paramref name="playHeirKin"/> whether the play heir is kin (Child, Spouse, Sibling, YoungChild, Parent).
    /// </summary>
    public List<string> SettleHero(Estate est, EntityId hero, int deadRow, SuccessionCandidate? heir, bool playHeirKin, PurseId purse, int day, long sec, int heirRowNow, IReadOnlyList<Store> goods,
        Func<int, bool>? unavailable = null)
    {
        Recipient? ph = heir == null ? null : new Recipient(heirRowNow >= 0 ? heirRowNow : heir.PersonIndex, hero, hero, PurseKind.Person, true, false, heir.Name);
        _shareFrom = hero;
        var p = MakePlan(hero, deadRow, true, ph, playHeirKin, purse, day, unavailable);
        var deeds = Apply(p, est, day, goods, heirRowNow, heir?.Guardian ?? default);
        State.Wills?.ByTestator.Remove(hero);
        Chronicle(est, sec, p);
        return deeds;
    }

    private void Chronicle(Estate est, long sec, Plan p)
    {
        int lord = est.Holdings.Count(h => h.Escheat), kin = est.Holdings.Count(h => !h.Escheat);
        State.Chronicle.Add(new ChronicleEntry(State.Ids.Allocate(IdKind.Event), new GameTime(sec), "estate_settled", [p.Dead], [new Cause(CauseKind.Death, null, "the death of the holder")],
            new Dictionary<string, string>
            {
                ["name"] = p.DeadName, ["holdings"] = est.Holdings.Count.ToString(CultureInfo.InvariantCulture), ["to_kin"] = kin.ToString(CultureInfo.InvariantCulture),
                ["escheat"] = lord.ToString(CultureInfo.InvariantCulture), ["debts"] = est.Debts.Sum(d => d.OwedMites).ToString(CultureInfo.InvariantCulture),
                ["unpaid"] = est.Debts.Sum(d => d.Left).ToString(CultureInfo.InvariantCulture), ["law"] = p.Law.ToString(), ["will"] = p.Will != null ? "1" : "0",
            }));
    }

    // ------------------------------------------------------------------ every other person who dies (H4 hour 1, before the labour market; ascending person id)

    /// <summary>
    /// The day's NPC estates (4.6): every dead person (not the hero) who held a deed, lease, claim, site, vehicle, animal, commission or share in his own name, or left a will, is settled by the same rule
    /// (ascending id); a will's business goes to its named heir before the labour market's law. Holdings are found through one index of the books by holder, built once per day.
    /// </summary>
    public int Daily(int day, long sec)
    {
        var dead = new SortedSet<EntityId>();
        var hero = EconomyState.HeroOwner;
        void Note(EntityId id) { if (id == hero || id.Kind != IdKind.Person) return; int r = Pt.IndexOf(id); if (r >= 0 && Pt.Status[r] == PersonStatus.Dead) dead.Add(id); }
        // a light pass over the books for dead holders (no valuation); the index is built only on a day someone died holding something
        if (State.Land is { } land) { foreach (var d in land.Deeds.Values) Note(d.Holder); foreach (var c in land.Claims) if (c.State == ClaimState.Open) Note(c.Claimant); foreach (var c in land.Charters) if (c.State == CharterState.Granted) Note(c.Grantee); }
        if (State.Construction is { } cb) { foreach (var s in cb.Sites.Values) if (s.Open) Note(s.Owner); foreach (var c in cb.Contracts.Values) if (c.Live) { Note(c.Client); if (c.State == ContractState.Signed) Note(c.Contractor); } }
        if (State.Mounts is { } rb) { foreach (var v in rb.Vehicles.Rows) Note(v.Owner); foreach (var a in rb.Animals.Rows) if (a.State != AnimalState.Dead) Note(a.Owner); }
        if (State.Crafting is { } cs) { foreach (var c in cs.Commissions.Values) if (c.Open) Note(c.Client); foreach (var a in cs.Apprenticeships.Values) if (!a.Ended) { Note(a.Apprentice); Note(a.Master); } }
        if (State.Ownership is { } ob) foreach (var h in ob.Holdings.Values) foreach (var s in h.Shares) Note(s.Holder);
        if (State.Wills is { } wb) foreach (var t in wb.ByTestator.Keys) Note(t);
        if (dead.Count == 0) return 0;
        _index = Index();
        int n = 0;
        try { foreach (var id in dead) { SettleNpc(id, day, sec); n++; } }
        finally { _index = null; }
        return n;
    }

    /// <summary>Settles one dead NPC's estate (the same rule as the hero's, with no play heir): recorded in the succession book when anything passed.</summary>
    public Estate? SettleNpc(EntityId dead, int day, long sec)
    {
        int row = Pt.IndexOf(dead);
        _shareFrom = dead;
        var p = MakePlan(dead, row, false, null, false, null, day);
        var will = p.Will;
        // the household's dwelling under a will: its head is the person the will names (when a member of it)
        int hh = row >= 0 ? Pt.Household[row] : -1;
        var book = State.Succession ??= new SuccessionBook();
        var est = new Estate { Id = book.Estates.Count + 1, Dead = dead, Day = day, Heir = p.Heirs.Count > 0 ? p.Heirs[0].Person : EntityId.None };
        if (will != null && row >= 0)
        {
            foreach (var b in will.Bequests.Where(b => b.What == BequestWhat.Deed))
            {
                int to = Pt.IndexOf(b.Person);
                if (to < 0 || Pt.Status[to] != PersonStatus.Alive) continue;
                int dh = Pt.Household[to];
                if (dh >= 0 && dh < Pop.Households.Count && Land?.Registry().DwellingOf(dh) is { } dw && dw.Id == b.Thing)
                {
                    Pop.Households.Head[dh] = to;
                    est.Holdings.Add(new EstateEntry { Kind = EstateThing.Deed, Id = dw.Id, ValueMites = Land.ValueOf(dw), To = Pop.Households.IdOf(dh), ToKind = PurseKind.Household, ToRow = to });
                }
            }
        }
        if (p.Lines.Count == 0 && est.Holdings.Count == 0 && !p.HasPurse) { State.Wills?.ByTestator.Remove(dead); return null; }
        Apply(p, est, day, row >= 0 && Econ.OtherStores.TryGetValue(dead, out var own) ? [own] : []);
        book.Estates.Add(est);
        State.Wills?.ByTestator.Remove(dead);
        Chronicle(est, sec, p);
        return est;
    }

    /// <summary>The will's heir of a dead NPC's business (for the labour market's succession): the person the will names for it or for everything, -1 none.</summary>
    public int WillHeirOf(int deadRow, EntityId business)
    {
        if (State.Wills is not { } wb || deadRow < 0 || deadRow >= Pt.Count || wb.Of(Pt.IdOf(deadRow)) is not { } w) return -1;
        var who = w.ThingTo(business); if (who == EntityId.None) who = w.AllTo;
        int r = who != EntityId.None ? Pt.IndexOf(who) : -1;
        return r >= 0 && Pt.Status[r] == PersonStatus.Alive ? r : -1;
    }

    // ------------------------------------------------------------------ creditors' claims at the season's start (S6)

    private static int ClaimRank(EstateThing k) => k switch
    {
        EstateThing.Stall => 0, EstateThing.Share => 1, EstateThing.Vehicle or EstateThing.Animal => 2, EstateThing.Business => 3, EstateThing.Deed or EstateThing.Lease => 4, _ => 9,
    };

    /// <summary>
    /// Every estate with debts left (4.2 rule 2, S6): each creditor in the debt order claims what the estate passed, stalls first, then shares, vehicles and animals, businesses, deeds, the lowest
    /// value first, until paid; each claim a transfer at the one valuation (the excess paid back by the creditor when his purse can). A holding sold on since is out of reach.
    /// </summary>
    public int Seasonal(int day, long sec)
    {
        if (State.Succession is not { } book) return 0;
        int n = 0;
        foreach (var est in book.Estates)
        {
            if (!est.Debts.Any(d => d.Left > 0)) continue;
            foreach (var d in est.Debts)
            {
                if (d.Left <= 0) continue;
                var creditor = d.Kind == DebtKind.Commission && Pt.IndexOf(d.Creditor) is int mr && mr >= 0 ? Npc(mr) : OfHolder(d.Creditor, d.CreditorKind);
                foreach (var e in est.Holdings.Where(e => e.ClaimedBy == EntityId.None && ClaimRank(e.Kind) < 9).OrderBy(e => ClaimRank(e.Kind)).ThenBy(e => e.ValueMites).ThenBy(e => e.Id).ToList())
                {
                    if (d.Left <= 0) break;
                    var h = Rebuild(e); if (h == null) continue;
                    long value = h.Value;
                    _shareFrom = e.To;
                    var holderNow = OfHolder(e.To, e.ToKind);
                    Transfer(h, creditor, day, -1, default, null);
                    e.ClaimedBy = creditor.Holder;
                    long take = Math.Min(value, d.Left);
                    d.PaidMites += take;
                    if (d.Kind is DebtKind.Rent && State.Land?.Deeds.TryGetValue(d.Subject, out var rd) == true) { rd!.OwedMites = Math.Max(0, rd.OwedMites - take); if (rd.OwedMites == 0) { rd.ArrearsSeasons = 0; rd.InDebt = false; } }
                    if (value > take)
                    {
                        var from = PurseOf(creditor); var back = PurseOf(holderNow); long pay = Math.Min(value - take, Econ.Ledger.Balance(from).Mites);
                        if (pay > 0 && from != back) Econ.Ledger.Transfer(from, back, new Coin(pay), TransferReason.Trade);
                    }
                    n++;
                    State.Chronicle.Add(new ChronicleEntry(State.Ids.Allocate(IdKind.Event), new GameTime(sec), "estate_claimed", [est.Dead, creditor.Holder, e.Id], [new Cause(CauseKind.Law, null, "a debt of the estate")],
                        new Dictionary<string, string> { ["kind"] = e.Kind.ToString(), ["value"] = value.ToString(CultureInfo.InvariantCulture), ["left"] = d.Left.ToString(CultureInfo.InvariantCulture) }));
                }
            }
        }
        return n;
    }

    /// <summary>The live holding an estate line passed, if it is still where the estate put it (its value now), else null.</summary>
    private Held? Rebuild(EstateEntry e)
    {
        switch (e.Kind)
        {
            case EstateThing.Deed or EstateThing.Lease or EstateThing.Stall:
                if (State.Land?.Deeds.TryGetValue(e.Id, out var d) == true && d!.Holder == e.To) return new Held { Kind = e.Kind, Id = e.Id, Value = DeedValue(d), Settlement = SettlementOf(d), Ref = d };
                return null;
            case EstateThing.Business:
                if (State.Production?.ById(e.Id) is { State: BusinessState.Open } b && (e.ToRow >= 0 ? b.Owner == e.ToRow : true)) return new Held { Kind = e.Kind, Id = e.Id, Value = BusinessValue(b), Settlement = b.Settlement, Ref = b };
                return null;
            case EstateThing.Share:
                if (State.Ownership?.Of(e.Id) is { } bh && bh.Shares.FirstOrDefault(s => s.Holder == e.To) is { Pm: > 0 } sr && State.Production?.ById(e.Id) is { } sb)
                    return new Held { Kind = e.Kind, Id = e.Id, Value = Valuation.SharePrice(BusinessValue(sb), sr.Pm), Settlement = sb.Settlement, Ref = bh };
                return null;
            case EstateThing.Vehicle:
                if (State.Mounts?.Vehicles.Find(e.Id) is { } v && v.Owner == e.To) return new Held { Kind = e.Kind, Id = e.Id, Ref = v };
                return null;
            case EstateThing.Animal:
                if (State.Mounts?.Animals.Find(e.Id) is { } a && a.Owner == e.To) return new Held { Kind = e.Kind, Id = e.Id, Ref = a };
                return null;
        }
        return null;
    }
}

/// <summary>The estates' place in the abstract tick (inheritance 4.6): NPC estates at hour 1 right after the labour market's succession (H4, the day's deaths); creditors' claims on a season's first day (H6, S6).</summary>
[Ravensguard.Sim.Abstract.AbstractSystem]
public sealed class EstateSystem : Ravensguard.Sim.Abstract.AbstractSystem
{
    public const int ClaimHour = 8;
    public EstateSystem() : base("estates", Ravensguard.Sim.Abstract.TickPhase.H4DailySlice | Ravensguard.Sim.Abstract.TickPhase.H6Seasonal, Ravensguard.Sim.Abstract.SystemOrder.DailyGlobalsLabour + 1) { }   // after the labour market (it reads the wills first)

    public override void Step(Ravensguard.Sim.Abstract.TickContext ctx)
    {
        var ew = ctx.World.Get<EstateWorld>(); if (ew == null) return;
        long sec = ctx.Hour * 3600L;
        if (ctx.Phase == Ravensguard.Sim.Abstract.TickPhase.H4DailySlice && ctx.HourOfDay == 1) ew.Daily((int)ctx.Day, sec);
        else if (ctx.Phase == Ravensguard.Sim.Abstract.TickPhase.H6Seasonal && ctx.HourOfDay == ClaimHour && ctx.IsSeasonStartDay) ew.Seasonal((int)ctx.Day, sec);
    }
}

/// <summary>Installs the estates on the home region (P5-H hook in <see cref="Ravensguard.Sim.Worlds.WorldRuntime"/>): the world in the sim's services, the labour market's will hook.</summary>
public static class EstateWiring
{
    public static EstateWorld Attach(Ravensguard.Sim.Worlds.RegionWorld home, WorldState state, ContentDatabase content)
    {
        var ew = new EstateWorld(state, content);
        home.Sim.Set(ew);
        home.Economy.Labour.WillHeir = ew.WillHeirOf;
        return ew;
    }

    /// <summary>The estate world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (tests, the first-hour bot).</summary>
    public static EstateWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<EstateWorld>() is { } ew) return ew;
        return Loose.GetValue(state, s => new EstateWorld(s, content));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, EstateWorld> Loose = new();
}
