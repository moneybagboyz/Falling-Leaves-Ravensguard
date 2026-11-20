using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Contracts;

/// <summary>One contract as the board and the Journal show it (a copy made at a barrier: the screens read, never write).</summary>
public sealed record ContractRow(int Id, string Kind, long PayMites, int DeadlineDay, string IssuerKind, string WhyKey, long WhyArg, WorkState State, bool Mine, bool Pinned, bool Criminal, int Settlement, string PlaceName,
    long PlaceXM, long PlaceYM, long Delivered, long Grams, long BondMites, bool FromJob, IReadOnlyList<(string Type, int Day)> Evidence, ulong PostedEntry = 0);

/// <summary>A tournament as the fair's board shows it.</summary>
public sealed record TournamentRow(int Id, string Place, long PurseMites, long FeeMites, int FairDay, int Entrants, bool Entered, bool Drawn, bool Paid, bool Cancelled, bool HeroWon);

/// <summary>The contract board of a place and the hero's own contracts (contracts-and-rumours-v2 6): notice-board rows with pay, deadline, issuer and why; what the hero has taken with the evidence of how it went; the fair's tournament.</summary>
public sealed class ContractsView
{
    public int Day, Settlement;
    public string PlaceName = "";
    public List<ContractRow> Board = [], Mine = [];
    public List<TournamentRow> Fairs = [];
    public long CoinMites;

    /// <summary>
    /// Builds the view: the open contracts of the settlement the hero stands in (criminal ones only when the poster trusts him, 4.1), the Phase 3 bounty jobs of the place as bounty rows (their escrow stays in the job
    /// until the culprit is brought in, <c>Bounties.Collect</c>), and the contracts the hero has taken or closed in the last month. Rows are in contract id order.
    /// </summary>
    public static ContractsView Build(WorldState st, ContentDatabase? db, int settlement, int day, EntityId hero, ContractWorld? w = null)
    {
        var content = ContractContent.From(db); var pop = st.Population; var region = pop.Region;
        var v = new ContractsView { Day = day, Settlement = settlement, PlaceName = settlement >= 0 && settlement < region.Settlements.Count ? region.Settlements[settlement].Name : "" };
        if (st.Economy?.Ledger is { } led && led.TryFind(Ravensguard.Sim.Economy.PurseKind.Person, Ravensguard.Sim.Economy.EconomyState.HeroOwner, out var hp)) v.CoinMites = led.Balance(hp).Mites;
        var book = st.Contracts;
        string PlaceOf(int s) => s >= 0 && s < region.Settlements.Count ? region.Settlements[s].Name : "";
        (long, long) XY(int s) => s >= 0 && s < region.Settlements.Count ? (region.Settlements[s].CenterX * 1000L, region.Settlements[s].CenterY * 1000L) : (0, 0);
        IReadOnlyList<(string, int)> Ev(WorkContract c) => [.. c.Evidence.Select(e => st.Chronicle.Get(new EntityId(e))).Where(e => e != null).Select(e => (e!.TypeId, (int)e.When.TotalDays))];
        if (book != null)
            foreach (var c in book.Contracts.Values)
            {
                var k = content.Kind(c.Kind); bool mine = c.State == WorkState.Taken && c.Taker == hero || c.Closed && c.Taker == hero && c.ClosedDay >= 0 && day - c.ClosedDay <= 30 && c.TakenDay >= 0;
                bool onBoard = c.Open && c.Settlement == settlement && (k is not { Crime: true } || (w?.TrustPm(c.Issuer, hero) ?? 0) >= 300);
                if (!mine && !onBoard) continue;
                var (x, y) = XY(c.Settlement);
                var row = new ContractRow(c.Id, c.Kind, c.PayMites, c.DeadlineDay, k?.Issuer ?? "person", "ct_why_" + (k?.Why ?? "other"), c.Param, c.State, mine, c.Pinned, k is { Crime: true }, c.Settlement, PlaceOf(c.Settlement), x, y, c.Delivered, c.Grams, c.BondMites, false, Ev(c), c.PostedEntry);
                (mine ? v.Mine : v.Board).Add(row);
            }
        if (st.Jobs != null)
            foreach (var j in st.Jobs.Jobs.Values.Where(j => j.Kind == OddJobKind.Bounty && j.State == JobState.Open && j.Settlement == settlement).OrderBy(j => j.Id))
            {
                var (x, y) = XY(j.Settlement);
                v.Board.Add(new ContractRow(-(int)(j.Id.Value & 0x7FFFFFFF), "bounty_dead_or_alive", j.WageMites, j.DeadlineSec >= long.MaxValue / 8 ? -1 : (int)(j.DeadlineSec / 86_400), "fief", "ct_why_crime", 0, WorkState.Open, false, false, false, j.Settlement, PlaceOf(j.Settlement), x, y, 0, 0, 0, true, []));
            }
        if (book != null)
            foreach (var t in book.Tournaments.Values.Where(t => t.Settlement == settlement && !t.Cancelled).OrderBy(t => t.Id))
                v.Fairs.Add(new TournamentRow(t.Id, PlaceOf(t.Settlement), t.PurseMites, t.Fee, t.FairDay, t.Entrants.Count, t.Entrants.Contains(hero.Value), t.Drawn, t.Paid, t.Cancelled, t.Paid && t.Winner == hero.Value));
        return v;
    }

    /// <summary>The compass word for where a place lies from the hero (region millimetres): "north-east" and so on, for the soft pin on a contract.</summary>
    public static string Direction(long dx, long dy)
    {
        if (Math.Abs(dx) <= Math.Abs(dy) / 3) return dy > 0 ? "south" : "north";
        if (Math.Abs(dy) <= Math.Abs(dx) / 3) return dx > 0 ? "east" : "west";
        return (dy > 0 ? "south" : "north") + "-" + (dx > 0 ? "east" : "west");
    }
}
