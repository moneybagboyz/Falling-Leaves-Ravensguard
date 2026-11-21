using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy.Markets;

namespace Ravensguard.Sim.Caravans;

/// <summary>
/// The works' own caravans (P7-IN4, industries 5.10): a caravan whose owner is a stage of a works, not a merchant. It is an ordinary Phase 2 caravan (the same road, speed, tolls, encounters and ambush fights, the same hourly step); only what is said at the ends
/// of a trip is the works': who is paid what each day, what the destination buys, what comes home and how it is booked. <see cref="ContinentTrade"/> asks the hook whether it owns a caravan and, if so, hands those moments to it.
/// </summary>
public interface IOwnCaravans
{
    /// <summary>Whether the works owns the caravan (it has a trip on the works' book).</summary>
    bool Owns(Caravan c);
    /// <summary>The day's pay at hour 0: the master, the drivers and guards, and the draught animals' fodder, from the caravan's purse.</summary>
    void PayWages(Caravan c, GameTime now);
    /// <summary>The outbound leg is done: sell at the border gate (<see cref="ContinentTrade.GateTradeAt"/>) or at the town's book, and turn for home.</summary>
    void Arrive(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time);
    /// <summary>Home again: unsold goods back to the works' store, the master's share, the wear, the rest of the purse to the works' cash box, the trip written to the chronicle.</summary>
    void Home(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time);
    /// <summary>The caravan was lost on the road: the trip is written off.</summary>
    void Lost(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time);
}
