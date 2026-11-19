using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using static Ravensguard.Sim.Combat.Wounds.MedicineTables;

namespace Ravensguard.Sim.Combat.Wounds;

public sealed partial class WoundBook
{
    /// <summary>
    /// The once-a-game-day step (wounds 3.4, 4.5, 4.7): infection rolls, healing progress, malunion at completion, the sepsis death. A pure function of the book and the
    /// day, so the abstract tier (at the day boundary) and the live tier (when its clock crosses one) give the same book. Call after <see cref="AdvanceTo"/> the day's first second.
    /// </summary>
    public void DailyStep(long day, int speedPm = 1000)
    {
        long sec = day * GameSecPerDay;
        if (Dead) return;
        AdvanceTo(sec);
        for (int i = 0; i < Records.Count && !Dead; i++)
        {
            var w = Records[i];
            if (w.State == WoundState.Scar || w.Is(Aid.Resolved)) { continue; }
            Infection(ref w, day, sec);
            if (Dead) { Records[i] = w; break; }
            Heal(ref w, day, sec, speedPm);
            Records[i] = w;
        }
        // a tourniquet kept on for 12 game hours costs the limb (wounds 4.3)
        for (int i = 0; i < Records.Count; i++)
        {
            var w = Records[i];
            if (w.Dressing == Dressing.Tourniquet && !w.Is(Aid.Resolved) && sec - w.DressedAt >= (long)TourniquetLossHours * 3600 && w.State != WoundState.Scar)
            { LoseLimb(w.Part, sec); break; }
        }
        if (ConcussedUntilDay != 0 && day >= ConcussedUntilDay) { ConcussedUntilDay = 0; Note(sec, BookEventKind.Recovered); Evaluate(sec); }
        Records.RemoveAll(r => r.State == WoundState.Scar && r.Is(Aid.Resolved) && Records.Count > 24);
    }

    private static bool BreaksSkin(in WoundRecord w) =>
        w.Cat != WoundCat.Concussion && (w.Kind is WoundKind.Cut or WoundKind.Burn || w.Has(WoundFlags.Severed) || w.Has(WoundFlags.Compound) || w.Cat is WoundCat.Organ or WoundCat.Vessel);

    private void Infection(ref WoundRecord w, long day, long sec)
    {
        uint d1 = w.Id * 4 + 1, d2 = w.Id * 4 + 2, d3 = w.Id * 4 + 3;
        if (w.InfectionLevel == 0)
        {
            if (!BreaksSkin(w) || w.Cat == WoundCat.Organ && w.Internal) return;
            bool dressed = w.Dressing is Dressing.Pressure or Dressing.Bandage && sec - w.DressedAt <= 2L * GameSecPerDay || w.Dressing == Dressing.Tourniquet && sec - w.DressedAt <= 2L * GameSecPerDay;
            int p = w.State == WoundState.Stitched || w.StitchedAt > 0 ? InfectStitchedPm : dressed ? InfectDressedPm : InfectOpenPm;
            if (w.Has(WoundFlags.Compound)) p *= 3;
            if (w.Is(Aid.Dirty)) p = p * 3 / 2;
            if (w.StitchedAt > 0 && w.StitchedAt - w.Born <= 6 * 3600) p /= 2;
            if (DetRng.Range(Seed, day, Key, RngStreams.Infection, d1, 0, 1000) < p)
            {
                w.InfectionLevel = 1; Note(sec, BookEventKind.Infected, w.Part);
            }
            return;
        }
        if (w.InfectionLevel < 3)
        {
            if (DetRng.Range(Seed, day, Key, RngStreams.Infection, d2, 0, 1000) < InfectRisePm)
            {
                w.InfectionLevel++;
                if (w.InfectionLevel == 3)
                {
                    w.SepsisDay = (int)day;
                    w.DeathDay = (int)day + 3 + DetRng.Range(Seed, day, Key, RngStreams.Infection, d3, 0, 3);
                    Note(sec, BookEventKind.Sepsis, w.Part);
                }
            }
            return;
        }
        if (w.DeathDay > 0 && day >= w.DeathDay) { Die(sec, DeathKind.Infection, w.Id); }
    }

    private void Heal(ref WoundRecord w, long day, long sec, int speedPm)
    {
        if (w.InfectionLevel > 0) return;                                        // an infected wound does not close until it is cleaned
        bool eligible;
        bool repaired = w.Is(Aid.Repaired);
        switch (w.Cat)
        {
            case WoundCat.Nerve or WoundCat.Spine: return;                           // never (wounds 3.4)
            case WoundCat.Tendon: eligible = repaired; break;
            case WoundCat.Organ: eligible = repaired; break;
            case WoundCat.Ligament: eligible = w.Grade0 < 3 || repaired; break;      // ruptured stays unstable without surgery
            case WoundCat.Bone: eligible = true; break;
            default: eligible = w.Clots || w.BleedX10 == 0 || w.Dressing is Dressing.Pressure or Dressing.Bandage || w.State == WoundState.Stitched || w.Is(Aid.Cauterised) || repaired; break;
        }
        if (!eligible) return;
        if (w.NeedX10 <= 0) return;
        w.HealX10 += 10 * speedPm / 1000;
        if (w.State is WoundState.Open or WoundState.Dressed or WoundState.Stitched && w.HealX10 * 2 >= w.NeedX10) w.State = WoundState.Healing;
        if (w.Cat == WoundCat.Muscle && !w.Is(Aid.HalfDone) && w.HealX10 * 2 >= w.NeedX10 && w.Grade > 0) { w.Grade--; w.Aids |= Aid.HalfDone; }
        if (w.HealX10 < w.NeedX10) return;
        Complete(ref w, day, sec);
    }

