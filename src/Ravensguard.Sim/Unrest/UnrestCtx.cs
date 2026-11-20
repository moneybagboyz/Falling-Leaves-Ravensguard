using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Unrest;

/// <summary>A crime a riot commits, for the law (unrest-and-revolt 4.2): the act, who did it to whom, where, when, what it was worth and who saw it. The riot's chronicle entry is the act's cause.</summary>
public sealed record RiotCrime(string Kind, EntityId Doer, EntityId Victim, int Settlement, long Sec, long Value, IReadOnlyList<EntityId> Witnesses, string NoteKey, ulong Riot, bool Report);

/// <summary>
/// What the unrest passes read from outside the sim world (installed by <see cref="UnrestWiring"/>, not saved). Every hook is optional: a bare world without a law, a standing book or a church still has a ladder.
/// The hooks of the parts that are built alongside are named here so each can wire its own: <see cref="LordBanned"/> (faith politics, E2), <see cref="Claimants"/> (family politics, E3), the bonded (slavery, D1), purveyance and
/// the enclosure of commons (Phase 7 and the Phase 5 prospecting hook) which call <see cref="UnrestBook.Add"/> directly.
/// </summary>
public sealed class UnrestLinks
{
    /// <summary>Hands a crime to the law (commit and report); returns the crime's chronicle entry, none when no law is installed.</summary>
    public Func<RiotCrime, EntityId>? Crime { get; set; }
    /// <summary>Puts a deed of the doer into the standing book (doer, deed row id, entry, settlement, day).</summary>
    public Action<EntityId, string, EntityId, int, int>? Deed { get; set; }
    /// <summary>A person's standing with a settlement in permille (the standing book's); 0 when none is kept.</summary>
    public Func<EntityId, int, int> StandingWithSettlement { get; set; } = (_, _) => 0;
    /// <summary>Whether a fief's lord is excommunicated or his fief under interdict (E2).</summary>
    public Func<int, bool> LordBanned { get; set; } = _ => false;
    /// <summary>Whether slavery is present in a settlement whose culture's law says none (D1: the law's eye already finds them; this counts toward the grievance).</summary>
    public Func<int, bool> SlaveryUnlawful { get; set; } = _ => false;
    /// <summary>Rival claimants to a fief, best first (E3's disputed inheritances: a dispossessed heir); empty by default.</summary>
    public Func<int, IReadOnlyList<ulong>> Claimants { get; set; } = _ => [];
    /// <summary>Whether a household suffered a crime this season (the economy's Crime flag is read as well).</summary>
    public Func<int, bool> CrimeVictim { get; set; } = _ => false;
    /// <summary>Opens the seat's gaol to a crowd (the persons let out); empty when it holds none.</summary>
    public Func<int, long, IReadOnlyList<EntityId>>? Jailbreak { get; set; }
    /// <summary>Drops the open crimes of a pardoned person.</summary>
    public Action<EntityId>? Pardon { get; set; }
    /// <summary>The hero's person id value and whether he is the hero.</summary>
    public Func<ulong, bool> IsHero { get; set; } = _ => false;
    /// <summary>The persons of a settlement the law arms: its guards, ascending (the people's own table when null).</summary>
    public Func<int, IReadOnlyList<int>>? Guards { get; set; }
}

/// <summary>What an unrest pass reads and writes: the book, the rule's context (economy, people, chronicle, ledger), the outbox deaths and entries go to, the politics book when there is one and the day.</summary>
public sealed class UnrestCtx
{
    public required UnrestBook B { get; init; }
    public required RuleCtx R { get; init; }
    /// <summary>Where deaths and tick-written entries go; null for a concession or a command at the barrier (which kills nobody).</summary>
    public RegionOutbox? Ob { get; init; }
    public UnrestLinks Links { get; init; } = new();
    public PoliticsBook? PB { get; init; }
    public int Day { get; init; }
    public long Hour { get; init; }

    public SimWorld W => R.W;
    public RuleBook RB => R.B;
    public UnrestSettingsDef S => B.Settings;
    public RuleChron Chron => R.Chron;
    public ulong Seed => R.Seed;
    public GameTime Now => Hour > 0 ? GameTime.FromGameHours(Hour) : RuleCtx.At(Day);
    public int SeasonIndex => Day / Ravensguard.Sim.Core.TimeConst.DaysPerSeason;

    public int FiefOfSettlement(int s) => (uint)s < (uint)R.Region.Settlements.Count ? R.Region.Settlements[s].FiefIndex : -1;
    public EntityId PlaceId(int s) => (uint)s < (uint)R.Region.Settlements.Count ? R.Region.Settlements[s].Id : EntityId.None;
    public string PlaceName(int s) => (uint)s < (uint)R.Region.Settlements.Count ? R.Region.Settlements[s].Name : "";
    /// <summary>The lord of the fief (the holder), -1 none.</summary>
    public long LordOf(int fief) => RB.FiefOf(fief) is { Forfeited: false } f ? f.Holder : -1;
}
