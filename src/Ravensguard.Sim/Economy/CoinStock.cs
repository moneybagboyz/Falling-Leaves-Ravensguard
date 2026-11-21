namespace Ravensguard.Sim.Economy;

/// <summary>
/// The world's coin by kind (P7-CN1, coinage 5.2): the gold book and the copper count. Purses hold <b>value</b> (the <see cref="Ledger"/>, unchanged); gold is tracked as pieces only where it matters, for the opted-in purses
/// (treasuries, chests, hoards, caravans by kind, and the purses a part of the game adds with <see cref="OptIn"/>: the hero's, lords' households, a money changer's). A purse with <c>g</c> pieces always holds at least
/// <c>g x 1,000</c> mites. <b>Loose</b> gold is gold in circulation in purses that do not track it. The world invariant, exact at every moment, is <c>sum(opted) + loose = struck - melted</c>.
/// <para>Copper is token coin and is only counted (<see cref="CopperPieces"/>, for the 4% cap), never tracked per purse. The ledger calls <see cref="AfterTransfer"/> and <see cref="AfterSink"/>; nothing here moves value.
/// Draws nothing. Not saved by itself: <see cref="Ravensguard.Sim.Save.CoinageSave"/> carries it in the optional "coinage" section.</para>
/// </summary>
public sealed class CoinStock
{
    /// <summary>A payment of at least this many mites carries gold pieces with it (content <c>gold_transfer_min_mites</c>, 10 gp).</summary>
    public long TransferMinMites { get; set; } = 10_000;

    public long GoldStruck, GoldMelted, CopperStruck, CopperMelted, Loose;
    /// <summary>Metal left over when coin is melted (mg short of a whole gram), so no metal is lost to the gram unit of the stores.</summary>
    public long GoldMeltDustMg, SilverMeltDustMg, CopperMeltDustMg;
    /// <summary>Gold pieces by purse index, for the opted-in purses holding any (ascending).</summary>
    public SortedDictionary<int, long> Opted { get; } = [];
    /// <summary>Purses opted in beyond the kinds that always are (the hero's, a lord's household, a money changer's).</summary>
    public SortedSet<int> Extra { get; } = [];

    public long CopperPieces => CopperStruck - CopperMelted;
    /// <summary>The copper coin's face in mites (10 each): what the cap is measured on.</summary>
    public long CopperFace => CopperPieces * Coin.MitesPerCopper;
    public long OptedTotal { get { long s = 0; foreach (var v in Opted.Values) s += v; return s; } }
    /// <summary>Gold in the world: struck less melted.</summary>
    public long GoldInWorld => GoldStruck - GoldMelted;
    public bool Conserved => OptedTotal + Loose == GoldInWorld;
    public bool IsEmpty => GoldStruck == 0 && GoldMelted == 0 && CopperStruck == 0 && CopperMelted == 0 && Loose == 0 && Opted.Count == 0 && Extra.Count == 0
        && GoldMeltDustMg == 0 && SilverMeltDustMg == 0 && CopperMeltDustMg == 0;

    /// <summary>Whether a purse tracks its gold: its kind is a treasury, chest, hoard or caravan, or it was opted in.</summary>
    public bool IsOpted(Ledger l, PurseId p)
    {
        var k = l.KindOf(p);
        return k is PurseKind.Treasury or PurseKind.Chest or PurseKind.Hoard or PurseKind.Caravan || Extra.Contains(p.Index);
    }

    public void OptIn(PurseId p) { if (!p.IsNone) Extra.Add(p.Index); }

    /// <summary>Gold pieces a purse holds (0 for a purse that does not track gold).</summary>
    public long Pieces(PurseId p) => p.IsNone ? 0 : Opted.GetValueOrDefault(p.Index);

    private void Set(int purse, long pieces) { if (pieces > 0) Opted[purse] = pieces; else Opted.Remove(purse); }

    /// <summary>Gold pieces given to a purse: an opted-in one takes as many as its balance allows, the rest of them are loose.</summary>
    private void Give(Ledger l, PurseId to, long n)
    {
        if (n <= 0) return;
        if (IsOpted(l, to))
        {
            long cur = Pieces(to), room = Math.Max(0, l.Balance(to).Mites / Coin.MitesPerGold - cur);
            long take = Math.Min(n, room);
            Set(to.Index, cur + take); n -= take;
        }
        Loose += n;
    }

    /// <summary>
    /// After a transfer (5.2): a payment of at least <see cref="TransferMinMites"/> from an opted-in payer moves <c>min(pieces, amount / 1,000)</c> gold pieces with it; then, if the payer's balance no longer covers its
    /// pieces (making change), the excess pieces go to the payee or to loose. No value moves here, only the count.
    /// </summary>
    internal void AfterTransfer(Ledger l, PurseId from, PurseId to, long amount)
    {
        if (Opted.Count == 0 || !Opted.TryGetValue(from.Index, out long g) || g <= 0) return;
        if (amount >= TransferMinMites)
        {
            long mv = Math.Min(g, amount / Coin.MitesPerGold);
            g -= mv; Set(from.Index, g); Give(l, to, mv);
        }
        Clamp(l, from, to);
    }

    /// <summary>After coin leaves the world from a purse (a melt, a border crossing): pieces its balance no longer covers become loose.</summary>
    internal void AfterSink(Ledger l, PurseId from)
    {
        if (!Opted.ContainsKey(from.Index)) return;
        Clamp(l, from, PurseId.None);
    }

