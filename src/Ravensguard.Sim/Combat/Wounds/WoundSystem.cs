using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Combat.Wounds;

/// <summary>
/// The abstract tier's hourly wound step (wounds 5): advances the shared <see cref="WoundLedger"/> to the end of the hour (the same function the live tier runs at 20 Hz, so a
/// bleeder with no help dies in the same sim time), runs the day boundaries (infection, healing), then turns what happened into the person records: a death by blood loss, organ,
/// brain, breath, sepsis or a lost limb becomes <see cref="Demography.Kill"/> with its cause and the chronicle entry; a malunion lowers the person's strength or dexterity for good.
/// It also does the first aid the people around a wounded person would give (<see cref="AbstractFirstAid"/>).
/// </summary>
public sealed class WoundSystem : AbstractSystem
{
    public WoundSystem() : base("wounds", TickPhase.H4DailySlice, SystemOrder.Wounds) { }

    public override void Step(TickContext ctx)
    {
        var l = ctx.World.Get<WoundLedger>();
        if (l == null || l.IsEmpty) return;
        long to = (ctx.Hour + 1) * 3600;
        lock (l.Sync)
        {
            var seen = Ravensguard.Sim.Medicine.AbstractPhysic.Run(ctx.World, l, ctx.Hour * 3600);          // medicine 4.4: a physician or barber-surgeon first, where the town has one
            AbstractFirstAid.Run(ctx.World, l, ctx.Hour * 3600, seen);
            l.AdvanceTo(to);
            Resolve(ctx.World, ctx.Outbox, l);
        }
    }

    /// <summary>Applies the books' deaths and permanent losses to the person rows; callable from a test.</summary>
    public static void Resolve(SimWorld w, RegionOutbox ob, WoundLedger l)
    {
        var pt = w.People;
        List<ulong>? gone = null;
        foreach (var (key, b) in l.Books)
        {
            int row = pt.IndexOf(new EntityId(key));
            foreach (var e in b.DrainEvents())
            {
                if (row < 0 || pt.Status[row] != PersonStatus.Alive) continue;
                if (e.Kind == BookEventKind.Died)
                {
                    var cause = CauseOf(b.Death);
                    Demography.Kill(w, row, cause, new GameTime(e.Sec), ob, chain: Describe(b));
                    (gone ??= []).Add(key);
                }
                else if (e.Kind == BookEventKind.Malunion || e.Kind == BookEventKind.PermanentLoss)
                {
                    int idx = e.Note.StartsWith("strength", StringComparison.Ordinal) ? PersonTable.StrengthIdx : e.Note.StartsWith("dexterity", StringComparison.Ordinal) ? PersonTable.DexterityIdx : e.Note.StartsWith("wits", StringComparison.Ordinal) ? PersonTable.WitsIdx : -1;
                    if (idx >= 0) { int a = row * PersonTable.AttrCount + idx; pt.Attr[a] = (byte)Math.Max(1, pt.Attr[a] - (e.Note.EndsWith(":2", StringComparison.Ordinal) ? 2 : 1)); }
                }
            }
        }
        if (gone != null) foreach (var k in gone) l.Books.Remove(k);
        PruneDead(l, key => { int row = pt.IndexOf(new EntityId(key)); return row >= 0 && pt.Status[row] == PersonStatus.Alive; });
    }

    /// <summary>
    /// Review finding 3 (with the fighters' books kept in the ledger): a dead body's book that no person row will take up (a traveller or bandit of a road fight, someone whose row is already
    /// dead) never heals, so it would stay in the ledger and the save for good. It goes, after its events were drained. The hero's stays: the game turns it into the succession.
    /// </summary>
    public static int PruneDead(WoundLedger l, Func<ulong, bool> livingPerson)
    {
        List<ulong>? gone = null;
        foreach (var (key, b) in l.Books)
            if (b.Dead && key != Economy.EconomyState.HeroOwner.Value && !livingPerson(key)) (gone ??= []).Add(key);
        if (gone != null) foreach (var k in gone) l.Books.Remove(k);
        return gone?.Count ?? 0;
    }

    public static DeathCause CauseOf(DeathKind k) => k switch
    {
        DeathKind.BloodLoss => DeathCause.BloodLoss, DeathKind.Organ => DeathCause.Organ, DeathKind.Brain => DeathCause.Brain, DeathKind.Breath => DeathCause.Suffocation,
        DeathKind.Infection => DeathCause.Infection, DeathKind.Ischaemia => DeathCause.Ischaemia, _ => DeathCause.Violence,
    };

    /// <summary>The death's cause as a key (phase-4 S7, chronicle-full 4.1): the sentence is en.csv's, not the sim's.</summary>
    private static Cause Describe(WoundBook b) => new(CauseKind.Wound, null, "", b.Death switch
    {
        DeathKind.BloodLoss => "ch_note_death_bloodloss",
        DeathKind.Organ => "ch_note_death_organ",
        DeathKind.Brain => "ch_note_death_brain",
        DeathKind.Breath => "ch_note_death_breath",
        DeathKind.Infection => "ch_note_death_infection",
        _ => "ch_note_death_wounds",
    });
}

/// <summary>
/// The abstract tier's first aid (wounds 5): a bleeding person in a settlement is dressed with a chance by who is home. The chance is 90% when a household member is at home,
/// 40% when only neighbours are, 0 on the road alone; a bandaged wound stops at once for the abstract tier (the hour's closed form starts from the dressed rate).
/// </summary>
public static class AbstractFirstAid
{
    public static int ChancePct(bool householdHome, bool neighbours) => householdHome ? 90 : neighbours ? 40 : 0;

    public static void Run(SimWorld w, WoundLedger l, long hourStartSec, ISet<ulong>? treatedByPhysician = null)
    {
        var pt = w.People;
        foreach (var (key, b) in l.Books)
        {
            if (b.Dead || b.OpenCount == 0 || treatedByPhysician != null && treatedByPhysician.Contains(key)) continue;
            int row = pt.IndexOf(new EntityId(key));
            if (row < 0 || pt.Status[row] != PersonStatus.Alive) continue;
            int hh = pt.Household[row];
            bool home = hh >= 0 && w.Households.MemberCount[hh] > 1;
            bool near = hh >= 0 && pt.HomeSettlement[row] >= 0;
            int pct = ChancePct(home, near);
            if (pct == 0) continue;
            if (DetRng.Range(w.Seed, hourStartSec / 3600, key, RngStreams.SurgeryResult, 0x8000_0000u, 0, 100) >= pct) continue;
            b.AdvanceTo(hourStartSec);
            for (int n = 0; n < 4; n++)
            {
                int i = Treatment.WorstBleeder(b, -1, hourStartSec);
                if (i < 0) break;
                var wd = b.Records[i];
                wd.Dressing = Dressing.Bandage; wd.DressFromS = hourStartSec / MedicineTables.TicksPerSimS; wd.DressedAt = hourStartSec; wd.State = WoundState.Dressed;
                WoundBook.Retime(ref wd); b.Records[i] = wd;
            }
        }
    }
}
