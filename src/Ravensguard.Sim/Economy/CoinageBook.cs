namespace Ravensguard.Sim.Economy;

/// <summary>Metal a seller handed over at a mint's counter and that waits to be struck (coinage 5.1): milligrams of one metal, and the purse that is paid when it is struck (-1: none, the mint's own dust).</summary>
public sealed class MintDelivery
{
    public int Realm;
    public MintMetal Metal;
    public long Mg;
    public int Seller = -1;
}

/// <summary>What a mint struck this season (coinage 8): pieces by metal, the seigniorage it kept, and whether the copper cap turned metal away. Written to the chronicle at the next season's first day, then cleared.</summary>
public sealed class MintSeasonRow
{
    public int Realm;
    public long Gold, Silver, Copper, Seigniorage, CopperCapped;
}

/// <summary>
/// The coinage of the home region (P7-CN1): the gold book and copper count (<see cref="Stock"/>), the metal waiting at each mint (<see cref="Queue"/>, in the order handed over), the pieces each mint struck today (its
/// moneyers' day) and this season. The mints themselves are derived from the region, never saved. The optional "coinage" section holds it, absent while all of it is empty (coinage 6).
/// </summary>
public sealed class CoinageBook
{
    public CoinStock Stock { get; set; } = new();
    public List<MintDelivery> Queue { get; } = [];
    /// <summary>The day the <see cref="Used"/> counts belong to, and the pieces each realm's mint has struck that day (its moneyers' capacity).</summary>
    public int UsedDay { get; set; } = -1;
    public SortedDictionary<int, long> Used { get; } = [];
    /// <summary>The season number the rows of <see cref="Season"/> belong to, and the rows (ascending realm).</summary>
    public int SeasonNo { get; set; } = -1;
    public List<MintSeasonRow> Season { get; } = [];

    public bool IsEmpty => Stock.IsEmpty && Queue.Count == 0 && Season.Count == 0 && Used.Count == 0;

    /// <summary>The <c>CoinHash</c> of the Phase 7 trail (coinage 6): the stock, the gold book and the queues, in order. Joins the trail once the book is non-empty; empty text while it is empty.</summary>
    public string Hash()
    {
        if (IsEmpty) return "";
        var s = Stock;
        var text = string.Join("|", s.GoldStruck, s.GoldMelted, s.CopperStruck, s.CopperMelted, s.Loose, s.GoldMeltDustMg, s.SilverMeltDustMg, s.CopperMeltDustMg,
            string.Join(",", s.Opted.Select(kv => kv.Key + ":" + kv.Value)), string.Join(",", s.Extra),
            string.Join(",", Queue.Select(d => $"{d.Realm}:{(int)d.Metal}:{d.Mg}:{d.Seller}")), string.Join(",", Season.Select(r => $"{r.Realm}:{r.Gold}:{r.Silver}:{r.Copper}:{r.Seigniorage}:{r.CopperCapped}")));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }

    public MintSeasonRow SeasonOf(int realm)
    {
        foreach (var r in Season) if (r.Realm == realm) return r;
        var n = new MintSeasonRow { Realm = realm };
        int i = 0; while (i < Season.Count && Season[i].Realm < realm) i++;
        Season.Insert(i, n);
        return n;
    }

    /// <summary>The metal waiting at a mint, in mg, for one metal (the queue's rows plus the mint's dust).</summary>
    public long WaitingMg(int realm, MintMetal m) { long s = 0; foreach (var d in Queue) if (d.Realm == realm && d.Metal == m) s += d.Mg; return s; }
}
