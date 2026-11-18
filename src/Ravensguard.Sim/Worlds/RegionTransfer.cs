using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Worlds;

/// <summary>What a household move carried: the rows made in the destination (in the order the members were moved), the coin and goods that crossed.</summary>
public sealed record HouseholdMove(int Household, int[] Rows, Coin Coin, long MassG);

/// <summary>
/// People are records that move (seamless-regions spec 3.6): the origin region leaves a <see cref="PersonStatus.Departed"/> tombstone, the destination gets a new
/// row that keeps the person's origin id (E2: the region bits stay, so ids never collide and the person can be followed). Pure in the two record sets and the
/// arguments: the same call on the same records always makes the same rows. Call at a barrier, with neither region stepping.
/// <para>
/// <see cref="MoveHousehold"/> is the public entry point for migration and caravans (spec 3.6; wiring them to it belongs to the economy work): the whole household
/// goes with its coin and stock, kin inside it stay linked, kin outside it become foreign references (<see cref="PersonTable.ForeignKin"/>), pregnancies go on.
/// <see cref="MovePerson"/> moves one adult who is free to go and refuses anyone whose leaving would split a family.
/// Coin and goods are conserved across the two regions: coin leaves the origin's ledger as Emigrate and enters the destination's as Immigrate (<see cref="CoinOut"/>, <see cref="CoinIn"/>),
/// goods leave as <see cref="DestroyReason.Export"/> and arrive as <see cref="CreateReason.Import"/>, so each region's audit still holds and the two together lose and gain nothing.
/// </para>
/// </summary>
public static class RegionTransfer
{
    /// <summary>How coin that crosses a border leaves the origin's ledger: the dedicated emigrate reason (economy decision I33), counted in the audit's sunk coin, matched by immigrate in the destination.</summary>
    public const SinkReason CoinOut = SinkReason.Emigrate;
    /// <summary>How it enters the destination's ledger.</summary>
    public const SourceReason CoinIn = SourceReason.Immigrate;

    /// <summary>The age (years) from which a person may move alone.</summary>
    public const int AdultAge = 16;

    // ------------------------------------------------------------------ one person

    /// <summary>
    /// Moves person <paramref name="person"/> of <paramref name="from"/> into <paramref name="to"/>, into the lowest-numbered household with a free bed in settlement
    /// <paramref name="toSettlement"/> (-1: the lowest settlement that has one). Returns the new row, or -1 when nothing changes: the person is not alive, no household has
    /// room, or leaving would split a family (a minor, a married person, a pregnant woman, a parent of minors in the household: use <see cref="MoveHousehold"/>).
    /// The person takes nothing with them (their household keeps its coin and stock); parents and a dead spouse who stay behind are kept as foreign kin links.
    /// </summary>
    public static int MovePerson(RegionWorld from, RegionWorld to, int person, int toSettlement, GameTime when)
    {
        var fp = from.Sim.People; var th = to.Sim.Households;
        if (ReferenceEquals(from, to) || !CanMovePerson(from, person, when)) return -1;
        int hh = -1;
        for (int h = 0; h < th.Count && hh < 0; h++)
            if (th.MemberCount[h] > 0 && th.MemberCount[h] < th.Beds[h] && (toSettlement < 0 || th.Settlement[h] == toSettlement)) hh = h;
        if (hh < 0) return -1;

        var map = new Dictionary<int, int>();
        int r = CopyPerson(from, to, person, hh, map);
        Link(from, to, [(person, r)], map);
        Tombstone(from, person);
        to.Population.ResetKin(); from.Population.ResetKin();
        return r;
    }

