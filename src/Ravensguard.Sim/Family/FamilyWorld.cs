using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Family;

/// <summary>
/// What family life reads and writes (family-and-lifespan 2), gathered so a test can build it over a hand-made world state and the runtime from the running one: the person and household tables, the ledger,
/// the chronicle, the hero (his row, record, party and the people he knows), the kin links, standing and renown, the homes he holds, the captive book and the home region's sim (for births and deaths that
/// go through the Phase 2 kernels). Everything is read at the barrier or on the tick worker; nothing here is thread-safe on its own.
/// </summary>
public sealed class FamilyWorld
{
    public required WorldState St { get; init; }
    public FamilyContent F { get; init; } = FamilyContent.Default;
    public FamilySettingsDef S => F.S;
    public ContentDatabase? Content { get; init; }
    public PersonTable People => St.Population.People;
    public HouseholdTable Households => St.Population.Households;
    public Population Pop => St.Population;
    public EconomyState Econ => St.Economy!;
    public Ledger Ledger => St.Economy!.Ledger;
    public Chronicle Chronicle => St.Chronicle;
    public IdAllocator Ids => St.Ids;
    public ulong Seed => St.Seed;
    public EntityId HeroId { get; init; }
    public VitalRates Rates { get; init; } = VitalRates.EconomyDefault;
    /// <summary>The home region's sim, when the world runs one (births and deaths through the Phase 2 kernels); null in a bare state.</summary>
    public SimWorld? Sim => St.HomeRecords?.Sim;

    public int HeroRow => HeroPerson.RowOf(People, St.Hero, HeroId);
    public FamilyBook Book => St.Family ??= new FamilyBook();

    public PurseId HeroPurse => Ledger.TryFind(PurseKind.Person, HeroId, out var p) ? p : Ledger.Open(PurseKind.Person, HeroId);
    public PurseId HouseholdPurse(int hh) => Econ.HouseholdPurse[hh];

    public string Name(int row) => row >= 0 && row < People.Count ? People.FullName(row) : "";
    public EntityId PlaceId(int settlement) => settlement >= 0 && settlement < Pop.Region.Settlements.Count ? Pop.Region.Settlements[settlement].Id : EntityId.None;
    public string PlaceName(int settlement) => settlement >= 0 && settlement < Pop.Region.Settlements.Count ? Pop.Region.Settlements[settlement].Name : "";

    /// <summary>The hero's standing with a settlement's people (permille); 0 with no standing book.</summary>
    public int StandingPm(int settlement) => Standing.Of(St.Standing, HeroId, new GroupRef(GroupKind.Settlement, settlement));
    public int RenownPm => St.Standing != null && St.Standing.Renowns.TryGetValue(HeroId.Value, out var r) ? r.Pm : 0;

    /// <summary>The hero's trust (permille) and familiarity of a person (the people he knows; a stranger has the Phase 3 start).</summary>
    public int TrustPm(EntityId person) => St.Party != null && St.Party.Known.TryGetValue(person, out var a) ? a.TrustPm : 0;

    /// <summary>The homes the hero holds as a deed (a lot or a building he owns or leases), as (settlement, lot) in deed order; asked only when a wedding needs a roof.</summary>
    public Func<IReadOnlyList<(int S, int Lot)>> HeldHomes { get; init; } = () => [];

    /// <summary>Whether a kidnapper band roams within reach of the hero's home in a season (a stand-in until bands are world objects: a draw on the season).</summary>
    public Func<int, bool> BandNear { get; init; } = _ => false;

    public static FamilyWorld From(WorldState st, ContentDatabase? db)
    {
        var heroId = st.Party?.Hero ?? EconomyState.HeroOwner;
        var sim = st.HomeRecords?.Sim;
        var content = FamilyContent.From(db);
        return new FamilyWorld
        {
            St = st, Content = db, F = content, HeroId = heroId, Rates = sim?.Rates ?? VitalRates.EconomyDefault,
            HeldHomes = () =>
            {
                if (db == null || st.Economy == null) return [];
                var lw = LandWiring.For(st, db);
                var list = new List<(int, int)>();
                foreach (var d in lw.Registry().HeldBy(heroId))
                    if (d.Kind is HoldingKind.Lot or HoldingKind.Building) { var (s, l) = LandIds.SplitLot(d.Subject); list.Add((s, l)); }
                return list;
            },
            BandNear = season => DetRng.Range(st.Seed, season, heroId.Value, RngStreams.Kidnapping, 1, 0, 1000) < content.S.KidnapBandPresencePm * st.Difficulty.BanditPct / 100,
        };
    }
}
