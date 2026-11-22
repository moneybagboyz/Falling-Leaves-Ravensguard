using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Battle;

/// <summary>One side's end of a battle: the men it had (live and reserve), those out of the fight (dead, down, fled, yielded), those on their feet, the reserve that rejoins its host and the waves it sent.</summary>
public sealed record BattleSideResult(int Team, int Realm, ulong Commander, ulong HostId, int Men, int Lost, int Dead, int Down, int Fled, int Yielded, int Standing, int Reserve, int Waves);

/// <summary>One soldier as the battle leaves him for the people layer: who he is, how he stands, where, and what ammunition he has left. His wounds are in the world ledger (they were never copied out of it).</summary>
public readonly record struct BattleSoldierEnd(ulong Person, int Team, int Fighter, FAct Act, bool Routing, int X, int Y, int Ammo);

/// <summary>What a closed battle reports: the winning team (-1: none), the outcome as the first side reads it, the ticks it lasted, the sides, every soldier, and the chronicle entry it wrote.</summary>
public sealed record BattleResult(ulong Id, int WinnerTeam, string OutcomeKey, int Ticks, long GameSec, IReadOnlyList<BattleSideResult> Sides, IReadOnlyList<BattleSoldierEnd> Soldiers, EntityId Entry);

public sealed partial class BattleBubble
{
    public const string OutWon = "ch_bt_out_won", OutLost = "ch_bt_out_lost", OutDrawn = "ch_bt_out_drawn", OutTruce = "ch_bt_out_truce";

    private int ActiveIn(FormationRow r)
    {
        int n = 0;
        foreach (int id in r.Members) if (W.Fighters[id].Active) n++;
        return n;
    }

    /// <summary>
    /// Closes the battle (4.1): the winner is the side with the most men on their feet in the box (the only side with any); every soldier is reported where he stands, with the wounds he keeps in the ledger and the ammunition
    /// left; reserves rejoin their hosts; the paused chunks are let go; the chronicle entries are written. Nobody is created or lost here: the fight's fighters stay in the fight for the aftermath (captives, first aid,
    /// loot) the Phase 4 layer runs for the field's holder.
    /// </summary>
    private void Finish(bool truce)
    {
        if (Closed) return;
        Truce = truce;
        var stand = new int[Sides.Count];
        var sides = new List<BattleSideResult>();
        for (int i = 0; i < Sides.Count; i++)
        {
            var s = Sides[i]; int dead = 0, down = 0, fled = 0, yielded = 0, on = 0;
            foreach (var f in W.Fighters)
            {
                if (f.Team != s.Team) continue;
                switch (f.Act)
                {
                    case FAct.Dead: dead++; break;
                    case FAct.Down: down++; break;
                    case FAct.Fled: fled++; break;
                    case FAct.Surrendered: yielded++; break;
                    default: if (InBox(f.X, f.Y)) on++; else fled++; break;
                }
            }
            stand[i] = on;
            int reserve = s.Reserve.Sum(u => u.Count) + s.Pending.Sum(u => u.Count - u.Entered);
            sides.Add(new BattleSideResult(s.Team, s.Realm, s.Commander, s.HostId, s.Strength, dead + down + fled + yielded, dead, down, fled, yielded, on, reserve, s.WavesSent));
        }
        int best = -1, bestN = -1; bool tie = false;
        for (int i = 0; i < stand.Length; i++) { if (stand[i] > bestN) { best = i; bestN = stand[i]; tie = false; } else if (stand[i] == bestN) tie = true; }
        int winner = truce || tie || bestN <= 0 ? -1 : Sides[best].Team;
        string outcome = truce ? OutTruce : winner < 0 ? OutDrawn : winner == Sides[0].Team ? OutWon : OutLost;
        var soldiers = new List<BattleSoldierEnd>();
        foreach (var f in W.Fighters)
            soldiers.Add(new BattleSoldierEnd(f.Person, f.Team, f.Id, f.Act, f.Routing, f.X, f.Y, f.Launcher != null ? f.Ammo + (f.Loaded ? 1 : 0) : f.Kit.AmmoCount));
        Closed = true;
        var entry = WriteChronicle(sides, winner, outcome);
        Result = new BattleResult(Id, winner, outcome, W.Tick - StartTick, W.GameSec, sides, soldiers, entry);
        _paused = [];
    }

    // ------------------------------------------------------------------ the chronicle (battle-bubble-and-formations 8)

    private static string FormKey(FormationKind k) => k switch
    {
        FormationKind.Archers => "ch_bt_form_archers", FormationKind.Crossbows => "ch_bt_form_crossbows", FormationKind.Cavalry => "ch_bt_form_cavalry", FormationKind.Retinue => "ch_bt_form_retinue",
        FormationKind.Allied => "ch_bt_form_allied", FormationKind.Skirmishers => "ch_bt_form_skirmishers", FormationKind.EngineCrew => "ch_bt_form_engine", FormationKind.WagonFort => "ch_bt_form_wagon", _ => "ch_bt_form_infantry",
    };

