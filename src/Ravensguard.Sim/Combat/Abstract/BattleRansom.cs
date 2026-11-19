using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// The coin of a battle's aftermath (abstract-battles-v1 4.1.5, 4.7; the v1 bug "prisoner ransom is paid from nowhere"): a ransom is one ledger transfer from the payer's treasury
/// to the captor, never more than the treasury holds (a payer that cannot pay pays nothing, and the captive stays a captive: morale-capture-succession 4.5); loot is the robbery's
/// own transfers (<see cref="Robbery"/>). Nothing is minted or sunk, so the ledger's audit stays exact (test AB-9).
/// </summary>
public static class BattleRansom
{
    /// <summary>Pays up to <paramref name="mites"/> from <paramref name="treasury"/> to <paramref name="captor"/>; returns what was paid.</summary>
    public static long Pay(Ledger ledger, PurseId treasury, PurseId captor, long mites)
    {
        if (mites <= 0 || treasury == PurseId.None || captor == PurseId.None || treasury == captor) return 0;
        long pay = Math.Min(mites, ledger.Balance(treasury).Mites);
        return pay > 0 && ledger.Transfer(treasury, captor, new Coin(pay), AftermathCoin.Ransom) ? pay : 0;
    }
}
