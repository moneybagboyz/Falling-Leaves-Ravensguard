using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Medicine;

/// <summary>What happened to one line of a paid treatment.</summary>
public readonly record struct DeskOutcome(QuoteLine Line, bool Done, bool Success, string Why, int PaidMites);

/// <summary>
/// The physician's side of "Treat me" (medicine 4.4): <see cref="Quote"/> lists, wound by wound, what he would do (the <see cref="TreatAi.Next"/> order for bleeders, then the repairs and the
/// set a patient with time on his hands would want) with the fee from <c>prices.jsonc</c> and the chance his skill gives; <see cref="Perform"/> does the lines the patient picks, each at its
/// finish second with the Phase 4 rules and streams (0x420 to 0x422), and moves each fee from the patient's purse to the man's business box. The hero's screen (step 3, B1) wraps it.
/// </summary>
public static class TreatDesk
{
    /// <summary>The man's kit: bandages, rope, splints, a needle and a poultice or two; their price is in his fees.</summary>
    public static ItemBag Kit() => new ItemBag().Add("bandage", 8).Add("rope", 2).Add("splint", 2).Add("needle_and_thread", 1).Add("poultice", 2);

    public static List<QuoteLine> Quote(WoundBook patient, BodyPlanV2? plan, PhysicianKind who, int skill, long now)
    {
        var lines = new List<QuoteLine>();
        var seen = new HashSet<(TreatKind, int)>();
        void Add(TreatKind k, int part, int grade = 1)
        {
            if (skill < Treatment.MinSkill(k) || !PhysicianFees.Offers(who, k) || !seen.Add((k, part))) return;
            lines.Add(new QuoteLine(k, part, PhysicianFees.Fee(who, k), PhysicianFees.ChancePct(k, skill, grade, herbs: true)));
        }
        if (patient.Dead) return lines;
        // the bleeders first, worst by rate, as a helper would (a tourniquet on a limb that spurts, else a bandage)
        int worst = Treatment.WorstBleeder(patient, -1, now);
        if (worst >= 0)
        {
            var rec = patient.Records[worst];
            int root = plan == null ? -1 : TreatAi.LimbRoot(plan, rec.Part);
            if (root >= 0 && rec.BleedX10 >= TreatAi.TourniquetBleedX10) Add(TreatKind.Tourniquet, root); else Add(TreatKind.Bandage, rec.Part);
        }
        foreach (var w in patient.Records)
        {
            if (w.Is(Aid.Resolved) || w.State == WoundState.Scar) continue;
            switch (w.Cat)
            {
                case WoundCat.Tendon when !w.Is(Aid.Repaired) && now - w.Born <= MedicineTables.GameSecPerDay: Add(TreatKind.TendonRepair, w.Part); break;
                case WoundCat.Ligament when !w.Is(Aid.Repaired) && w.Grade0 >= 2 && now - w.Born <= 2L * MedicineTables.GameSecPerDay: Add(TreatKind.LigamentRepair, w.Part); break;
                case WoundCat.Organ when w.Internal && !w.Is(Aid.Repaired): Add(TreatKind.OrganRepair, w.Part); break;
                case WoundCat.Bone:
                    if (w.Grade >= 2 && !w.Is(Aid.Set) && !w.Is(Aid.SetFailed)) Add(TreatKind.SetBone, w.Part, w.Grade0);
                    if (w.Grade >= 1 && !w.Is(Aid.Splinted)) Add(TreatKind.Splint, w.Part);
                    break;
                case WoundCat.Skin or WoundCat.Muscle or WoundCat.Vessel when w.Kind == Ravensguard.Sim.Combat.WoundKind.Cut && w.DepthPm >= 500 && w.State != WoundState.Stitched: Add(TreatKind.Stitch, w.Part); break;
            }
            if (w.InfectionLevel > 0) Add(TreatKind.Clean, w.Part);
        }
        return lines;
    }

    public static int Total(IEnumerable<QuoteLine> lines) { int n = 0; foreach (var l in lines) n += l.FeeMites; return n; }

    /// <summary>
    /// Performs the chosen lines in order, the first starting at <paramref name="startSec"/> (each following when the last is finished); a line is paid only when it was done and the patient's
    /// purse holds the fee, and stops the list when it does not.
    /// </summary>
    public static List<DeskOutcome> Perform(Ledger ledger, WoundBook patient, BodyPlanV2? plan, IEnumerable<QuoteLine> lines, PhysicianKind who, int skill, ulong manId,
        PurseId payer, PurseId payee, long startSec, bool light = true)
    {
        var outcomes = new List<DeskOutcome>();
        var kit = Kit(); long t = startSec;
        foreach (var line in lines)
        {
            if (ledger.Balance(payer).Mites < line.FeeMites) { outcomes.Add(new DeskOutcome(line, false, false, "cannot pay", 0)); break; }
            long finish = t + (long)Treatment.SimSeconds(line.Kind) * MedicineTables.TicksPerSimS;
            var res = Treatment.Perform(patient, plan, new TreatRequest(line.Kind, line.Part, finish, skill, manId, light), kit);
            if (!res.Ok) { outcomes.Add(new DeskOutcome(line, false, false, res.Reason, 0)); continue; }
            if (line.FeeMites > 0) ledger.Transfer(payer, payee, new Coin(line.FeeMites), MedicineEnums.FeeReason);
            outcomes.Add(new DeskOutcome(line, true, res.Success, res.Reason, line.FeeMites));
            t = finish;
        }
        return outcomes;
    }
}
