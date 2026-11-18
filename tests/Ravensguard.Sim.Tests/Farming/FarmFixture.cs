using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Farming;

/// <summary>Weather the tests script: a constant mild day unless a delegate says otherwise. Cells are continental cell ids.</summary>
public sealed class FakeFarmWeather : IFarmWeather
{
    public Func<long, long> RainRaw = _ => 2 * 65536;               // 2 mm a day
    public Func<long, int> TempTenths = _ => 100;                    // 10 C
    public Func<long, bool> Storm = _ => false;
    public Func<long, int> Snow = _ => 0;
    public Func<int, int, LongCondition> LongFn = (_, _) => default;
    public long DailyRainRaw(int cell, long day) => RainRaw(day);
    public int TempAt5Tenths(int cell, long day) => TempTenths(day);
    public bool Thunderstorm(int cell, long day) => Storm(day);
    public int SnowDepthCm(int cell, long day) => Snow(day);
    public LongCondition Long(int culture, int year) => LongFn(culture, year);
}

/// <summary>A small hand-built world: one village with a lord (household 0) and one holder household per parcel; slots are filled round-robin.</summary>
public sealed record SynthSpec(int ParcelsPerSlot = 3, int FieldsPerParcel = 4, int Fertility = 70, OwnerKind Owner = OwnerKind.Tenant, int WorkersPerHolder = 4,
    int PoolHouseholds = 0, int Variance = 0, int Year = 5, bool CoinLeases = false, int Sheep = 40, int HolderCrowns = 20);

public sealed class Synth
{
    public required RegionData Region { get; init; }
    public required Population Pop { get; init; }
    public required EconomyState Econ { get; init; }
    public required FarmingSystem Sys { get; init; }
    public required FakeFarmWeather Wx { get; init; }
    public required Chronicle Chron { get; init; }
    public required IdAllocator Ids { get; init; }
    public int Year => Sys.State.Year;
    public FarmingState State => Sys.State;
    public ushort Good(string id) => Econ.Goods.IdOf(id);

    public static GameTime At(int year, int doy, int hour = 0) => GameTime.FromGameDays((long)year * TimeConst.DaysPerYear + doy - 1) + Duration.FromGameHours(hour);

