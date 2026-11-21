namespace Ravensguard.Sim.Economy;

/// <summary>The result of striking silver (money spec 3.7): whole crowns only, a seigniorage cut for the faction, the rest for the seller, a silver remainder kept as dust.</summary>
public readonly record struct StrikeResult(Coin Struck, Coin Seigniorage, Coin ToSeller, long DustMg);

/// <summary>P7-CN1 (coinage 3): the three metals a mint strikes. The stored values are bytes of the coinage section: append only.</summary>
public enum MintMetal : byte { Gold, Silver, Copper }

/// <summary>What a copper strike did (coinage 5.1): the pieces struck, their face, what the mint pays the seller for the metal it took, and what it handed back (the cap, or less than a piece).</summary>
public readonly record struct CopperStrike(long Pieces, Coin Face, Coin ToSeller, long UsedMg, long HandedBackMg);

/// <summary>Pure mint arithmetic. The ledger call that creates the coin is <see cref="Ledger.Mint"/> with <see cref="SourceReason.Mint"/>.</summary>
public static class MintMath
{
    public const int CrownSilverMg = 1300;
    public const int GoldPieceMg = 1100, CopperPieceMg = 5000, GoldSeigniorageBp = 500, CopperCapPermille = 40;

    /// <summary>Metal in one piece, in mg: 1,100 of gold, 1,300 of silver, 5,000 of copper (the content's numbers override these defaults).</summary>
    public static int PieceMg(MintMetal m, int gold = GoldPieceMg, int silver = CrownSilverMg, int copper = CopperPieceMg) => m switch { MintMetal.Gold => gold, MintMetal.Silver => silver, _ => copper };
    /// <summary>The face value of one piece in mites: 1,000, 100 or 10.</summary>
    public static long PieceMites(MintMetal m) => m switch { MintMetal.Gold => Coin.MitesPerGold, MintMetal.Silver => Coin.MitesPerSilver, _ => Coin.MitesPerCopper };

    /// <summary>Gold: <c>pieces = floor(mg / 1100)</c>, <c>struck = pieces x 1000</c>, seigniorage at the basis points (500), the rest to the seller, the dust kept (CN-4).</summary>
    public static StrikeResult StrikeGold(long goldMg, int seigniorageBp = GoldSeigniorageBp, int pieceMg = GoldPieceMg)
    {
        if (goldMg < 0 || seigniorageBp is < 0 or > 10000 || pieceMg <= 0) throw new ArgumentOutOfRangeException(nameof(goldMg));
        long pieces = goldMg / pieceMg;
        var struck = Coin.FromGold(pieces);
        var seign = new Coin(struck.Mites * seigniorageBp / 10000);
        return new StrikeResult(struck, seign, struck - seign, goldMg - pieces * pieceMg);
    }

    /// <summary>
    /// Copper is token coin (coinage 5.1, CN-5 and CN-6): the mint pays the seller the bid for the metal it takes and strikes the pieces at face (10 mites); it strikes only while the region's copper face stays within
    /// <paramref name="capPermille"/> of the money supply, and hands back the metal it could not use. <paramref name="bidMitesPerKg"/> is the book's bid for copper.
    /// </summary>
    public static CopperStrike StrikeCopper(long copperMg, long bidMitesPerKg, long supplyMites, long copperFaceMites, int capPermille = CopperCapPermille, int pieceMg = CopperPieceMg)
    {
        if (copperMg < 0 || bidMitesPerKg < 0 || capPermille is < 0 or > 1000 || pieceMg <= 0) throw new ArgumentOutOfRangeException(nameof(copperMg));
        long room = Math.Max(0, supplyMites / 1000 * capPermille + supplyMites % 1000 * capPermille / 1000 - copperFaceMites);
        long pieces = Math.Min(copperMg / pieceMg, room / Coin.MitesPerCopper);
        long used = pieces * pieceMg;
        long pay = (long)((Int128)bidMitesPerKg * used / 1_000_000);
        return new CopperStrike(pieces, Coin.FromCopper(pieces), new Coin(pay), used, copperMg - used);
    }

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
