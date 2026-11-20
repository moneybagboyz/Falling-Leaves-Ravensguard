using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Contracts;

/// <summary>Where a contract stands: on the board, taken, or closed (done, failed by the taker, expired unclaimed, cancelled by its issuer). Append only.</summary>
public enum WorkState : byte { Open, Taken, Done, Failed, Expired, Cancelled }

/// <summary>
/// One contract (contracts-and-rumours-v2 3): work the world needed, its pay held in a real escrow chest (a ledger transfer from the issuer's purse, <see cref="Ravensguard.Sim.Economy.TransferReason.ContractEscrow"/>),
/// a deadline that is an absolute game day, a kind row of content, what it is about, who took it, and the chronicle entries that explain it. Named <c>WorkContract</c> because Phase 5's building contract
/// (<c>Ravensguard.Sim.Construction.Contract</c>) keeps its own name and book.
/// </summary>
public sealed class WorkContract
{
    public int Id;
    public string Kind = "";
    public EntityId Issuer;
    /// <summary>The issuer's purse (a ledger index) the pay came from and returns to.</summary>
    public int IssuerPurse = -1;
    /// <summary>The escrow chest (a ledger index).</summary>
    public int Escrow = -1;
    public long PayMites;
    public int PostedDay, DeadlineDay;
    public int Settlement = -1;
    /// <summary>What it is about: a person (the culprit, the captive, the runaway), the site, the caravan.</summary>
    public EntityId Target;
    /// <summary>The owner of the store a delivery fills (a household or business id); None for the others.</summary>
    public EntityId TargetOwner;
    public int Good = -1;
    public long Grams;
    /// <summary>What the target store's mass ledger had taken in of the good when the contract was taken (a delivery is done when the store's inflow has risen by <see cref="Grams"/> since).</summary>
    public long Baseline;
    /// <summary>Grams of it the store has taken in since the taker took the contract (the verifier's running count).</summary>
    public long Delivered;
    /// <summary>A number a kind keeps: a crime id (bounty), a guard count, a tournament id.</summary>
    public long Param;
    public WorkState State;
    public EntityId Taker;
    /// <summary>The taker is a company (its id in <see cref="Taker"/>), not a person.</summary>
    public bool ByCompany;
    public int TakenDay = -1, ClosedDay = -1;
    /// <summary>The bond the taker put up (a chest), charged on a failure.</summary>
    public int BondChest = -1;
    public long BondMites;
    /// <summary>The need's chronicle entry (the shortage, the crime, the band) and the entry of the posting.</summary>
    public ulong CauseEntry, PostedEntry;
    public List<ulong> Evidence = [];
    /// <summary>The hero pinned it to the map (a soft marker the player chose).</summary>
    public bool Pinned;
    /// <summary>A Phase 3 job this contract stands for (a bounty is still an escrowed <c>OddJobKind.Bounty</c> job), else 0.</summary>
    public ulong JobId;

    public bool Open => State == WorkState.Open;
    public bool Live => State is WorkState.Open or WorkState.Taken;
    public bool Closed => !Live;
}

/// <summary>A slander (spec 4.5): the false rumour, who told it, whom it blackens, whether it has been shown a lie.</summary>
public sealed class SlanderRow
{
    public ulong Rumour, Author, Victim;
    public int Day;
    public bool Exposed;
    public int ExposedDay = -1;
}

/// <summary>The tournament of a great fair (spec 4.4): the purse in escrow, the entrants, the bracket and the bout records the purse is paid from.</summary>
public sealed class Tournament
{
    public int Id, Settlement, Year, FairDay;
    public int Escrow = -1, TreasuryPurse = -1;
    public long PurseMites;
    public List<ulong> Entrants = [];
    /// <summary>The bracket's bouts, round by round: round, slot, the two fighters (0: a bye), the winner (0: not fought yet).</summary>
    public List<Bout> Bouts = [];
    public ulong Winner;
    public bool Paid, Cancelled, Drawn;
    public long Fee;
}

/// <summary>One bout of a bracket. <see cref="Winner"/> is written only by the sim's own resolution or by a fight the sim ran, never taken on a caller's word. A bye has <see cref="B"/> 0 and its winner is <see cref="A"/> at once.</summary>
public struct Bout
{
    public int Round, Slot;
    public ulong A, B, Winner;
    /// <summary>The joust's closing angle in degrees off head-on (0 to 15), for a lance bout.</summary>
    public int AngleDeg;
}

