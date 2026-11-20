using Ravensguard.Content;
using System.Globalization;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Family;

/// <summary>
/// The family's day (family-and-lifespan 4.4 to 4.8), run at hour 4 of each day on the tick worker (the H4 slice): the hero's birthdays (attributes lost to age, the old-age roll and its warning), the
/// wedding, conception and births in the hero's couple (the hero's own row is skipped by the Phase 2 vital rolls, so the couple is rolled here with the same stream and rate), the children's lessons,
/// the season's kidnapping roll. Pure in the world state and the day; every draw keys on ids and game time.
/// </summary>
public static class FamilySystem
{
    public static void Daily(FamilyWorld w, int day)
    {
        int hero = w.HeroRow;
        if (hero < 0 || w.St.Hero == null || w.St.Economy == null) return;
        var book = w.Book;
        if (book.Death != null && book.Death.Row != hero) ClearDeath(w);
        if (book.Death == null)
        {
            Ageing(w, day, hero);
            Courting.Daily(w, day);
            Conception(w, day, hero);
            Births(w, day, hero);
            Lessons(w, day, hero);
            Kidnapping(w, day, hero);
        }
        book.LastDay = day;
        if (book.IsEmpty) w.St.Family = null;
    }

    /// <summary>The heir has taken up the hero's place: the dead hero's row is marked with the cause that killed him, and the pending death is cleared.</summary>
    public static void ClearDeath(FamilyWorld w)
    {
        var d = w.Book.Death; if (d == null) return;
        var pt = w.People;
        if (d.Row >= 0 && d.Row < pt.Count && pt.Status[d.Row] == PersonStatus.Dead && pt.DeathCause[d.Row] == DeathCause.Other) pt.DeathCause[d.Row] = d.Cause;
        w.Book.Death = null; w.Book.Ageing = null;
        foreach (var c in w.Book.Courtships.Values) if (c.State is CourtState.Courting or CourtState.Betrothed) c.State = CourtState.Broken;      // the heir is not the dead hero's suitor
    }

    // ------------------------------------------------------------------ the hero's ageing (4.6)

