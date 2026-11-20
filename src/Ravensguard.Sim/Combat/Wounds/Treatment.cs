using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using static Ravensguard.Sim.Combat.Wounds.MedicineTables;

namespace Ravensguard.Sim.Combat.Wounds;

public enum TreatKind : byte { Pressure, Bandage, Tourniquet, Splint, SetBone, Stitch, Clean, Cauterise, TendonRepair, LigamentRepair, OrganRepair, Amputate, ReleasePressure }

/// <summary>What a treatment may consume: the inventory of the helper (or a test's bag). Items are good ids (<c>bandage</c>, <c>splint</c>, <c>rope</c>, <c>poultice</c>, <c>herbs</c>, <c>needle_and_thread</c>).</summary>
public interface ITreatItems
{
    bool Has(string item, int count);
    void Take(string item, int count);
}

/// <summary>A bag that holds counts, for tests and the harness.</summary>
public sealed class ItemBag : ITreatItems
{
    private readonly Dictionary<string, int> _n = new(StringComparer.Ordinal);
    public ItemBag Add(string item, int n) { _n[item] = _n.GetValueOrDefault(item) + n; return this; }
    public int Count(string item) => _n.GetValueOrDefault(item);
    public bool Has(string item, int count) => _n.GetValueOrDefault(item) >= count;
    public void Take(string item, int count) => _n[item] = _n.GetValueOrDefault(item) - count;
}

public readonly record struct TreatRequest(TreatKind Kind, int Part, long GameSec, int Skill = 0, ulong Actor = 0, bool Light = true, bool Fire = false)
{
    /// <summary>Pain relief on the patient (poppy, ale) halves the surgery scream.</summary>
    public bool Dulled { get; init; }
}

public readonly record struct TreatResult(bool Ok, bool Success, string Reason, int SimSeconds)
{
    public static TreatResult No(string why) => new(false, false, why, 0);
}

/// <summary>
/// First aid and surgery (wounds 4.3, 4.6). <see cref="SimSeconds"/> is how long the action takes; the caller (a job step, the AI, the hero's menu) waits that long, then calls
/// <see cref="Perform"/> at the completion time. Perform advances the book to that time first, so both tiers see the same blood.
/// </summary>
public static class Treatment
{
    /// <summary>How long a treatment takes, sim-seconds (content <c>medicine/treatments.jsonc</c>).</summary>
    public static int SimSeconds(TreatKind k) => MedicineRules.Current.Treat[(int)k].SimS;

    public static int MinSkill(TreatKind k) => MedicineRules.Current.Treat[(int)k].MinSkill;

    /// <summary>The items a treatment consumes, as (good, count) alternatives in order; the first that is held is taken.</summary>
    public static IReadOnlyList<(string Item, int Count)> Needs(TreatKind k) => MedicineRules.Current.Treat[(int)k].Needs;

    /// <summary>True when the surgery wants light (content).</summary>
    public static bool NeedsLight(TreatKind k) => MedicineRules.Current.Treat[(int)k].Light;

    private static bool Take(ITreatItems? items, TreatKind k)
    {
        var needs = Needs(k);
        if (needs.Count == 0 || items == null) return needs.Count == 0 || items == null && false;
        if (MedicineRules.Current.Treat[(int)k].ToolQuality.Length > 0) return items.Has(needs[0].Item, needs[0].Count);   // a tool (the sewing needle): kept
        foreach (var (item, n) in needs) if (items.Has(item, n)) { items.Take(item, n); return true; }
        return false;
    }

    public static bool Descends(BodyPlanV2 plan, int part, int root)
    {
        for (int p = part; p >= 0; p = plan.Parts[p].Parent) if (p == root) return true;
        var pp = plan.Parts[part]; return pp.IsHosted && pp.Host >= 0 && Descends(plan, pp.Host, root);
    }

    private static int Worst(WoundBook b, int part, long sec, Func<WoundRecord, bool>? pred = null)
    {
        long s = sec / TicksPerSimS; int best = -1; long rate = 0;
        for (int i = 0; i < b.Records.Count; i++)
        {
            var w = b.Records[i];
            if (w.Internal || w.Is(Aid.Resolved) || w.State == WoundState.Scar || w.BleedX10 <= 0) continue;
            if (part >= 0 && w.Part != part) continue;
            if (pred != null && !pred(w)) continue;
            long r = (long)w.BleedX10 * 100;                 // by the base rate: the worst wound first
            if (w.Clots) r = r * (long)(ClotQ32(s - w.Born / TicksPerSimS) >> 16) >> 16;
            if (r > rate) { rate = r; best = i; }
        }
        return best;
    }