/// <summary>A company of hired fighters or builders (spec 4.2): an id, a strength, how many it counts and the purse the pay goes to.</summary>
public readonly record struct CompanyInfo(EntityId Id, string Name, int Settlement, int Strength, int Size, int Purse, string Kind);

/// <summary>
/// The contracts of the world (the optional "contracts" section, P6-C2; no format bump, absent while empty): every contract, the tournaments of the fairs, the slanders told and the counters.
/// </summary>
public sealed class ContractBook
{
    public readonly SortedDictionary<int, WorkContract> Contracts = [];
    public readonly SortedDictionary<int, Tournament> Tournaments = [];
    public readonly List<SlanderRow> Slanders = [];
    public int NextId = 1, NextTournament = 1;
    public int LastScanDay = -1, LastSlanderSeason = -1;
    /// <summary>Closed contracts are dropped after this many days (the Journal shows recent ones).</summary>
    public const int KeepClosedDays = 30;

    public bool IsEmpty => Contracts.Count == 0 && Tournaments.Count == 0 && Slanders.Count == 0 && NextId == 1 && NextTournament == 1 && LastScanDay == -1 && LastSlanderSeason == -1;

    public WorkContract? Get(int id) => Contracts.GetValueOrDefault(id);
    public IEnumerable<WorkContract> OpenAt(int settlement) => Contracts.Values.Where(c => c.Open && c.Settlement == settlement);
    public IEnumerable<WorkContract> OfTaker(EntityId taker) => Contracts.Values.Where(c => c.State == WorkState.Taken && c.Taker == taker);

    /// <summary>The <c>ContractHash</c>: every contract, tournament and slander in key order, escrows included (spec 5).</summary>
    public ulong Hash()
    {
        ulong h = 0xC0117AC70A5E0001UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        foreach (var (id, c) in Contracts)
        {
            M((ulong)(uint)id); foreach (char ch in c.Kind) M(ch); M(0xFF); M(c.Issuer.Value); M((ulong)(uint)c.IssuerPurse); M((ulong)(uint)c.Escrow); M((ulong)c.PayMites); M((ulong)(uint)c.PostedDay); M((ulong)(uint)c.DeadlineDay);
            M((ulong)(uint)c.Settlement); M(c.Target.Value); M(c.TargetOwner.Value); M((ulong)(uint)c.Good); M((ulong)c.Grams); M((ulong)c.Baseline); M((ulong)c.Delivered); M((ulong)c.Param); M((ulong)c.State);
            M(c.Taker.Value); M(c.ByCompany ? 1UL : 0); M((ulong)(uint)c.TakenDay); M((ulong)(uint)c.ClosedDay); M((ulong)(uint)c.BondChest); M((ulong)c.BondMites); M(c.CauseEntry); M(c.PostedEntry);
            foreach (var e in c.Evidence) M(e);
            M(c.Pinned ? 1UL : 0); M(c.JobId);
        }
        foreach (var (id, t) in Tournaments)
        {
            M((ulong)(uint)id); M((ulong)(uint)t.Settlement); M((ulong)(uint)t.Year); M((ulong)(uint)t.FairDay); M((ulong)(uint)t.Escrow); M((ulong)(uint)t.TreasuryPurse); M((ulong)t.PurseMites);
            foreach (var e in t.Entrants) M(e);
            foreach (var b in t.Bouts) { M((ulong)(uint)b.Round); M((ulong)(uint)b.Slot); M(b.A); M(b.B); M(b.Winner); M((ulong)(uint)b.AngleDeg); }
            M(t.Winner); M(t.Paid ? 1UL : 0); M(t.Cancelled ? 1UL : 0); M(t.Drawn ? 1UL : 0); M((ulong)t.Fee);
        }
        foreach (var s in Slanders) { M(s.Rumour); M(s.Author); M(s.Victim); M((ulong)(uint)s.Day); M(s.Exposed ? 1UL : 0); M((ulong)(uint)s.ExposedDay); }
        M((ulong)(uint)NextId); M((ulong)(uint)NextTournament); M((ulong)(uint)LastScanDay); M((ulong)(uint)LastSlanderSeason);
        return h;
    }
}
