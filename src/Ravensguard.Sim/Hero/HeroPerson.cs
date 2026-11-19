using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Succession;

/// <summary>
/// The hero as a row of the person table (inheritance 3, P5-A4; the Phase 4 Notes: "the hero is not a person row"). At a new game <see cref="Insert"/> appends the hero's row (sex, a birth
/// day from the origin's age, attributes and the four best skills from the record, the origin's occupation and class) with the id the hero's purse, pack and wounds already use
/// (<see cref="EconomyState.HeroOwner"/>, kept as the row's foreign id), in a household of the hero's own at the start settlement (no dwelling, no deed), and links the kin: the origin's
/// <c>kin</c> rows rolled with stream 0x560 (the tanner's widow: a dead husband and 0 to 2 children in the town; the disgraced squire: a living father at a manor; the deserter: none), or
/// with none given the Phase 4 rule (the grown child of a household of the start region, <see cref="HeroKin.Choose"/>), whose parents become the row's Father and Mother.
/// <para>The row's status is <see cref="PersonStatus.Hero"/>: every NPC system skips it (it reads <c>Alive</c>), so the hero is never hired, married, aged to death, walked by the live
/// bubble or brought back as a returning emigrant; he is not one of his household's eating members either (he eats from his pack), only its head. <see cref="HeroRecord"/> stays the
/// hero's authoritative stats; <see cref="Mirror"/> copies them onto the row at each barrier (one direction, hero to row). A v7 save migrates with the row pending (<see cref="Pending"/>):
/// the load inserts it from the Phase 4 kin links, so its continuation is the same as a world that had it from the start.</para>
/// </summary>
public static class HeroPerson
{
    /// <summary>The hero section's <c>PersonIndex</c>: no row (a world made without one: tools, bare test fixtures), or a v7 save whose row the load inserts.</summary>
    public const int None = -1, Pending = -2;
    public const int DefaultMinAge = 22, DefaultMaxAge = 30;

