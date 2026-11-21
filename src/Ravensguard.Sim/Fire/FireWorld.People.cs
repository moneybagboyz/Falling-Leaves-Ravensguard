using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Sites;

namespace Ravensguard.Sim.Burning;

public sealed partial class FireWorld
{
    // ---- hooks for people and goods ----
    public WoundLedger? Wounds { get; set; }
    /// <summary>The body plan of a person (the human plan by default).</summary>
    public Func<ulong, BodyPlanV2?>? PlanOf { get; set; }
    /// <summary>The stores standing on a cell (a placed chest, a barn's bin): what burns with it.</summary>
    public Func<int, int, int, IEnumerable<Store>>? StoresAt { get; set; }

    /// <summary>Metres of storey: a room's air is its area times three (fire 4.3).</summary>
    public const int StoreyM = 3;

    // ------------------------------------------------------------------ people: flame and smoke (4.3)

    private readonly Dictionary<RoomV2, int> _burningIn = [];

    private void Exposure(long s, long sec)
    {
        if (PeopleNear == null && Book.Exposed.Count == 0) return;
        _people.Clear();
        PeopleNear?.Invoke(_people);
        if (_people.Count == 0 && Book.Exposed.Count == 0) return;
        _burningIn.Clear();
        foreach (var c in Book.Cells.Values)
        {
            var (x, y, z) = Unpack(c.Key);
            var r = Rooms.RoomAt(x, y, z) ?? Rooms.RoomAt(x, y, z - 1);
            if (r != null && r.Indoor) _burningIn[r] = _burningIn.GetValueOrDefault(r) + 1;
        }
        foreach (var p in _people)
        {
            long ck = Key(p.X, p.Y, p.Z);
            // standing in a burning cell: a burn wound to the exposed parts every 5 sim-s, graded by intensity
            if (Book.Cells.TryGetValue(ck, out var cell) && cell.State == CellFireState.Burning && s % FireClock.SpreadEveryS == 0) BurnPerson(p, cell, s, sec);
            var room = Rooms.RoomAt(p.X, p.Y, p.Z);
            int burning = room != null && room.Indoor ? _burningIn.GetValueOrDefault(room) : 0;
            if (burning > 0 && room != null)
            {
                var ex = ExposureOf(p.Person, FireOf(ck, room));
                ex.LastSeenSec = sec;
                ex.SmokeS++;
                int breathable = Math.Max(1, room.Area * StoreyM / (burning * 2));
                if (ex.SmokeS > breathable && ex.StopSec < 0) Suffocate(ex, sec);
            }
            else if (Book.Exposed.TryGetValue(p.Person, out var ex))
            {
                ex.LastSeenSec = sec;
                if (ex.StopSec >= 0) Breathe(ex, sec);
                ex.SmokeS = Math.Max(0, ex.SmokeS - 2);
            }
        }
        // someone no longer listed (died, left the bubble): forget a spent exposure
        List<ulong>? drop = null;
        foreach (var e in Book.Exposed.Values)
        {
            if (e.StopSec < 0 && e.SmokeS == 0 && sec - e.LastSeenSec > 600) (drop ??= []).Add(e.Person);
            else if (e.StopSec >= 0 && sec - e.LastSeenSec > 600) Breathe(e, sec);               // gone from the bubble for 30 sim-s (carried off, or the live tier let him go): he breathes again
        }
        if (drop != null) foreach (var d in drop) Book.Exposed.Remove(d);
    }

    private ulong FireOf(long cellKey, RoomV2 room)
    {
        if (Book.Cells.TryGetValue(cellKey, out var c)) return c.Fire;
        foreach (var k in room.CellKeys)
        {
            if (Book.Cells.TryGetValue(k, out var rc)) return rc.Fire;
            var (x, y, z) = Unpack(k);
            if (Book.Cells.TryGetValue(Key(x, y, z + 1), out var up)) return up.Fire;
        }
        foreach (var c2 in Book.Cells.Values) return c2.Fire;
        return 0;
    }

    private Exposure ExposureOf(ulong person, ulong fire)
    {
        if (!Book.Exposed.TryGetValue(person, out var e)) Book.Exposed[person] = e = new Exposure { Person = person, Fire = fire };
        if (fire != 0) e.Fire = fire;
        return e;
    }

    private BodyPlanV2? Plan(ulong person) => PlanOf?.Invoke(person);

    /// <summary>The smoke stops a person's breath: the wound book's breath clock starts (unconscious at 30 sim-s, dead at 240 without air, Phase 4).</summary>
    private void Suffocate(Exposure ex, long sec)
    {
        ex.StopSec = sec;
        if (Wounds == null || Plan(ex.Person) is not { } plan) return;
        var book = Wounds.For(ex.Person, plan, sec);
        lock (Wounds.Sync) { if (book.BreathStopSec < 0 && !book.Dead) book.BreathStopSec = sec; book.AdvanceTo(sec); }
        if (Book.Incidents.TryGetValue(ex.Fire, out var inc)) inc.Burned.Add(ex.Person);
    }

    /// <summary>He is out of the smoke (pulled out, or the fire is out): his breath returns.</summary>
    private void Breathe(Exposure ex, long sec)
    {
        ex.StopSec = -1;
        if (Wounds == null || Wounds.Find(ex.Person) is not { } book) return;
        lock (Wounds.Sync) { if (!book.Dead) { book.BreathStopSec = -1; book.AdvanceTo(sec); } }
    }

