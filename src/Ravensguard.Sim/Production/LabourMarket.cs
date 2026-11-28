using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production;

/// <summary>
/// The labour market of a region (households spec 3.5, production spec 3.3): businesses hire the idle working-age people of their settlement by the wage on offer,
/// and a person changes job only by being hired or by quitting (three unpaid days, or the employer closing). Runs at the daily globals (H5), settlements and
/// businesses in ascending order, candidates by skill then lower index (G-B13). Also the owner's succession: a dead owner's business passes to an adult child,
/// else the most skilled member of the staff, else the most skilled idle adult of the settlement, else it closes (production spec 3.5).
/// <para>Rules this class adds to the spec: apprentices start at 12 (a craft slot may take a 12 to 15 year old when no adult is idle, at half wage, learning the
/// trade); nobody under 16 owns a business or holds a management post (<see cref="ManagementMinAge"/>); staff learn their trade by doing (+<see cref="SkillPerSeason"/>
/// a season up to <see cref="SkillCap"/>); a business hires only when its cash box covers a fortnight of the new wage.</para>
/// </summary>
public sealed class LabourMarket
{
    public const int ManagementMinAge = 16, ApprenticeMinAge = 12, RetireAge = 65, SkillPerSeason = 2, SkillCap = 80, StartSkill = 12;
    public const int ApprenticeWagePct = 50, ReserveDays = 14;

    private readonly ProductionSystem _sys;
    private readonly Population _pop;
    private readonly EconomyState _econ;
    private readonly ulong _seed;
    private readonly Func<int, IEnumerable<int>> _children;
    private bool[] _staff = [], _farmAway = [];
    private int[] _manorLord = [];
    private readonly List<int>[] _idle;

    public long Hired, Apprenticed, Succeeded, ClosedNoOwner, StaffGone, Quit;

    public LabourMarket(ProductionSystem sys, Population pop, EconomyState econ, ulong seed, Func<int, IEnumerable<int>> childrenOf)
    {
        _sys = sys; _pop = pop; _econ = econ; _seed = seed; _children = childrenOf;
        _idle = new List<int>[pop.Region.Settlements.Count];
        for (int i = 0; i < _idle.Length; i++) _idle[i] = [];
    }

    /// <summary>True when the person is on a business's staff (not a candidate for another post).</summary>
    public bool IsStaff(int person) => person < _staff.Length && _staff[person];

    /// <summary>True when the person's days belong to a workshop, crew or stall, so the farm labour planner must not count him; a manor's staff also work the demesne and still count.</summary>
    public bool IsAwayFromFarm(int person) => person < _farmAway.Length && _farmAway[person];

    /// <summary>The household of the lord whose demesne a manor retainer works (the manor's owner), -1 for anyone else (integration decision I31).</summary>
    public int ManorLordOf(int person) => person < _manorLord.Length ? _manorLord[person] : -1;

    /// <summary>Rebuilds the staff snapshot from the businesses.</summary>
    public void Rebuild()
    {
        int n = _pop.People.Count;
        if (_staff.Length < n) { _staff = new bool[Math.Max(n, _staff.Length * 2)]; _farmAway = new bool[_staff.Length]; }
        else { Array.Clear(_staff); Array.Clear(_farmAway); }
        if (_manorLord.Length < _staff.Length) _manorLord = new int[_staff.Length];
        Array.Fill(_manorLord, -1);
        _owners.Clear();
        foreach (var b in _sys.Businesses)
        {
            bool manor = _sys.KindOf(b).Id == "manor";
            foreach (int s in b.Staff) { _staff[s] = true; if (!manor) _farmAway[s] = true; else if (b.State == BusinessState.Open && b.OwnerHousehold >= 0) _manorLord[s] = b.OwnerHousehold; }
            if (b.State == BusinessState.Open && b.Owner >= 0) _owners.Add(b.Owner);
        }
    }
    private readonly HashSet<int> _owners = [];

    // ------------------------------------------------------------------ daily

    /// <summary>The hiring and succession pass of one day (H5, hour 0).</summary>
    public void Daily(GameTime now)
    {
        var people = _pop.People; int day = (int)now.TotalDays;
        Rebuild();
        foreach (var b in _sys.Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            // staff who died or left the region
            for (int i = b.Staff.Count - 1; i >= 0; i--)
                if (people.Status[b.Staff[i]] != PersonStatus.Alive) { _sys.DropStaff(b, i, alive: false); StaffGone++; }
            if (_sys.KindOf(b).Stage != null) continue;                                              // P7-IN1: a works' stages are never inherited; the industry world closes a works whose owner is gone
            if (!OwnerFit(b, day)) Succeed(b, day);
            else if (b.Owner >= 0 && people.Household[b.Owner] >= 0) b.OwnerHousehold = people.Household[b.Owner];
        }
        // idle adults by settlement (only built when someone is hiring)
        bool built = false;
        foreach (var b in _sys.Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            var kind = _sys.KindOf(b);
            if (kind.Slots.Length == 0 && !_sys.NeedsMaster(b)) continue;
            int vac = Vacancies(b, kind);
            if (vac <= 0) continue;
            if (!built) { BuildIdle(day); built = true; }
            Hire(b, kind, day);
        }
        if (built) Rebuild();
    }

