using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Battle;

/// <summary>
/// The hero's command path into the formation book (P7-BB-D, battle-bubble-and-formations 4.8): the three recorded inputs <see cref="FightInputKind.FormOrder"/>, <see cref="FightInputKind.FormFace"/> and
/// <see cref="FightInputKind.FormCaptain"/> (so a replay and a bug report carry them like any other fight input), their encoding, and the draft the tactical overlay stages orders in while the game is
/// paused. An order is issued through <see cref="FormationBook.Issue"/> with the hero as issuer and his Leadership: its arrival is its issue tick plus its delay, in fight ticks, so a paused game gives no
/// free time (BB-11). Nothing here reads a clock.
/// </summary>
public static class FormationCommands
{
    /// <summary>A = row id (8 bits) | kind (5) &lt;&lt; 8 | queued &lt;&lt; 13 | pivot right &lt;&lt; 14; B, C the target point (mm). For an advance, charge, attack or flank order B is instead the enemy formation's row index plus one (0: the nearest) and C is 0.</summary>
    public static FightInput Order(byte row, OrderKind kind, int x = 0, int y = 0, bool queue = false, bool pivotRight = false) =>
        new(FightInputKind.FormOrder, row | (int)kind << 8 | (queue ? 1 << 13 : 0) | (pivotRight ? 1 << 14 : 0), x, y);

    /// <summary>An order at an enemy formation (<paramref name="enemyRowIndex"/> is its index in the book's rows).</summary>
    public static FightInput At(byte row, OrderKind kind, int enemyRowIndex, bool queue = false) => Order(row, kind, enemyRowIndex + 1, 0, queue);

    /// <summary>True for the kinds whose B is a target formation, not a point.</summary>
    public static bool TakesFormation(OrderKind k) => k is OrderKind.Advance or OrderKind.Charge or OrderKind.AttackFormation or OrderKind.FlankLeft or OrderKind.FlankRight;

    /// <summary>The facing (a Q10 vector) and frontage (mm) of the order just given to the row.</summary>
    public static FightInput Face(byte row, int faceX, int faceY, int frontageMm) =>
        new(FightInputKind.FormFace, row, (ushort)(short)faceX | (int)(ushort)(short)faceY << 16, frontageMm);

    public static FightInput Captain(byte row, bool always) => new(FightInputKind.FormCaptain, row, always ? 1 : 0);

    /// <summary>True for the inputs this class applies (the overlay's draft holds only these).</summary>
    public static bool IsCommand(FightInput i) => i.Kind is FightInputKind.FormOrder or FightInputKind.FormFace or FightInputKind.FormCaptain;

    /// <summary>Applies one recorded command of the hero <paramref name="hero"/> to a formation of his side. A row that is not his, or a fight with no formations, ignores it.</summary>
    public static void Apply(FightWorld w, Fighter hero, FightInput i)
    {
        var book = w.Formations;
        if (book == null) return;
        var r = book.Find(hero.Team, (byte)(i.A & 0xFF));
        if (r == null) return;
        switch (i.Kind)
        {
            case FightInputKind.FormOrder:
            {
                var kind = (OrderKind)((i.A >> 8) & 0x1F);
                var o = FormationOrder.Of(kind);
                if (TakesFormation(kind)) o.TargetFormation = Math.Max(0, i.B); else { o.TargetX = i.B; o.TargetY = i.C; }
                if (kind == OrderKind.FollowMe) o.TargetFighter = hero.Id;
                if (kind == OrderKind.Wheel) { o.Arg = (i.A >> 14 & 1) != 0 ? 1 : -1; o.FaceX = r.FaceX; o.FaceY = r.FaceY; }
                book.Issue(w, r, o, hero.Id, book.HeroLeadership, (i.A >> 13 & 1) != 0);
                break;
            }
            case FightInputKind.FormFace:
            {
                int k = -1, serial = int.MinValue;                                  // the order given this tick with the highest serial: the one the player has just placed
                for (int p = 0; p < r.PendingCount; p++)
                    if (r.Pending[p].IssuedTick == w.Tick && r.Pending[p].Serial > serial) { serial = r.Pending[p].Serial; k = p; }
                if (k < 0) break;
                r.Pending[k].FaceX = (short)(i.B & 0xFFFF); r.Pending[k].FaceY = (short)(i.B >> 16);
                r.Pending[k].Frontage = Math.Clamp(i.C, 0, 400_000);
                break;
            }
            case FightInputKind.FormCaptain: r.CaptainAlways = i.B != 0; r.Micro = w.Tick; break;
        }
    }
}

/// <summary>
/// Orders the tactical overlay has placed while the game is paused, kept until the player resumes (battle 4.8: "Ctrl+Z undoes the last placement before resuming"). Committing sends them through the
/// recorded input path at the tick the game stands at, which is the tick it was paused at: the delay of each runs from there, so pausing for any length of real time gives nothing for free (BB-11).
/// Pure; it holds inputs and no clock.
/// </summary>
public sealed class OrderDraft
{
    private readonly List<(byte Row, FightInput[] Inputs)> _placed = [];

    public int Count => _placed.Count;

    /// <summary>The placements of one row, oldest first (the overlay draws them as pending arrows).</summary>
    public IEnumerable<FightInput[]> Of(byte row) { foreach (var p in _placed) if (p.Row == row) yield return p.Inputs; }

    /// <summary>One placement: an order and, for a drawn frontage, the face that goes with it.</summary>
    public void Place(byte row, params FightInput[] inputs) => _placed.Add((row, inputs));

    /// <summary>Takes back the last placement; false when there is none.</summary>
    public bool Undo() { if (_placed.Count == 0) return false; _placed.RemoveAt(_placed.Count - 1); return true; }

    /// <summary>Takes back every placement of a row (Delete: clear the queue).</summary>
    public void Clear(byte row) => _placed.RemoveAll(p => p.Row == row);

    public void Clear() => _placed.Clear();

    /// <summary>Sends every placement, in the order placed, through <paramref name="send"/> (the recorded input path) and empties the draft.</summary>
    public void Commit(Action<FightInput> send)
    {
        var all = _placed.ToArray();
        _placed.Clear();
        foreach (var p in all) foreach (var i in p.Inputs) send(i);
    }
}
