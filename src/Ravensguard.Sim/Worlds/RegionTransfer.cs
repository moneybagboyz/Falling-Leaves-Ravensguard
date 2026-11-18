using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Worlds;

/// <summary>
/// People are records that move (seamless-regions spec 3.6): the origin region leaves a <see cref="PersonStatus.Departed"/> tombstone, the destination gets a new
/// row that keeps the person's origin id (E2: the region bits stay, so ids never collide and the person can be followed). Pure in the two record sets and the
/// arguments: the same call on the same records always makes the same rows. Call at a barrier, with neither region stepping.
/// </summary>
public static class RegionTransfer
{
    /// <summary>
    /// Moves person <paramref name="person"/> of <paramref name="from"/> into <paramref name="to"/>, into the lowest-numbered household with a free bed in settlement
    /// <paramref name="toSettlement"/> (-1: the lowest settlement that has one). Returns the new row, or -1 when the person is not alive or no household has room (nothing changes).
    /// </summary>
    public static int MovePerson(RegionWorld from, RegionWorld to, int person, int toSettlement, GameTime when)
    {
        var fp = from.Sim.People; var tp = to.Sim.People; var th = to.Sim.Households;
        if (ReferenceEquals(from, to) || (uint)person >= (uint)fp.Count || fp.Status[person] != PersonStatus.Alive) return -1;
        int hh = -1;
        for (int h = 0; h < th.Count && hh < 0; h++)
            if (th.MemberCount[h] > 0 && th.MemberCount[h] < th.Beds[h] && (toSettlement < 0 || th.Settlement[h] == toSettlement)) hh = h;
        if (hh < 0) return -1;

        // the new row: every personal column, none of the links into the old region's tables
        var id = fp.IdOf(person);
        int r = tp.Add();
        tp.Given[r] = fp.Given[person]; tp.Family[r] = fp.Family[person]; tp.Byname[r] = fp.Byname[person]; tp.Sex[r] = fp.Sex[person]; tp.BirthDay[r] = fp.BirthDay[person];
        tp.Culture[r] = fp.Culture[person]; tp.Occupation[r] = fp.Occupation[person]; tp.Class[r] = fp.Class[person]; tp.Traits[r] = fp.Traits[person];
        Array.Copy(fp.Attr, person * PersonTable.AttrCount, tp.Attr, r * PersonTable.AttrCount, PersonTable.AttrCount);
        Array.Copy(fp.Skills, person * PersonTable.SkillSlots * 2, tp.Skills, r * PersonTable.SkillSlots * 2, PersonTable.SkillSlots * 2);
        tp.Notable[r] = fp.Notable[person]; tp.Dynasty[r] = fp.Dynasty[person];
        tp.Status[r] = PersonStatus.Alive; tp.LastBirthDay[r] = fp.LastBirthDay[person]; tp.MarriageWait[r] = fp.MarriageWait[person];
        tp.Household[r] = hh; tp.HomeSettlement[r] = th.Settlement[hh]; tp.HomeLot[r] = th.Lot[hh];
        tp.SetForeignId(r, id);
        th.AddMemberLater(hh, r);
        to.Sim.Economy.AddPerson(r);
        to.Sim.Vital.Alive++; to.Sim.Vital.Immigrants++;
        to.Population.ResetKin();

        // the tombstone
        int old = fp.Household[person];
        fp.Status[person] = PersonStatus.Departed; fp.PregnantUntilDay[person] = -1; fp.FatherOfUnborn[person] = -1;
        int sp = fp.Spouse[person];
        if (sp >= 0 && fp.Spouse[sp] == person) fp.Spouse[sp] = -1;
        if (old >= 0) from.Sim.Households.RemoveMember(old, person);
        from.Sim.Vital.Alive--; from.Sim.Vital.Emigrants++;
        from.Population.ResetKin();
        _ = when;
        return r;
    }
}
