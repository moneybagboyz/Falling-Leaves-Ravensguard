using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Society;

namespace Ravensguard.Sim.Law;

/// <summary>
/// Punishments on the body (crime-and-justice 3, 4.6; overview 8.4: no hit-point shortcut). Flogging is skin cuts on the back, branding a burn on the hand or cheek with a mark, the hand an amputation at the wrist
/// through <see cref="Treatment.Perform"/> (a tourniquet first, the stump cauterised, skill 40), the stocks pain, and the rope a death of the breath. Each wound carries the sentence's chronicle entry as its
/// cause, so the wound book's own cause chain reaches the court and the crime. A world with no wound ledger or body plan gets only the record (the caller's entry).
/// </summary>
public static class Bodily
{
    private static WoundBook? Book(LawWorld w, EntityId person, long sec) => w.Wounds != null && w.Human != null ? w.Wounds.For(person.Value, w.Human, sec) : null;

    private static WoundV2 Skin(BodyPlanV2 plan, int part, WoundKind kind, int depthPm, int bleedX10, WoundFlags flags)
    {
        var p = plan.Parts[part];
        var skin = p.Layers.FirstOrDefault(l => l.IsSoft) ?? p.Layers[0];
        return new WoundV2 { Part = (short)part, Tissue = (short)skin.Tissue.Index, Structure = -1, Organ = -1, Kind = kind, DepthPm = depthPm, BleedX10 = bleedX10, Flags = flags };
    }

    /// <summary>The flogging: <c>lashes / 10</c> cuts (at least one) across the back, grade 1, each with the lash's pain; they bleed a little, clot, and can fester as any wound. Returns the cuts.</summary>
    public static int Flog(LawWorld w, EntityId person, int lashes, long sec, ulong entry)
    {
        if (Book(w, person, sec) is not { } book) return 0;
        int cuts = Math.Max(1, lashes / 10);
        var plan = w.Human!; int back = SiteBlows.Part(plan, "upper_body", false);
        var ws = new List<WoundV2>();
        for (int i = 0; i < cuts; i++) ws.Add(Skin(plan, back, WoundKind.Cut, 200, 2, WoundFlags.None));
        lock (w.Wounds!.Sync) { book.Apply(plan, [.. ws], sec, sec / 20, entry, 0, false); book.PainX100 += cuts * w.S.LashCutPain * 100; }
        return cuts;
    }

    /// <summary>The brand: a grade 2 burn on the right hand (a thief) or the cheek (a murderer or a traitor) and the mark that follows him (<see cref="Brand"/>).</summary>
    public static bool Brand(LawWorld w, EntityId person, bool cheek, long sec, ulong entry)
    {
        var marks = w.Marks();
        marks?.AddBrand(person, cheek ? Society.Brand.Murderer : Society.Brand.Thief);
        if (Book(w, person, sec) is not { } book) return false;
        var plan = w.Human!; int part = cheek ? plan.IndexOf("head") : SiteBlows.Part(plan, "hand", true);
        if (part < 0) part = SiteBlows.Part(plan, "upper_body", false);
        lock (w.Wounds!.Sync) book.Apply(plan, [Skin(plan, part, WoundKind.Burn, 600, 0, WoundFlags.Burn)], sec, sec / 20, entry, 0, false);
        return true;
    }

    /// <summary>
    /// The hand: a cut at the wrist, a tourniquet of rope on the hand, the amputation by the executioner (skill 40), the stump cauterised. Falls back on the book's own severing should the treatment refuse.
    /// Returns the part's id ("hand_r"), or "" when there is no body to do it on.
    /// </summary>
    public static string Hand(LawWorld w, EntityId person, long sec, ulong entry)
    {
        if (Book(w, person, sec) is not { } book) return "";
        var plan = w.Human!; int hand = SiteBlows.Part(plan, "hand", true);
        var items = new ItemBag().Add("rope", 1);
        lock (w.Wounds!.Sync)
        {
            book.Apply(plan, [Skin(plan, hand, WoundKind.Cut, 500, 15, WoundFlags.None)], sec, sec / 20, entry, 0, false);
            int skill = w.S.ExecutionerSkill;
            var tq = Treatment.Perform(book, plan, new TreatRequest(TreatKind.Tourniquet, hand, sec, skill, 0), items);
            var am = tq.Ok ? Treatment.Perform(book, plan, new TreatRequest(TreatKind.Amputate, hand, sec, skill, 0), items) : tq;
            if (!am.Ok) book.LoseLimb(hand, sec, [hand]);
        }
        return plan.Parts[hand].Id;
    }

    /// <summary>A day in the stocks: the pelting's pain (the setting's, added as the book counts pain).</summary>
    public static void Stocks(LawWorld w, EntityId person, long sec)
    {
        if (Book(w, person, sec) is not { } book) return;
        lock (w.Wounds!.Sync) book.PainX100 += w.S.StocksPain * 100;
    }

    /// <summary>The rope: the hero's wound book is told to stop the breath (the succession takes over from there); an NPC goes through <see cref="LawWorld.Execute"/> (Demography.Kill, cause Executed). Returns whether anyone died.</summary>
    public static bool Hang(LawWorld w, EntityId person, long sec, ulong entry)
    {
        if (w.IsHero(person))
        {
            if (Book(w, person, sec) is not { } book) return false;
            lock (w.Wounds!.Sync) book.Hang(sec);
            return book.Dead;
        }
        if (w.Execute == null) return false;
        w.Execute(person, sec, entry);
        return true;
    }
}
