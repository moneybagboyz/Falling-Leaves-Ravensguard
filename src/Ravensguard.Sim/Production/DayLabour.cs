using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production;

/// <summary>
/// Who has worked by the day lately (businesses spec 4.3): a person's last day of day labour and his days this season, sparse by person index (ascending). Saved with the region's production
/// section (additive keys, no format bump); the day's posts themselves are transient (spec 9: "not saved past the day"): they are filled and paid at the hiring hour.
/// </summary>
public sealed class DayLabourBook
{
    public SortedDictionary<int, (int LastDay, int Season, int Days)> Workers { get; } = [];
    public long Hired, PaidMites, Lapsed, PostsOffered;
    public bool IsEmpty => Workers.Count == 0 && Hired == 0 && PaidMites == 0 && Lapsed == 0 && PostsOffered == 0;
}

/// <summary>One post of a hiring hour: who pays, how many hands, the purse the wage comes from.</summary>
public readonly record struct DayPost(int Settlement, int Hands, PurseId Payer, string Source);

/// <summary>
/// The hiring hour (businesses spec 4.3): every settlement at 06:00 in the square. Posts come from the work there is that day: construction sites, farms short of hands, businesses with a one-day
/// need, haulage (<see cref="Posts"/>, set by the region's systems), and the lord's works (decided here, P5-F: ditching, hedging, carting and roads on the demesne, paid from the fief's treasury and the
/// lord's purse above their reserve, <c>lord_works_pm_per_day</c> of it a day: the lords' idle coin is the plan's "haulage and farms short of hands" in the abstract). Idle adults of 16 to 59
/// without a staff post take them first come by (distance from the square, person id) at the unskilled wage (150 mites, 10 hours), paid from the post's purse at the hour (the day's work
/// is the post; the save never holds half a day). A day labourer is a <see cref="Occupation.Labourer"/> while he has worked within <c>lapse</c> days; ten days in a season and he is a candidate
/// for a staff post first. No draws: the order is the people's distance and id.
/// </summary>
public sealed class DayLabour
{
    private readonly ProductionSystem _sys;
    private readonly Population _pop;
    private readonly EconomyState _econ;
    private readonly OwnershipRulesDef _r;
    private readonly LabourMarket _labour;
    public DayLabourBook Book => _sys.DayLabour;
    /// <summary>Other posts of a settlement for the day (construction, farms, haulage), each with its payer; null: none.</summary>
    public Func<int, int, IEnumerable<DayPost>>? Posts { get; set; }
    public OwnershipRulesDef Rules => _r;

    public DayLabour(ProductionSystem sys, Population pop, EconomyState econ, OwnershipRulesDef rules, LabourMarket labour)
    {
        _sys = sys; _pop = pop; _econ = econ; _r = rules; _labour = labour;
        labour.Priority = Priority;
    }

    /// <summary>Ten days of day labour this season: a candidate for a staff post first.</summary>
    public bool Priority(int person)
        => Book.Workers.TryGetValue(person, out var w) && w.Season == _pop.NowDay / TimeConst.DaysPerSeason && w.Days >= _r.DayLabourPriorityDays;

