using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>A purse named by its owner (the ledger's key), so a captive's payer survives a save without a purse index.</summary>
public readonly record struct PurseRef(PurseKind Kind, EntityId Owner)
{
    public PurseId? Find(Ledger l) => l.TryFind(Kind, Owner, out var p) ? p : null;
}

/// <summary>
/// Ransom (morale-capture-succession 4.6): the amount by rank and wealth, a wanderer's by purse and property, the renegotiation of an unpaid ransom (halved after 5 days, quartered after 10),
/// and the payment, which is only ever a <see cref="Ledger.Transfer"/> from the payer's purse (a faction treasury, the household, or friends giving up to 30% each) to the captor's: no coin
/// is created (plan 4A.3, MC-9).
/// </summary>
public static class Ransom
{
    public static long RankBase(AftermathRules r, CaptiveRank rank) => rank switch
    {
        CaptiveRank.Levy => r.D.RankLevy, CaptiveRank.ManAtArms => r.D.RankManAtArms, CaptiveRank.Knight => r.D.RankKnight, CaptiveRank.Baron => r.D.RankBaron, _ => r.D.WandererFloor,
    };

    /// <summary><c>rank_base x (1000 + wealth_pm) / 1000</c>.</summary>
    public static long ForRank(AftermathRules r, CaptiveRank rank, int wealthPm = 0) => RankBase(r, rank) * (1000 + Math.Max(-900, wealthPm)) / 1000;

    /// <summary>A wanderer hero with no faction: <c>max(1,500, 2 x purse + 0.1 x property value)</c> (C3: poverty is not a free pass, wealth is the risk).</summary>
    public static long ForWanderer(AftermathRules r, long purseMites, long propertyMites = 0) => Math.Max(r.D.WandererFloor, 2 * Math.Max(0, purseMites) + Math.Max(0, propertyMites) / 10);

    /// <summary>What the captors ask on <paramref name="day"/>: the full ransom, half once it has gone 5 days unpaid past the due day, a quarter after 10.</summary>
    public static long Current(AftermathRules r, long baseMites, int dueDay, int day)
    {
        int d = day - dueDay;
        return d >= r.D.QuarterAfterDays ? baseMites / 4 : d >= r.D.HalveAfterDays ? baseMites / 2 : baseMites;
    }

    /// <summary>What the payer could pay now (the bandits' "ransom estimate" of 4.4): the faction's or household's balance, or friends' 30% shares (each purse once).</summary>
    public static long Payable(AftermathRules r, Ledger l, PayerKind kind, IReadOnlyList<PurseRef> payers)
    {
        if (kind != PayerKind.Friends)
            return payers.Count > 0 && payers[0].Find(l) is { } id ? l.Balance(id).Mites : 0;
        long sum = 0;
        foreach (var (_, share) in FriendShares(r, l, payers, null, long.MaxValue)) sum += share;
        return sum;
    }

    /// <summary>
    /// The friends' shares towards <paramref name="amount"/>: each distinct purse once (two companions of one household are one purse; review finding 4), in order, up to 30% of its balance,
    /// never the captor's own.
    /// </summary>
    private static List<(PurseId Purse, long Share)> FriendShares(AftermathRules r, Ledger l, IReadOnlyList<PurseRef> payers, PurseId? captor, long amount)
    {
        var shares = new List<(PurseId, long)>();
        var seen = new HashSet<PurseId>();
        long left = amount;
        foreach (var pr in payers)
        {
            if (left <= 0) break;
            if (pr.Find(l) is not { } p || p == captor || !seen.Add(p)) continue;
            long share = Math.Min(left, l.Balance(p).Mites * r.D.FriendSharePm / 1000);
            if (share <= 0) continue;
            shares.Add((p, share)); left -= share;
        }
        return shares;
    }

    /// <summary>
    /// Pays <paramref name="amount"/> to <paramref name="captor"/> if the payer can: a faction or household from its one purse; friends each up to 30% of their purse, in order, only when together
    /// they reach it (no part payments). True when paid. Nothing is minted: every mite is a ledger transfer.
    /// </summary>
    public static bool TryPay(AftermathRules r, Ledger l, PayerKind kind, IReadOnlyList<PurseRef> payers, PurseId captor, long amount)
    {
        if (amount <= 0) return true;
        if (kind == PayerKind.None || payers.Count == 0) return false;
        if (kind != PayerKind.Friends)
        {
            if (payers[0].Find(l) is not { } p || p == captor || l.Balance(p).Mites < amount) return false;
            return l.Transfer(p, captor, Coin.FromMites(amount), AftermathCoin.Ransom);
        }
        // the shares are planned first and paid only when together they reach the ransom: a shortfall moves nothing (review finding 4: a purse named twice was counted twice, so the
        // friends paid part of it every day and the captive stayed)
        var shares = FriendShares(r, l, payers, captor, amount);
        long total = 0;
        foreach (var (_, share) in shares) total += share;
        if (total < amount) return false;
        long paid = 0;
        foreach (var (p, share) in shares) if (l.Transfer(p, captor, Coin.FromMites(share), AftermathCoin.Ransom)) paid += share;
        return paid >= amount;
    }
}

/// <summary>
/// The ways out and their odds (4.7), in permille, and their rolls. Bonds: <c>Athletics + Stealth</c> (their mean) against difficulty 40; a lock: Lockpicking against 30 (a hideout) or 50 (an army
/// cage), an improvised pick 10 points worse; each point of skill over the difficulty is 1%, from 50% at par (5% to 95%). Guard notice per attempt: <c>25% x (1 - stealth / 150) x dark (0.5)</c>.
/// A transfer's night window: <c>15% + stealth / 5</c>. A rescue party abstractly: 5% a day per member.
/// </summary>
public static class EscapeTable
{
    public static int SkillChancePm(int skill, int difficulty) => Math.Clamp(500 + (skill - difficulty) * 10, 50, 950);
    public static int BondsPm(AftermathRules r, int athletics, int stealth) => SkillChancePm((athletics + stealth) / 2, r.D.BondsDifficulty);
    public static int LockDifficulty(AftermathRules r, CaptorKind captor, bool improvised) => (captor == CaptorKind.Army ? r.D.ArmyCageLock : r.D.HideoutLock) + (improvised ? r.D.ImprovisedPickPenalty : 0);
    public static int LockPm(AftermathRules r, int lockpicking, CaptorKind captor, bool improvised) => SkillChancePm(lockpicking, LockDifficulty(r, captor, improvised));
    public static int NoticePm(AftermathRules r, int stealth, bool dark) =>
        (int)((long)r.D.NoticeBasePm * Math.Max(0, 150 - Math.Clamp(stealth, 0, 150)) / 150 * (dark ? r.D.DarkNoticePm : 1000) / 1000);
    public static int TransferEscapePm(AftermathRules r, int stealth) => Math.Min(950, r.D.TransferEscapeBasePm + Math.Clamp(stealth, 0, 100) * 2);
    public static int RescueDailyPm(AftermathRules r, int members) => Math.Min(950, r.D.RescueDailyPerMemberPm * Math.Max(0, members));
    /// <summary>Finding something to pick a lock with (a nail worked loose, a bent buckle): 20% + 1% per point of perception, an hour's search.</summary>
    public static int SearchPm(int perception) => Math.Clamp(200 + perception * 10, 50, 950);
    /// <summary>A ransom offered to a guard (Speech check): 30% + 1% per point of Speech.</summary>
    public static int OfferPm(int speech) => Math.Clamp(300 + speech * 10, 50, 950);

    /// <summary>Dark from 20:00 to 06:00 (darkness hides you, Phase 3 sight rules).</summary>
    public static bool Dark(long sec) { long h = sec / 3600 % 24; return h >= 20 || h < 6; }

    /// <summary>Guards awake now: a bandit hideout keeps one awake at night and all by day; an army camp keeps its two per tent.</summary>
    public static int GuardsAwake(CaptorKind captor, int guards, long sec) => captor == CaptorKind.Army ? guards : Dark(sec) ? Math.Min(1, guards) : guards;

    /// <summary>A counter roll on a game-time key (the Phase 3 rule: GameSec / 20 windows), 0..999.</summary>
    public static int Roll(ulong seed, long key, EntityId who, uint stream, uint draw) => DetRng.Range(seed, key, who.Value, stream, draw, 0, 1000);
}
