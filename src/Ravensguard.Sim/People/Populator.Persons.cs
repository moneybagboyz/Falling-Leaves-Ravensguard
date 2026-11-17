using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.People;

public sealed partial class Populator
{
    /// <summary>What one household needs before its people are created.</summary>
    private sealed class HhPlan
    {
        public int Lot = -1, Cap, Size;
        public DwellingType Dwelling;
        public Occupation HeadOcc;
        public int WorkLot = -1, HeadEmployer = -1;
        public bool Celibate;
        public SocialClass Class;
        public DeedKind Deed;
        public bool Farm;
    }

    // ---------- people ----------
    private int NewPerson(Rnd r, NamePool np, int hh, int si, int lot, Sex sex, int age, string family, int culture, SocialClass cls, bool allowByname = true)
    {
        int i = _p.Add();
        _p.Sex[i] = sex; _p.Culture[i] = (byte)culture; _p.Class[i] = cls;
        _p.BirthDay[i] = _nowDay - age * TimeConst.DaysPerYear - r.Range(0, TimeConst.DaysPerYear);
        _p.Household[i] = hh; _p.HomeSettlement[i] = si; _p.HomeLot[i] = (short)lot;
        _p.Family[i] = family;
        bool female = sex == Sex.Female;
        string given = "", by = "";
        bool ok = false;
        for (int t = 0; t < 12 && !ok; t++)
        {
            given = np.Given(female, r);
            if (allowByname && t >= 6 && age >= 16) by = np.Byname(r);
            ok = _used.Add(Key(given, family, by));
        }
        for (int t = 0; t < 40 && !ok; t++) { given = np.Given(female, r); by = np.Byname(r); ok = _used.Add(Key(given, family, by)); }
        for (int t = 2; !ok; t++) { by = np.Byname(r) + " " + RomanLike(t); ok = _used.Add(Key(given, family, by)); }
        if (by.Length == 0 && allowByname && age >= 18 && r.Pct(9)) { string alt = np.Byname(r); if (_used.Add(Key(given, family, alt))) { _used.Remove(Key(given, family, "")); by = alt; } }
        _p.Given[i] = given; _p.Byname[i] = by;
        _hh.AddMember(hh, i);
        _tot++; if (age < 15) _kid++; else if (age >= 50) _eld++;
        return i;
    }

    private static string Key(string g, string f, string b) => g + "|" + f + "|" + b;
    private static string RomanLike(int n) => n switch { 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => "the " + n + "th" };

    private void SetJob(int i, Occupation occ, int employer, int si, int workLot)
    {
        // Nobody under the job's minimum age holds it: a young relative of a master works as a servant (an apprentice in all but name), a younger child not at all.
        if (occ != Occupation.None && _p.AgeYears(i, _nowDay) < OccupationInfo.MinWorkAge(occ))
        {
            bool old12 = _p.AgeYears(i, _nowDay) >= OccupationInfo.MinWorkAge(Occupation.Servant);
            occ = old12 ? Occupation.Servant : Occupation.None; if (!old12) employer = -1; workLot = occ == Occupation.Servant ? workLot : -1;
        }
        _p.Occupation[i] = occ; _p.Employer[i] = employer;
        if (occ == Occupation.None) { _p.WorkSettlement[i] = -1; _p.WorkLot[i] = -1; return; }
        _p.WorkSettlement[i] = si; _p.WorkLot[i] = (short)workLot;
    }