    /// <summary>True when person <paramref name="person"/> may move alone: alive, an adult, not pregnant, no living spouse, and not a parent of minors in their household.</summary>
    public static bool CanMovePerson(RegionWorld from, int person, GameTime when)
    {
        var fp = from.Sim.People;
        if ((uint)person >= (uint)fp.Count || fp.Status[person] != PersonStatus.Alive) return false;
        long day = when.TotalDays;
        if (from.Sim.AgeOn(person, day) < AdultAge || fp.PregnantUntilDay[person] >= 0) return false;
        int sp = fp.Spouse[person];
        if (sp >= 0 && fp.Status[sp] == PersonStatus.Alive) return false;
        int old = fp.Household[person];
        if (old >= 0)
            foreach (int m in from.Sim.Households.Members(old))
                if (m != person && fp.Status[m] == PersonStatus.Alive && (fp.Mother[m] == person || fp.Father[m] == person) && from.Sim.AgeOn(m, day) < AdultAge) return false;
        return true;
    }

    // ------------------------------------------------------------------ a household

    /// <summary>
    /// Moves the whole household <paramref name="household"/> of <paramref name="from"/> into <paramref name="to"/>: every living member (kin links among them kept, pregnancies
    /// kept), the household's coin and goods stock. The household takes the lowest-numbered empty household of the destination (a vacated dwelling) in settlement
    /// <paramref name="toSettlement"/> (-1: any) with beds for all of them; the origin's dwelling is vacated. Returns null, changing nothing, when the household has no
    /// living member or the destination has no empty dwelling with room.
    /// </summary>
    public static HouseholdMove? MoveHousehold(RegionWorld from, RegionWorld to, int household, int toSettlement, GameTime when)
    {
        var fh = from.Sim.Households; var fp = from.Sim.People; var th = to.Sim.Households;
        if (ReferenceEquals(from, to) || (uint)household >= (uint)fh.Count) return null;
        var members = fh.Members(household).Where(m => fp.Status[m] == PersonStatus.Alive).OrderBy(m => m).ToArray();
        if (members.Length == 0) return null;
        int hh = -1;
        for (int h = 0; h < th.Count && hh < 0; h++)
            if (th.MemberCount[h] == 0 && th.Beds[h] >= members.Length && (toSettlement < 0 || th.Settlement[h] == toSettlement)) hh = h;
        if (hh < 0) return null;

        var map = new Dictionary<int, int>();
        var rows = new int[members.Length];
        for (int i = 0; i < members.Length; i++) rows[i] = CopyPerson(from, to, members[i], hh, map);
        Link(from, to, members.Select((m, i) => (m, rows[i])).ToArray(), map);
        int head = fh.Head[household];
        th.Head[hh] = head >= 0 && map.TryGetValue(head, out int nh) ? nh : rows[0];
        th.Class[hh] = fh.Class[household]; th.Name[hh] = fh.Name[household];
        foreach (int m in members) Tombstone(from, m);
        fh.Head[household] = -1;
        Demography_Vacate(from, household);

        // coin and goods go with the household: out of the origin's books, into the destination's
        var fe = from.Sim.Economy; var te = to.Sim.Economy;
        var coin = fe.Ledger.Balance(fe.HouseholdPurse[household]);
        if (coin.Mites > 0) { fe.Ledger.Sink(fe.HouseholdPurse[household], coin, CoinOut); te.Ledger.Mint(te.HouseholdPurse[hh], coin, CoinIn); }
        long mass = 0;
        var src = fe.HouseholdStore[household]; var dst = te.HouseholdStore[hh];
        foreach (var (g, q) in src.Rows())
        {
            mass += fe.Goods.MassG(g, q);
            src.Destroy(g, q, DestroyReason.Export);
            dst.Create(g, q, CreateReason.Import);
        }
        to.Population.ResetKin(); from.Population.ResetKin();
        return new HouseholdMove(hh, rows, coin, mass);
    }

    /// <summary>The dwelling of a household that left is empty: its lot no longer holds it (the same step the region's own emigration takes).</summary>
    private static void Demography_Vacate(RegionWorld from, int hh) => Ravensguard.Sim.People.Vital.Demography.VacateDwelling(from.Sim, hh);

    // ------------------------------------------------------------------ the pieces