    /// <summary>The wound a helper should dress first on <paramref name="part"/> (-1: anywhere): the highest remaining undressed external rate, or -1.</summary>
    public static int WorstBleeder(WoundBook b, int part, long sec) => Worst(b, part, sec, w => w.Dressing == Dressing.None);

    /// <param name="plan">The patient's body plan; null where none is at hand (the abstract tier): a tourniquet and an amputation then cannot be done.</param>
    public static TreatResult Perform(WoundBook b, BodyPlanV2? plan, in TreatRequest r, ITreatItems? items = null)
    {
        if (b.Dead) return TreatResult.No("dead");
        b.AdvanceTo(r.GameSec);
        if (b.Dead) return TreatResult.No("dead");
        int secs = SimSeconds(r.Kind);
        long now = r.GameSec; long nowS = (now + TicksPerSimS - 1) / TicksPerSimS;
        if (r.Skill < MinSkill(r.Kind)) return TreatResult.No($"needs surgery skill {MinSkill(r.Kind)}");
        switch (r.Kind)
        {
            case TreatKind.Pressure:
                {
                    int i = r.Part < 0 ? Worst(b, -1, now, w => w.Dressing == Dressing.None) : Worst(b, r.Part, now, w => w.Dressing == Dressing.None);
                    if (i < 0) return TreatResult.No("nothing bleeds there");
                    var w = b.Records[i];
                    w.Dressing = Dressing.Pressure; w.DressFromS = nowS + PressureEffectDelayS; w.DressedAt = now; w.State = WoundState.Dressed; WoundBook.Retime(ref w);
                    b.Records[i] = w;
                    return new(true, true, "", secs);
                }
            case TreatKind.ReleasePressure:
                {
                    // a pressure hold lets go; a tourniquet is loosened (medicine 4.3): the wound bleeds again at its current rate and the clock of the limb stays where it is
                    for (int i = 0; i < b.Records.Count; i++)
                    {
                        var w = b.Records[i];
                        if (w.Dressing == Dressing.Pressure) { if (r.Part >= 0 && w.Part != r.Part) continue; }
                        else if (w.Dressing == Dressing.Tourniquet) { if (r.Part >= 0 && w.Root != r.Part && w.Part != r.Part) continue; }
                        else continue;
                        w.Dressing = Dressing.None; if (w.State == WoundState.Dressed) w.State = WoundState.Open; b.Records[i] = w;
                    }
                    return new(true, true, "", secs);
                }
            case TreatKind.Bandage:
                {
                    int i = Worst(b, r.Part, now, w => w.Dressing is Dressing.None or Dressing.Pressure);
                    if (i < 0) return TreatResult.No("nothing to bandage");
                    if (!Take(items, TreatKind.Bandage)) return TreatResult.No("no bandage");
                    int part = b.Records[i].Part;
                    for (int k = 0; k < b.Records.Count; k++)
                    {
                        var w = b.Records[k];
                        if (w.Part != part || w.Internal || w.Is(Aid.Resolved) || w.State == WoundState.Scar || w.Dressing is not (Dressing.None or Dressing.Pressure)) continue;
                        if (w.BleedX10 <= 0 && w.Kind != WoundKind.Cut) continue;
                        w.Dressing = Dressing.Bandage; w.DressFromS = nowS; w.DressedAt = now; if (w.State == WoundState.Open) w.State = WoundState.Dressed; WoundBook.Retime(ref w);
                        b.Records[k] = w;
                    }
                    return new(true, true, "", secs);
                }
            case TreatKind.Tourniquet:
                {
                    if (plan == null) return TreatResult.No("no body plan");
                    if (r.Part < 0 || plan.Parts[r.Part].Limb == null || plan.Parts[r.Part].IsHosted) return TreatResult.No("a tourniquet goes on a limb");
                    bool any = false;
                    for (int k = 0; k < b.Records.Count; k++)
                        if (Descends(plan, b.Records[k].Part, r.Part) && !b.Records[k].Internal && b.Records[k].BleedX10 > 0 && b.Records[k].State != WoundState.Scar && !b.Records[k].Is(Aid.Resolved)) { any = true; break; }
                    if (!any) return TreatResult.No("nothing bleeds below there");
                    if (!Take(items, TreatKind.Tourniquet)) return TreatResult.No("no rope or bandages");
                    // the limb's clock never resets (medicine 4.3): a tourniquet tied again carries on from what the limb has already had; the distal parts are noted for the ischaemia steps
                    uint clock = 0; ulong distal = 0;
                    for (int k = 0; k < b.Records.Count; k++) if (b.Records[k].Root == r.Part || Descends(plan, b.Records[k].Part, r.Part)) clock = Math.Max(clock, b.Records[k].TourniquetOnS);
                    for (int pi = 0; pi < plan.Parts.Length && pi < 64; pi++) if (Descends(plan, pi, r.Part)) distal |= 1UL << pi;
                    for (int k = 0; k < b.Records.Count; k++)
                    {
                        var w = b.Records[k];
                        if (!Descends(plan, w.Part, r.Part) || w.Internal || w.BleedX10 <= 0 || w.State == WoundState.Scar || w.Is(Aid.Resolved)) continue;
                        w.Dressing = Dressing.Tourniquet; w.DressFromS = nowS; w.DressedAt = now; w.Root = (short)r.Part; w.TourniquetOnS = clock; w.DistalMask = distal; b.Records[k] = w;
                    }
                    return new(true, true, "", secs);
                }
            case TreatKind.Splint:
                {
                    int i = FindBone(b, r.Part, w => !w.Is(Aid.Splinted));
                    if (i < 0) return TreatResult.No("no broken bone there");
                    if (!Take(items, TreatKind.Splint)) return TreatResult.No("no splint");
                    var w = b.Records[i]; w.Aids |= Aid.Splinted; WoundBook.Retime(ref w); b.Records[i] = w;
                    return new(true, true, "", secs);
                }
            case TreatKind.SetBone:
                {
                    int i = FindBone(b, r.Part, w => !w.Is(Aid.Set) && w.Grade0 >= 2);
                    if (i < 0) return TreatResult.No("no unset fracture there");
                    var w = b.Records[i];
                    int p = Math.Clamp(30 + r.Skill * 6 / 10 - 10 * w.Grade0, 5, 95);
                    if (now - w.Born > GameSecPerDay) p -= 30;                             // the callus has begun
                    p = Math.Max(p, 5);
                    bool ok = Roll(b, r, w.Id, p);
                    if (ok) { w.Aids |= Aid.Set; w.Aids &= ~Aid.SetFailed; } else w.Aids |= Aid.SetFailed;
                    WoundBook.Retime(ref w); b.Records[i] = w; b.PainX100 += 1000;
                    return new(true, ok, ok ? "" : "the bone slips", secs);
                }
            case TreatKind.Stitch:
                {
                    if (items != null && !Take(items, TreatKind.Stitch)) return TreatResult.No("no needle and thread");
                    int n = 0;
                    for (int k = 0; k < b.Records.Count; k++)
                    {
                        var w = b.Records[k];
                        if (w.Part != r.Part && r.Part >= 0 || w.Cat is not (WoundCat.Skin or WoundCat.Muscle or WoundCat.Vessel) || w.Kind != WoundKind.Cut || w.State is WoundState.Stitched or WoundState.Scar || w.Is(Aid.Resolved)) continue;
                        w.State = WoundState.Stitched; w.StitchedAt = now;
                        if (w.Dressing == Dressing.None) { w.Dressing = Dressing.Bandage; w.DressFromS = nowS; w.DressedAt = now; }
                        WoundBook.Retime(ref w); b.Records[k] = w; n++;
                    }
                    return n == 0 ? TreatResult.No("nothing to stitch") : new(true, true, "", secs);
                }
            case TreatKind.Clean:
                {
                    bool herbs = items != null && (items.Has("poultice", 1) ? Use(items, "poultice") : items.Has("herbs", 1) && Use(items, "herbs"));
                    int p = Math.Clamp(30 + r.Skill * 4 / 10 + (herbs ? 20 : 0), 20, 85);
                    int cleared = 0;
                    for (int k = 0; k < b.Records.Count; k++)
                    {
                        var w = b.Records[k];
                        if (r.Part >= 0 && w.Part != r.Part) continue;
                        w.Aids &= ~Aid.Dirty; w.DressedAt = w.Dressing == Dressing.None ? w.DressedAt : now;
                        if (w.InfectionLevel > 0 && Roll(b, r, w.Id, p)) { w.InfectionLevel--; w.SepsisDay = w.InfectionLevel == 3 ? w.SepsisDay : 0; if (w.InfectionLevel < 3) w.DeathDay = 0; cleared++; }
                        b.Records[k] = w;
                    }
                    return new(true, cleared > 0, "", secs);
                }
            case TreatKind.Cauterise:
                {
                    if (!r.Fire) return TreatResult.No("needs fire and iron");
                    int n = 0;
                    for (int k = 0; k < b.Records.Count; k++)
                    {
                        var w = b.Records[k];
                        if (w.Part != r.Part && r.Part >= 0 || w.Internal || w.BleedX10 <= 0 || w.State == WoundState.Scar || w.Is(Aid.Resolved)) continue;
                        if (w.Has(WoundFlags.ArteryHit) && !Roll(b, r, w.Id, 40)) continue;
                        w.BleedX10 = 0; w.Aids |= Aid.Cauterised; if (w.State == WoundState.Open) w.State = WoundState.Dressed; WoundBook.Retime(ref w); b.Records[k] = w; n++;
                    }
                    b.PainX100 += 4000;
                    return n == 0 ? TreatResult.No("the iron cannot stop it") : new(true, true, "", secs);
                }
            case TreatKind.TendonRepair: return Repair(b, r, WoundCat.Tendon, 1, 50, secs, items);
            case TreatKind.LigamentRepair: return Repair(b, r, WoundCat.Ligament, 2, 60, secs, items);
            case TreatKind.OrganRepair: return Repair(b, r, WoundCat.Organ, int.MaxValue / 2, 60, secs, items);
            case TreatKind.Amputate:
                {
                    if (plan == null) return TreatResult.No("no body plan");
                    if (r.Part < 0 || plan.Parts[r.Part].Limb == null || !plan.Parts[r.Part].Severable) return TreatResult.No("not a limb");
                    bool tq = false;
                    foreach (var w in b.Records) if (w.Dressing == Dressing.Tourniquet && w.Root >= 0 && Descends(plan, r.Part, w.Root) | w.Root == r.Part) { tq = true; break; }
                    if (!tq) return TreatResult.No("tie a tourniquet first");
                    var gone = new List<int>();
                    for (int pi = 0; pi < plan.Parts.Length; pi++) if (Descends(plan, pi, r.Part)) gone.Add(pi);
                    b.LoseLimb(r.Part, now, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(gone));
                    b.PainX100 += r.Dulled ? 1500 : 3000;
                    return new(true, true, "", secs);
                }
        }
        return TreatResult.No("unknown");
    }