    /// <summary>The settlement a character of this origin starts in: the kind the origin asks for (a town, else a village) if it is clear of wars and lairs, else any safe one (plan §3f).</summary>
    public static int StartSettlement(RegionData region, OriginDef? o)
    {
        string want = o?.StartLocation ?? "village";
        var cls = want == "town" ? SettlementClass.Town : SettlementClass.Village;
        int first = -1, any = -1;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            var st = region.Settlements[s];
            if (!region.IsSafeStart(st.CenterX, st.CenterY)) continue;
            if (any < 0) any = s;
            if (first < 0 && st.Class == cls) first = s;
        }
        return first >= 0 ? first : any >= 0 ? any : region.Settlements.Count > 0 ? 0 : -1;
    }

    /// <summary>The key of the kin roll: the world seed and the hero's person id (stream 0x560).</summary>
    private static Rnd Roll(ulong seed, EntityId hero) => new(seed, hero.Value, RngStreams.HeroKin);

    private static int RollIn(Rnd r, IReadOnlyList<int> range, int lo, int hi)
    {
        int a = range.Count > 0 ? range[0] : lo, b = range.Count > 1 ? range[1] : range.Count > 0 ? range[0] : hi;
        if (b < a) (a, b) = (b, a);
        return r.Range(a, b + 1);
    }

    /// <summary>
    /// Appends the hero's row, the hero's household and the kin, and returns the row. <paramref name="econ"/> (when the world already has its economy) gets the new rows' columns and the
    /// household's purse and store; a household with children is stocked like a household at world creation (a month of food, fuel and salt). <paramref name="kin"/> receives the links.
    /// </summary>
    public static int Insert(Population pop, EconomyState? econ, ContentDatabase content, HeroRecord hero, EntityId heroId, ulong seed, int nowDay, int settlement,
        List<HeroKinLink> kin, Ravensguard.Sim.Abstract.SimWorld? sim = null, IdAllocator? ids = null, bool rollKin = true)
    {
        var pt = pop.People; var hht = pop.Households; var region = pop.Region;
        if (pt.IndexOf(heroId) >= 0) return pt.IndexOf(heroId);
        content.TryGet<OriginDef>(hero.Origin, out var origin);
        var r = Roll(seed, heroId);
        if (settlement < 0 || settlement >= region.Settlements.Count) settlement = StartSettlement(region, origin);
        int culture = settlement >= 0 ? region.Settlements[settlement].Culture : 0;
        var cls = HeroOutfit.ClassOf(hero.Origin);

        // the Phase 4 rule when the origin names no kin: the hero is the grown child of a household of the start region
        bool none = !rollKin || (origin?.Kin.Any(k => k.Rel == "none") ?? false);
        List<HeroKinLink> phase4 = !none && (origin == null || origin.Kin.Count == 0) ? HeroKin.Choose(pt, hht, region.Settlements.Count, nowDay) : [];

        // the hero's household: of the hero's own, at the start settlement, no dwelling and no deed
        ids?.EnsureNext(IdKind.Household, hht.BaseCounter + (ulong)hht.Count); ids?.Allocate(IdKind.Household);     // ids stay in step with the table, as a wedding's household does
        int hh = hht.Add(Math.Max(0, settlement), -1, DwellingType.None, 0);
        hht.Deed[hh] = DeedKind.None; hht.Class[hh] = cls;

        // the hero's row
        int age = RollIn(r, origin?.Age ?? [], DefaultMinAge, DefaultMaxAge);
        int row = AddRow(pt, econ, ids);
        pt.SetForeignId(row, heroId);
        var (given, family) = SplitName(hero.Name);
        pt.Given[row] = given; pt.Family[row] = family; pt.Sex[row] = hero.Female ? Sex.Female : Sex.Male;
        pt.BirthDay[row] = nowDay - age * TimeConst.DaysPerYear - r.Range(0, TimeConst.DaysPerYear);
        pt.Culture[row] = (byte)Math.Clamp(culture, 0, 255); pt.Household[row] = hh; pt.HomeSettlement[row] = settlement; pt.Class[row] = cls;
        pt.Occupation[row] = Enum.TryParse<Occupation>(origin?.Occupation ?? "", true, out var occ) ? occ : Occupation.None;
        pt.Status[row] = PersonStatus.Hero;
        hht.Head[hh] = row; hht.Name[hh] = family;
        if (econ != null && econ.HouseholdCount == hh) econ.AddHousehold(hht, hh);
        Mirror(pt, row, hero);

        if (!none && origin != null && origin.Kin.Count > 0)
        {
            var names = new NamePools(content.All<NamePoolDef>(), region.Seed ^ 0x50455031UL).For(culture);
            int spouseDead = -1;
            foreach (var k in origin.Kin)
            {
                int n = RollIn(r, k.Count, 1, 1);
                for (int i = 0; i < n; i++)
                    switch (k.Rel)
                    {
                        case "spouse_dead":
                        {
                            int sp = AddRow(pt, econ, ids);
                            bool female = !hero.Female;
                            pt.Given[sp] = names.Given(female, new Rnd(seed, pt.IdOf(sp).Value, RngStreams.HeroKin)); pt.Family[sp] = family; pt.Sex[sp] = female ? Sex.Female : Sex.Male;
                            int spAge = RollIn(r, k.Age, age, age + 6);
                            int died = r.Range(30, 2 * TimeConst.DaysPerYear);
                            pt.BirthDay[sp] = nowDay - spAge * TimeConst.DaysPerYear - r.Range(0, TimeConst.DaysPerYear);
                            pt.Culture[sp] = pt.Culture[row]; pt.Household[sp] = hh; pt.HomeSettlement[sp] = settlement; pt.Class[sp] = cls;
                            pt.Occupation[sp] = pt.Occupation[row];
                            pt.Status[sp] = PersonStatus.Dead; pt.DeathDay[sp] = nowDay - died; pt.DeathCause[sp] = DeathCause.Disease;
                            pt.Spouse[sp] = row;                                         // the dead keep the link as a record; the widow's is cleared (Demography.Kill)
                            spouseDead = sp;
                            kin.Add(new HeroKinLink(pt.IdOf(sp), KinRelation.Spouse));
                            break;
                        }
                        case "child":
                        {
                            int c = AddRow(pt, econ, ids);
                            bool female = r.Range(0, 2) == 0;
                            pt.Given[c] = names.Given(female, new Rnd(seed, pt.IdOf(c).Value, RngStreams.HeroKin)); pt.Family[c] = family; pt.Sex[c] = female ? Sex.Female : Sex.Male;
                            int cAge = RollIn(r, k.Age, 6, 14);
                            pt.BirthDay[c] = nowDay - cAge * TimeConst.DaysPerYear - r.Range(0, TimeConst.DaysPerYear);
                            pt.Culture[c] = pt.Culture[row]; pt.Household[c] = hh; pt.HomeSettlement[c] = settlement; pt.Class[c] = cls;
                            if (hero.Female) { pt.Mother[c] = row; pt.Father[c] = spouseDead; } else { pt.Father[c] = row; pt.Mother[c] = spouseDead; }
                            pt.Status[c] = PersonStatus.Alive;
                            hht.AddMemberLater(hh, c);
                            if (sim != null) { sim.Vital.Alive++; sim.NoteBirth(c); }
                            kin.Add(new HeroKinLink(pt.IdOf(c), KinRelation.Child));
                            break;
                        }
                        case "parent":
                        {
                            // a living head of a household of the start region of the hero's class and of the age asked (a squire's father at a manor), not already kin
                            int p = FindParent(pop, cls, RollIn(r, k.Age, 45, 70), nowDay, kin);
                            if (p < 0) break;
                            if (pt.Sex[p] == Sex.Female) pt.Mother[row] = p; else pt.Father[row] = p;
                            kin.Add(new HeroKinLink(pt.IdOf(p), KinRelation.Parent));
                            break;
                        }
                    }
            }
            if (hht.MemberCount[hh] > 0 && econ != null) econ.SeedStartingStock(hht, hh);
        }
        else if (!none)
        {
            // Phase 4: the parents of the chosen household are the row's Father and Mother, their children the siblings
            foreach (var l in phase4)
            {
                if (kin.Any(x => x.Person == l.Person)) continue;
                kin.Add(l);
                int p = pt.IndexOf(l.Person);
                if (l.Relation != KinRelation.Parent || p < 0) continue;
                if (pt.Sex[p] == Sex.Female) { if (pt.Mother[row] < 0) pt.Mother[row] = p; }
                else if (pt.Father[row] < 0) pt.Father[row] = p;
            }
        }
        pop.ResetKin();
        return row;
    }

    /// <summary>The row a v7 save's hero gets at load: the hero's row and household from the Phase 4 kin links already saved (no draw of anything but the age), so nothing else moves.</summary>
    public static int InsertForLoad(Population pop, EconomyState? econ, ContentDatabase content, HeroRecord hero, EntityId heroId, ulong seed, int nowDay, IReadOnlyList<HeroKinLink> links,
        IdAllocator? ids = null)
    {
        var pt = pop.People;
        int settlement = -1;
        foreach (var l in links) { int i = pt.IndexOf(l.Person); if (i >= 0 && pt.HomeSettlement[i] >= 0) { settlement = pt.HomeSettlement[i]; break; } }
        // the origin's kin are not rolled for a hero already under way: only the row and the household, linked to the kin the save holds
        int row = Insert(pop, econ, content, hero, heroId, seed, nowDay, settlement, [], null, ids, rollKin: false);
        // the row's own links follow the saved kin (no new people): parents as Father and Mother
        foreach (var l in links)
        {
            int p = pt.IndexOf(l.Person);
            if (l.Relation != KinRelation.Parent || p < 0) continue;
            if (pt.Sex[p] == Sex.Female) { if (pt.Mother[row] < 0) pt.Mother[row] = p; }
            else if (pt.Father[row] < 0) pt.Father[row] = p;
        }
        pop.ResetKin();
        return row;
    }

    private static int FindParent(Population pop, SocialClass cls, int age, int nowDay, List<HeroKinLink> kin)
    {
        var pt = pop.People; var hht = pop.Households;
        int best = -1, bestD = int.MaxValue;
        for (int hh = 0; hh < hht.Count; hh++)
        {
            int head = hht.Head[hh];
            if (head < 0 || head >= pt.Count || pt.Status[head] != PersonStatus.Alive || hht.MemberCount[hh] == 0) continue;
            if (hht.Class[hh] != cls) continue;
            int d = Math.Abs(pt.AgeYears(head, nowDay) - age);
            if (d < bestD && !kin.Any(k => k.Person == pt.IdOf(head))) { bestD = d; best = head; }
        }
        return best;
    }

    private static int AddRow(PersonTable pt, EconomyState? econ, IdAllocator? ids)
    {
        ids?.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count); ids?.Allocate(IdKind.Person);
        int row = pt.Add();
        if (econ != null && econ.PersonCount == row) econ.AddPerson(row);
        return row;
    }

    /// <summary>"Given Family ..." into the given name and the rest.</summary>
    public static (string Given, string Family) SplitName(string name)
    {
        var parts = (name ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => ("", ""), 1 => (parts[0], ""), _ => (parts[0], parts[1]) };
    }

    /// <summary>
    /// The row mirrors the record (one direction, hero to row, at each barrier): the five attributes and the four best skills (ties by the skill's order). Cheap; nothing else writes the row.
    /// </summary>
    public static void Mirror(PersonTable pt, int row, HeroRecord hero)
    {
        if (row < 0 || row >= pt.Count) return;
        for (int a = 0; a < PersonTable.AttrCount; a++) pt.Attr[row * PersonTable.AttrCount + a] = (byte)Math.Clamp(hero.Attribute((HeroAttr)a), 0, 255);
        var best = Enum.GetValues<Skill>().Where(s => s != Skill.None).Select(s => (Skill: s, Level: hero.Skill(HeroRecord.SkillId(s))))
            .Where(x => x.Level > 0).OrderByDescending(x => x.Level).ThenBy(x => (int)x.Skill).Take(PersonTable.SkillSlots).ToList();
        for (int k = 0; k < PersonTable.SkillSlots; k++)
            if (k < best.Count) pt.SetSkill(row, k, best[k].Skill, best[k].Level); else pt.SetSkill(row, k, Skill.None, 0);
    }

    /// <summary>The hero's row in <paramref name="pt"/>: the saved index when it still names the hero's id, else by id; -1 when the hero has none.</summary>
    public static int RowOf(PersonTable pt, HeroRecord? hero, EntityId heroId)
    {
        if (hero is { PersonIndex: >= 0 } h && h.PersonIndex < pt.Count && pt.Status[h.PersonIndex] == PersonStatus.Hero) return h.PersonIndex;
        int i = pt.IndexOf(heroId);
        return i >= 0 && pt.Status[i] == PersonStatus.Hero ? i : -1;
    }
}
