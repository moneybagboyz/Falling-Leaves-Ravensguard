using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Trade;

/// <summary>The order a squad member follows (spec 3.3): it overrides their schedule while it holds.</summary>
public enum SquadOrder : byte { Follow, Hold, GuardPlace, Work, Rest, Travel, Scatter }

/// <summary>Slave (appended, P6-E3): a slave of the hero taken into his party (overview 10a); no wage, never counts against the squad cap.</summary>
public enum FollowerOrigin : byte { Hired, Recruited, Slave }

public enum HireState : byte { Active, Ended, Quit, Dismissed }

/// <summary>A companion or hired hand (spec 3.3 <c>Follower</c> and <c>Hire</c> in one row): who, since when, on what wage, how loyal, the order they follow and what is owed.</summary>
public sealed class Follower
{
    public EntityId Person;
    public int PersonIndex;
    public string Name = "";
    public FollowerOrigin Origin;
    public HireState State;
    public int Loyalty;
    public int JoinedDay, EndDay;
    public long WageMitesPerDay;
    public HireTerms Terms;
    public int PaidThroughDay;
    public byte UnpaidDays;
    public SquadOrder Order;
    /// <summary>Where a Hold or Guard order holds (region millimetres), or the job of a Work order.</summary>
    public long OrderX, OrderY;
    public EntityId OrderJob;
    public int MaxSkill;
    public string Trade = "";
    /// <summary>The person's household purse when they joined (the wage goes there: the household sees the income).</summary>
    public EntityId Household;
    public bool Active => State == HireState.Active;
}

/// <summary>What happened to a follower at the daily pay (for the HUD and the chronicle).</summary>
public readonly record struct SquadEvent(EntityId Person, string Name, string Kind, long Mites);

/// <summary>
/// The hero's party (spec 3.3, 4.6): its members in join order, and the daily wage run at hour 0 (H4 slice): each active member is paid a day's wage from the hero's purse to their
/// household's purse through the ledger (reason Wage); a day not paid costs 100 loyalty and counts toward the three unpaid days after which they quit. A hire's term ends on its last day.
/// </summary>
public sealed class Squad
{
    public List<Follower> Members { get; } = [];
    public int LastPaidDay { get; set; } = int.MinValue;

    public IEnumerable<Follower> Active => Members.Where(m => m.Active);
    public int ActiveCount => Members.Count(m => m.Active);
    public Follower? Find(EntityId person) => Members.FirstOrDefault(m => m.Person == person && m.Active);

    /// <summary>Runs the wage day <paramref name="day"/> once (idempotent). Returns what happened, in member order.</summary>
    public List<SquadEvent> PayDay(TradeSettingsDef t, Ledger ledger, PurseId heroPurse, Func<Follower, PurseId?> purseOf, int day)
    {
        var ev = new List<SquadEvent>();
        if (day <= LastPaidDay) return ev;
        for (int d = LastPaidDay == int.MinValue ? day : LastPaidDay + 1; d <= day; d++)
        {
            foreach (var m in Members)
            {
                if (!m.Active || d <= m.JoinedDay || d <= m.PaidThroughDay) continue;
                if (m.Origin == FollowerOrigin.Hired && m.EndDay > 0 && d > m.EndDay) { m.State = HireState.Ended; ev.Add(new(m.Person, m.Name, "ended", 0)); continue; }
                bool paid = m.WageMitesPerDay <= 0;
                if (!paid && purseOf(m) is { } to && to != heroPurse)
                    paid = ledger.Transfer(heroPurse, to, Coin.FromMites(m.WageMitesPerDay), TransferReason.Wage);
                if (paid)
                {
                    m.PaidThroughDay = d; m.UnpaidDays = 0; m.Loyalty = Math.Min(1000, m.Loyalty + t.LoyaltyPaidDay);
                    ev.Add(new(m.Person, m.Name, "paid", m.WageMitesPerDay));
                }
                else
                {
                    m.UnpaidDays++; m.Loyalty = Math.Max(0, m.Loyalty + t.LoyaltyUnpaidDay);
                    ev.Add(new(m.Person, m.Name, "unpaid", m.WageMitesPerDay));
                    if (m.UnpaidDays >= t.UnpaidDaysToQuit || m.Loyalty <= 0) { m.State = HireState.Quit; ev.Add(new(m.Person, m.Name, "quit", 0)); }
                }
            }
        }
        LastPaidDay = day;
        Members.RemoveAll(m => !m.Active);                  // a member who quit or whose term ended leaves the party (T-14)
        return ev;
    }
}
