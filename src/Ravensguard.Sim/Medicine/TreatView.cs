using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Medicine;

/// <summary>One wound on the paper-doll (medicine 4.1): its part, sort, grade, state, dressing, the live bleed in hundredths of ml/s, when it came and whom it is owed to.</summary>
public sealed record TreatWoundView(uint Id, int Part, WoundCat Cat, WoundKind Kind, int Grade, WoundState State, Dressing Dressing, int BleedCentiMlPerS, int Infection, long BornSec, ulong Cause, int TourniquetNextS, Aid Aids);

/// <summary>One verb the paper-doll offers on a part: the time, what it uses and whether the pack has it, the chance, the skill floor, why it is refused (a text key) and a physician's fee (0 when the hero does it).</summary>
public sealed record TreatVerbView(TreatKind Kind, int Part, int SimS, IReadOnlyList<(string Item, int Count)> Uses, bool HasItems, int ChancePct, int MinSkill, bool Allowed, string Refusal, int FeeMites);

/// <summary>A paper-doll part (a non-hosted part, its hosted parts' wounds folded in): the plan's part id and side, its wounds, the verbs, the worst live bleed.</summary>
public sealed record TreatPartView(int Part, string Id, string BaseId, int Side, IReadOnlyList<TreatWoundView> Wounds, IReadOnlyList<TreatVerbView> Verbs, int BleedCentiMlPerS);

/// <summary>What is queued, for the bar at the bottom of the screen.</summary>
public sealed record TreatStepView(ulong Patient, TreatKind Kind, int Part, long FinishSec, bool Planned, bool DressAll);

/// <summary>
/// What the treatment screen shows for one patient (medicine 4.1, 6), built at a barrier from the patient's book and the pack: every part with its wounds and verbs, the rate of bleeding now, the
/// blood left, the queue and the last outcomes. Pure data; the screen draws it and sends <see cref="HeroTreatDesk"/> commands.
/// </summary>
public sealed record TreatView(ulong Patient, bool Self, bool Dead, long RateMilli, int BloodMl, int BloodMaxMl, int SecondsToCollapse, int Skill, int PainPct,
    IReadOnlyList<TreatPartView> Parts, IReadOnlyList<TreatStepView> Queue, IReadOnlyList<TreatOutcome> Log, int BleedingWounds, long NowSec, string PlanId)
{
    /// <summary>A physician's quote (medicine 4.4) when the patient is in his hands: the lines he would do with the fee and the chance; null when the hero does the work himself.</summary>
    public IReadOnlyList<TreatVerbView>? Quote { get; init; }
    /// <summary>The physician's person id (0 when none) and sort.</summary>
    public ulong Physician { get; init; }
    public PhysicianKind? PhysicianSort { get; init; }
    /// <summary>Bandages in the pack, for the header.</summary>
    public int Bandages { get; init; }
    /// <summary>The part with the worst live bleed, or -1 when nothing bleeds (where the after-fight report's call to action puts the cursor).</summary>
    public int WorstBleederPart { get { int best = -1, rate = 0; foreach (var p in Parts) if (p.BleedCentiMlPerS > rate) { rate = p.BleedCentiMlPerS; best = p.Part; } return best; } }
}

