namespace Ravensguard.Sim.Economy;

/// <summary>What an exchange did: whether it went through, a text key when it did not, the gold pieces that moved and the fee paid.</summary>
public readonly record struct ExchangeResult(bool Ok, string Why, long Pieces, long Fee)
{
    public static ExchangeResult No(string why) => new(false, why, 0, 0);
}

/// <summary>
/// A money changer's exchange of one coin for another (P7-CN1, coinage 5.4). Value never changes: purses hold value, so changing silver for gold only moves gold <i>pieces</i> in the gold book (the changer's own, else the loose
/// pool) to the customer, and the customer pays the changer's fee (<c>changer_fee_permille</c>, 20 of the amount) as one logged <see cref="TransferReason.Exchange"/> transfer. Changing gold back hands pieces to the changer
/// (or loose) and pays the same fee on their value. If the changer and the pool hold no gold there is none to be had that day. Draws nothing.
/// </summary>
public static class MoneyChanger
{
    /// <summary>Silver to gold: the customer offers <paramref name="mites"/> (CN-9: 10,000 at 20 permille pays 200 and gets 9 gp, 800 mites staying silver).</summary>
    public static ExchangeResult ToGold(Ledger l, CoinStock st, PurseId customer, PurseId changer, long mites, int feePermille = 20)
    {
        if (mites <= 0 || customer == changer) return ExchangeResult.No("co_bad_amount");
        long bal = l.Balance(customer).Mites;
        if (bal < mites) return ExchangeResult.No("co_cannot_afford");
        long fee = mites * feePermille / 1000, want = (mites - fee) / Coin.MitesPerGold;
        if (want <= 0) return ExchangeResult.No("co_too_small");
        st.OptIn(customer); st.OptIn(changer);
        long avail = st.Pieces(changer) + st.Loose;
        if (avail <= 0) return ExchangeResult.No("co_no_gold");
        long room = Math.Max(0, (bal - fee) / Coin.MitesPerGold - st.Pieces(customer));
        long pieces = Math.Min(want, Math.Min(avail, room));
        if (pieces <= 0) return ExchangeResult.No("co_cannot_afford");
        if (pieces < want) fee = fee * pieces / want;                                                  // a part of the order is filled, a part of the fee is charged
        if (fee > 0 && !l.Transfer(customer, changer, new Coin(fee), TransferReason.Exchange)) return ExchangeResult.No("co_cannot_afford");
        long got = st.TakeForExchange(changer, pieces);
        st.GiveForExchange(l, customer, got);
        return new ExchangeResult(true, "", got, fee);
    }

    /// <summary>Gold to silver: the customer hands over <paramref name="pieces"/> gold pieces and pays the fee on their value; the pieces go to the changer or the pool.</summary>
    public static ExchangeResult ToSilver(Ledger l, CoinStock st, PurseId customer, PurseId changer, long pieces, int feePermille = 20)
    {
        if (pieces <= 0 || customer == changer) return ExchangeResult.No("co_bad_amount");
        if (st.Pieces(customer) < pieces) return ExchangeResult.No("co_no_gold");
        long fee = pieces * Coin.MitesPerGold * feePermille / 1000;
        if (l.Balance(customer).Mites < fee) return ExchangeResult.No("co_cannot_afford");
        st.OptIn(changer);
        st.TakeFromPurse(customer, pieces);
        if (fee > 0 && !l.Transfer(customer, changer, new Coin(fee), TransferReason.Exchange))
        {
            st.GiveForExchange(l, customer, pieces);
            return ExchangeResult.No("co_cannot_afford");
        }
        st.GiveForExchange(l, changer, pieces);
        return new ExchangeResult(true, "", pieces, fee);
    }
}