    private static void Ageing(FamilyWorld w, int day, int hero)
    {
        var pt = w.People; var hr = w.St.Hero!; var book = w.Book; var f = w.F;
        int age = pt.AgeYears(hero, day);
        var a = book.Ageing;
        if (a == null || a.Hero != w.HeroId.Value || a.Row != hero) { book.Ageing = a = new HeroAgeing { Hero = w.HeroId.Value, Row = hero, LastAge = age }; return; }
        if (age > a.LastAge)
        {
            var lost = new int[PersonTable.AttrCount];
            for (int now = a.LastAge + 1; now <= age; now++)
            {
                for (int k = 0; k < lost.Length; k++)
                {
                    int cut = Math.Min(f.LossOnBirthday(k, now), Math.Max(0, hr.Attributes[k] - 1));
                    hr.Attributes[k] -= cut; a.Lost[k] += cut; lost[k] += cut;
                }
                int year = day / TimeConst.DaysPerYear - (age - now);
                if (FamilyRules.DiesOfOldAge(f, w.Seed, w.HeroId.Value, year, now)) { OldAgeDeath(w, day, hero, now); a.LastAge = now; break; }
                a.LastAge = now;
            }
            if (lost.Sum() > 0)
            {
                HeroPerson.Mirror(pt, hero, hr);
                w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L), "hero_ageing", [w.HeroId], [new Cause(CauseKind.Death, null, NoteKey: "ch_note_years")],
                    new Dictionary<string, string> { ["age"] = a.LastAge.ToString(CultureInfo.InvariantCulture) }, TextKey: "ch_family_ageing", Params: [ChronParam.Person("hero", w.HeroId), ChronParam.Count("age", a.LastAge)]));
            }
        }
        Warn(w, day, hero, a);
    }

    /// <summary>The day on which a person's age becomes <paramref name="age"/> at the world's ageing pace (the smallest day with (day - birth) x pace / 60 >= age).</summary>
    public static int BirthdayOf(PersonTable pt, int row, int age)
    {
        int pace = Math.Max(1, pt.AgingPace);
        return pt.BirthDay[row] + (age * TimeConst.DaysPerYear + pace - 1) / pace;
    }

    /// <summary>A season before the roll, once the hazard is at least 3 percent, the hero feels it ("Your hands shake in the cold mornings").</summary>
    private static void Warn(FamilyWorld w, int day, int hero, HeroAgeing a)
    {
        int next = a.LastAge + 1;
        if (a.WarnedYear == next || w.F.OldAgePct(next) < w.S.OldAgeWarnPct) return;
        if (day < BirthdayOf(w.People, hero, next) - TimeConst.DaysPerSeason) return;
        a.WarnedYear = next;
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L), "old_age_warning", [w.HeroId], [new Cause(CauseKind.Death, null, NoteKey: "ch_note_years")],
            new Dictionary<string, string>(), TextKey: "ch_family_old_age_warning", Params: [ChronParam.Person("hero", w.HeroId)]));
    }

    private static void OldAgeDeath(FamilyWorld w, int day, int hero, int age)
    {
        w.Book.Death = new PendingDeath { Day = day, Row = hero, Age = age, Cause = DeathCause.Age };
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L + 4 * 3600), "hero_died_old_age", [w.HeroId], [new Cause(CauseKind.Death, null, NoteKey: "ch_note_old_age", NoteParams: [ChronParam.Count("age", age)])],
            new Dictionary<string, string> { ["age"] = age.ToString(CultureInfo.InvariantCulture) }, TextKey: "ch_family_died_old_age", Params: [ChronParam.Person("hero", w.HeroId), ChronParam.Count("age", age)]));
    }

    // ------------------------------------------------------------------ conception and birth (4.4)

    private static void Conception(FamilyWorld w, int day, int hero)
    {
        var pt = w.People; int sp = pt.Spouse[hero];
        if (sp < 0 || sp >= pt.Count || pt.Status[sp] != PersonStatus.Alive || pt.Spouse[sp] != hero) return;
        int mother = pt.Sex[hero] == Sex.Female ? hero : sp, father = mother == hero ? sp : hero, hh = pt.Household[hero];
        if (pt.Sex[sp] == pt.Sex[hero] || hh < 0 || pt.PregnantUntilDay[mother] >= 0 || pt.Status[mother] is not (PersonStatus.Alive or PersonStatus.Hero)) return;
        var r = w.Rates; int age = pt.AgeYears(mother, day);
        if (age < r.FertilityAgeMin || age > r.FertilityAgeMax || day - pt.LastBirthDay[mother] < r.PostpartumDays) return;
        var hht = w.Households;
        int ppm = VitalMath.ConceptionPpm(r, w.Econ.NutritionCd[mother], VitalMath.Crowding(hht.MemberCount[hh], hht.Beds[hh]));
        long hour = day * 24L + 4;
        if (VitalMath.Roll(w.Seed, hour, pt.IdOf(mother).Value, VitalStreams.Conception, 0, ppm)) { pt.PregnantUntilDay[mother] = day + r.PregnancyDays; pt.FatherOfUnborn[mother] = father; }
    }

    private static void Births(FamilyWorld w, int day, int hero)
    {
        var pt = w.People; var hht = w.Households;
        // the hero's own labour: Phase 2 never reads his row, so the pregnancy comes to term here
        if (pt.Sex[hero] == Sex.Female && pt.PregnantUntilDay[hero] >= 0 && pt.PregnantUntilDay[hero] <= day) HeroBirth(w, day, hero);
        int hh = pt.Household[hero]; if (hh < 0) return;
        foreach (int m in hht.Members(hh).ToList())
        {
            if (pt.Status[m] != PersonStatus.Alive || (pt.Father[m] != hero && pt.Mother[m] != hero)) continue;
            var id = pt.IdOf(m);
            if (!w.Book.Born.Add(id.Value)) continue;
            Adopt(w, day, hero, m, newborn: pt.BirthDay[m] >= day - 2);
        }
    }

    /// <summary>The hero (a woman) is delivered: a child row with both parents, born into her household; the childbed risk (1 percent, <see cref="DeathCause.Childbirth"/>) is the hero's own.</summary>
    private static void HeroBirth(FamilyWorld w, int day, int mother)
    {
        var pt = w.People; var hht = w.Households; int hh = pt.Household[mother];
        int father = pt.FatherOfUnborn[mother];
        w.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
        var id = w.Ids.Allocate(IdKind.Person);
        int c = pt.Add();
        if (pt.IdOf(c) != id) throw new InvalidOperationException("person id out of step with the table: " + pt.IdOf(c) + " vs " + id);
        var motherId = pt.IdOf(mother);
        bool female = DetRng.Range(w.Seed, day * 24L + 4, motherId.Value, VitalStreams.Conception, 1, 0, 2) == 1;
        pt.Sex[c] = female ? Sex.Female : Sex.Male;
        pt.Mother[c] = mother; pt.Father[c] = father >= 0 && father < pt.Count ? father : -1;
        pt.Culture[c] = pt.Culture[mother]; pt.Class[c] = hht.Class[hh]; pt.BirthDay[c] = day; pt.Status[c] = PersonStatus.Alive;
        pt.Family[c] = pt.Father[c] >= 0 ? pt.Family[pt.Father[c]] : pt.Family[mother];
        var names = w.Sim?.Names;
        pt.Given[c] = names != null ? names.For(pt.Culture[c]).Given(female, new Rnd(w.Seed, id.Value, VitalStreams.Name)) : (female ? "Daughter" : "Son");
        pt.Dynasty[c] = pt.Father[c] >= 0 && pt.Dynasty[pt.Father[c]] != EntityId.None ? pt.Dynasty[pt.Father[c]] : pt.Dynasty[mother];
        pt.HomeSettlement[c] = hht.Settlement[hh]; pt.HomeLot[c] = hht.Lot[hh]; pt.Household[c] = hh;
        hht.AddMemberLater(hh, c);
        w.Econ.AddPerson(c);
        w.Pop.ResetKin();
        w.Sim?.NoteBirth(c);
        pt.PregnantUntilDay[mother] = -1; pt.FatherOfUnborn[mother] = -1; pt.LastBirthDay[mother] = day;
        if (w.St.Abstract is { } ab) { ab.Vital.Alive++; ab.Vital.Births++; ab.Vital.Year(day / TimeConst.DaysPerYear).Births++; }
        // the childbed: the hero's own chance (the child is delivered first)
        if (w.S.ChildbirthRiskPct > 0 && DetRng.Range(w.Seed, day, motherId.Value, VitalStreams.Conception, 2, 0, 100) < w.S.ChildbirthRiskPct)
        {
            w.Book.Death = new PendingDeath { Day = day, Row = mother, Age = pt.AgeYears(mother, day), Cause = DeathCause.Childbirth };
            w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L + 4 * 3600), "hero_died_childbed", [w.HeroId], [new Cause(CauseKind.Death, null, NoteKey: "ch_note_childbed")],
                new Dictionary<string, string>(), TextKey: "ch_family_died_childbed", Params: [ChronParam.Person("hero", w.HeroId)]));
        }
    }

    /// <summary>A child of the hero comes to notice: a newborn gets the parents' mean attributes (0x641), the kin link and a chronicle line; a child already grown (the origin's) only the link. An NPC mother faces the childbed risk.</summary>
    private static void Adopt(FamilyWorld w, int day, int hero, int c, bool newborn)
    {
        var pt = w.People; var id = pt.IdOf(c); var s = w.S;
        if (newborn)
        {
            int other = pt.Father[c] == hero ? pt.Mother[c] : pt.Father[c];
            Span<int> a = stackalloc int[PersonTable.AttrCount], b = stackalloc int[PersonTable.AttrCount];
            for (int k = 0; k < a.Length; k++) { a[k] = pt.Attribute(hero, k); b[k] = other >= 0 ? pt.Attribute(other, k) : a[k]; }
            var attrs = FamilyRules.ChildAttributes(s, w.Seed, id.Value, a, b);
            for (int k = 0; k < attrs.Length; k++) pt.Attr[c * PersonTable.AttrCount + k] = attrs[k];
            var ps = new List<ChronParam> { ChronParam.Person("child", id), ChronParam.Person("parent", w.HeroId), ChronParam.Place("place", w.PlaceId(pt.HomeSettlement[c])) };
            w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L + 4 * 3600), "hero_child_born", pt.Mother[c] >= 0 ? [id, w.HeroId, pt.IdOf(pt.Mother[c])] : [id, w.HeroId],
                [new Cause(CauseKind.Family, null, NoteKey: "ch_note_a_child")], new Dictionary<string, string> { ["name"] = pt.FullName(c) }, TextKey: "ch_family_child_born", Params: ps));
        }
        var sb = w.St.Succession ??= new SuccessionBook();
        if (!sb.Kin.Any(k => k.Person == id)) sb.Kin.Add(new HeroKinLink(id, KinRelation.Child));
        int mom = pt.Mother[c];
        if (newborn && mom >= 0 && mom != hero && pt.Status[mom] == PersonStatus.Alive && s.ChildbirthRiskPct > 0
            && DetRng.Range(w.Seed, pt.BirthDay[c], pt.IdOf(mom).Value, VitalStreams.Conception, 2, 0, 100) < s.ChildbirthRiskPct && w.Sim is { } sim)
        {
            var ob = new RegionOutbox(w.Ids);
            Demography.Kill(sim, mom, DeathCause.Childbirth, new GameTime(day * 86400L + 4 * 3600), ob, chain: new Cause(CauseKind.Death, null, NoteKey: "ch_note_childbed"));
            ob.ApplyTo(w.Chronicle, null, new Dictionary<string, long>(StringComparer.Ordinal));
        }
    }

    // ------------------------------------------------------------------ the children's lessons (4.4)

    /// <summary>The skills a course teaches (a trade course the skill the head chose; the household's own trade the hero's best skill).</summary>
    public static IReadOnlyList<Skill> CourseSkills(FamilyWorld w, Upbringing u, int hero)
    {
        switch (u.Course)
        {
            case Course.Arms: return [Skill.Blades];
            case Course.Letters: return [Skill.Literacy];
            case Course.Church: return [Skill.Theology];
            case Course.Trade: return u.TradeSkill == Skill.None ? [] : [u.TradeSkill];
            default:
            {
                var pt = w.People; Skill best = Skill.None; int level = 0;
                for (int k = 0; k < PersonTable.SkillSlots; k++) { var sl = pt.SkillAt(hero, k); if (sl.Skill != Skill.None && sl.Level > level) { best = sl.Skill; level = sl.Level; } }
                return best == Skill.None ? [] : [best];
            }
        }
    }

    private static void SetSkill(PersonTable pt, int row, Skill s, int level)
    {
        int slot = -1, weakest = 0, weakLevel = int.MaxValue;
        for (int k = 0; k < PersonTable.SkillSlots; k++)
        {
            var sl = pt.SkillAt(row, k);
            if (sl.Skill == s) { slot = k; break; }
            if (sl.Skill == Skill.None) { if (slot < 0) slot = k; }
            else if (sl.Level < weakLevel) { weakLevel = sl.Level; weakest = k; }
        }
        pt.SetSkill(row, slot >= 0 ? slot : weakest, s, level);
    }

    private static void Lessons(FamilyWorld w, int day, int hero)
    {
        var pt = w.People; var hht = w.Households; var s = w.S; int hh = pt.Household[hero];
        if (hh < 0) return;
        int pace = Math.Max(1, pt.AgingPace);
        foreach (int c in hht.Members(hh).ToList())
        {
            if (pt.Status[c] != PersonStatus.Alive) continue;
            int age = pt.AgeYears(c, day);
            if (age >= s.AdultAge || age < s.LessonStartAge) continue;
            ulong id = pt.IdOf(c).Value;
            w.Book.Upbringings.TryGetValue(id, out var u);
            int tutor = u != null && u.HasTutor ? pt.IndexOf(new EntityId(u.Tutor)) : -1;
            bool tutorOk = u != null && u.Course != Course.None && u.HasTutor && (u.Tutor == w.HeroId.Value || tutor >= 0 && pt.Status[tutor] == PersonStatus.Alive);
            bool taught = tutorOk;
            if (!taught && age < s.UntaughtStartAge) continue;
            if (taught && (u!.Tutor != w.HeroId.Value) && !TutorPaid(w, u, tutor, hh)) continue;
            if (u == null) { u = new Upbringing { Child = id, Course = Course.Household }; w.Book.Upbringings[id] = u; }
            long rate = (long)s.LessonMilli * pace * (taught ? 100 : s.UntaughtPct) / 100;
            foreach (var sk in CourseSkills(w, u, hero))
            {
                long milli = u.Milli.GetValueOrDefault((int)sk), gain = FamilyRules.LessonGain(rate, milli);
                u.Milli[(int)sk] = milli += gain;
                int level = (int)Math.Min(100, milli / 1000);
                if (pt.SkillLevelOf(c, sk) != level) SetSkill(pt, c, sk, level);
            }
            if (taught && u.Course == Course.Arms)
                foreach (int attr in new[] { PersonTable.StrengthIdx, PersonTable.DexterityIdx })
                {
                    int key = Upbringing.AttrBase + attr; long before = u.Milli.GetValueOrDefault(key), after = before + (long)s.ArmsAttrMilliPerDay * pace; u.Milli[key] = after;
                    if (after / 1000 != before / 1000) { int at = c * PersonTable.AttrCount + attr; pt.Attr[at] = (byte)Math.Min(100, pt.Attr[at] + (int)(after / 1000 - before / 1000)); }
                }
            u.Hours += 4;
        }
    }

    /// <summary>Lessons from a household member (or the hero) are free, a priest's Letters free to a hero the people hold in standing, any other tutor is paid the day's wage from the hero's purse.</summary>
    private static bool TutorPaid(FamilyWorld w, Upbringing u, int tutor, int hh)
    {
        var pt = w.People; var s = w.S;
        if (pt.Household[tutor] == hh) return true;
        if (u.Course == Course.Letters && pt.Occupation[tutor] == Occupation.Priest && w.StandingPm(pt.HomeSettlement[tutor]) >= s.PriestFreeStandingPm) return true;
        int th = pt.Household[tutor]; if (th < 0) return false;
        if (w.Ledger.Balance(w.HeroPurse).Mites < s.HiredTutorMitesPerDay) return false;
        return w.Ledger.Transfer(w.HeroPurse, w.HouseholdPurse(th), Coin.FromMites(s.HiredTutorMitesPerDay), TransferReason.Wage);
    }

    // ------------------------------------------------------------------ kidnapping (4.5)

    /// <summary>The chance (percent x 100) that a band takes one of the hero's kin in a season: 4 percent x renown / 500.</summary>
    public static int KidnapChanceX100(FamilySettingsDef s, int renownPm) => (int)Math.Min(10_000L, (long)s.KidnapPctPerSeason * 100 * Math.Max(0, renownPm) / Math.Max(1, s.KidnapRenownDiv));

    private static void Kidnapping(FamilyWorld w, int day, int hero)
    {
        int season = day / TimeConst.DaysPerSeason;
        var book = w.Book;
        if (day % TimeConst.DaysPerSeason != 0 || book.LastKidnapSeason == season) return;
        book.LastKidnapSeason = season;
        TryKidnap(w, day, hero, season);
    }

    /// <summary>The draw alone (stream 0x643, key the hero and the season): true with 4 percent x renown / 500.</summary>
    public static bool KidnapDraw(FamilySettingsDef s, ulong seed, ulong hero, int season, int renownPm) =>
        DetRng.Range(seed, season, hero, RngStreams.Kidnapping, 0, 0, 10_000) < KidnapChanceX100(s, renownPm);

    /// <summary>The season's roll: when a kidnapper band is near and the draw (stream 0x643, key the hero and the season) falls under 4 percent x renown / 500, one of his kin who is free is taken for ransom.</summary>
    public static bool TryKidnap(FamilyWorld w, int day, int hero, int season)
    {
        if (!w.BandNear(season) || w.Content == null) return false;
        if (!KidnapDraw(w.S, w.Seed, w.HeroId.Value, season, w.RenownPm)) return false;
        var pt = w.People; var kin = w.St.Succession?.Kin ?? [];
        var targets = kin.Select(k => pt.IndexOf(k.Person)).Where(i => i >= 0 && i != hero && pt.Status[i] == PersonStatus.Alive && w.St.Captivity?.Find(pt.IdOf(i)) is not { Held: true }).Distinct().OrderBy(i => pt.IdOf(i).Value).ToList();
        if (targets.Count == 0) return false;
        int victim = targets[DetRng.Range(w.Seed, season, w.HeroId.Value, RngStreams.Kidnapping, 2, 0, targets.Count)];
        var cap = AftermathDesk.BookOf(w.St, w.Content); var R = cap.Rules;
        var rank = pt.Class[victim] switch { SocialClass.Gentry => CaptiveRank.Knight, SocialClass.Burgher => CaptiveRank.ManAtArms, _ => CaptiveRank.Levy };
        long ransom = Ransom.ForRank(R, rank);
        var band = AftermathCoin.BandId(0x4B1D_0000UL + (ulong)season);
        w.Ledger.Open(PurseKind.Household, band);
        long sec = day * 86400L + 4 * 3600; int s = pt.HomeSettlement[victim]; var region = w.Pop.Region;
        long px = s >= 0 && s < region.Settlements.Count ? region.Settlements[s].CenterX * 1000L : 0, py = s >= 0 && s < region.Settlements.Count ? region.Settlements[s].CenterY * 1000L : 0;
        int Sk(Skill k) => pt.SkillLevelOf(victim, k);
        cap.Capture(R, CaptorKind.Bandits, pt.IdOf(victim), false, band, new PurseRef(PurseKind.Household, band), sec, rank, ransom, PayerKind.Household, [new PurseRef(PurseKind.Person, w.HeroId)], [],
            (Sk(Skill.Stealth), Sk(Skill.Athletics), Sk(Skill.Lockpicking), pt.Attribute(victim, PersonTable.PerceptionIdx), Sk(Skill.Speech)), 0, px, py);
        w.Book.Kidnaps++;
        var ps = new List<ChronParam> { ChronParam.Person("victim", pt.IdOf(victim)), ChronParam.Person("hero", w.HeroId), ChronParam.Mites("ransom", ransom) };
        if (w.PlaceId(s) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(s)));
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "kin_kidnapped", [w.HeroId, pt.IdOf(victim)], [new Cause(CauseKind.Crime, null, NoteKey: "ch_note_kidnappers")],
            new Dictionary<string, string> { ["name"] = pt.FullName(victim), ["settlement"] = w.PlaceName(s), ["ransom"] = ransom.ToString(CultureInfo.InvariantCulture) }, TextKey: "ch_family_kidnapped", Params: ps));
        return true;
    }
}
