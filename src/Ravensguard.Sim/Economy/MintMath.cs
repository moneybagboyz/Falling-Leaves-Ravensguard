namespace Ravensguard.Sim.Economy;

/// <summary>The result of striking silver (money spec 3.7): whole crowns only, a seigniorage cut for the faction, the rest for the seller, a silver remainder kept as dust.</summary>
public readonly record struct StrikeResult(Coin Struck, Coin Seigniorage, Coin ToSeller, long DustMg);

/// <summary>Pure mint arithmetic. The ledger call that creates the coin is <see cref="Ledger.Mint"/> with <see cref="SourceReason.Mint"/>.</summary>
public static class MintMath
{
    public const int CrownSilverMg = 1300;

    /// <summary><c>struck = floor(silver_mg / 1300) x 100</c> mites; <c>seigniorage = floor(struck x bp / 10000)</c>; the seller gets the rest.</summary>
    public static StrikeResult Strike(long silverMg, int seigniorageBp, int crownSilverMg = CrownSilverMg)
    {
        if (silverMg < 0 || seigniorageBp is < 0 or > 10000 || crownSilverMg <= 0) throw new ArgumentOutOfRangeException(nameof(silverMg));
        long crowns = silverMg / crownSilverMg;
        var struck = Coin.FromCrowns(crowns);
        var seign = new Coin(struck.Mites * seigniorageBp / 10000);
        return new StrikeResult(struck, seign, struck - seign, silverMg - crowns * crownSilverMg);
    }

    /// <summary>
    /// Strikes silver into a ledger: the coin is created once (all of it into the seller's purse, reason Mint), then the seigniorage moves to the faction
    /// treasury as a logged Seigniorage transfer. Returns the result.
    /// </summary>
    public static StrikeResult StrikeInto(Ledger ledger, PurseId seller, PurseId treasury, long silverMg, int seigniorageBp, int crownSilverMg = CrownSilverMg)
    {
        var r = Strike(silverMg, seigniorageBp, crownSilverMg);
        if (r.Struck.Mites > 0) ledger.Mint(seller, r.Struck, SourceReason.Mint);
        if (r.Seigniorage.Mites > 0 && seller != treasury) ledger.Transfer(seller, treasury, r.Seigniorage, TransferReason.Seigniorage);
        return r;
    }
}
