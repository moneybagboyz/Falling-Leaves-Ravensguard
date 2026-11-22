using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// Engines are targets (sieges-and-engines 4.2): a stone or a bolt breaks the part it hits, fire burns the parts that burn, wet hides and boards stand between a missile and the crew, and an engine that loses what it works by is
/// disabled (once, in the chronicle). The blow is the same kg m/s the walls take: a part takes <c>(M - threshold) x hit points per kg m/s</c> and passes on what it did not stop, so a bolt (60) breaks one part and a stone (4,500) goes through
/// a whole cell. Part hit points are the part's own (content).
/// </summary>
public sealed partial class EngineBook
{
    /// <summary>
    /// Strikes the parts of one cell of a vehicle from the top down with <paramref name="momentumKgMs"/>; a part that breaks passes on the rest, one that holds stops the blow. A stone comes down from above (every deck of the cell);
    /// a bolt flies flat along a level, so it meets the stack from that deck down (<paramref name="fromDeck"/>). Returns how many parts broke. An engine of the book is settled after (its working parts, a tower's base, the chronicle).
    /// </summary>
    public int StrikeCell(VehicleRow v, int cx, int cy, long momentumKgMs, long sec, EngineWorld? w = null, int fromDeck = int.MaxValue)
    {
        var r = Table.Rules; long m = momentumKgMs; int broken = 0; long unit = Math.Max(1, r.PartHpPerKgMs) * 1000L;
        foreach (int i in Stack(v, cx, cy))
        {
            if (m <= r.PartThresholdKgMs) break;
            if (v.Parts[i].Deck > fromDeck) continue;
            var p = v.Parts[i]; if (p.State != PartState.Built) continue;
            long dmg = (m - r.PartThresholdKgMs) * unit, applied = Math.Min(dmg, p.HpMilli);
            Rides.Vehicles.Damage(v, i, (int)applied);
            if (v.Parts[i].State != PartState.Broken) break;
            broken++;
            m -= r.PartThresholdKgMs + (applied + unit - 1) / unit;
        }
        if (Engines.TryGetValue(v.Id, out var e)) { if (broken > 0) Lose(e, v); Settle(e, sec, w, "ch_note_sg_smashed"); }
        return broken;
    }

    /// <summary>Takes parts that broke off the burning list.</summary>
    private static void Lose(Engine e, VehicleRow v) => e.Burning.RemoveAll(i => i >= v.Parts.Count || v.Parts[i].State != PartState.Built);

    /// <summary>
    /// Settles an engine after damage: a tower whose base beams are over half gone topples; an engine that no longer works is disabled and the chronicle says so once (a mended one works again). <paramref name="note"/> says how it fell.
    /// </summary>
    public void Settle(Engine e, long sec, EngineWorld? w, string note)
    {
        var v = VehicleOf(e); if (v == null) return;
        if (e.Kind == EngineKind.Tower && !e.Toppled)
        {
            int total = 0, built = 0;
            foreach (var p in v.Parts) if (p.Deck == 0 && Cat.PartList[p.Part].Id == "siege_beam") { total++; if (p.State == PartState.Built) built++; }
            if (total > 0 && built * 2 < total) e.Toppled = true;
        }
        bool works = Works(e);
        if (!works && !e.Disabled)
        {
            e.Disabled = true;
            if (w?.Chronicle is { } ch)
            {
                int cx = CellOf(v.XMm), cy = CellOf(v.YMm);
                ch(new GameTime(sec), "engine_wrecked", "ch_sg_engine_wrecked", [ChronParam.TextKey("engine", "ch_sg_kind_" + e.Template), ChronParam.Count("x", cx), ChronParam.Count("y", cy)], [],
                    [new Cause(CauseKind.Siege, w.Cause == default ? null : w.Cause, "", note)], [("x", cx.ToString()), ("y", cy.ToString()), ("engine", e.Template)]);
            }
        }
        else if (works && e.Disabled) e.Disabled = false;
    }

    // ------------------------------------------------------------------ cover: the boards between a missile and the crew

    /// <summary>The share of missiles (permille) the engine's boards stop for its crew: its template's cover, times the share of its cover parts (penthouse, mantlet boards, hides) that still stand.</summary>
    public int CoverPm(Engine e)
    {
        var v = VehicleOf(e); var st = Stat(e); if (v == null || st.CoverPm <= 0) return 0;
        int total = Count(v, "cover", out int built);
        return total == 0 ? 0 : (int)((long)st.CoverPm * built / total);
    }