    /// <summary>The fire a person died in, for the death's cause chain (null when none): the incident's entry. Read by the wound step at the death.</summary>
    public EntityId? FireOfDeath(ulong person) => Book.Exposed.TryGetValue(person, out var e) && e.Fire != 0 ? new EntityId(e.Fire) : null;

    /// <summary>Records that a person died in a fire (the incident's death list, for the <c>fire_ended</c> entry) and forgets his exposure.</summary>
    public void NoteDeath(ulong person)
    {
        if (!Book.Exposed.TryGetValue(person, out var e)) return;
        if (Book.Incidents.TryGetValue(e.Fire, out var inc)) inc.Deaths.Add(person);
        Book.Exposed.Remove(person);
    }

    // ------------------------------------------------------------------ burns

    /// <summary>The part a flame reaches by chance, standing in it: legs and feet first, then hands and arms, the trunk, rarely the head.</summary>
    private static string PartFor(int roll, out bool right)
    {
        right = (roll & 1) == 1; int r = roll >> 1;
        return (r % 100) switch { < 25 => "lower_leg", < 40 => "foot", < 55 => "upper_leg", < 70 => "lower_arm", < 80 => "hand", < 94 => "upper_body", _ => "head" };
    }

    private void BurnPerson(FirePerson p, FireCell cell, long s, long sec) => BurnAt(p, cell.Intensity, cell.Fire, s, sec);

    /// <summary>A burn wound to a person at an intensity (1 scorch, 2 burn, 3 deep burn) from a fire (an incident id, or 0 for a blast with none).</summary>
    public void BurnAt(FirePerson p, int intensity, ulong fire, long s, long sec)
    {
        var ex = ExposureOf(p.Person, fire);
        ex.LastSeenSec = sec;
        if (Book.Incidents.TryGetValue(fire, out var inc)) inc.Burned.Add(p.Person);
        if (Wounds == null || Plan(p.Person) is not { } plan) return;
        int roll = DetRng.Range(Seed, s / FireClock.SpreadEveryS, p.Person, RngStreams.FireSpread, 100, 0, 200);
        int idx = SiteBlows.Part(plan, PartFor(roll, out bool right), right);
        var part = plan.Parts[idx];
        var skin = part.Layers.FirstOrDefault(l => l.IsSoft) ?? part.Layers[0];
        int depth = intensity switch { 1 => 250, 2 => 600, _ => 900 };
        var wounds = new List<WoundV2> { new() { Part = (short)idx, Tissue = (short)skin.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Burn, DepthPm = depth, Flags = WoundFlags.Burn } };
        if (intensity >= 3 && part.Layers.FirstOrDefault(l => l.IsMuscle) is { } muscle)
            wounds.Add(new WoundV2 { Part = (short)idx, Tissue = (short)muscle.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Burn, DepthPm = 600, Grade = (byte)MuscleGrade.Torn, Flags = WoundFlags.Burn });
        var book = Wounds.For(p.Person, plan, sec);
        lock (Wounds.Sync) book.Apply(plan, [.. wounds], sec, s, fire, 0, false);
    }

    // ------------------------------------------------------------------ goods that burn with the room (4.5, FB-8)

    private void Goods(long s, long sec)
    {
        if (StoresAt == null) return;
        foreach (var c in Book.Cells.Values)
        {
            var (x, y, z) = Unpack(c.Key);
            foreach (var st in StoresAt(x, y, z)) BurnStore(st, c, sec);
            foreach (var st in StoresAt(x, y, z - 1)) if (Rooms.PieceAt(x, y, z) is { Kind: PieceKind.Roof }) BurnStore(st, c, sec);
        }
    }

    private readonly List<(ushort Good, long Qty)> _rows = [];

    private void BurnStore(Store st, FireCell c, long sec)
    {
        if (st.RowCount == 0) return;
        _rows.Clear();
        for (int i = 0; i < st.RowCount; i++) _rows.Add((st.GoodAt(i), st.QtyAt(i)));
        int left = Math.Max(1, c.FuelLeftS);
        foreach (var (good, qty) in _rows)
        {
            if (!Fuels.BurnableGoodClasses.Contains(st.Catalog.Def(good).Class)) continue;
            long take = (qty + left - 1) / left;
            long burnt = st.DestroyUpTo(good, take, DestroyReason.Burned);
            if (burnt <= 0) continue;
            if (Book.Incidents.TryGetValue(c.Fire, out var inc))
            {
                inc.GoodsGrams += st.Catalog.MassG(good, burnt);
                inc.GoodsMites += ValueMites(st.Catalog, good, burnt);
            }
        }
    }

    /// <summary>The reference value of a quantity of a good, mites (base price per kg or per piece).</summary>
    public static long ValueMites(GoodCatalog cat, ushort good, long qty)
    {
        long milli = cat.BasePriceMilliMites(good);                                            // the base price in thousandths of a mite, made integer once (S16)
        return cat.IsPiece(good) ? (milli * qty + 500) / 1000 : (milli * qty + 500_000) / 1_000_000;
    }
}