    private static bool Use(ITreatItems items, string id) { items.Take(id, 1); return true; }

    private static int FindBone(WoundBook b, int part, Func<WoundRecord, bool> pred)
    {
        for (int i = 0; i < b.Records.Count; i++)
        {
            var w = b.Records[i];
            if (w.Cat != WoundCat.Bone || w.Is(Aid.Resolved) || w.Grade < 1 || part >= 0 && w.Part != part || !pred(w)) continue;
            return i;
        }
        return -1;
    }

    private static bool Roll(WoundBook b, in TreatRequest r, uint woundId, int pct) =>
        DetRng.Range(b.Seed, r.GameSec / TicksPerSimS, r.Actor, RngStreams.SurgeryResult, woundId, 0, 100) < pct;

    /// <summary>Tendon (skill 50, within 1 game day), ligament (skill 60, 2 days) and organ (skill 60) surgery: <c>P = clamp(40 + (skill - min), 40, 95)</c>, one retry at -20 (wounds 4.6).</summary>
    private static TreatResult Repair(WoundBook b, in TreatRequest r, WoundCat cat, int minGrade, int minSkill, int secs, ITreatItems? items)
    {
        if (!r.Light) return TreatResult.No("too dark to operate");
        if (items != null && !items.Has("needle_and_thread", 1)) return TreatResult.No("no needle and thread");
        long window = cat == WoundCat.Tendon ? GameSecPerDay : cat == WoundCat.Ligament ? 2L * GameSecPerDay : long.MaxValue / 4;
        int idx = -1;
        for (int i = 0; i < b.Records.Count; i++)
        {
            var w = b.Records[i];
            if (w.Cat != cat || w.Is(Aid.Repaired) || w.Is(Aid.Resolved) || r.Part >= 0 && w.Part != r.Part) continue;
            if (cat == WoundCat.Ligament && w.Grade0 < minGrade + 0 && w.Grade0 < 3) continue;
            if (cat == WoundCat.Organ && !w.Internal) continue;
            idx = i; break;
        }
        if (idx < 0) return TreatResult.No("nothing to repair");
        var wd = b.Records[idx];
        if (r.GameSec - wd.Born > window) return TreatResult.No("too late: it has set");
        if (wd.Is(Aid.Retried)) return TreatResult.No("one retry only");
        int p = Math.Clamp(40 + (r.Skill - minSkill), 40, 95);
        if (wd.Is(Aid.TendonTried)) p -= 20;
        bool ok = Roll(b, r, wd.Id, p);
        if (ok) { wd.Aids |= Aid.Repaired; WoundBook.Retime(ref wd); if (wd.State == WoundState.Open) wd.State = WoundState.Dressed; }
        else wd.Aids |= wd.Is(Aid.TendonTried) ? Aid.Retried : Aid.TendonTried;
        b.Records[idx] = wd;
        b.PainX100 += r.Dulled ? 1500 : 3000;
        return new(true, ok, ok ? "" : "the stitches do not hold", secs);
    }
}
