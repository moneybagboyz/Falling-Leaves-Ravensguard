using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Succession;

/// <summary>What a bequest gives (inheritance 3 <c>Will</c>): everything, one deed, one business, or a share of the coin (permille). Append only (saved as a byte).</summary>
public enum BequestWhat : byte { All, Deed, Business, Coin }

/// <summary>One line of a will: to whom, what, which thing (a deed's or a business's id; none for All and Coin), and for coin the share in permille.</summary>
public readonly record struct Bequest(EntityId Person, BequestWhat What, EntityId Thing = default, int CoinPm = 0);

/// <summary>
/// A will (inheritance 3): the testator, the bequests in the order made, the witness (the priest or the reeve who took it down) and the day. One per person; a newer one replaces it. What it does not
/// name follows the law (4.1 rule 1).
/// </summary>
public sealed class Will
{
    public EntityId Testator;
    public List<Bequest> Bequests { get; } = [];
    public EntityId Witness;
    public int Day;
    public long FeeMites;

    /// <summary>The person the will gives everything to (the first "all" line), or none.</summary>
    public EntityId AllTo => Bequests.FirstOrDefault(b => b.What == BequestWhat.All).Person;
    /// <summary>Who the will gives a deed or a business to (none when it does not name it).</summary>
    public EntityId ThingTo(EntityId thing) => Bequests.FirstOrDefault(b => b.What is BequestWhat.Deed or BequestWhat.Business && b.Thing == thing).Person;
    public bool Names(EntityId person) => Bequests.Any(b => b.Person == person);
}

/// <summary>
/// Every will of the world (the optional "wills" save section, overview 7; absent while empty, so a world that never made one keeps its bytes). Keyed by the testator (ascending id); a will is struck out
/// once its testator's estate is settled.
/// </summary>
public sealed class WillBook
{
    /// <summary>The fee for making a will (inheritance 3: 2 crowns, to the witness's purse).</summary>
    public const long FeeMites = 200;

    public SortedDictionary<EntityId, Will> ByTestator { get; } = [];
    public bool IsEmpty => ByTestator.Count == 0;

    public Will? Of(EntityId testator) => ByTestator.TryGetValue(testator, out var w) ? w : null;

    /// <summary>The "wills" part of the hash trail (folded into <c>EstateHash</c> when any exists).</summary>
    public void Hash(Action<long> L)
    {
        L(ByTestator.Count);
        foreach (var w in ByTestator.Values)
        {
            L((long)w.Testator.Value); L((long)w.Witness.Value); L(w.Day); L(w.FeeMites); L(w.Bequests.Count);
            foreach (var b in w.Bequests) { L((long)b.Person.Value); L((long)b.What); L((long)b.Thing.Value); L(b.CoinPm); }
        }
    }

    /// <summary>
    /// Makes (or replaces) a will before a witness (dialogue <c>make_will</c> at a church or the reeve): the fee goes from the testator's purse to the witness's through the ledger (<c>Tithe</c>), and the
    /// will is kept by the testator's id. Refused when the purse cannot pay, when it names nobody, or when its coin shares add up to more than the whole.
    /// </summary>
    public (bool Ok, string Key) Make(Ledger ledger, EntityId testator, PurseId from, PurseId witnessPurse, EntityId witness, IReadOnlyList<Bequest> bequests, int day)
    {
        if (bequests.Count == 0 || bequests.Any(b => b.Person == EntityId.None || b.Person == testator)) return (false, "in_wl_names_nobody");
        if (bequests.Where(b => b.What == BequestWhat.Coin).Sum(b => (long)b.CoinPm) > 1000 || bequests.Any(b => b.What == BequestWhat.Coin && b.CoinPm <= 0)) return (false, "in_wl_coin_over");
        if (ledger.Balance(from).Mites < FeeMites || from == witnessPurse) return (false, "in_wl_cannot_afford");
        if (!ledger.Transfer(from, witnessPurse, new Coin(FeeMites), TransferReason.Tithe)) return (false, "in_wl_cannot_afford");
        var w = new Will { Testator = testator, Witness = witness, Day = day, FeeMites = FeeMites };
        w.Bequests.AddRange(bequests);
        ByTestator[testator] = w;
        return (true, "in_wl_made");
    }
}