    private bool OwnerFit(Business b, int day)
    {
        if (b.Owner < 0) return false;
        var p = _pop.People;
        if (p.Status[b.Owner] != PersonStatus.Alive && p.Status[b.Owner] != PersonStatus.Hero) return false;     // P5-F: the hero owns businesses (his row's status is Hero)
        return true;
    }

    // ------------------------------------------------------------------ succession

    private void Succeed(Business b, int day)
    {
        var people = _pop.People; var kind = _sys.KindOf(b);
        int old = b.Owner;
        int heir = -1;
        // (0) a crew belongs to the lord of its fief: the new lord, when there is one
        if (kind.Extractions.Length > 0 || kind.Id == "mine" || kind.Id == "manor")
        {
            int fief = b.Settlement >= 0 ? _pop.Region.Settlements[b.Settlement].FiefIndex : -1;
            if (fief >= 0 && fief < _pop.FiefLord.Length && _pop.FiefLord[fief] >= 0 && people.Status[_pop.FiefLord[fief]] == PersonStatus.Alive) heir = _pop.FiefLord[fief];
        }
        // (1) P5-H (inheritance 4.1): the will's heir, else the law's order of the dead owner's kin (sons, daughters, the spouse, siblings, parents: EstateSettler.Heirs), alive, of management age
        // and owning no other business
        bool kinHeir = false;
        if (heir < 0 && old >= 0)
        {
            bool Fit(int c) => c >= 0 && people.Status[c] == PersonStatus.Alive && people.AgeYears(c, day) >= ManagementMinAge && people.AgeYears(c, day) < RetireAge && !OwnsBusiness(c);
            int w = WillHeir?.Invoke(old, b.Id) ?? -1;
            if (Fit(w)) heir = w;
            else foreach (var h in Ravensguard.Sim.Succession.EstateSettler.Heirs(people, old, day, ChildrenOf)) if (Fit(h.Row)) { heir = h.Row; break; }
            kinHeir = heir >= 0;
        }
        var skill = OccupationInfo.Skills(kind.OwnerOccupation).A;
        // (2) the most skilled adult of the staff
        if (heir < 0)
        {
            int bi = -1, bs = -1;
            for (int i = 0; i < b.Staff.Count; i++)
            {
                int p = b.Staff[i];
                if (people.AgeYears(p, day) < ManagementMinAge || people.AgeYears(p, day) >= RetireAge) continue;
                int sk = people.SkillLevelOf(p, skill);
                if (sk > bs) { bs = sk; bi = i; }
            }
            if (bi >= 0) { heir = b.Staff[bi]; _sys.DropStaff(b, bi, alive: true, keepJob: true); }
        }
        // (3) the most skilled idle adult of the settlement
        if (heir < 0)
        {
            BuildIdle(day, rebuild: true);
            int bs = -1;
            foreach (int p in _idle[b.Settlement])
            {
                int age = people.AgeYears(p, day);
                if (age < ManagementMinAge || age >= RetireAge || OwnsBusiness(p)) continue;
                int sk = people.SkillLevelOf(p, skill);
                if (sk > bs || (sk == bs && p < heir)) { bs = sk; heir = p; }
            }
        }
        bool crewLike = kind.Extractions.Length > 0 || kind.Id is "mine" or "manor";
        if (heir < 0 || (OwnsBusiness(heir) && !crewLike))
        {
            ClosedNoOwner++;
            _sys.Close(b);
            return;
        }
        if (kinHeir) _sys.Inherit(b, heir); else _sys.SetOwner(b, heir);       // P5-H: an heir of another trade keeps his own and the business hires a master (IN-10)
        _owners.Add(heir);
        if (!crewLike && (b.OwnerWorks || !kinHeir)) EnsureSkill(heir, skill, StartSkill);
        Succeeded++;
    }

    private bool OwnsBusiness(int person) => _owners.Contains(person);

    /// <summary>P5-H: the heir a dead owner's will names for a business (-1 none), set by the estates (<c>EstateWiring</c>).</summary>
    public Func<int, EntityId, int>? WillHeir { get; set; }