    /// <summary>Rolls attributes (strength, toughness, dexterity, perception, wits) and the starting skills for the job (or the last job for the retired).</summary>
    private void Finish(int i, Rnd r, Occupation skillOcc, int age)
    {
        int Roll() => 20 + r.Range(0, 21) + r.Range(0, 21) + r.Range(0, 21);
        var bonus = OccupationInfo.Bonus(skillOcc);
        int str = Roll() + bonus.Str, tou = Roll() + bonus.Tou, dex = Roll() + bonus.Dex, per = Roll() + bonus.Per, wit = Roll() + bonus.Wit;
        if (_p.Sex[i] == Sex.Female) { str -= 6; dex += 2; }
        if (age < 15) { int f = 55 + age * 3; str = str * f / 100; tou = tou * f / 100; dex = dex * (f + 10) / 100; }
        else if (age >= 55) { str -= (age - 54) / 2; tou -= (age - 54) / 2; dex -= (age - 54) / 3; }
        int b = i * PersonTable.AttrCount;
        _p.Attr[b] = Clamp(str); _p.Attr[b + 1] = Clamp(tou); _p.Attr[b + 2] = Clamp(dex); _p.Attr[b + 3] = Clamp(per); _p.Attr[b + 4] = Clamp(wit);
        if (age < 15) return;
        var (sa, sb) = OccupationInfo.Skills(skillOcc);
        int yrs = Math.Max(0, Math.Min(age, 60) - 14);
        if (sa != Skill.None) _p.SetSkill(i, 0, sa, 8 + yrs * 3 / 2 + r.Range(0, 19));
        if (sb != Skill.None) _p.SetSkill(i, 1, sb, 4 + yrs / 2 + r.Range(0, 13));
        _p.SetSkill(i, 2, Skill.Housekeeping, 5 + yrs / 2 + r.Range(0, 15));
        if (_p.Sex[i] == Sex.Male && age is >= 16 and < 60 && skillOcc is not (Occupation.Guard or Occupation.Lord)) _p.SetSkill(i, 3, Skill.Melee, r.Range(0, 20));
        else if (skillOcc == Occupation.Guard || skillOcc == Occupation.Lord) _p.SetSkill(i, 3, Skill.Melee, 25 + yrs + r.Range(0, 20));
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 1, 100);

    private static void Link(PersonTable p, int a, int b) { p.Spouse[a] = b; p.Spouse[b] = a; }

    // ---------- generic household ----------
    private Occupation SpouseJob(Occupation head, bool spouseFemale, Rnd r)
    {
        if (head == Occupation.Lord) return Occupation.None;
        if (!spouseFemale) return r.Pct(60) ? head : Occupation.Labourer;
        bool craft = OccupationInfo.IsTrade(head) && head is not (Occupation.Smith or Occupation.Mason or Occupation.Mayor or Occupation.GuildMaster);
        int x = r.Range(0, 100);
        if (craft) return x < 50 ? head : x < 90 ? Occupation.Spinner : Occupation.None;
        if (head is Occupation.Farmer or Occupation.Ploughman or Occupation.Shepherd or Occupation.Reeve) return x < 30 ? head : x < 80 ? Occupation.Spinner : Occupation.None;
        return x < 45 ? Occupation.Spinner : Occupation.None;
    }

    private Occupation ChildJob(Occupation head, Sex sex, int age, Rnd r)
    {
        if (head == Occupation.Lord) return Occupation.None;
        if (age < 12) return Occupation.None;
        if (age < 15) return r.Pct(35) ? head : Occupation.None;
        if (age >= 65) return r.Pct(30) ? head : Occupation.None;
        if (sex == Sex.Female) return r.Pct(55) ? Occupation.Spinner : r.Pct(30) ? head : Occupation.None;
        return r.Pct(78) ? head : Occupation.Labourer;
    }