    private void Clamp(Ledger l, PurseId from, PurseId to)
    {
        long g = Pieces(from), cap = l.Balance(from).Mites / Coin.MitesPerGold;
        if (g <= cap) return;
        Set(from.Index, cap);
        if (to.IsNone) Loose += g - cap; else Give(l, to, g - cap);
    }

    /// <summary>Gold struck into a purse that already holds the value (the mint minted it there): the pieces enter the book, held by the purse when it tracks gold, else loose.</summary>
    public void AddStruck(Ledger l, PurseId into, long pieces)
    {
        if (pieces <= 0) return;
        GoldStruck += pieces;
        if (!IsOpted(l, into)) { Loose += pieces; return; }
        Set(into.Index, Pieces(into) + pieces);
        if (Pieces(into) * Coin.MitesPerGold > l.Balance(into).Mites) throw new InvalidOperationException("the struck gold is not covered by the purse");
    }

    /// <summary>
    /// Melts gold pieces of an opted-in purse into metal (<see cref="SinkReason.Melt"/>, 5.5): <c>pieces x 1,000</c> mites leave the money supply; the metal (1,100 mg a piece) is credited to <paramref name="store"/>
    /// as <paramref name="goldGood"/> in whole grams, the rest carried in <see cref="GoldMeltDustMg"/>. False (nothing changes) when the purse holds fewer pieces.
    /// </summary>
    public bool MeltGold(Ledger l, PurseId from, long pieces, Goods.Store store, ushort goldGood, int pieceMg = MintMath.GoldPieceMg)
    {
        if (pieces <= 0 || Pieces(from) < pieces || l.Balance(from).Mites < pieces * Coin.MitesPerGold) return false;
        Set(from.Index, Pieces(from) - pieces); GoldMelted += pieces;
        if (!l.Sink(from, Coin.FromGold(pieces), SinkReason.Melt)) { Set(from.Index, Pieces(from) + pieces); GoldMelted -= pieces; return false; }
        long mg = pieces * pieceMg + GoldMeltDustMg;
        long grams = mg / 1000; GoldMeltDustMg = mg % 1000;
        if (grams > 0) store.Create(goldGood, grams, Goods.CreateReason.Dematerialised);
        return true;
    }

    /// <summary>Melts silver pieces (100 mites, 1,300 mg each) of any purse into metal; the value leaves the money supply (<see cref="SinkReason.Melt"/>). False when the purse holds less.</summary>
    public bool MeltSilver(Ledger l, PurseId from, long pieces, Goods.Store store, ushort silverGood, int pieceMg = MintMath.CrownSilverMg)
    {
        if (pieces <= 0 || !l.Sink(from, Coin.FromSilver(pieces), SinkReason.Melt)) return false;
        long mg = pieces * pieceMg + SilverMeltDustMg;
        long grams = mg / 1000; SilverMeltDustMg = mg % 1000;
        if (grams > 0) store.Create(silverGood, grams, Goods.CreateReason.Dematerialised);
        return true;
    }

    /// <summary>Melts copper pieces (10 mites, 5,000 mg each) into metal: nobody does it unless copper is dear (a piece is worth far less than its face). False when the world has fewer copper pieces or the purse holds less.</summary>
    public bool MeltCopper(Ledger l, PurseId from, long pieces, Goods.Store store, ushort copperGood, int pieceMg = MintMath.CopperPieceMg)
    {
        if (pieces <= 0 || pieces > CopperPieces || !l.Sink(from, Coin.FromCopper(pieces), SinkReason.Melt)) return false;
        CopperMelted += pieces;
        long mg = pieces * pieceMg + CopperMeltDustMg;
        long grams = mg / 1000; CopperMeltDustMg = mg % 1000;
        if (grams > 0) store.Create(copperGood, grams, Goods.CreateReason.Dematerialised);
        return true;
    }

    /// <summary>Takes up to <paramref name="n"/> gold pieces for a customer: the changer's own first, then the loose pool. Returns how many were taken.</summary>
    internal long TakeForExchange(PurseId changer, long n)
    {
        long own = Math.Min(n, Pieces(changer)); if (own > 0) Set(changer.Index, Pieces(changer) - own);
        long loose = Math.Min(n - own, Loose); Loose -= loose;
        return own + loose;
    }

    /// <summary>Removes pieces from a purse's count (the caller puts them somewhere at once).</summary>
    internal void TakeFromPurse(PurseId from, long n) => Set(from.Index, Pieces(from) - n);

    /// <summary>Gold pieces handed to a purse by an exchange: it holds them if it tracks gold and its balance covers them, else they are loose.</summary>
    internal void GiveForExchange(Ledger l, PurseId to, long n) => Give(l, to, n);

    /// <summary>The audit: the pieces held are covered by the purses and the world's gold adds up. Returns the first failure, or "".</summary>
    public string Check(Ledger l)
    {
        if (!Conserved) return $"gold: opted {OptedTotal} + loose {Loose} != struck {GoldStruck} - melted {GoldMelted}";
        foreach (var (p, g) in Opted)
            if (g <= 0 || g * Coin.MitesPerGold > l.Balance(new PurseId(p)).Mites) return $"purse {p} holds {g} gold pieces but only {l.Balance(new PurseId(p)).Mites} mites";
        if (CopperStruck < CopperMelted || GoldMelted > GoldStruck || Loose < 0) return "negative coin count";
        return "";
    }
}
