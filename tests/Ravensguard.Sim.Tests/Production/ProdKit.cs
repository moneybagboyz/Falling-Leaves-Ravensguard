using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>Content and tiny synthetic worlds for the production tests: one village, a household or two, businesses built by hand.</summary>
public sealed class ProdKit
{
    public static readonly Lazy<ProductionContent> SharedContent = new(() => ProductionContent.Load(EconFixture.Content, EconFixture.Catalog()));
    public static ProductionContent Content => SharedContent.Value;
    public static RecipeBook Recipes => Content.Recipes;

    public ProductionContent Used { get; }
    public EconomyState Econ { get; }
    public Population Pop { get; }
    public ProductionSystem Sys { get; }
    public MarketSystem? Markets { get; }
    public PersonTable People => Pop.People;
    public GoodCatalog Cat => Econ.Goods;

    /// <summary>A region with one village and <paramref name="households"/> households of one adult each; persons get skills as given.</summary>
    public ProdKit(int households = 3, bool markets = false, ulong seed = 42, ResourceLayer? layer = null, ProductionContent? content = null)
    {
        var region = new RegionData { Seed = seed };
        region.Settlements.Add(new RegionSettlement { Id = EntityId.Make(IdKind.Settlement, 1), Name = "Testby", Class = SettlementClass.Village, CenterX = 16000, CenterY = 16000, TargetPop = households });
        var people = new PersonTable(households, 1); var hh = new HouseholdTable(households, 1);
        for (int i = 0; i < households; i++)
        {
            int h = hh.Add(0, 0, DwellingType.Cottage, 6); hh.Class[h] = SocialClass.Peasant;
            int p = people.Add(); people.Household[p] = h; people.BirthDay[p] = -30 * TimeConst.DaysPerYear; people.HomeSettlement[p] = 0; hh.AddMember(h, p);
            hh.Head[h] = p;
        }
        Pop = new Population
        {
            Region = region, People = people, Households = hh, NowDay = 0, LotHousehold = [[]], LotBeds = [[]], LotKeeper = [[]], FiefLord = [], FiefLordHousehold = [],
            ParcelHolder = [], PriestHousehold = [-1], SettlementPeople = [households],
        };
        Econ = EconomyState.FromContent(EconFixture.Content, hh.Count, people.Count);
        Econ.OpenHouseholds(hh);
        Markets = markets ? new MarketSystem(Econ, Pop, seed) : null;
        Used = content ?? Content;
        Sys = new ProductionSystem(Econ, Pop, Used, seed, Markets, layer);
    }

    public void Fund(int household, long mites) => Econ.Ledger.Mint(Econ.HouseholdPurse[household], new Coin(mites), SourceReason.Seed);

    /// <summary>A business of the given kind owned by person 0 with staff persons 1.. (given occupation/skill pairs); stations from the kind.</summary>
    public Business Make(string kind, long cash, int staff = 0, Occupation occ = Occupation.Baker, Skill skill = Skill.Baking, int level = 50, bool ownerWorks = true, int ownerLevel = -1)
    {
        var k = Used.Kind(kind);
        People.Occupation[0] = occ; People.SetSkill(0, 0, skill, ownerLevel >= 0 ? ownerLevel : level);
        var b = Sys.NewBusiness(k, 0, 0, 0); b.OwnerWorks = ownerWorks;
        foreach (var (sk, count) in k.Stations) for (int c = 0; c < count; c++) b.Stations.Add(new Station { Kind = sk });
        for (int s = 1; s <= staff && s < People.Count; s++) { People.Occupation[s] = occ; People.SetSkill(s, 0, skill, level); Sys.AddStaff(b, s); }
        if (cash > 0) Econ.Ledger.Mint(b.Purse, new Coin(cash), SourceReason.Seed);
        b.CashStart = cash;
        return b;
    }

    public void Give(Business b, string good, long qty) => b.Store.Create(Cat.IdOf(good), qty, CreateReason.Seed);
    public long Have(Business b, string good) => b.Store.Qty(Cat.IdOf(good));
    public Recipe R(string id) => Used.Recipes[id];

    /// <summary>Runs hourly work from day 0 hour 0 until the predicate holds or the hour limit; returns the hour index it stopped at.</summary>
    public long RunHours(long fromHour, long toHour, Func<bool>? stop = null)
    {
        for (long h = fromHour; h < toHour; h++)
        {
            Sys.WorkHour(GameTime.FromGameHours(h));
            if (stop != null && stop()) return h;
        }
        return toHour;
    }
}
