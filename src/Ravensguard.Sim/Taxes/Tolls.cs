using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Taxes;

/// <summary>Where a toll is taken (caravans spec 3.5, taxes spec 3.4).</summary>
public enum TollKind : byte
{
    /// <summary>5 percent of the seller's proceeds (money spec 3.3); the market books take it themselves, this is the one place the sum is written.</summary>
    Market,
    /// <summary>A realm or fief gate: so many mites a cart plus 5 permille of the cargo's value.</summary>
    Gate,
    /// <summary>A bridge: so many mites a cart (a ford costs nothing).</summary>
    Bridge,
}

/// <summary>
/// The public toll API (taxes spec 3.4). The caravan system calls <see cref="Charge(SimWorld, PurseId, PurseId, TollKind, int, long)"/> at a gate or bridge it passes; when
/// this class is absent a caller records the toll itself. A toll is a transfer from the payer's purse to the holder's treasury (reason <see cref="TransferReason.Toll"/>);
/// a payer who cannot pay the whole sum pays what it holds (the rest is not charged: tolls carry no arrears). Never at region borders (decision O5).
/// </summary>
public static class Tolls
{
    public static TaxPolicyDef PolicyOf(SimWorld w) => w.Get<EconomyWorld>()?.Taxes.Policy ?? new TaxPolicyDef { Id = "default" };

    /// <summary>Market toll on a sale: <c>proceeds x permille / 1000</c> rounded half up (G-T5: 7,776 mites at 50 permille is 389, the seller keeps 7,387).</summary>
    public static long MarketToll(long proceedsMites, int permille = 50) => (long)(((Int128)proceedsMites * permille + 500) / 1000);

    /// <summary>The toll a party owes: gate = carts x the cart toll + cargo value x the goods permille; bridge = carts x the bridge toll; market = the sale's value x the market permille.</summary>
    public static long Compute(TaxPolicyDef p, TollKind kind, int vehicles, long cargoValueMites, int marketPermille = 50) => kind switch
    {
        TollKind.Gate => (long)Math.Max(0, vehicles) * p.GateTollMitesPerCart + (long)(((Int128)Math.Max(0, cargoValueMites) * p.GateTollPermilleGoods + 500) / 1000),
        TollKind.Bridge => (long)Math.Max(0, vehicles) * p.BridgeTollMitesPerCart,
        _ => MarketToll(cargoValueMites, marketPermille),
    };

    /// <summary>The purse that holds a settlement's gates and bridges: its fief's treasury, else its town's, else its realm's (None when it has none).</summary>
    public static PurseId HolderPurse(SimWorld w, int settlement)
    {
        var t = w.Get<EconomyWorld>()?.Taxes.Treasuries; if (t == null || settlement < 0 || settlement >= t.PayeeSlot.Length) return PurseId.None;
        int slot = t.PayeeSlot[settlement]; if (slot < 0) slot = t.RealmSlotOfSettlement[settlement];
        return slot < 0 ? PurseId.None : t[slot].Purse;
    }

    /// <summary>Charges the toll to <paramref name="payer"/> and pays it to <paramref name="holder"/>. Returns the mites paid (0 for a free pass, a payer without coin, or no holder).</summary>
    public static long Charge(SimWorld w, PurseId payer, PurseId holder, TollKind kind, int vehicles, long cargoValueMites)
    {
        if (holder.IsNone || payer.IsNone || holder == payer) return 0;
        long due = Compute(PolicyOf(w), kind, vehicles, cargoValueMites, w.Economy.Money.MarketTollPermille);
        var led = w.Economy.Ledger; long pay = Math.Min(due, led.Balance(payer).Mites);
        if (pay <= 0) return 0;
        return led.Transfer(payer, holder, new Coin(pay), kind == TollKind.Market ? TransferReason.MarketToll : TransferReason.Toll) ? pay : 0;
    }

    /// <summary>The same, paid to whoever holds the gate or bridge at <paramref name="settlement"/> (see <see cref="HolderPurse"/>).</summary>
    public static long ChargeAt(SimWorld w, PurseId payer, int settlement, TollKind kind, int vehicles, long cargoValueMites)
        => Charge(w, payer, HolderPurse(w, settlement), kind, vehicles, cargoValueMites);
}