public static class TreatViews
{
    /// <summary>
    /// Builds the view. <paramref name="items"/> is what the helper holds (the pack); <paramref name="skill"/> his surgery; <paramref name="self"/> says the patient is the helper himself (two-handed
    /// verbs are refused); <paramref name="who"/> is a physician or barber-surgeon when the fees are to be shown.
    /// </summary>
    public static TreatView Build(ulong patient, WoundBook book, BodyPlanV2 plan, ITreatItems items, int skill, bool self, long nowSec, TreatQueue? queue = null, PhysicianKind? who = null)
    {
        long s = Math.Max(book.LastSec, nowSec) / MedicineTables.TicksPerSimS;
        var byPart = new Dictionary<int, List<WoundRecord>>();
        foreach (var w in book.Records)
        {
            if (w.Is(Aid.Resolved) && w.State == WoundState.Scar) continue;
            int host = plan.Parts[w.Part].IsHosted ? plan.Parts[w.Part].Host : w.Part;
            if (!byPart.TryGetValue(host, out var l)) byPart[host] = l = [];
            l.Add(w);
        }
        var parts = new List<TreatPartView>();
        int bleeding = 0;
        for (int i = 0; i < plan.Parts.Length; i++)
        {
            var pp = plan.Parts[i];
            if (pp.IsHosted) continue;
            byPart.TryGetValue(i, out var recs); recs ??= [];
            var wounds = new List<TreatWoundView>(); int worst = 0;
            foreach (var w in recs)
            {
                int rate = (int)(WoundBook.LossMilli(w, s) / 10);
                if (rate > 0 && w.Dressing == Dressing.None) bleeding++;
                worst = Math.Max(worst, rate);
                int next = -1;
                if (w.Dressing == Dressing.Tourniquet && w.TourniquetOnS > 0)
                {
                    uint on = w.TourniquetOnS;
                    int step = on < WoundBook.IschaemiaStep1S ? WoundBook.IschaemiaStep1S : on < WoundBook.IschaemiaStep2S ? WoundBook.IschaemiaStep2S : WoundBook.IschaemiaLossS;
                    next = (int)(step - on);
                }
                wounds.Add(new TreatWoundView(w.Id, w.Part, w.Cat, w.Kind, w.Grade, w.State, w.Dressing, rate, w.InfectionLevel, w.Born, w.Cause, next, w.Aids));
            }
            var verbs = VerbsFor(book, plan, i, recs, items, skill, self, nowSec, who);
            parts.Add(new TreatPartView(i, pp.Id, pp.BaseId, pp.Side, wounds, verbs, worst));
        }
        var q = new List<TreatStepView>();
        if (queue != null) foreach (var st in queue.Steps) q.Add(new TreatStepView(st.Patient, st.Kind == TreatQueue.DressAllKind ? TreatKind.Bandage : (TreatKind)st.Kind, st.Part, st.FinishSec, st.Planned, st.Kind == TreatQueue.DressAllKind));
        int pain = book.KnockoutPainX100 <= 0 ? 0 : (int)(book.PainX100 * 100L / book.KnockoutPainX100);
        return new TreatView(patient, self, book.Dead, book.RateMilliAt(s), book.BloodMl, (int)(book.BloodMaxUl / 1000), book.SecondsToCollapse(), skill, pain, parts, q, queue?.Log ?? [], bleeding, nowSec, book.PlanId);
    }

