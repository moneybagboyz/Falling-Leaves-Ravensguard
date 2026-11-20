using Ravensguard.Content;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.Tests.Dialogue;

namespace Ravensguard.Sim.Tests.Bondage;

/// <summary>
/// Four towns by hand for the bondage tests: Saltmouth (0, chattel: the slave market), Ashby (1, no bondage: Valeholt's law), Highfold (2, debt) and Thorp (3, war captives), forty people each in households of four (person 0 is the
/// hero), a fief with a treasury, a ledger with a purse for each household and one for the hero, and the law's content. A day is a number; <see cref="Sec"/> turns it into game seconds.
/// </summary>
internal sealed class BondRig
{
    public const int Chattel = 0, None = 1, Debt = 2, War = 3;
    public static readonly EntityId Fief = EntityId.Make(IdKind.Faction, 900);
    internal static readonly Lazy<ContentDatabase> Db = new(V2Fixture.Load);
    public const ulong Seed = 1234;

    public PersonTable P = null!; public HouseholdTable H = null!; public Ledger L = new(); public Chronicle C = null!; public IdAllocator Ids = new();
    public BondBook Book = new(); public CrimeBook Crimes = new(); public MarkBook Marks = new(); public JobTable Jobs = new(); public CaptiveBook Caps = new(Seed);
    public LawWorld W = null!; public BondWorld Bw = null!;
    public PurseId Treasury, HeroPurse;
    public readonly Dictionary<int, PurseId> HhPurse = [];
    public Func<EntityId, EntityId, int> Opinion = (_, _) => 0;
    public readonly int[] Guards = new int[4];
    public readonly Dictionary<ulong, int> Leadership = [];
    public readonly SlaveLaw[] Laws = [SlaveLaw.Chattel, SlaveLaw.None, SlaveLaw.Debt, SlaveLaw.WarCaptives];
    public Func<EntityId, bool> WillFrees = _ => false;
    public EntityId Hero => P.IdOf(0);

    public static BondRig Make(int per = 40)
    {
        var r = new BondRig();
        var (tw, chron) = TalkKit.Town(per, Seed, 4, ["Saltmouth", "Ashby", "Highfold", "Thorp"]); r.C = chron; r.P = tw.People;
        r.H = new HouseholdTable(per, 5000);
        for (int h = 0; h < r.P.Count / 4; h++)
        {
            int hh = r.H.Add(r.P.HomeSettlement[h * 4], 0, DwellingType.Cottage, 6);
            for (int k = 0; k < 4; k++) r.H.AddMember(hh, h * 4 + k);
            r.H.Head[hh] = h * 4;
            for (int k = 0; k < 4; k++) r.P.Household[h * 4 + k] = hh;
            var pu = r.L.Open(PurseKind.Household, r.H.IdOf(hh)); r.L.Mint(pu, new Coin(3_000), SourceReason.Seed); r.HhPurse[hh] = pu;
        }
        r.Treasury = r.L.Open(PurseKind.Treasury, Fief); r.L.Mint(r.Treasury, new Coin(100_000), SourceReason.Seed);
        r.HeroPurse = r.L.Open(PurseKind.Person, r.Hero); r.L.Mint(r.HeroPurse, new Coin(40_000), SourceReason.Seed);
        var db = Db.Value;
        var content = new LawContent(db.All<CrimeDef>(), new LawSettingsDef { Id = "law", ArrestPct = 0 }, db.All<PunishmentDef>());
        r.W = new LawWorld
        {
            Chronicle = r.C, Ids = r.Ids, People = r.P, Households = r.H, SettlementNames = ["Saltmouth", "Ashby", "Highfold", "Thorp"],
            SettlementIds = [.. Enumerable.Range(0, 4).Select(i => EntityId.Make(IdKind.Settlement, (ulong)i + 1))], SettlementFief = [0, 0, 0, 0], FiefIds = [Fief], FiefSeat = [0], Seed = Seed,
            Ledger = r.L, Jobs = r.Jobs, JobEnv = new JobEnv { Ledger = r.L }, TreasuryOf = _ => r.Treasury, Law = content, Captives = r.Caps, Marks = () => r.Marks, HeroId = r.Hero,
            PurseOf = p => p == r.Hero ? r.HeroPurse : r.HhPurse.GetValueOrDefault(r.P.Household[Math.Max(0, r.P.IndexOf(p))]),
            VictimPurse = v => { var id = new EntityId(v); if (id.Kind == IdKind.Household) { int h = r.H.IndexOf(id); return h >= 0 ? r.HhPurse[h] : null; } return id == r.Hero ? r.HeroPurse : r.HhPurse.GetValueOrDefault(r.P.Household[Math.Max(0, r.P.IndexOf(id))]); },
            OpinionPm = (a, b) => r.Opinion(a, b),
            DistanceM = (a, b) => Math.Abs(a - b) * 5_000L,
        };
        r.Bw = new BondWorld
        {
            W = r.W, Rules = BondRules.From(db), LawAt = s => s >= 0 && s < 4 ? r.Laws[s] : SlaveLaw.None, CultureAt = s => s, GuardsAt = s => r.Guards[s], WillFrees = p => r.WillFrees(p),
            LeadershipOf = p => r.Leadership.GetValueOrDefault(p.Value), NearestFreeTown = from => from == None ? -1 : None,
        };
        return r;
    }

    public static long Sec(int day, int hour = 10) => day * 86_400L + hour * 3600L;
    public EntityId Id(int row) => P.IdOf(row);
    /// <summary>A person of a town: the n'th of forty (after the hero), by town.</summary>
    public EntityId Of(int town, int n) => P.IdOf(town * 40 + n);
    public long Coin(EntityId owner) => Ledger(owner);
    private long Ledger(EntityId owner) => Bw.PurseOf(owner) is { } p ? L.Balance(p).Mites : 0;

    /// <summary>A slaver's pen: a person listed at a market for the table's price, sold by a band.</summary>
    public Bond Pen(EntityId person, int market = Chattel, int day = 1)
    {
        var band = Slavery.BandId(0, 0); Slavery.BandPurse(Bw, band);
        return Slavery.List(Bw, Book, person, band, PurseKind.Household, market, day, Sec(day), 0);
    }

    public void SetAge(EntityId p, int years, int day) { P.BirthDay[P.IndexOf(p)] = day - years * TimeConst.DaysPerYear; }
}
