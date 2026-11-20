using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Bondage;

/// <summary>A culture's law of bondage (slavery 3, <c>cultures.jsonc</c> "slavery").</summary>
public enum SlaveLaw : byte { None, Debt, WarCaptives, Chattel }

public enum BondKind : byte { Debt, Thrall, Chattel, Convict }
public enum BondOrigin : byte { Captured, Sold, Sentenced, Born, Debt }
/// <summary>The owner's setting: how the slave is fed and handled (4.2).</summary>
public enum Treatment : byte { Harsh, Fair, Kind }
/// <summary>What the slave is set to do: the fields or a mine (work as staff), the house, following the hero in his party, or nothing.</summary>
public enum BondWork : byte { Idle, Fields, Mine, House, Follow }

/// <summary>
/// One bonded person (slavery 3): who, whose, what kind of bond and how it began, when it ends, what he last cost, how he is treated, chained or not, and the grievances a revolt counts. A bonded person stays alive in the person
/// table: he eats, sleeps, ages and can be wounded as anyone; he takes no wage and cannot leave, marry or own without the owner's leave. A bond with <see cref="Ask"/> above zero stands in a pen at <see cref="Place"/>.
/// The peculium is a purse of the ledger (the slave's own), not a number kept here.
/// </summary>
public sealed class Bond
{
    public ulong Person;
    /// <summary>The owner: a person (the hero or an NPC), a household, a fief or business (<c>IdKind.Faction</c>/<c>Other</c>), or a slaver band (the bond is then in a pen).</summary>
    public ulong Owner;
    public BondKind Kind;
    public BondOrigin Origin;
    public int Since;
    /// <summary>The day a Debt or Convict bond ends (0: for life).</summary>
    public int TermEndDay;
    /// <summary>What he last cost (the price he can buy himself free at).</summary>
    public long PriceMites;
    public Treatment Treatment = Treatment.Fair;
    public Restraint Chain = Restraint.Bound;
    public BondWork Work = BondWork.House;
    /// <summary>The settlement he is held at (the market of a pen; where the owner is, for the law), -1 on the run.</summary>
    public int Place = -1;
    /// <summary>The household row he came from (he goes back to it when freed and not wanted), -1 none.</summary>
    public int HomeHousehold = -1;
    /// <summary>The asking price while he stands in a pen, 0 otherwise. The seller's purse is <see cref="SellerKind"/> and <see cref="SellerOwner"/>.</summary>
    public long Ask;
    public byte SellerKind;
    public ulong SellerOwner;
    /// <summary>Morale 0..100: below the flee line he runs unless chained.</summary>
    public int Morale = 50;
    public int HungryDays;
    /// <summary>The day of the last flogging and of the last sale that parted him from kin (grievances, 4.5), and the chronicle entry of the flogging.</summary>
    public int FlogDay = -1000, SeparatedDay = -1000;
    public ulong FlogEntry;
    /// <summary>On the run since this day (0: not), the crime record of the runaway notice and its bounty job.</summary>
    public int RunawayDay;
    public int RunawayCrime;
    public ulong BountyJob;
    /// <summary>The chronicle entry that made him a slave (the cause of every later entry of his).</summary>
    public ulong Entry;
    public bool InPen => Ask > 0;
    public bool OnRun => RunawayDay > 0;
}

/// <summary>A slaver band of the region (slavery 3): its camp (the nearest settlement), its size, and the season it last raided. Its purse is a ledger household account under <see cref="Id"/>.</summary>
public sealed class SlaverBand
{
    public ulong Id;
    public int Camp;
    public int Size;
    public int LastRaidSeason = -1;
    public int Raids;
}

/// <summary>A band of freed bonded people after a revolt (4.5): who leads it, how many, where, when. The unrest part (E1) turns it into a bandit group.</summary>
public sealed class RebelBand
{
    public ulong Leader;
    public int Place, Day, Members;
    public ulong Entry;
}

/// <summary>
/// Everyone bonded, the pens' sellers and the slaver bands (slavery 3, the optional "bondage" section, no format bump): bonds by person id (a sorted map: every walk is in id order). No random draw lives here.
/// </summary>
public sealed class BondBook
{
    public readonly SortedDictionary<ulong, Bond> Bonds = [];
    public readonly SortedDictionary<ulong, SlaverBand> Bands = [];
    public readonly List<RebelBand> Rebels = [];
    /// <summary>Captives whose "sold" fate has been taken up (pen listed): never listed twice.</summary>
    public readonly SortedSet<ulong> Swept = [];
    /// <summary>People freed by the law, a revolt or a will, counted for the view.</summary>
    public int FreedTotal;
    public int LastDay = -1, LastSeason = -1;

    public bool IsEmpty => Bonds.Count == 0 && Bands.Count == 0 && Rebels.Count == 0 && Swept.Count == 0 && FreedTotal == 0;

    public Bond? Of(EntityId person) => Bonds.GetValueOrDefault(person.Value);
    public bool Has(EntityId person) => Bonds.ContainsKey(person.Value);
    /// <summary>True when the person is a slave (owned, not standing in a pen of a band's).</summary>
    public bool IsBonded(EntityId person) => Bonds.ContainsKey(person.Value);

    /// <summary>The bonds an owner holds, in person order (not those still in a pen).</summary>
    public IEnumerable<Bond> OwnedBy(EntityId owner) => Bonds.Values.Where(b => b.Owner == owner.Value && !b.InPen);

    /// <summary>The pen at a market: the bonds standing for sale there, in person order.</summary>
    public IEnumerable<Bond> Pen(int settlement) => Bonds.Values.Where(b => b.InPen && b.Place == settlement);

    /// <summary>The <c>BondHash</c> of the daily trail (slavery 5): bonds, pens, bands and rebels, in ascending order.</summary>
    public ulong Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        foreach (var (k, b) in Bonds)
        {
            M(k); M(b.Owner); M((ulong)b.Kind); M((ulong)b.Origin); M((ulong)(uint)b.Since); M((ulong)(uint)b.TermEndDay); M((ulong)b.PriceMites); M((ulong)b.Treatment); M((ulong)b.Chain); M((ulong)b.Work);
            M((ulong)(uint)b.Place); M((ulong)(uint)b.HomeHousehold); M((ulong)b.Ask); M(b.SellerKind); M(b.SellerOwner); M((ulong)(uint)b.Morale); M((ulong)(uint)b.HungryDays);
            M((ulong)(uint)b.FlogDay); M((ulong)(uint)b.SeparatedDay); M(b.FlogEntry); M((ulong)(uint)b.RunawayDay); M((ulong)(uint)b.RunawayCrime); M(b.BountyJob); M(b.Entry);
        }
        foreach (var (k, s) in Bands) { M(k); M((ulong)(uint)s.Camp); M((ulong)(uint)s.Size); M((ulong)(uint)s.LastRaidSeason); M((ulong)(uint)s.Raids); }
        foreach (var r in Rebels) { M(r.Leader); M((ulong)(uint)r.Place); M((ulong)(uint)r.Day); M((ulong)(uint)r.Members); M(r.Entry); }
        foreach (var s in Swept) M(s);
        M((ulong)(uint)FreedTotal); M((ulong)(uint)LastDay); M((ulong)(uint)LastSeason);
        return h;
    }
}
