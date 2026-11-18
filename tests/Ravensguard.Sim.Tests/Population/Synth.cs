using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Tests.Abstract;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.PopulationRates;

/// <summary>
/// Builds small hand-made populations on the shared seed-1234 region (so settlements, lots and fiefs are real): households are added one at a time with their
/// members, in the dwelling lots of one settlement, and the economy is opened for them.
/// </summary>
public sealed class Synth
{
    public const int Day0 = 600 * TimeConst.DaysPerYear; // an arbitrary "now": 600 years in
    private static readonly Lazy<int> Home = new(() =>
    {
        var r = EconFixture.Pop.Region;
        for (int s = 0; s < r.Settlements.Count; s++)
            if (r.Settlements[s].FiefIndex >= 0 && r.Settlements[s].Plan.Lots.Count(l => l.IsDwelling && l.Dwelling != DwellingType.Manor) >= 40) return s;
        throw new InvalidOperationException("no settlement with 40 dwellings");
    });

    public int Settlement { get; } = Home.Value;
    public int Fief => EconFixture.Pop.Region.Settlements[Settlement].FiefIndex;
    public PersonTable People { get; } = new(256, 1);
    public HouseholdTable Households { get; } = new(64, 1);
    public List<int> DwellingLots { get; } = [];
    private readonly int[][] _lotHousehold, _lotBeds;
    private int _nextLot;

    public Synth()
    {
        var region = EconFixture.Pop.Region;
        _lotHousehold = new int[region.Settlements.Count][]; _lotBeds = new int[region.Settlements.Count][];
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            int n = region.Settlements[s].Plan.Lots.Count;
            _lotHousehold[s] = new int[n]; Array.Fill(_lotHousehold[s], -1); _lotBeds[s] = new int[n];
        }
        foreach (var l in region.Settlements[Settlement].Plan.Lots) if (l.IsDwelling && l.Dwelling != DwellingType.Manor) DwellingLots.Add(l.Index);
    }

    /// <summary>Adds a household in the next dwelling lot with the given beds and members (sex, age in years); the first member is the head. Returns the household index.</summary>
    public int Household(int beds, params (Sex Sex, int Age)[] members) => Household(beds, SocialClass.Peasant, members);

    public int Household(int beds, SocialClass cls, params (Sex Sex, int Age)[] members)
    {
        int lot = DwellingLots[_nextLot++];
        _lotBeds[Settlement][lot] = beds;
        int hh = Households.Add(Settlement, lot, DwellingType.Cottage, beds);
        Households.Class[hh] = cls; Households.Deed[hh] = DeedKind.Rented; _lotHousehold[Settlement][lot] = hh;
        string family = "Fam" + hh;
        foreach (var (sex, age) in members)
        {
            int p = People.Add();
            People.Sex[p] = sex; People.BirthDay[p] = Day0 - age * TimeConst.DaysPerYear - 1; People.Family[p] = family; People.Given[p] = (sex == Sex.Male ? "M" : "F") + p;
            People.Household[p] = hh; People.HomeSettlement[p] = Settlement; People.HomeLot[p] = (short)lot; People.Class[p] = cls;
            Households.AddMember(hh, p);
        }
        Households.Head[hh] = Households.MemberIdx[Households.MemberStart[hh]];
        Households.Name[hh] = family;
        return hh;
    }

    /// <summary>Leaves the next <paramref name="n"/> dwelling lots empty but with beds (vacancies for the marriage brake).</summary>
    public void Vacancies(int n, int beds = 6)
    {
        for (int i = 0; i < n; i++) _lotBeds[Settlement][DwellingLots[_nextLot++]] = beds;
    }

    public void Marry(int a, int b) { People.Spouse[a] = b; People.Spouse[b] = a; }

    public int MemberOf(int hh, int k) => Households.MemberIdx[Households.MemberStart[hh] + k];

    public SimWorld Build(VitalRates? rates = null, bool names = true)
    {
        var region = EconFixture.Pop.Region;
        var pop = new Population
        {
            Region = region, People = People, Households = Households, NowDay = Day0, LotHousehold = _lotHousehold, LotBeds = _lotBeds,
            LotKeeper = region.Settlements.Select(s => Enumerable.Repeat(-1, s.Plan.Lots.Count).ToArray()).ToArray(),
            FiefLord = Enumerable.Repeat(-1, region.Fiefs.Count).ToArray(), FiefLordHousehold = Enumerable.Repeat(-1, region.Fiefs.Count).ToArray(),
            ParcelHolder = Enumerable.Repeat(-1, region.Parcels.Count).ToArray(), PriestHousehold = Enumerable.Repeat(-1, region.Settlements.Count).ToArray(),
            SettlementPeople = new int[region.Settlements.Count],
        };
        var econ = EconomyState.FromContent(EconFixture.Content, Households.Count, People.Count);
        econ.OpenHouseholds(Households);
        var w = new SimWorld(AbstractFixture.Seed, pop, econ, names: names ? AbstractFixture.Names(pop) : null,
            state: new AbstractState { HourDone = (long)Day0 * 24 }, env: new ConstantEnvironment(12));
        if (rates != null) w.Rates = rates;
        return w;
    }

    /// <summary>Gives a household coin and a store of wheat (each member eats 1.1 kg a day).</summary>
    public static void Fund(SimWorld w, int hh, long mites, long wheatG = 0)
    {
        var e = w.Economy;
        if (mites > 0) e.Ledger.Mint(e.HouseholdPurse[hh], Coin.FromMites(mites), SourceReason.Seed);
        if (wheatG > 0) e.HouseholdStore[hh].Create(e.Goods.IdOf("wheat"), wheatG, Ravensguard.Sim.Goods.CreateReason.Seed);
    }

    /// <summary>The systems of the vital kernels only (needs, rolls, daily globals, seasonal), no food supply.</summary>
    public static AbstractSystemRegistry Registry(bool needs = true, bool daily = true, bool seasonal = true)
    {
        var r = new AbstractSystemRegistry();
        if (needs) r.Add(new HouseholdNeedsSystem());
        r.Add(new VitalRollsSystem());
        if (daily) r.Add(new PopulationDailySystem());
        if (seasonal) { r.Add(new PopulationSeasonalSystem()); r.Add(new PopulationMigrationSystem()); }
        return r;
    }

    public static (long Coin, long Sunk, long Sourced) CoinTotals(SimWorld w)
    {
        var a = w.Economy.Ledger.Audit();
        return (a.InPurses.Mites, a.Sunk.Mites, a.Sourced.Mites);
    }
}