    private EntityId WriteChronicle(List<BattleSideResult> sides, int winner, string outcome)
    {
        var sink = Chronicle;
        if (sink == null || sides.Count < 2) return EntityId.None;
        var when = new GameTime(W.GameSec);
        var a = sides[0]; var b = sides[1];
        var ps = new List<ChronParam>();
        if (Place != EntityId.None) ps.Add(ChronParam.Place("place", Place));
        ps.Add(ChronParam.Person("commander_a", new EntityId(a.Commander))); ps.Add(ChronParam.Person("commander_b", new EntityId(b.Commander)));
        ps.Add(ChronParam.Count("men_a", a.Men)); ps.Add(ChronParam.Count("men_b", b.Men)); ps.Add(ChronParam.Count("lost_a", a.Lost)); ps.Add(ChronParam.Count("lost_b", b.Lost));
        ps.Add(ChronParam.Count("waves_a", a.Waves)); ps.Add(ChronParam.Count("waves_b", b.Waves)); ps.Add(ChronParam.TextKey("outcome", outcome));
        var subjects = new List<EntityId>();
        foreach (var s in sides) if (s.Commander != 0) subjects.Add(new EntityId(s.Commander));
        var causes = new List<Cause>();
        if (CauseEntry != EntityId.None) causes.Add(new Cause(CauseKind, CauseEntry, "", CauseNoteKey));
        var id = sink(when, "battle_fought", "ch_bt_battle", ps, subjects, causes,
            [("battle", Id.ToString()), ("ticks", (W.Tick - StartTick).ToString()), ("winner", winner.ToString()), ("lost_a", a.Lost.ToString()), ("lost_b", b.Lost.ToString()), ("sides", sides.Count.ToString())]);
        var cause = new Cause(CauseKind.Battle, id, "", "ch_note_bt_field");
        // the hero led a side: his own line
        if (Hero >= 0)
        {
            var hs = Sides.FindIndex(s => s.Human);
            if (hs >= 0)
            {
                string hout = Truce ? OutTruce : winner < 0 ? OutDrawn : winner == Sides[hs].Team ? OutWon : OutLost;
                int forms = W.Formations!.Rows.Count(r => r.Side == Sides[hs].Team);
                var hp = new List<ChronParam>();
                if (Place != EntityId.None) hp.Add(ChronParam.Place("place", Place));
                hp.Add(ChronParam.Count("formations", forms)); hp.Add(ChronParam.Count("lost", sides[hs].Lost)); hp.Add(ChronParam.TextKey("outcome", hout));
                sink(when, "battle_hero_led", "ch_bt_hero_led", hp, [], [cause], [("battle", Id.ToString())]);
            }
        }
        // a named captain who fell, one entry each (knights and named captains only: a captain with a person behind him)
        foreach (int cid in NamedCaptains.Distinct().Order())
        {
            var f = W.Fighters[cid];
            if (f.Act != FAct.Dead) continue;
            var row = W.Formations!.RowOf(f);
            var cp = new List<ChronParam> { ChronParam.Person("person", new EntityId(f.Person)), ChronParam.TextKey("formation", FormKey(row?.Kind ?? FormationKind.Infantry)) };
            if (Place != EntityId.None) cp.Add(ChronParam.Place("place", Place));
            sink(when, "captain_fell", "ch_bt_captain_fell", cp, [new EntityId(f.Person)], [cause], [("battle", Id.ToString())]);
        }
        // a banner that fell to a named man of the other side: the nearest man of the winning side with a person behind him within 30 m
        if (winner >= 0)
            foreach (var r in W.Formations!.Rows)
            {
                if (r.Banner < 0 || r.Side == winner) continue;
                var bf = W.Fighters[r.Banner];
                if (bf.Active || bf.Person == 0) continue;
                var side = Sides.FirstOrDefault(s => s.Team == r.Side); if (side == null || side.Commander == 0) continue;
                Fighter? taker = null; long bestD = 30_000L * 30_000L;
                foreach (var f in W.Fighters)
                {
                    if (f.Team != winner || !f.Active || f.Person == 0) continue;
                    long dx = f.X - bf.X, dy = f.Y - bf.Y, d2 = dx * dx + dy * dy;
                    if (d2 < bestD) { bestD = d2; taker = f; }
                }
                if (taker == null) continue;
                var bp = new List<ChronParam> { ChronParam.Person("lord", new EntityId(side.Commander)), ChronParam.Person("taker", new EntityId(taker.Person)) };
                if (Place != EntityId.None) bp.Add(ChronParam.Place("place", Place));
                sink(when, "banner_taken", "ch_bt_banner_taken", bp, [new EntityId(taker.Person)], [cause], [("battle", Id.ToString())]);
            }
        return id;
    }
}
