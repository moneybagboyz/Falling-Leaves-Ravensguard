using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

public enum SettlementKind { Hamlet, Village, Town, City }
public enum NotableRole { Noble, Ruler, Heir, General, Rebel, MasterCrafter, Saint, Outlaw }
public enum ArtefactStatus { Held, Lost, Buried }
public enum CasusBelliKind { None, BorderClaim, Insult, Succession, Raid, Excommunication }

public sealed class Settlement
{
    public EntityId Id;
    public string Name = "";
    public int Cell;
    public int Pop;
    public int PeakPop;
    /// <summary>Index into <see cref="WorldHistory.Realms"/>, -1 for none.</summary>
    public int Realm = -1;
    public int FoundedTick;
    public bool OnRiver, Coastal;
    public int Mining;
    public int Hinterland;
    public int RouteCount;
    /// <summary>Hamlets, villages and manors: never grow into towns, and the history's main source of small ruins.</summary>
    public bool Minor, Manor;
    public bool IsRuin;
    public int RuinTick = -1;
    /// <summary>sacked, razed, burned, flooded, plague, famine, collapse, abandoned.</summary>
    public string RuinKind = "";
    public EntityId RuinEvent, RuinCauseEvent;
    public EntityId LastBlow;
    public int LastBlowTick = -100;
    public SettlementKind Kind => Pop >= 1500 ? SettlementKind.City : Pop >= 500 ? SettlementKind.Town : Pop >= 120 ? SettlementKind.Village : SettlementKind.Hamlet;
    public SettlementKind PeakKind = SettlementKind.Hamlet;
}

public sealed class RealmState
{
    public int Index;
    public EntityId Id;
    public string Name = "";
    public int CultureId;
    public int Capital = -1;
    public EntityId Ruler, Dynasty;
    public bool Alive = true;
    public int FoundedTick, EndTick = -1;
    public EntityId FoundedEvent, EndEvent;
    public string EndKind = "";
    public int Parent = -1;
    public int Cells, Pop, Treasury, Unrest;
    public int WarsWon, WarsLost;
    public EntityId LastDefeatEvent, LastLossEvent;
    public int LastFamineTick = -100;
    /// <summary>History v1: the realm's faith (an index into <see cref="WorldHistory.Religions"/>), -1 under v0.</summary>
    public int Religion = -1;
}

public sealed class Notable
{
    public EntityId Id;
    public string Name = "";
    public string Epithet = "";
    public bool Female;
    public NotableRole Role;
    public int BirthYear;
    public int DeathYear = -1;
    public EntityId Dynasty, Father, Mother, Spouse;
    public int Realm = -1, HomeRealm = -1;
    public int Culture;
    public int Ambition;
    public int Victories;
    public int Skill;
    public int Residence = -1;
    public int Kids;
    public EntityId DeathEvent, RiseEvent;
    public string DeathCause = "";
    public bool Alive => DeathYear < 0;
    public string Full => Epithet.Length > 0 ? $"{Name} {Epithet}" : Name;
}

public sealed class Dynasty
{
    public EntityId Id;
    public string Name = "";
    public int Culture;
    public EntityId Founder;
    public int FoundedYear;
    public bool Ruling;
}

public sealed class Artefact
{
    public EntityId Id;
    public string Name = "";
    public string Kind = "";
    public EntityId Creator;
    public int CreatedTick;
    public EntityId CreatedEvent;
    /// <summary>A Person, Faction or Settlement id while <see cref="Status"/> is Held.</summary>
    public EntityId Holder;
    public ArtefactStatus Status;
    public int Cell = -1;
    public int LostTick = -1;
    public EntityId LossEvent;
    public string LossNote = "";
    public int Transfers;
}

public sealed class TradeRoute
{
    public EntityId A, B;
    public int Cost;
    public int[] Cells = [];
    public int FoundedTick;
    public bool Abandoned;
}

public sealed record Battlefield(int Cell, EntityId Event, string Name, int Tick, int War, int Dead);
public sealed class Grudge
{
    public int From, To;
    public int Strength;
    public EntityId CauseEvent;
    public int Tick;
    public string Note = "";
}

public sealed class WarRecord
{
    public int Index, Attacker, Defender;
    public int StartTick, EndTick = -1;
    public CasusBelliKind Cb;
    public EntityId CbEvent, DeclEvent, EndEvent;
    public string Name = "", Outcome = "", CbNote = "";
    public int Score;                      // attacker's view, -100..100
    public int WearyA, WearyD;
    public int ArmyA, ArmyD;
    public int Battles, Sieges;
    public bool Naval;                     // seaborne raiding between realms with no land border
    public int Target = -1;                // settlement index under threat
    public int BlockedTarget = -1, BlockedUntil, SiegeFails;
    public int SiegeTicks = -1;
    public EntityId SiegeEvent;
    public EntityId LastBattleEvent;
    public bool Alive => EndTick < 0;
}

public sealed record Snapshot(int Year, byte[] Owner);

public enum TreatyKind { Truce, MarriageAlliance, Tribute, Vassalage }
/// <summary>For Tribute and Vassalage, <c>A</c> is the winner (overlord) and <c>B</c> the realm that pays or serves.</summary>
public sealed record Treaty(int A, int B, TreatyKind Kind, int StartTick, int ExpiryTick, EntityId Event);

/// <summary>History v1 (history-recalibration 4.2): a religion of the legends. Founded at year 0, one per culture; a schism adds more (<see cref="SchismFrom"/> names the faith it broke from).</summary>
public sealed class HistoryReligion
{
    public int Index;
    public string Name = "";
    public int Culture;
    public EntityId FoundedEvent, Founder;
    public int FoundedTick;
    public int SchismFrom = -1;
    /// <summary>The settlement that holds its holy site (a history settlement id), none when it fell.</summary>
    public EntityId HolySite;
}

/// <summary>History v1: a heresy that rose in a religion (<see cref="Religion"/>) and found a realm. It ends when the realm returns to the faith or turns it into a religion (a schism).</summary>
public sealed class Heresy
{
    public int Religion, Realm, StartTick, EndTick = -1;
    public EntityId Event, CauseEvent;
    public bool Alive => EndTick < 0;
}

/// <summary>History v1: a ruler cut off from the church. The excommunication is a casus belli for the realm's neighbours of the faith; <see cref="WarTick"/> is when one used it (-1 until then).</summary>
public sealed class Excommunication
{
    public int Realm, Tick, WarTick = -1;
    /// <summary>How many neighbouring realms of the faith were given the ban as a casus belli.</summary>
    public int Given;
    public EntityId Ruler, Event;
}

/// <summary>History v1: a slave raid by a realm whose culture holds bondage by capture, and where its captives were sold.</summary>
public sealed class SlaveRaid
{
    public int Raider, Victim, Captives, Tick, Market = -1;
    public EntityId Event, MarketEvent;
}