    /// <summary>
    /// A missile of <paramref name="momentumMilli"/> shot at the crew: whether a board stops it (stream 0x7C0 keyed by the engine and its cover serial), and if so one of the standing cover parts, drawn, takes the blow. False: it comes through, to be
    /// resolved against the person.
    /// </summary>
    public bool Covered(Engine e, long momentumMilli, long sec, EngineWorld? w = null)
    {
        int pm = CoverPm(e); var v = VehicleOf(e); if (pm <= 0 || v == null) return false;
        uint serial = e.CoverSerial++;
        if (DetRng.Range(Seed, serial, e.Vehicle.Value, RngStreams.EngineShotLanding, 0x4000_0000u | serial * 2, 0, 1000) >= pm) return false;
        var boards = new List<int>();
        for (int i = 0; i < v.Parts.Count; i++) if (v.Parts[i].State == PartState.Built && Cat.PartList[v.Parts[i].Part].Tags.Contains("cover")) boards.Add(i);
        if (boards.Count == 0) return false;
        int pick = boards[DetRng.Range(Seed, serial, e.Vehicle.Value, RngStreams.EngineShotLanding, 0x4000_0000u | (serial * 2 + 1), 0, boards.Count)];
        HitPart(v, pick, momentumMilli / 1000);
        Lose(e, v); Settle(e, sec, w, "ch_note_sg_smashed");
        return true;
    }

    private void HitPart(VehicleRow v, int index, long momentumKgMs)
    {
        var r = Table.Rules;
        if (momentumKgMs <= r.PartThresholdKgMs || v.Parts[index].State != PartState.Built) return;
        long dmg = (momentumKgMs - r.PartThresholdKgMs) * Math.Max(1, r.PartHpPerKgMs) * 1000L;
        Rides.Vehicles.Damage(v, index, (int)Math.Min(dmg, v.Parts[index].HpMilli));
    }

    // ------------------------------------------------------------------ fire

    /// <summary>Whether the part burns: not iron, not the ballast, and a pair of hides only when they are dry.</summary>
    private bool Burns(Engine e, VehicleRow v, int i, long nowSec)
    {
        if (v.Parts[i].State != PartState.Built) return false;
        var tags = Cat.PartList[v.Parts[i].Part].Tags;
        if (Table.Fireproof(tags)) return false;
        return !(tags.Contains("hides") && nowSec < e.WetUntilSec);
    }

    /// <summary>
    /// A burning pot or torch lands at a point: if it is over the engine, the top part there that burns catches with the rules' chance (permille), a tenth of it with wet hides over the cell (stream 0x7C0, keyed by the engine and its fire serial).
    /// True when something is alight.
    /// </summary>
    public bool Ignite(Engine e, long wx, long wy, long nowSec)
    {
        var v = VehicleOf(e); if (v == null || CellAt(v, wx, wy) is not { } c) return false;
        uint serial = e.FireSerial++;
        var stack = Stack(v, c.X, c.Y);
        bool wet = nowSec < e.WetUntilSec && stack.Any(i => v.Parts[i].State == PartState.Built && Cat.PartList[v.Parts[i].Part].Tags.Contains("hides"));
        int pm = Table.Rules.IgnitePm * (wet ? Table.Rules.WetHidesPm : 1000) / 1000;
        if (DetRng.Range(Seed, serial, e.Vehicle.Value, RngStreams.EngineShotLanding, 0x8000_0000u | serial, 0, 1000) >= pm) return false;
        foreach (int i in stack)
            if (Burns(e, v, i, nowSec)) { Light(e, i); return true; }
        return false;
    }

    private static void Light(Engine e, int i) { if (!e.Burning.Contains(i)) { e.Burning.Add(i); e.Burning.Sort(); } }

    /// <summary>The crew pour water on the hides: they are wet again for the rules' hours, and what burns on the engine is put out.</summary>
    public void Douse(Engine e, long nowSec)
    {
        e.WetUntilSec = nowSec + Table.Rules.HidesDryHours * 3600L;
        e.Burning.Clear();
    }

    /// <summary>What burns on the engine loses hit points for <paramref name="seconds"/>; a part that burns out lets the fire down to the next that burns in its cell.</summary>
    private void Burn(Engine e, long seconds, long nowSec, EngineWorld? w)
    {
        if (e.Burning.Count == 0 || seconds <= 0) return;
        var v = VehicleOf(e); if (v == null) { e.Burning.Clear(); return; }
        e.BurnCarry += Table.Rules.BurnHpPerMin * 1000L * seconds;
        long loss = e.BurnCarry / 60; e.BurnCarry %= 60;
        if (loss <= 0) return;
        foreach (int i in e.Burning.ToList())
        {
            if (i >= v.Parts.Count || v.Parts[i].State != PartState.Built) continue;
            Rides.Vehicles.Damage(v, i, (int)Math.Min(loss, v.Parts[i].HpMilli));
            if (v.Parts[i].State != PartState.Broken) continue;
            var p = v.Parts[i];
            var stack = Stack(v, p.X, p.Y); bool below = false;
            foreach (int j in stack)
            {
                if (j == i) { below = true; continue; }
                if (below && Burns(e, v, j, nowSec)) { Light(e, j); break; }
            }
        }
        Lose(e, v);
        Settle(e, nowSec, w, "ch_note_sg_burnt");
    }
}