    /// <summary>The verbs that apply to a part given the wounds on it (and, for a tourniquet, on the limb below it).</summary>
    public static List<TreatVerbView> VerbsFor(WoundBook book, BodyPlanV2 plan, int part, IReadOnlyList<WoundRecord> recs, ITreatItems items, int skill, bool self, long now, PhysicianKind? who = null)
    {
        var list = new List<TreatVerbView>(); var seen = new HashSet<TreatKind>();
        void Add(TreatKind k, int at, int grade = 1)
        {
            if (!seen.Add(k)) return;
            if (who is { } m && !PhysicianFees.Offers(m, k)) return;
            var needs = Treatment.Needs(k);
            bool has = needs.Count == 0 || (Treatment.MinSkill(k) >= 0 && HasAny(items, k, needs));
            int min = Treatment.MinSkill(k);
            string refusal = self && HeroTreatDesk.TwoHanded(k) ? "tr_two_hands" : skill < min ? "tr_skill" : "";
            int fee = who is { } w2 ? PhysicianFees.Fee(w2, k) : 0;
            list.Add(new TreatVerbView(k, at, Treatment.SimSeconds(k), needs, has, PhysicianFees.ChancePct(k, skill, grade, herbs: items.Has("poultice", 1)), min, refusal.Length == 0, refusal, fee));
        }
        foreach (var w in recs)
        {
            if (w.Is(Aid.Resolved) || w.State == WoundState.Scar) continue;
            bool bleeds = w.BleedX10 > 0 && !w.Internal;
            int root = TreatAi.LimbRoot(plan, w.Part);
            if (bleeds && w.Dressing == Dressing.None)
            {
                Add(TreatKind.Pressure, part); Add(TreatKind.Bandage, part);
                if (root >= 0) Add(TreatKind.Tourniquet, root);
            }
            if (w.Dressing == Dressing.Pressure) Add(TreatKind.ReleasePressure, part);
            if (w.Dressing == Dressing.Tourniquet) { Add(TreatKind.ReleasePressure, w.Root >= 0 ? w.Root : part); if (root >= 0 && plan.Parts[root].Severable) Add(TreatKind.Amputate, root); }
            switch (w.Cat)
            {
                case WoundCat.Bone:
                    if (w.Grade >= 2 && !w.Is(Aid.Set)) Add(TreatKind.SetBone, part, w.Grade0);
                    if (w.Grade >= 1 && !w.Is(Aid.Splinted)) Add(TreatKind.Splint, part);
                    break;
                case WoundCat.Tendon when !w.Is(Aid.Repaired): Add(TreatKind.TendonRepair, part); break;
                case WoundCat.Ligament when !w.Is(Aid.Repaired) && w.Grade0 >= 2: Add(TreatKind.LigamentRepair, part); break;
                case WoundCat.Organ when w.Internal && !w.Is(Aid.Repaired): Add(TreatKind.OrganRepair, part); break;
                case WoundCat.Skin or WoundCat.Muscle or WoundCat.Vessel when w.Kind == WoundKind.Cut && w.DepthPm >= 500 && w.State != WoundState.Stitched: Add(TreatKind.Stitch, part); break;
            }
            if (w.InfectionLevel > 0) Add(TreatKind.Clean, part);
        }
        return list;
    }

    private static bool HasAny(ITreatItems items, TreatKind k, IReadOnlyList<(string Item, int Count)> needs)
    {
        if (MedicineRules.Current.Treat[(int)k].ToolQuality.Length > 0) return items.Has(needs[0].Item, needs[0].Count);
        foreach (var (item, n) in needs) if (items.Has(item, n)) return true;
        return false;
    }

    /// <summary>The view of a patient for the hero at a barrier (the game asks this through <c>AtBarrier</c>); null when the patient has no book (nothing to treat).</summary>
    public static TreatView? ForHero(HeroContext ctx, ulong patient, long nowSec, ulong? physician = null)
    {
        var l = ctx.State.Wounds; if (l == null) return null;
        lock (l.Sync)
        {
            if (l.Find(patient) is not { } book) return null;
            var plan = MedicineWiring.PlanOf(ctx.Content, book.PlanId); if (plan == null) return null;
            PhysicianKind? who = null; int skill = HeroTreatDesk.SkillOf(ctx); ITreatItems items = HeroTreatDesk.ItemsOf(ctx);
            var med = ctx.State.Medicine;
            if (physician is { } man && med != null && med.TryGet(man, out var row))
            {
                who = row.Kind; int mrow = ctx.State.Population.People.IndexOf(new Ravensguard.Sim.Core.EntityId(man));
                if (mrow >= 0) skill = ctx.State.Population.People.SkillLevelOf(mrow, Ravensguard.Sim.People.Skill.Surgery);
                items = TreatDesk.Kit();
            }
            var v = Build(patient, book, plan, items, skill, patient == ctx.Party.Hero.Value && who == null, nowSec, med?.Queue, who);
            if (ctx.Econ.Goods.TryIdOf("bandage", out var bg)) v = v with { Bandages = (int)Math.Min(999, ctx.Pack.Qty(bg)) };
            if (who is { } sort && physician is { } man2)
            {
                var quote = TreatDesk.Quote(book, plan, sort, skill, nowSec).Select(q => new TreatVerbView(q.Kind, q.Part, Treatment.SimSeconds(q.Kind), [], true, q.ChancePct, Treatment.MinSkill(q.Kind), true, "", q.FeeMites)).ToList();
                v = v with { Quote = quote, Physician = man2, PhysicianSort = sort };
            }
            return v;
        }
    }
}