    /// <summary>Builds a household of <see cref="HhPlan.Size"/> people from age buckets, a married couple or widowed head, children, grown children, kin elders and lodgers.</summary>
    private int BuildHousehold(HhPlan pl, int si, int culture, NamePool np, Rnd r)
    {
        var s = _r.Settlements[si];
        int n = pl.Size;
        int hh = _hh.Add(si, pl.Lot, pl.Dwelling, pl.Cap);
        _hh.Class[hh] = pl.Class; _hh.Deed[hh] = pl.Deed;
        _hh.Purse[hh] = pl.Class switch { SocialClass.Gentry => r.Range(500, 3000), SocialClass.Burgher => r.Range(40, 400), _ => r.Range(2, 60) };

        // Age buckets: error feedback keeps the running pyramid on the life table (36 / 48 / 16).
        int pk = Math.Clamp(KidPermille + (KidPermille * _tot - 1000 * _kid) / 50, 80, 700);
        int pe = Math.Clamp(ElderPermille + (ElderPermille * _tot - 1000 * _eld) / 50, 40, 400);
        int k = 0, e = 0;
        for (int t = 0; t < n; t++) { int x = r.Range(0, 1000); if (x < pk) k++; else if (x < pk + pe) e++; }
        if (pl.Celibate) e = Math.Min(e, 1);
        int a = n - k - e;
        if (a == 0 && k > 0 && (e == 0 || r.Pct(80))) { k--; a++; }
        if (a == 0 && e == 0) { a = 1; }
        bool elderHead = a == 0;

        var kidAges = new List<int>();
        for (int t = 0; t < k; t++) kidAges.Add(r.Range(0, 15));
        kidAges.Sort((x, y) => y.CompareTo(x));
        int oldest = k > 0 ? kidAges[0] : -1;
        int lo = Math.Max(18, oldest + 16);
        int span = 49 - lo + 1;
        int momAge = lo + r.Range(0, span);
        int manAge = Math.Clamp(momAge + r.Range(-3, 9), lo, 49);

        bool couple = !pl.Celibate && (elderHead ? e >= 2 && r.Pct(80) : a >= 2 && r.Pct(90));
        bool headMale = pl.Celibate || (couple ? r.Pct(80) : r.Pct(60));
        bool patro = pl.Class == SocialClass.Peasant && !pl.Celibate && r.Pct(35);
        string fam = pl.Class == SocialClass.Gentry ? np.FamilyName(r) : patro ? "" : np.FamilyName(r);
        string FamFor(Sex sex, string fatherGiven) => patro ? np.Patronymic(fatherGiven, sex == Sex.Female) : fam;

        var members = new List<int>();
        int head, spouse = -1;
        int headAge;
        var elders = new List<int>();
        int elderCount = e;
        if (!elderHead)
        {
            headAge = pl.Celibate ? Math.Max(28, manAge) : headMale ? manAge : momAge;
            var hs = headMale ? Sex.Male : Sex.Female;
            head = NewPerson(r, np, hh, si, pl.Lot, hs, headAge, FamFor(hs, np.Given(false, r)), culture, pl.Class);
            SetJob(head, pl.HeadOcc, pl.HeadEmployer, si, pl.WorkLot);
            Finish(head, r, pl.HeadOcc, headAge);
            members.Add(head);
            if (couple)
            {
                var ss = headMale ? Sex.Female : Sex.Male;
                int sAge = headMale ? momAge : manAge;
                spouse = NewPerson(r, np, hh, si, pl.Lot, ss, sAge, FamFor(ss, np.Given(false, r)), culture, pl.Class);
                var sj = SpouseJob(pl.HeadOcc, ss == Sex.Female, r);
                SetJob(spouse, sj, sj == pl.HeadOcc && pl.HeadEmployer >= 0 ? pl.HeadEmployer : sj == pl.HeadOcc ? head : -1, si, sj == pl.HeadOcc ? pl.WorkLot : -1);
                Finish(spouse, r, sj, sAge);
                Link(_p, head, spouse);
                members.Add(spouse);
            }
        }
        else
        {
            // Oldest elder heads the house; a second elder may be the spouse.
            headAge = 50 + r.Range(0, 15);
            var hs = headMale ? Sex.Male : Sex.Female;
            head = NewPerson(r, np, hh, si, pl.Lot, hs, headAge, FamFor(hs, np.Given(false, r)), culture, pl.Class);
            SetJob(head, headAge < 65 ? pl.HeadOcc : Occupation.None, headAge < 65 ? pl.HeadEmployer : -1, si, pl.WorkLot);
            Finish(head, r, pl.HeadOcc, headAge);
            members.Add(head);
            elderCount--;
            if (couple)
            {
                var ss = headMale ? Sex.Female : Sex.Male;
                int sAge = Math.Clamp(headAge + r.Range(-6, 5), 50, 82);
                spouse = NewPerson(r, np, hh, si, pl.Lot, ss, sAge, FamFor(ss, np.Given(false, r)), culture, pl.Class);
                SetJob(spouse, Occupation.None, -1, si, -1);
                Finish(spouse, r, pl.HeadOcc, sAge);
                Link(_p, head, spouse);
                members.Add(spouse);
                elderCount--;
            }
        }
        int mother = -1, father = -1;
        if (head >= 0)
        {
            int partner = spouse;
            if (_p.Sex[head] == Sex.Female) { mother = head; father = partner; } else { father = head; mother = partner; }
            if (elderHead) { mother = father = -1; } // grandchildren of an elder head have no living parents here
        }
        string childFatherGiven = father >= 0 ? _p.Given[father] : head >= 0 ? _p.Given[head] : "";

        // Extra adults beyond the couple: grown children when the parents are old enough, else lodgers and servants.
        int usedAdults = elderHead ? 0 : couple ? 2 : 1;
        for (int t = usedAdults; t < a; t++)
        {
            int pAge = elderHead ? 0 : Math.Min(mother >= 0 ? _p.AgeYears(mother, _nowDay) : 99, father >= 0 ? _p.AgeYears(father, _nowDay) : 99);
            bool child = !elderHead && !pl.Celibate && (mother >= 0 || father >= 0) && pAge >= 33 && r.Pct(65);
            if (child)
            {
                var cs = r.Pct(50) ? Sex.Male : Sex.Female;
                int cAge = r.Range(15, Math.Min(26, pAge - 16) + 1);
                int c = NewPerson(r, np, hh, si, pl.Lot, cs, cAge, FamFor(cs, childFatherGiven), culture, pl.Class);
                _p.Father[c] = father; _p.Mother[c] = mother;
                var occ = ChildJob(pl.HeadOcc, cs, cAge, r);
                SetJob(c, occ, occ == pl.HeadOcc ? (pl.HeadEmployer >= 0 ? pl.HeadEmployer : head) : occ == Occupation.None ? -1 : head, si, occ == pl.HeadOcc ? pl.WorkLot : -1);
                Finish(c, r, occ, cAge);
                members.Add(c);
            }
            else
            {
                var cs = r.Pct(50) ? Sex.Male : Sex.Female;
                int cAge = r.Range(15, 50);
                int c = NewPerson(r, np, hh, si, pl.Lot, cs, cAge, np.FamilyName(r), culture, SocialClass.Peasant);
                Occupation occ = pl.HeadOcc == Occupation.Lord ? Occupation.Servant : r.Pct(45) && OccupationInfo.IsTrade(pl.HeadOcc) ? pl.HeadOcc : r.Pct(55) ? Occupation.Servant : Occupation.Labourer;
                SetJob(c, occ, head, si, occ == pl.HeadOcc || occ == Occupation.Servant ? pl.WorkLot : -1);
                Finish(c, r, occ, cAge);
                members.Add(c);
            }
        }
        // Children: of the couple or widowed head; wards of an elder head or of a priest.
        for (int t = 0; t < k; t++)
        {
            int cAge = kidAges[t];
            var cs = r.Pct(50) ? Sex.Male : Sex.Female;
            bool kin = !pl.Celibate && !elderHead && (mother >= 0 || father >= 0);
            int c = NewPerson(r, np, hh, si, pl.Lot, cs, cAge, kin ? FamFor(cs, childFatherGiven) : np.FamilyName(r), culture, kin ? pl.Class : SocialClass.Peasant, false);
            if (kin) { _p.Father[c] = father; _p.Mother[c] = mother; }
            var occ = kin ? ChildJob(pl.HeadOcc, cs, cAge, r) : Occupation.None;
            SetJob(c, occ, occ == Occupation.None ? -1 : head, si, occ == pl.HeadOcc ? pl.WorkLot : -1);
            Finish(c, r, occ, cAge);
            members.Add(c);
        }
        // Elders: the first is a parent of the household's mother (or father); others are unrelated lodgers and widows.
        int linkTo = mother >= 0 ? mother : father;
        int parentAge = linkTo >= 0 ? _p.AgeYears(linkTo, _nowDay) : 0;
        int firstElder = -1;
        for (int t = 0; t < elderCount; t++)
        {
            bool parent = t == 0 && linkTo >= 0 && !elderHead && !pl.Celibate;
            var es = parent ? (r.Pct(60) ? Sex.Female : Sex.Male) : (r.Pct(55) ? Sex.Female : Sex.Male);
            int eAge = parent ? Math.Min(86, Math.Max(50, parentAge + 16) + r.Range(0, 12)) : t == 1 && firstElder >= 0 ? Math.Clamp(Math.Max(_p.AgeYears(firstElder, _nowDay) + r.Range(-5, 5), parentAge + 16), 50, 86) : 50 + r.Range(0, 28);
            string ef = parent && es == Sex.Male && !patro ? fam : np.FamilyName(r);
            if (patro && parent) ef = np.Patronymic(np.Given(false, r), es == Sex.Female);
            int c = NewPerson(r, np, hh, si, pl.Lot, es, eAge, ef, culture, parent ? pl.Class : SocialClass.Peasant);
            if (parent)
            {
                if (es == Sex.Female) _p.Mother[linkTo] = c; else _p.Father[linkTo] = c;
                firstElder = c;
            }
            else if (t == 1 && firstElder >= 0 && _p.Sex[firstElder] != es && _p.Spouse[firstElder] < 0)
            {
                Link(_p, firstElder, c);
                // The second parent is the other parent of the mother (or father) as well.
                if (es == Sex.Female && _p.Mother[linkTo] < 0) _p.Mother[linkTo] = c;
                else if (es == Sex.Male && _p.Father[linkTo] < 0) _p.Father[linkTo] = c;
            }
            Occupation occ = eAge < 65 && r.Pct(55) ? (es == Sex.Female ? Occupation.Spinner : pl.HeadOcc == Occupation.Lord ? Occupation.None : pl.HeadOcc) : Occupation.None;
            SetJob(c, occ, occ == Occupation.None || occ == Occupation.Spinner ? -1 : pl.HeadEmployer >= 0 ? pl.HeadEmployer : head, si, occ == pl.HeadOcc ? pl.WorkLot : -1);
            Finish(c, r, occ != Occupation.None ? occ : pl.HeadOcc == Occupation.Lord ? Occupation.None : pl.HeadOcc, eAge);
            members.Add(c);
        }
        _hh.Head[hh] = head;
        _hh.Name[hh] = _p.Family[head];
        // Beds in member order; people past the dwelling's capacity are flagged homeless (the sizing keeps this empty).
        for (int m = 0; m < members.Count; m++)
        {
            int c = members[m];
            if (m < pl.Cap) _p.Bed[c] = (short)m;
            else { _p.Bed[c] = -1; _p.Homeless[c] = HomelessReason.NoRoom; }
        }
        return hh;
    }

