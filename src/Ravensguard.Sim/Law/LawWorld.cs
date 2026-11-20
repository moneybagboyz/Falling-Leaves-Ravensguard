using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Law;

/// <summary>
/// What the law's systems read and write (crime-and-justice 2), gathered so a test can build a town by hand and the runtime can build it from the world state: the chronicle and its ids, the people and
/// households, the fief of each settlement and its treasury, the jobs board that escrows bounties, and the few questions the law asks of other systems (renown, hunger, names).
/// </summary>
public sealed class LawWorld
{
    public required Chronicle Chronicle { get; init; }
    public required IdAllocator Ids { get; init; }
    public required PersonTable People { get; init; }
    public HouseholdTable? Households { get; init; }
    public required string[] SettlementNames { get; init; }
    /// <summary>The settlements' ids (for a chronicle place parameter); empty entries fall back to no place.</summary>
    public EntityId[] SettlementIds { get; init; } = [];
    /// <summary>Per settlement: the fief it belongs to (an index into <see cref="FiefIds"/>), -1 none.</summary>
    public int[] SettlementFief { get; init; } = [];
    public EntityId[] FiefIds { get; init; } = [];
    public ulong Seed { get; init; }
    public LawContent Law { get; init; } = LawContent.Default;
    public int RegionId { get; init; }
    public Ledger? Ledger { get; init; }
    public JobTable? Jobs { get; init; }
    public JobEnv? JobEnv { get; init; }
    /// <summary>The treasury a settlement's lord posts bounties from (null: none to draw on).</summary>
    public Func<int, PurseId?>? TreasuryOf { get; init; }
    /// <summary>A person's renown in permille (the standing book's), 0 when none.</summary>
    public Func<EntityId, int> RenownPm { get; init; } = _ => 0;
    /// <summary>Whether a household is desperate (hungry, missed meals, or in arrears): the source of NPC theft.</summary>
    public Func<int, bool> Desperate { get; init; } = _ => false;
    /// <summary>A person's name for the chronicle's data and the rumour (the hero is not in the person table).</summary>
    public Func<EntityId, string>? NameOf { get; init; }
    /// <summary>P6-B4 (faith 4.3): told of every crime recorded (the doer and the kind), so a sin costs its piety; null: nobody keeps account.</summary>
    public Action<EntityId, string>? Committed { get; init; }

    public LawSettingsDef S => Law.S;

    public string Name(EntityId p)
    {
        if (NameOf?.Invoke(p) is { Length: > 0 } n) return n;
        int i = People.IndexOf(p); return i >= 0 ? People.FullName(i) : "";
    }

    public EntityId PlaceId(int settlement) => settlement >= 0 && settlement < SettlementIds.Length ? SettlementIds[settlement] : EntityId.None;
    public string PlaceName(int settlement) => settlement >= 0 && settlement < SettlementNames.Length ? SettlementNames[settlement] : "";
    public int FiefOf(int settlement) => settlement >= 0 && settlement < SettlementFief.Length ? SettlementFief[settlement] : -1;

    /// <summary>The law's view of the running world at a barrier: the home region's people, households, fiefs and treasuries, the jobs board, the standing book's renown and the economy's grievances.</summary>
    public static LawWorld From(WorldState st, ContentDatabase? db)
    {
        var pop = st.Population; var region = pop.Region; var e = st.Economy;
        var names = region.Settlements.Select(s => s.Name).ToArray();
        var ids = region.Settlements.Select(s => s.Id).ToArray();
        var fief = region.Settlements.Select(s => s.FiefIndex >= 0 && s.FiefIndex < region.Fiefs.Count ? s.FiefIndex : -1).ToArray();
        var fiefIds = region.Fiefs.Select(f => f.Id).ToArray();
        JobEnv? env = e == null || db == null ? null : new JobEnv { Ledger = e.Ledger, Econ = e, Pop = pop, Tuning = Ravensguard.Sim.Behaviour.BehaviourContent.From(db).OddJobs };
        var standing = st.Standing;
        var hero = st.Party?.Hero ?? EntityId.None;
        return new LawWorld
        {
            Chronicle = st.Chronicle, Ids = st.Ids, People = pop.People, Households = pop.Households, SettlementNames = names, SettlementIds = ids, SettlementFief = fief, FiefIds = fiefIds,
            Seed = st.Seed, Law = LawContent.From(db), RegionId = st.HomeRegion.Id, Ledger = e?.Ledger, Jobs = e == null ? null : (st.Jobs ??= new JobTable()), JobEnv = env,
            TreasuryOf = s =>
            {
                int f = s >= 0 && s < fief.Length ? fief[s] : -1;
                return e != null && f >= 0 && e.Ledger.TryFind(PurseKind.Treasury, fiefIds[f], out var p) ? p : null;
            },
            RenownPm = p => standing != null && standing.Renowns.TryGetValue(p.Value, out var r) ? r.Pm : 0,
            Desperate = hh => e != null && hh >= 0 && hh < e.HouseholdCount && (e.Grievances[hh] & (Grievance.Hunger | Grievance.MissedMeal | Grievance.Tax)) != 0,
            NameOf = p => p == hero ? st.Hero?.Name ?? "" : "",
            Committed = (doer, kind) => Ravensguard.Sim.Faith.FaithWiring.OnCrime(st, doer, kind),
        };
    }
}
