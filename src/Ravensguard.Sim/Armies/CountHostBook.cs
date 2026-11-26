using Ravensguard.Sim.Combat.Abstract;

namespace Ravensguard.Sim.Armies;

/// <summary>What a count host is doing on the continent (abstract-war 3, 4.4).</summary>
public enum CountState : byte { Standing, Marching, Besieging, InDetail, Disbanded }

/// <summary>
/// A host of the continent (abstract-war 3 <c>CountHost</c>), saved in the <c>armies</c> section (key 17): a realm's men by class, their quality, where they are (a summary region, or an
/// edge and the metres walked), the grain they carry, their objective and what they have been through. The men were taken out of their home region's population (and go back to it:
/// the survivors, the crippled, the deserters and the prisoners the host took, when it is disbanded), so people are never made or lost without a ledger line.
/// </summary>
public sealed class CountHost
{
    public int Id, Realm, Home, War;
    /// <summary>The realm this host fights (255 none): hosts of two realms that are each other's foes fight when they meet in a region.</summary>
    public int Foe = 255;
    /// <summary>The summary region the host stands in (-1 while on an edge).</summary>
    public int Region = -1;
    /// <summary>On an edge: its index, the region it left, and the metres walked along it.</summary>
    public int Edge = -1, FromRegion = -1, WalkedM;
    public int Objective = -1;
    public CountState State;
    public int[] Men = new int[CountScenarios.Classes];
    public int[] QualityPm = [1000, 1000, 1000, 1000, 1000];
    public int MoralePm = 1000;
    /// <summary>Grams of grain carried, days hungry, days besieging, men held prisoner, the day it was raised.</summary>
    public long GrainG;
    public int HungryDays, SiegeDays, Prisoners, FormedDay;
    /// <summary>The supply-book host that stands for it while it is in a detailed region (0 none).</summary>
    public int Supply;
    public int Total => Men.Sum();
    public bool OnEdge => Edge >= 0;
}

/// <summary>
/// Where everything the count hosts took or gave went (every line a total the conservation test adds up): grain eaten by hosts, taken from fields, spoiled by foragers and eaten by
/// besieged towns; men who died, were crippled, ran, deserted or were taken; places taken.
/// </summary>
public sealed class CountLedger
{
    public const int N = 12;
    public const int GrainEaten = 0, GrainForaged = 1, GrainWasted = 2, SiegeEaten = 3, MenDead = 4, MenCrippled = 5, MenFled = 6, MenDeserted = 7, MenCaptured = 8, PlacesTaken = 9, Battles = 10, Storms = 11;
    public long[] V = new long[N];
    public long this[int i] { get => V[i]; set => V[i] = value; }
    public bool IsEmpty => V.All(x => x == 0);
}

/// <summary>The continent's count hosts (saved in the armies section): by id, the next id and the ledger.</summary>
public sealed class CountHostBook
{
    public SortedDictionary<int, CountHost> Hosts { get; } = [];
    public int NextId = 1;
    public CountLedger Ledger { get; } = new();
    public bool IsEmpty => Hosts.Count == 0 && NextId == 1 && Ledger.IsEmpty;
}
