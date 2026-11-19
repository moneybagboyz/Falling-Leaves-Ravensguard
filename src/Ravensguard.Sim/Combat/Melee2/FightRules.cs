using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// The rules a kind of fight adds to the one fight step (overview 3, P4-F): a practice bout ends at three clean hits; a tournament bout ends when a fighter's pain reaches the
/// knock-out, who yields instead of collapsing; fights the setup builds for the world treat in the fight (watch-mode 4.3: a fighter clear of enemies presses or bandages a bleeder).
/// Derived from the setup's kind, so a saved fight gets them back from its record.
/// </summary>
public sealed record FightRules(int CleanHitsToWin = 0, bool YieldAtPainKo = false, bool TreatInFight = false)
{
    public static readonly FightRules Plain = new();

    public static FightRules For(FightKind k) => k switch
    {
        FightKind.Practice => new(CleanHitsToWin: 3, YieldAtPainKo: true, TreatInFight: true),
        FightKind.Tournament => new(YieldAtPainKo: true, TreatInFight: true),
        _ => new(TreatInFight: true),
    };
}

public sealed partial class FightWorld
{
    /// <summary>The fighter who landed the clean hits a practice bout needs (-1: nobody yet, or not a practice bout).</summary>
    public int HitsWinner()
    {
        if (Rules.CleanHitsToWin <= 0) return -1;
        foreach (var f in Fighters) if (f.Landed >= Rules.CleanHitsToWin) return f.Id;
        return -1;
    }

    /// <summary>A tournament or practice fighter whose pain reaches the knock-out yields (the marshal stops the bout) instead of going down. True when it yielded now.</summary>
    private bool YieldAtPain(Fighter f)
    {
        if (!Rules.YieldAtPainKo || f.Book.Dead || f.Act is FAct.Dead or FAct.Fled or FAct.Surrendered) return false;     // a blow that knocked out by pain is yielded too
        if (f.Book.PainX100 < f.Book.KnockoutPainX100 && f.Book.State is not (Consciousness.Unconscious or Consciousness.Collapsed)) return false;
        bool wasDown = f.Act == FAct.Down;
        f.Act = FAct.Surrendered; f.EndedAt = Tick; f.Routing = false;
        Emit(FightEventKind.Surrender, f.Id, NearestEnemyId(f), -1, 0, 1);
        if (!wasDown) Announce(f);
        return true;
    }

    private int NearestEnemyId(Fighter f)
    {
        int best = -1; long bd = long.MaxValue;
        foreach (var e in Fighters) if (e.Team != f.Team && e.Active) { long d = DistMm(e, f); if (d < bd) { bd = d; best = e.Id; } }
        return best;
    }

    // ------------------------------------------------------------------ treating in the fight (watch-mode 4.3, wounds 4.3)

    /// <summary>Treating starts when no enemy is within 8 m; the patient is the guarded person, then oneself, then an ally within 6 m bleeding 5 ml/s or more.</summary>
    public const int TreatClearMm = 8_000, FleeClearMm = 20_000, TreatAllyMm = 6_000, TreatAllyBleedX10 = 50;

    private void TreatStep()
    {
        foreach (var f in Fighters)
        {
            if (!f.Stance.TreatAllies || f.Act is FAct.Dead or FAct.Down or FAct.Surrendered || Tick < f.TreatBusyUntil || !f.Book.Conscious) continue;
            if (f.Active && (f.Act is not (FAct.Idle or FAct.Block) || EnemyWithin(f, f.Routing ? FleeClearMm : TreatClearMm))) continue;     // a runner stops to press a wound 20 m clear
            if (ProtectOf(f) is { } charge && charge.Active && DistMm(f, charge) > Ravensguard.Sim.Combat.Watch.StanceBias.ProtectKeepMm) continue;   // a guard does not kneel away from its charge
            var patient = Patient(f);
            if (patient == null) continue;
            var req = TreatAi.Next(patient.Book, patient.Plan, NoItems, GameSec, f.Skill / 2, f.Person);
            if (req is not { } r) continue;
            var res = Treatment.Perform(patient.Book, patient.Plan, r, NoItems);
            if (!res.Ok) continue;
            if (patient.Id != f.Id) RefreshBody(patient);
            f.TreatBusyUntil = Tick + Math.Max(1, res.SimSeconds) * 20;
            f.Brain.Target = patient.Id;
            f.Cmd.MoveX = f.Cmd.MoveY = 0; f.Cmd.Sprint = false;
            Emit(FightEventKind.Treated, f.Id, patient.Id, r.Part, (int)r.Kind);
        }
    }

    private static readonly ItemBag NoItems = new();

    private bool EnemyWithin(Fighter f, long mm)
    {
        foreach (var e in Fighters) if (e.Team != f.Team && e.Active && DistMm(e, f) <= mm) return true;
        return false;
    }

    private Fighter? Patient(Fighter f)
    {
        long now = GameSec;
        bool Bleeds(Fighter p, int minX10) => !p.Book.Dead && p.Act != FAct.Dead && Treatment.WorstBleeder(p.Book, -1, now) is int i and >= 0 && p.Book.Records[i].BleedX10 >= minX10;
        if (f.Stance.Protect && f.ProtectFighter >= 0 && f.ProtectFighter < Fighters.Count && Fighters[f.ProtectFighter] is { } x && x.Act != FAct.Fled && DistMm(f, x) <= TreatAllyMm && Bleeds(x, 1)) return x;
        if (Bleeds(f, 1)) return f;
        if (f.Act == FAct.Fled) return null;
        Fighter? best = null; int bestRate = 0;
        foreach (var o in Fighters)
        {
            if (o.Id == f.Id || o.Team != f.Team || o.Act is FAct.Fled || DistMm(f, o) > TreatAllyMm) continue;
            int i = Treatment.WorstBleeder(o.Book, -1, now);
            if (i < 0 || o.Book.Dead) continue;
            int rate = o.Book.Records[i].BleedX10;
            if (rate >= TreatAllyBleedX10 && rate > bestRate) { best = o; bestRate = rate; }
        }
        return best;
    }
}

/// <summary>What a fight is (overview 3): the one setup builds every kind; the kind sets the rules (<see cref="FightRules.For"/>), the record keeps it as its name.</summary>
public enum FightKind : byte { Encounter, Raid, Ambush, SiteInhabitants, Tournament, Practice }