    /// <summary>People who found no bed in their settlement: a landless household without a dwelling.</summary>
    private void BuildVagrants(int si, int count, int culture, NamePool np, Rnd r)
    {
        if (count <= 0) return;
        int hh = _hh.Add(si, -1, DwellingType.None, 0);
        _hh.Class[hh] = SocialClass.Peasant; _hh.Deed[hh] = DeedKind.None;
        string fam = np.FamilyName(r);
        for (int t = 0; t < count; t++)
        {
            bool kid = t > 0 && r.Pct(36);
            int age = kid ? r.Range(0, 15) : t == 0 ? 20 + r.Range(0, 30) : r.Range(15, 62);
            var sex = t == 0 ? Sex.Male : r.Pct(50) ? Sex.Male : Sex.Female;
            int c = NewPerson(r, np, hh, si, -1, sex, age, fam, culture, SocialClass.Peasant);
            var occ = age < 15 ? Occupation.None : Occupation.Labourer;
            SetJob(c, occ, -1, si, -1);
            Finish(c, r, occ, age);
            _p.Bed[c] = -1; _p.Homeless[c] = HomelessReason.NoRoom;
        }
        _hh.Head[hh] = _hh.MemberIdx[_hh.MemberStart[hh]];
        _hh.Name[hh] = fam;
    }
}