    private void Complete(ref WoundRecord w, long day, long sec)
    {
        w.State = WoundState.Scar; w.HealX10 = w.NeedX10;
        switch (w.Cat)
        {
            case WoundCat.Muscle:
                if (w.Grade0 < 3) w.Grade = 0;                                        // mangled heals only to torn (wounds 3.4)
                else w.Grade = 2;
                if (w.Grade == 0) w.Aids |= Aid.Resolved;
                break;
            case WoundCat.Bone:
                {
                    int pct = w.Is(Aid.Set) ? 0 : MalunionPct(w.Grade0);
                    w.Grade = 0; w.Aids |= Aid.Resolved;
                    if (pct > 0 && DetRng.Range(Seed, w.Born, Key, RngStreams.Malunion, w.Id, 0, 100) < pct) Malunion(ref w, sec);
                    break;
                }
            case WoundCat.Ligament or WoundCat.Tendon or WoundCat.Organ: w.Grade = 0; w.Aids |= Aid.Resolved; break;
            case WoundCat.Skin or WoundCat.Inset or WoundCat.Vessel: w.Aids |= Aid.Resolved; break;
            case WoundCat.Severed: break;                                              // the loss is permanent; the stump closes
        }
        w.InfectionLevel = 0;
        Note(sec, BookEventKind.Scar, w.Part);
    }

    private void Malunion(ref WoundRecord w, long sec)
    {
        Permanent.Malunions++;
        switch (w.Attr)
        {
            case AttrLoss.Strength: Permanent.StrengthLoss += 2; Note(sec, BookEventKind.Malunion, w.Part, "strength:2"); break;
            case AttrLoss.Dexterity: Permanent.DexterityLoss += 2; Note(sec, BookEventKind.Malunion, w.Part, "dexterity:2"); break;
            case AttrLoss.Speech: Permanent.SpeechCap = (byte)Math.Min(100, Permanent.SpeechCap + 10); Note(sec, BookEventKind.Malunion, w.Part, "speech:10"); break;
            default: Note(sec, BookEventKind.Malunion, w.Part, "none"); break;
        }
    }

    /// <summary>Re-times a wound's healing after a treatment, keeping the fraction already done.</summary>
    internal static void Retime(ref WoundRecord w)
    {
        bool treated = w.Is(Aid.Set) || w.Is(Aid.Splinted) || w.Dressing is Dressing.Pressure or Dressing.Bandage || w.State == WoundState.Stitched || w.Is(Aid.Cauterised);
        int need = NeedX10(w.Cat, w.Grade0, w.DepthPm, treated, w.Is(Aid.Repaired));
        if (need <= 0) return;
        if (w.NeedX10 > 0 && need != w.NeedX10) w.HealX10 = (int)((long)w.HealX10 * need / w.NeedX10);
        w.NeedX10 = need;
    }

    /// <summary>Loses a limb to ischaemia or a surgeon's saw: the wounds under the tourniquet are gone with it and a stump is left (wounds 4.3, 4.6).</summary>
    internal void LoseLimb(int part, long sec, ReadOnlySpan<int> gone = default)
    {
        for (int i = 0; i < Records.Count; i++)
        {
            var w = Records[i];
            bool under = w.Dressing == Dressing.Tourniquet && w.Root == part || w.Part == part || gone.Contains(w.Part);
            if (!under || w.Is(Aid.Resolved) || w.Has(WoundFlags.Severed) && w.Is(Aid.Cauterised)) continue;
            w.Aids |= Aid.Resolved; w.State = WoundState.Scar; w.InfectionLevel = 0; Records[i] = w;
        }
        Permanent.AmputatedMask |= 1UL << part;
        Records.Add(new WoundRecord
        {
            Id = NextId++, Part = (short)part, Structure = -1, Organ = -1, Kind = WoundKind.Cut, Cat = WoundCat.Severed, BleedX10 = 5, Flags = WoundFlags.Severed, Root = (short)part,
            Dressing = Dressing.Bandage, Aids = Aid.Cauterised, State = WoundState.Dressed, Born = sec, DressFromS = sec / TicksPerSimS, DressedAt = sec, NeedX10 = 100,
        });
        Note(sec, BookEventKind.Amputated, part);
    }
}
