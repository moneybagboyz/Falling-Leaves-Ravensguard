using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Medicine;

/// <summary>
/// Physicians and barber-surgeons at the Phase 6 seeding pass (medicine 4.4, overview 7): one physician per 1,000 people of a town (at least one) and one barber-surgeon in each village of 150 or
/// more. Keyed on the world seed and person ids only (the ages are those of the generation's own "now", never the day the pass runs), so a new game and a v8 save loaded later derive the same men.
/// The pick is the adult of the settlement with the highest <see cref="Skill.Surgery"/>, else the highest Wits, lowest row first; people who run a business, a manor or the church are not asked. His
/// Surgery becomes at least 45 + Range(0, 26) (barber 25 + Range(0, 16)), stream 0x600 draw 0; he takes the <c>physician</c> business (his own capital 0) when the world has an economy, and the
/// occupation <c>Physician</c>. The shared seeding-pass skeleton (A2) calls <see cref="Run"/>.
/// </summary>
public static class PhysicianSeeding
{
    /// <summary>Seeds the world's <see cref="PhysicianBook"/> (made and put in the service bag when missing); a book that already has rows is left alone. Returns the book.</summary>
    public static PhysicianBook Run(SimWorld w)
    {
        var book = w.Get<PhysicianBook>();
        if (book == null) { book = new PhysicianBook(); w.Set(book); }
        if (!book.IsEmpty) return book;
        var rules = MedicineRules.Current.Rules;
        var pt = w.People; var region = w.Population.Region;
        var ew = w.Get<EconomyWorld>();
        var kind = ew?.Content.Kinds.FirstOrDefault(k => k.Id == "physician");
        var busy = new HashSet<int>();
        if (ew != null) foreach (var b in ew.Production.Businesses) { if (b.Owner >= 0) busy.Add(b.Owner); foreach (int s in b.Staff) busy.Add(s); }

        // the living people of each settlement, once
        var folk = new List<int>[region.Settlements.Count];
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i)) continue;
            int s = pt.HomeSettlement[i]; if (s < 0 || s >= folk.Length) continue;
            (folk[s] ??= []).Add(i);
        }
        int nowDay = w.Population.NowDay;
        bool founded = false;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            var list = folk[s]; if (list == null) continue;
            var cls = region.Settlements[s].Class;
            int want; PhysicianKind who;
            if (cls == SettlementClass.Town) { want = Math.Max(1, list.Count / Math.Max(1, rules.PeoplePerPhysician)); who = PhysicianKind.Physician; }
            else if (cls == SettlementClass.Village && list.Count >= rules.BarberMinPeople) { want = 1; who = PhysicianKind.Barber; }
            else continue;
            var picked = new HashSet<int>();
            for (int n = 0; n < want; n++)
            {
                int best = -1; int bSurg = -1, bWits = -1;
                foreach (int p in list)
                {
                    if (picked.Contains(p) || busy.Contains(p) || pt.AgeYears(p, nowDay) < 18 || Barred(pt.Occupation[p])) continue;
                    int su = pt.SkillLevelOf(p, Skill.Surgery), wi = pt.Attribute(p, PersonTable.WitsIdx);
                    if (best < 0 || su > bSurg || su == bSurg && wi > bWits) { best = p; bSurg = su; bWits = wi; }
                }
                if (best < 0) break;
                picked.Add(best);
                ulong id = pt.IdOf(best).Value;
                int lo = who == PhysicianKind.Physician ? rules.PhysicianSurgeryBase : rules.BarberSurgeryBase, spread = who == PhysicianKind.Physician ? rules.PhysicianSurgerySpread : rules.BarberSurgerySpread;
                int level = Math.Max(bSurg, lo + DetRng.Range(w.Seed, 0, id, RngStreams.Physician, 0, 0, Math.Max(1, spread)));
                SetSurgery(pt, best, level);
                int biz = -1;
                if (ew != null && kind != null)
                {
                    var b = ew.Production.NewBusiness(kind, s, pt.HomeLot[best], best);
                    ew.Production.SetOwner(b, best); b.OwnerWorks = false;
                    b.CashStart = 0; b.Capital = 0; biz = b.Index; founded = true;
                }
                pt.Occupation[best] = Occupation.Physician; pt.WorkSettlement[best] = s;
                book.Add(new PhysicianRow(id, biz, who, s));
            }
        }
        if (founded) ew!.Labour.Rebuild();
        return book;
    }

    private static bool Barred(Occupation o) => o is Occupation.Lord or Occupation.Steward or Occupation.Mayor or Occupation.GuildMaster or Occupation.Priest or Occupation.Guard or Occupation.Reeve;

    /// <summary>Raises (never lowers) the surgery skill, taking the slot that holds it, else an empty one, else the weakest.</summary>
    private static void SetSurgery(PersonTable pt, int p, int level)
    {
        int slot = -1, weakest = 0, weakLevel = int.MaxValue;
        for (int k = 0; k < PersonTable.SkillSlots; k++)
        {
            var s = pt.SkillAt(p, k);
            if (s.Skill == Skill.Surgery) { slot = k; break; }
            if (s.Skill == Skill.None && slot < 0) slot = k;
            if (s.Level < weakLevel) { weakLevel = s.Level; weakest = k; }
        }
        if (slot < 0) slot = weakest;
        if (pt.SkillAt(p, slot).Skill == Skill.Surgery && pt.SkillAt(p, slot).Level >= level) return;
        pt.SetSkill(p, slot, Skill.Surgery, level);
    }
}