    /// <summary>A kind's staff slots, plus one master of its trade when the owner inherited it from another trade and does not work it (P5-H, <see cref="ProductionSystem.NeedsMaster"/>).</summary>
    private (Occupation Occupation, int Count, WageClass Wage)[] SlotsOf(Business b, BusinessKind kind)
    {
        if (kind.Stage != null && _sys.Stages is { } sh && sh.SlotsFor(b, out var stageSlots)) return stageSlots;                  // P7-IN1: the works' crew target
        if (b.Mine >= 0 && !_sys.NeedsMaster(b)) return [.. kind.Slots.Select(x => x.Occupation == Occupation.Miner ? (x.Occupation, _sys.MinersWanted(b, x.Count), x.Wage) : x)];   // P8-EA-1: a mine's crew follows its book
        if (!_sys.NeedsMaster(b)) return kind.Slots;
        var l = kind.Slots.ToList();
        int i = l.FindIndex(x => x.Occupation == kind.OwnerOccupation);
        if (i >= 0) l[i] = (l[i].Occupation, l[i].Count + 1, l[i].Wage); else l.Add((kind.OwnerOccupation, 1, WageClass.Skilled));
        return [.. l];
    }

    private IEnumerable<int> ChildrenOf(int parent) => _children(parent).OrderBy(c => c);


    // ------------------------------------------------------------------ hiring

    /// <summary>Open posts of a business: slot count less the staff already in that slot's occupation (a post goes unfilled when the cash box cannot carry the wage).</summary>
    private int Vacancies(Business b, BusinessKind kind)
    {
        int vac = 0;
        foreach (var (occ, count, wc) in SlotsOf(b, kind))
        {
            int have = 0; foreach (int s in b.Staff) if (_pop.People.Occupation[s] == occ) have++;
            vac += Math.Max(0, count - have);
        }
        if (_sys.Steering is { } st && st.Steered(b)) vac = Math.Min(vac, Math.Max(0, st.HireCap(b) - b.Staff.Count));   // P5-F: the owner's "hire up to n" (a hands-on owner hires himself)
        return vac;
    }

    /// <summary>P5-F (businesses spec 4.3): a day labourer of ten days a season is a candidate for a staff post before the others (null: none).</summary>
    public Func<int, int, bool>? Priority { get; set; }

    /// <summary>P7-IN5: the idle adults of a settlement a hiring business could take on a day (the founders' look at the labour); the snapshot of the staff is rebuilt first, so the count does not depend on when the last hiring pass ran.</summary>
    public int IdleCount(int settlement, int day)
    {
        if (settlement < 0 || settlement >= _idle.Length) return 0;
        Rebuild(); BuildIdle(day);
        return _idle[settlement].Count;
    }