    public static Synth Build(SynthSpec s, FarmingSettingsDefOverride? tweak = null)
    {
        var region = new RegionData { Seed = 1, CellX = 100, CellY = 100 };
        int nParcels = s.ParcelsPerSlot * 3, nFields = nParcels * s.FieldsPerParcel;
        var fields = new Field[nFields];
        var people = new PersonTable(64, 1); var hh = new HouseholdTable(8, 1);
        int lord = hh.Add(0, 0, DwellingType.Manor, 6); hh.Class[lord] = SocialClass.Gentry;
        int lp = people.Add(); people.BirthDay[lp] = -40 * 60; people.Household[lp] = lord; people.Occupation[lp] = Occupation.Lord; hh.AddMember(lord, lp);
        var holders = new int[s.ParcelsPerSlot];
        for (int q = 0; q < s.ParcelsPerSlot; q++)
        {
            int h = hh.Add(0, q + 1, DwellingType.Cottage, 6); hh.Class[h] = SocialClass.Peasant; holders[q] = h;
            for (int w = 0; w < s.WorkersPerHolder; w++)
            {
                int m = people.Add(); people.BirthDay[m] = -30 * 60; people.Household[m] = h; people.Occupation[m] = Occupation.Farmer; hh.AddMember(h, m);
            }
        }
        var st = new RegionSettlement { Id = EntityId.Make(IdKind.Settlement, 1), Name = "Testby", Class = SettlementClass.Village, Culture = 0 };
        region.Settlements.Add(st);
        int fi = 0;
        for (int p = 0; p < nParcels; p++)
        {
            byte slot = (byte)(p / s.ParcelsPerSlot);                  // blocks of parcels per slot; every holder works one parcel of each slot
            region.Parcels.Add(new Parcel { Id = EntityId.Make(IdKind.Parcel, (ulong)p + 1), Settlement = 0, Owner = new ParcelOwner(s.Owner, 0, p + 1), Slot = slot, FieldStart = fi, FieldCount = s.FieldsPerParcel });
            st.Parcels.Add(p);
            for (int k = 0; k < s.FieldsPerParcel; k++, fi++)
            {
                int x = fi % 30, y = fi / 30 % 30;
                fields[fi] = new Field { Id = EntityId.Make(IdKind.Field, (ulong)fi + 1), Tile = y * RegionData.Size + x, Parcel = p, Fertility = (byte)s.Fertility, Slot = slot, Crop = slot == 2 ? Crop.Fallow : Crop.Wheat };
            }
        }
        for (int q = 0; q < s.PoolHouseholds; q++)
        {
            int h = hh.Add(0, 100 + q, DwellingType.Cottage, 6); hh.Class[h] = SocialClass.Peasant;
            for (int w = 0; w < 3; w++) { int m = people.Add(); people.BirthDay[m] = -25 * 60; people.Household[m] = h; people.Occupation[m] = Occupation.Labourer; hh.AddMember(h, m); }
        }
        region.Fields = fields;
        var holderOf = new int[nParcels]; for (int p = 0; p < nParcels; p++) holderOf[p] = holders[p % s.ParcelsPerSlot];
        var pop = new Population
        {
            Region = region, People = people, Households = hh, NowDay = s.Year * TimeConst.DaysPerYear,
            LotHousehold = [[]], LotBeds = [[]], LotKeeper = [[]], FiefLord = [lp], FiefLordHousehold = [lord], ParcelHolder = holderOf, PriestHousehold = [-1], SettlementPeople = [people.Count],
        };
        var econ = EconomyState.FromContent(EconFixture.Content, hh.Count, people.Count);
        econ.OpenHouseholds(hh);
        econ.Ledger.Mint(econ.HouseholdPurse[lord], Coin.FromCrowns(2000), SourceReason.Seed);
        for (int h = 1; h < hh.Count; h++) econ.Ledger.Mint(econ.HouseholdPurse[h], Coin.FromCrowns(s.HolderCrowns), SourceReason.Seed);
        var def = new Ravensguard.Content.FarmingSettingsDef { Id = "t", TargetMeanFertility = 0, VarianceSpreadPermille = s.Variance, CoinLeasePct = s.CoinLeases ? 100 : 0 };
        if (tweak != null) def = tweak(def);
        var settings = new FarmingSettings(def);
        var wx = new FakeFarmWeather(); var chron = new Chronicle(); var ids = new IdAllocator();
        var state = FarmingSystem.CreateState(region, pop, settings, 77, s.Year);
        state.Livestock.Sheep[0] = s.Sheep; state.Livestock.Oxen[0] = Math.Max(8, nFields * 3 / 100); state.Livestock.MeadowAcres[0] = 0;
        var sys = new FarmingSystem(region, pop, econ, settings, wx, chron, ids, 77, state);
        sys.ProvisionInitialStock();
        return new Synth { Region = region, Pop = pop, Econ = econ, Sys = sys, Wx = wx, Chron = chron, Ids = ids };
    }

    /// <summary>Steps whole days (seasonal then daily) from (year, doy) for the given number of days, with the economy's daily spoilage switched off.</summary>
    public void Run(int year, int fromDoy, int days)
    {
        var t = At(year, fromDoy);
        for (int i = 0; i < days; i++) { Sys.StepDay(t); t += Duration.FromGameDays(1); }
    }

    /// <summary>Sum of one good over every store.</summary>
    public long Total(string good) => Sys.TotalGrams(Good(good));

    public bool MassAudit() => Econ.MassAuditHolds();
}

public delegate Ravensguard.Content.FarmingSettingsDef FarmingSettingsDefOverride(Ravensguard.Content.FarmingSettingsDef def);
