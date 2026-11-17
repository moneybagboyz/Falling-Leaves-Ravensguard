using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

public enum SettlementKind { Hamlet, Village, Town, City }
public enum NotableRole { Noble, Ruler, Heir, General, Rebel, MasterCrafter, Saint, Outlaw }
public enum ArtefactStatus { Held, Lost, Buried }
public enum CasusBelliKind { None, BorderClaim, Insult, Succession, Raid }

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
    public int Target = -1;                // settlement index under threat
    public int SiegeTicks = -1;
    public EntityId SiegeEvent;
    public EntityId LastBattleEvent;
    public bool Alive => EndTick < 0;
}

public sealed record Snapshot(int Year, byte[] Owner);

public enum TreatyKind { Truce, MarriageAlliance }
public sealed record Treaty(int A, int B, TreatyKind Kind, int StartTick, int ExpiryTick, EntityId Event);