    /// <summary>The idle adults of each settlement who may take a day's work, by (distance from the square, person id).</summary>
    public List<int>[] Idle(int day)
    {
        var p = _pop.People; var hht = _pop.Households; var reg = _pop.Region;
        var lists = new List<(long D, int I)>[reg.Settlements.Count];
        for (int s = 0; s < lists.Length; s++) lists[s] = [];
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive || p.Employer[i] >= 0 || _labour.IsStaff(i)) continue;
            if (p.Occupation[i] is not (Occupation.None or Occupation.Labourer)) continue;
            int age = p.AgeYears(i, day); if (age < _r.DayLabourMinAge || age > _r.DayLabourMaxAge) continue;
            int hh = p.Household[i]; if (hh < 0 || hht.Class[hh] == SocialClass.Gentry) continue;
            int s = p.HomeSettlement[i]; if ((uint)s >= (uint)lists.Length) continue;
            var st = reg.Settlements[s]; long d = 0;
            int lot = hht.Lot[hh];
            if (lot >= 0 && lot < st.Plan.Lots.Count) { var r = st.Plan.Lots[lot].Rect; long dx = r.X + r.W / 2 - st.CenterX, dy = r.Y + r.H / 2 - st.CenterY; d = ProductionSystem.ISqrt(dx * dx + dy * dy); }
            lists[s].Add((d, i));
        }
        var outp = new List<int>[lists.Length];
        for (int s = 0; s < lists.Length; s++) { lists[s].Sort(); outp[s] = [.. lists[s].Select(x => x.I)]; }
        return outp;
    }

    /// <summary>The lord's works of each fief for the day: the hands the treasury and the lord's purse pay for above their reserve, shared among its settlements by their idle.</summary>
    private List<DayPost> LordWorks(List<int>[] idle)
    {
        var posts = new List<DayPost>(); var reg = _pop.Region; var led = _econ.Ledger; long wage = _r.DayLabourWageMites;
        if (_r.LordWorksPmPerDay <= 0) return posts;
        for (int f = 0; f < reg.Fiefs.Count; f++)
        {
            int want = 0; var mine = new List<int>();
            for (int s = 0; s < reg.Settlements.Count; s++) if (reg.Settlements[s].FiefIndex == f && idle[s].Count > 0) { mine.Add(s); want += idle[s].Count; }
            if (want == 0) continue;
            var payers = new List<(PurseId P, long Spare)>();
            if (led.TryFind(PurseKind.Treasury, reg.Fiefs[f].Id, out var t)) payers.Add((t, led.Balance(t).Mites));
            int lh = f < _pop.FiefLordHousehold.Length ? _pop.FiefLordHousehold[f] : -1;
            if (lh >= 0 && lh < _econ.HouseholdCount && _pop.Households.MemberCount[lh] > 0) payers.Add((_econ.HouseholdPurse[lh], Math.Max(0, led.Balance(_econ.HouseholdPurse[lh]).Mites - _r.LordWorksReserveMites)));
            foreach (var (p, spare) in payers)
            {
                int hands = (int)Math.Min(want, spare * _r.LordWorksPmPerDay / 1000 / wage);
                if (hands <= 0) continue;
                // shared out among the fief's settlements by their idle, in settlement order, the remainder to the first
                int given = 0;
                foreach (int s in mine)
                {
                    int n = (int)((long)hands * idle[s].Count / want);
                    if (n > 0) { posts.Add(new DayPost(s, n, p, "lord")); given += n; }
                }
                if (hands - given > 0) posts.Add(new DayPost(mine[0], hands - given, p, "lord"));
                want -= hands;
                if (want <= 0) break;
            }
        }
        return posts;
    }

    /// <summary>The hiring hour (H4 at 06:00): lapsed labourers go back to idle, the posts are filled first come and paid. Returns the hands hired.</summary>
    public int Hour(GameTime now)
    {
        int day = (int)now.TotalDays; int season = day / TimeConst.DaysPerSeason;
        var p = _pop.People;
        // a labourer with no day's work for the lapse days is idle again
        var lapsed = new List<int>();
        foreach (var (i, w) in Book.Workers) if (day - w.LastDay > _r.DayLabourLapseDays) lapsed.Add(i);
        foreach (int i in lapsed)
        {
            Book.Workers.Remove(i); Book.Lapsed++;
            if (i < p.Count && p.Occupation[i] == Occupation.Labourer && p.Employer[i] < 0 && !_labour.IsStaff(i)) { p.Occupation[i] = Occupation.None; p.WorkSettlement[i] = -1; }
        }
        _labour.Rebuild();
        var idle = Idle(day);
        var posts = new List<DayPost>();
        if (Posts != null) for (int s = 0; s < idle.Length; s++) if (idle[s].Count > 0) posts.AddRange(Posts(s, day));
        posts.AddRange(LordWorks(idle));
        var next = new int[idle.Length];
        int hired = 0; long wage = _r.DayLabourWageMites;
        foreach (var post in posts)
        {
            Book.PostsOffered += post.Hands;
            var list = idle[post.Settlement];
            for (int k = 0; k < post.Hands && next[post.Settlement] < list.Count; k++)
            {
                int person = list[next[post.Settlement]];
                int hh = p.Household[person];
                if (hh < 0 || hh >= _econ.HouseholdCount) { next[post.Settlement]++; k--; continue; }
                if (!_econ.Ledger.Transfer(post.Payer, _econ.HouseholdPurse[hh], new Coin(wage), TransferReason.Wage)) break;
                next[post.Settlement]++;
                Take(person, post.Settlement, day, season);
                Book.PaidMites += wage; hired++;
            }
        }
        Book.Hired += hired;
        return hired;
    }

    /// <summary>Records a day's work: the person is a day labourer of the place, his days counted.</summary>
    public void Take(int person, int settlement, int day, int season)
    {
        var p = _pop.People;
        if (p.Occupation[person] == Occupation.None) { p.Occupation[person] = Occupation.Labourer; p.WorkSettlement[person] = settlement; }
        var w = Book.Workers.TryGetValue(person, out var x) ? x : (LastDay: day, Season: season, Days: 0);
        if (w.Season != season) w = (day, season, 0);
        Book.Workers[person] = (day, season, w.Days + 1);
    }
}
