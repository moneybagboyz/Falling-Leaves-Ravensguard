using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Battle;

// P7-AW-E (AW-14, abstract-war 4.4 "Barrier"): a live battle whose last formations leave the bubble is converted (BattleDemotion, the Phase 4 code) and finished abstractly. The decision reads only the fight's
// state and the observer's place, so a replay of the same inputs converts at the same tick; the abstract battle is a function of the converted state and the fight's seed, so the result is too.
public sealed partial class BattleBubble
{
    /// <summary>How far from the observer (the hero's fighter) a soldier still counts as inside the bubble: 150 m, in millimetres.</summary>
    public const int EdgeRangeMm = 150_000;

    /// <summary>What the conversion kept: the soldiers carried over (everyone who was not a horse), how many stood, were down or dead at the instant of conversion, and what the abstract battle ended with.</summary>
    public sealed record EdgeConversion(int Soldiers, int StandingAtEdge, int DownAtEdge, int DeadAtEdge, int StandingAtEnd, int DownAtEnd, int DeadAtEnd, int FledAtEnd, int YieldedAtEnd, int Steps, ulong BattleHash);

    /// <summary>The conversion of this battle, once it has happened; null while the battle is live (or closed in the bubble).</summary>
    public EdgeConversion? Converted { get; private set; }
    private bool _converted;

    /// <summary>
    /// True when no soldier on his feet (and no one walking in from a reserve is counted: reserves rejoin their hosts) is within <see cref="EdgeRangeMm"/> of the observer's place, that is, the last formations have left the bubble.
    /// </summary>
    public bool AllBeyondEdge(int observerX, int observerY, int rangeMm = EdgeRangeMm)
    {
        long r2 = (long)rangeMm * rangeMm; int any = 0;
        foreach (var f in W.Fighters)
        {
            if (W.IsHorse(f) || !f.Active) continue;
            any++;
            long dx = f.X - observerX, dy = f.Y - observerY;
            if (dx * dx + dy * dy <= r2) return false;
        }
        return any > 0;
    }

    /// <summary>
    /// Converts the live fight at this instant and finishes it abstractly: <see cref="BattleDemotion.InputOf"/> makes the abstract battle from every soldier where he stands (wounds, pain and blood copied, the dead and the
    /// down kept as they are), <see cref="AbstractBattle.Run"/> fights it out, and every soldier's end (status and wound book) goes back to his fighter, so nobody is lost or made twice and the close writes the same
    /// chronicle entry and result as a battle that ended in the box. Returns false (nothing changed) with no wound tables in the content or when the battle is closed.
    /// </summary>
    public bool Demote(WoundTables? tables = null)
    {
        if (Closed || _converted) return false;
        tables ??= WoundTables.For(W.Content.Db);
        if (tables == null) return false;
        var fighters = new List<Fighter>();
        foreach (var f in W.Fighters) if (!W.IsHorse(f)) fighters.Add(f);
        int stand0 = 0, down0 = 0, dead0 = 0;
        foreach (var f in fighters) { if (f.Act == FAct.Dead) dead0++; else if (f.Act == FAct.Down) down0++; else if (f.Active) stand0++; }
        var input = BattleDemotion.InputOf(W, "bubble_edge");
        var ab = new AbstractBattle(W.Content, tables, input);
        ab.Run();
        if (ab.Soldiers.Count != fighters.Count) throw new InvalidOperationException("the demoted battle lost or made a soldier");
        int stand1 = 0, down1 = 0, dead1 = 0, fled1 = 0, yield1 = 0;
        for (int i = 0; i < fighters.Count; i++)
        {
            var f = fighters[i]; var s = ab.Soldiers[i];
            f.Book = s.Book; f.Ammo = s.Ammo;
            switch (s.Status)
            {
                case SoldierStatus.Dead: f.Act = FAct.Dead; dead1++; break;
                case SoldierStatus.Down: f.Act = FAct.Down; down1++; break;
                case SoldierStatus.Fled: f.Act = FAct.Fled; fled1++; break;
                case SoldierStatus.Surrendered: case SoldierStatus.Captured: f.Act = FAct.Surrendered; yield1++; break;
                default: if (!f.Active) f.Act = FAct.Idle; stand1++; break;
            }
            if (s.Status != SoldierStatus.Active) f.Routing = false;
        }
        // P7R-2: the abstract battle fought on clones, so the ledger still holds the pre-demotion books: the new ones replace them (a book with nothing in it leaves the ledger)
        if (W.Ledger is { } led)
            lock (led.Sync)
                foreach (var f in fighters)
                {
                    ulong key = f.Kit.Person;
                    if (key == 0 || !f.LedgerBook) continue;
                    if (f.Book.Empty) led.Books.Remove(key); else led.Adopt(key, f.Book, W.GameSec);
                }
        _converted = true;
        Converted = new EdgeConversion(fighters.Count, stand0, down0, dead0, stand1, down1, dead1, fled1, yield1, ab.Step, ab.Hash());
        Finish(truce: false);
        return true;
    }

    /// <summary>The live tier's call (every 5 s): converts the battle when the last formations have left the bubble around the observer, whose place is the hero's fighter (a battle with no hero in it is never converted here).</summary>
    public bool DemoteIfUnobserved(int heroFighter)
    {
        if (Closed || _converted || heroFighter < 0 || heroFighter >= W.Fighters.Count || W.Tick % 100 != 0) return false;
        var h = W.Fighters[heroFighter];
        return AllBeyondEdge(h.X, h.Y) && Demote();
    }
}