    /// <summary>The new row: every personal column, none of the links into the old region's tables (those are set by <see cref="Link"/>). Returns the row.</summary>
    private static int CopyPerson(RegionWorld from, RegionWorld to, int person, int hh, Dictionary<int, int> map)
    {
        var fp = from.Sim.People; var tp = to.Sim.People; var th = to.Sim.Households;
        var id = fp.IdOf(person);
        int r = tp.Add();
        map[person] = r;
        tp.Given[r] = fp.Given[person]; tp.Family[r] = fp.Family[person]; tp.Byname[r] = fp.Byname[person]; tp.Sex[r] = fp.Sex[person]; tp.BirthDay[r] = fp.BirthDay[person];
        tp.Culture[r] = fp.Culture[person]; tp.Occupation[r] = fp.Occupation[person]; tp.Class[r] = fp.Class[person]; tp.Traits[r] = fp.Traits[person];
        Array.Copy(fp.Attr, person * PersonTable.AttrCount, tp.Attr, r * PersonTable.AttrCount, PersonTable.AttrCount);
        Array.Copy(fp.Skills, person * PersonTable.SkillSlots * 2, tp.Skills, r * PersonTable.SkillSlots * 2, PersonTable.SkillSlots * 2);
        tp.Notable[r] = fp.Notable[person]; tp.Dynasty[r] = fp.Dynasty[person];
        tp.Status[r] = PersonStatus.Alive; tp.LastBirthDay[r] = fp.LastBirthDay[person]; tp.MarriageWait[r] = fp.MarriageWait[person];
        tp.PregnantUntilDay[r] = fp.PregnantUntilDay[person]; // a pregnancy goes on in the new region; the father is linked below when he moves too
        tp.Household[r] = hh; tp.HomeSettlement[r] = th.Settlement[hh]; tp.HomeLot[r] = th.Lot[hh];
        tp.SetForeignId(r, id);
        th.AddMemberLater(hh, r);
        var te = to.Sim.Economy;
        te.AddPerson(r);
        te.NutritionCd[r] = from.Sim.Economy.NutritionCd[person];
        to.Sim.Vital.Alive++; to.Sim.Vital.Immigrants++;
        return r;
    }

    /// <summary>
    /// Kin links of the moved rows: a link to someone who moved too points at their new row; a link to someone who stayed becomes a foreign reference by id.
    /// </summary>
    private static void Link(RegionWorld from, RegionWorld to, (int Old, int New)[] moved, Dictionary<int, int> map)
    {
        var fp = from.Sim.People; var tp = to.Sim.People;
        foreach (var (o, n) in moved)
        {
            tp.Father[n] = Resolve(fp.Father[o], KinLink.Father, n);
            tp.Mother[n] = Resolve(fp.Mother[o], KinLink.Mother, n);
            tp.Spouse[n] = Resolve(fp.Spouse[o], KinLink.Spouse, n);
            int fu = fp.FatherOfUnborn[o];
            tp.FatherOfUnborn[n] = fu >= 0 && map.TryGetValue(fu, out int nf) ? nf : -1;
            to.Sim.NoteBirth(n);
        }
        int Resolve(int oldKin, KinLink link, int row)
        {
            if (oldKin < 0 || oldKin >= fp.Count) return -1;
            if (map.TryGetValue(oldKin, out int nk)) return nk;
            tp.SetForeignKin(row, link, fp.IdOf(oldKin));
            return -1;
        }
    }

    /// <summary>The origin keeps the row as a <see cref="PersonStatus.Departed"/> tombstone; a spouse who stays behind is widowed in the region's own way (the link goes).</summary>
    private static void Tombstone(RegionWorld from, int person)
    {
        var fp = from.Sim.People;
        int old = fp.Household[person];
        fp.Status[person] = PersonStatus.Departed; fp.PregnantUntilDay[person] = -1; fp.FatherOfUnborn[person] = -1;
        int sp = fp.Spouse[person];
        if (sp >= 0 && fp.Spouse[sp] == person) fp.Spouse[sp] = -1;
        if (old >= 0) from.Sim.Households.RemoveMember(old, person);
        from.Sim.Vital.Alive--; from.Sim.Vital.Emigrants++;
    }
}