    private void BuildIdle(int day, bool rebuild = false)
    {
        var p = _pop.People; var hht = _pop.Households;
        foreach (var l in _idle) l.Clear();
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive) continue;
            int age = p.AgeYears(i, day);
            if (age < ApprenticeMinAge || age >= RetireAge) continue;
            var occ = p.Occupation[i];
            if (IsStaff(i)) continue;
            int hh = p.Household[i]; if (hh < 0) continue;
            if (hht.Class[hh] == SocialClass.Gentry) continue;
            bool destitute = _econ.FoodCoverDaysX10[hh] < 30 && _econ.Ledger.Balance(_econ.HouseholdPurse[hh]).Mites < 300;
            // anyone may be hired as an unskilled hand from a destitute household (except an owner, a lord, a priest or a magistrate); otherwise only the idle
            if (destitute ? (occ is Occupation.Lord or Occupation.Priest or Occupation.Mayor or Occupation.GuildMaster or Occupation.Merchant || OwnsBusiness(i))
                          : occ is not (Occupation.None or Occupation.Labourer or Occupation.Servant or Occupation.Spinner)) continue;
            if (hht.ParcelCount[hh] > 0 && hht.Head[hh] != i && occ == Occupation.None && age < 16) continue;   // a farm child works the family's land
            if (p.Employer[i] >= 0 && occ == Occupation.Servant) continue;                                 // a lord's servant keeps the post
            int s = p.HomeSettlement[i]; if (s < 0 || s >= _idle.Length) continue;
            _idle[s].Add(i);
        }
    }

    private readonly List<int> _fiefPool = [];

    /// <summary>A manor calls the poorest first (integration decision I9): 10,000 less the crowns the candidate's household holds, so the least well off have the highest score.</summary>
    private int PoorFirst(int person)
    {
        int hh = _pop.People.Household[person]; if (hh < 0) return 0;
        long crowns = _econ.Ledger.Balance(_econ.HouseholdPurse[hh]).Mites / 100;
        return (int)Math.Max(0, 100_000 - crowns);
    }

    /// <summary>A business hires only when it can carry the wage: a workshop or stall when its cash box covers a week of it; a manor when its box, the fief treasury and the lord's purse together cover a month of the whole bill.</summary>
    private bool CanCarry(Business b, BusinessKind kind, long wage)
    {
        var led = _econ.Ledger; long cash = led.Balance(b.Purse).Mites;
        if (kind.Id != "manor") return cash >= wage * 7;
        if (b.OwnerHousehold >= 0) cash += led.Balance(_econ.HouseholdPurse[b.OwnerHousehold]).Mites;
        int fief = _pop.Region.Settlements[b.Settlement].FiefIndex;
        if (fief >= 0 && fief < _pop.Region.Fiefs.Count && led.TryFind(PurseKind.Treasury, _pop.Region.Fiefs[fief].Id, out var t)) cash += led.Balance(t).Mites;
        return cash >= (wage + 150L * b.Staff.Count) * 30;
    }

    /// <summary>The idle a business may hire: its settlement's, and for a manor those of every settlement of the fief (the lord's labour is called from his villages).</summary>
    private List<int> PoolFor(Business b, BusinessKind kind)
    {
        if (kind.Id != "manor") return _idle[b.Settlement];
        _fiefPool.Clear();
        int fief = _pop.Region.Settlements[b.Settlement].FiefIndex;
        if (fief < 0) return _idle[b.Settlement];
        for (int s = 0; s < _idle.Length; s++) if (_pop.Region.Settlements[s].FiefIndex == fief) _fiefPool.AddRange(_idle[s]);
        return _fiefPool;
    }

    private void Hire(Business b, BusinessKind kind, int day)
    {
        var people = _pop.People; var ledger = _econ.Ledger;
        var pool = PoolFor(b, kind);
        if (pool.Count == 0) return;
        foreach (var (occ, count, wc) in SlotsOf(b, kind))
        {
            int have = 0; foreach (int s in b.Staff) if (people.Occupation[s] == occ) have++;
            int need = count - have;
            if (need <= 0) continue;
            var skill = OccupationInfo.Skills(occ).A;
            int minAge = occ is Occupation.Merchant ? ManagementMinAge : OccupationInfo.MinWorkAge(occ);
            long wage = Math.Max((int)b.WageOfferMites, 1); bool manor = kind.Id == "manor";
            for (int k = 0; k < need; k++)
            {
                if (!CanCarry(b, kind, wage)) break;
                if (_sys.Steering is { } steer && steer.Steered(b) && b.Staff.Count >= steer.HireCap(b)) break;      // P5-F: the owner's cap
                // adults first (skill, then lower index); apprentices (12-15) only for a craft post when no adult is idle
                int pick = -1, ps = -1;
                bool apprentice = false;
                bool pri = false; var prio = Priority;
                foreach (int c in pool)
                {
                    if (IsStaff(c) || people.Status[c] != PersonStatus.Alive || people.AgeYears(c, day) < minAge) continue;
                    int sk = manor ? PoorFirst(c) : people.SkillLevelOf(c, skill);
                    bool cp = prio != null && prio(c, day);
                    if ((cp && !pri) || (cp == pri && (sk > ps || (sk == ps && c < pick)))) { ps = sk; pick = c; pri = cp; }
                }
                if (pick < 0 && minAge > ApprenticeMinAge && occ != Occupation.Merchant)
                    foreach (int c in pool)
                    {
                        if (IsStaff(c) || people.Status[c] != PersonStatus.Alive) continue;
                        int age = people.AgeYears(c, day); if (age < ApprenticeMinAge || age >= minAge) continue;
                        int sk = people.SkillLevelOf(c, skill);
                        if (sk > ps || (sk == ps && c < pick)) { ps = sk; pick = c; apprentice = true; }
                    }
                if (pick < 0) break;
                pool.Remove(pick);
                if (!ReferenceEquals(pool, _idle[b.Settlement])) { int hs = people.HomeSettlement[pick]; if (hs >= 0 && hs < _idle.Length) _idle[hs].Remove(pick); }
                _sys.Employ(b, pick, occ);
                if (pick < _staff.Length) _staff[pick] = true;
                EnsureSkill(pick, skill, StartSkill);
                Hired++; if (apprentice) Apprenticed++;
                if (b.UnfilledDays > 0) b.UnfilledDays = 0;
            }
        }
    }

    /// <summary>Makes sure the person has the skill at least at <paramref name="level"/> (a free slot, else the lowest one is overwritten).</summary>
    public void EnsureSkill(int person, Skill skill, int level)
    {
        var p = _pop.People; int slot = -1, lowest = 0, low = int.MaxValue;
        for (int k = 0; k < PersonTable.SkillSlots; k++)
        {
            var sl = p.SkillAt(person, k);
            if (sl.Skill == skill) { if (sl.Level < level) p.SetSkill(person, k, skill, level); return; }
            if (sl.Skill == Skill.None && slot < 0) slot = k;
            if (sl.Level < low) { low = sl.Level; lowest = k; }
        }
        p.SetSkill(person, slot >= 0 ? slot : lowest, skill, level);
    }

    // ------------------------------------------------------------------ reopening (integration decision I19)

    /// <summary>Coin a household must hold to set up a closed workshop again: 30 crowns.</summary>
    public const long ReopenMinMites = 3000;
    public long Reopened;

    /// <summary>
    /// S5: a closed workshop opens again under a new owner when its main product is wanted (the book's price is over 1.1 times its reference, or it holds under a day of
    /// the demand): the most skilled idle adult of the settlement whose household holds at least <see cref="ReopenMinMites"/> takes it over and puts half his coin in as
    /// working capital. At most one workshop a settlement a season.
    /// </summary>
    public int Reopen(GameTime now)
    {
        if (_sys.Markets == null) return 0;
        int day = (int)now.TotalDays, n = 0; bool built = false; var done = new HashSet<int>();
        var people = _pop.People; var hht = _pop.Households; var led = _econ.Ledger;
        Rebuild();
        foreach (var b in _sys.Businesses)
        {
            if (b.State != BusinessState.Closed || done.Contains(b.Settlement)) continue;
            var kind = _sys.KindOf(b);
            if (kind.FoundedOnly || (_sys.Steering is { } steer && steer.Steered(b))) continue;            // P5-F: the hero's closed business is his (he sells it or opens it again)
            var book = _sys.Markets.BookForSettlement(b.Settlement); bool wanted = false;
            if (kind.Trader)
            {
                // a failed stall opens again when its book has no solvent merchant left (decision I38)
                wanted = true;
                foreach (var m in book.Merchants) if (led.Balance(m.Purse).Mites >= ProductionSystem.MerchantSolventMites) { wanted = false; break; }
            }
            else
            {
                if (kind.Stations.Length == 0 || kind.Extractions.Length > 0 || kind.Id is "mine" or "manor" or "charcoal_crew") continue;
                foreach (var r in kind.Recipes) { var gb = book.Goods[r.Outputs[0].Good]; if (gb.Demand > 0 && (book.MidRatioPermille(r.Outputs[0].Good) >= 1100 || gb.StockUnits < gb.Demand)) { wanted = true; break; } }
            }
            if (!wanted) continue;
            if (!built) { BuildIdle(day); built = true; }
            var skill = OccupationInfo.Skills(kind.OwnerOccupation).A;
            int best = -1, bs = -1;
            foreach (int p in _idle[b.Settlement])
            {
                int age = people.AgeYears(p, day);
                if (age < ManagementMinAge || age >= RetireAge || OwnsBusiness(p) || IsStaff(p)) continue;
                int hh = people.Household[p]; if (hh < 0 || led.Balance(_econ.HouseholdPurse[hh]).Mites < ReopenMinMites) continue;
                int sk = people.SkillLevelOf(p, skill);
                if (sk > bs || (sk == bs && p < best)) { bs = sk; best = p; }
            }
            if (best < 0) continue;
            _sys.Reopen(b, best); _owners.Add(best); EnsureSkill(best, skill, StartSkill); _idle[b.Settlement].Remove(best);
            done.Add(b.Settlement); n++; Reopened++;
        }
        return n;
    }

    // ------------------------------------------------------------------ seasonal

    /// <summary>Learning by doing: each staff member and owner at work gains skill in the trade each season (apprentices at the same pace), up to the cap.</summary>
    public void SeasonalTraining()
    {
        var p = _pop.People;
        foreach (var b in _sys.Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            var kind = _sys.KindOf(b);
            if (kind.Trader && b.Staff.Count == 0) continue;
            foreach (int s in b.Staff)
            {
                var skill = OccupationInfo.Skills(p.Occupation[s]).A; if (skill == Skill.None) continue;
                int cur = p.SkillLevelOf(s, skill);
                if (cur < SkillCap) EnsureSkill(s, skill, Math.Min(SkillCap, cur + SkillPerSeason));
            }
        }
    }
}
