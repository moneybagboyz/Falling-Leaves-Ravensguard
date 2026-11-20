using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Burning;

/// <summary>What the abstract fire rule knows of a building (fire 4.5): where it stands, how it is roofed and walled, whether a hearth warms it.</summary>
public sealed class BuildingProfile
{
    public int Structure;
    public int MinX, MinY, MaxX, MaxY;
    public bool ThatchRoof, FlammableRoof, FlammableWalls, Hearth, Dwelling, Altar;
    public int FlammableCells;
    public int CentreX => (MinX + MaxX) / 2;
    public int CentreY => (MinY + MaxY) / 2;
}

public sealed partial class FireWorld
{
    // ---- hooks of the abstract step ----
    /// <summary>Able adults within 200 m of a point (the bucket chain's strength, 4.5); null: none.</summary>
    public Func<int, int, int>? AbleAdultsNear { get; set; }
    /// <summary>Whether a building at this point belongs to a town (accidents 0.2%) rather than a village (0.1%).</summary>
    public Func<int, int, bool>? IsTown { get; set; }
    /// <summary>The stores of a structure's owner (a barn's granary): the share of a damaged building's goods burns.</summary>
    public Func<int, IEnumerable<Store>>? StoresOfStructure { get; set; }
    /// <summary>Whether a standing-crop cell is at (x, y, z) in late summer (fuel class straw, fire open question 2); null: no fields burn.</summary>
    public Func<int, int, int, bool>? CropAt { get; set; }
    /// <summary>A crop cell burnt out at (x, y): the farming system marks the field burnt and its yield lost.</summary>
    public Action<int, int, long>? CropBurnt { get; set; }

    public const int AbstractBurnHours = 3;
    public const int SpreadRangeM = 10;
    public const int ChainAdults = 10;

    // ------------------------------------------------------------------ profiles

    public BuildingProfile? ProfileOf(int structure)
    {
        var cells = Rooms.StructureCells(structure);
        if (cells.Count == 0) return null;
        var p = new BuildingProfile { Structure = structure, MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue };
        foreach (var k in cells)
        {
            var (x, y, z) = Unpack(k);
            p.MinX = Math.Min(p.MinX, x); p.MaxX = Math.Max(p.MaxX, x); p.MinY = Math.Min(p.MinY, y); p.MaxY = Math.Max(p.MaxY, y);
            var s = Rooms.PieceAt(x, y, z); var f = Rooms.FixtureAt(x, y, z);
            var sc = Fuels.ClassOf(s?.Id);
            if (s != null && s.Kind == PieceKind.Roof) { if (sc.Burns) { p.FlammableRoof = true; if (sc.Id == "thatch") p.ThatchRoof = true; } }
            else if (s != null && s.Kind is PieceKind.Wall or PieceKind.Post && sc.Burns) p.FlammableWalls = true;
            if (sc.Burns || Fuels.ClassOf(f?.Id).Burns) p.FlammableCells++;
            if (f != null && f.Tags.Contains("hearth")) p.Hearth = true;
            if (f != null && f.Tags.Contains("altar")) p.Altar = true;
        }
        p.Dwelling = Rooms.Facts(structure)?.IsDwelling ?? false;
        return p;
    }

    /// <summary>The profiles of every structure, in id order.</summary>
    public List<BuildingProfile> Profiles()
    {
        var l = new List<BuildingProfile>();
        foreach (var id in Rooms.StructureIds) if (ProfileOf(id) is { } p) l.Add(p);
        return l;
    }

    // ------------------------------------------------------------------ an abstract fire (4.5)

    /// <summary>Starts a fire in a building the player is not watching: a <c>fire_started</c> entry and a building fire that burns three game hours.</summary>
    public bool IgniteBuilding(int structure, long hour, FireCause cause)
    {
        if (Book.Buildings.ContainsKey(structure) || ProfileOf(structure) is not { } p || p.FlammableCells == 0) return false;
        if (Book.Cells.Count == 0 && Book.Buildings.Count == 0) Book.HourDone = hour - 1;
        var parameters = new List<ChronParam> { ChronParam.TextKey("cause", cause.NoteKey), ChronParam.Count("x", p.CentreX), ChronParam.Count("y", p.CentreY) };
        var id = Chronicle?.Invoke(GameTime.FromGameHours(hour), "fire_started", "ch_fire_started", parameters, cause.Subjects, cause.Causes,
            [("x", p.CentreX.ToString()), ("y", p.CentreY.ToString()), ("structure", structure.ToString()), ("cause", cause.Kind)]) ?? EntityId.None;
        if (id == EntityId.None) id = EntityId.Make(IdKind.Event, 0xF1E_0000_0000UL | Book.NextSerial++);
        Book.Buildings[structure] = new BuildingFire { Structure = structure, StartHour = hour, Fire = id.Value, CauseKind = cause.Kind, Flammable = p.FlammableCells };
        Book.Incidents[id.Value] = new FireIncident { Entry = id.Value, StartSec = hour * 3600, CauseKind = cause.Kind };
        return true;
    }

    /// <summary>The chance, permille per hour, that a burning building lights a neighbour within 10 m: 400 thatch roof, 150 timber walls under another roof, 0 stone under slate or tile (4.5), before wind and wet.</summary>
    public static int SpreadBasePm(BuildingProfile target) => target.ThatchRoof ? 400 : target.FlammableWalls || target.FlammableRoof ? 150 : 0;

    /// <summary>One game hour of every building fire: each burns one more hour (its damage grows), lights neighbours, and after three hours it is out. Hours ascend; game hour <paramref name="hour"/> is the one that just ended.</summary>
    public void HourStep(long hour)
    {
        if (Book.Buildings.Count == 0) return;
        long sec = (hour + 1) * 3600;
        var w = WeatherAt(hour * 3600);
        if (w.Raining) Book.LastRainSec = hour * 3600;
        int wet = WetPm(hour * 3600, w);
        // bounding boxes of every structure (cheap), full profiles only for what burns and what stands within range of it
        var boxes = new Dictionary<int, (int X0, int Y0, int X1, int Y1)>();
        foreach (var id in Rooms.StructureIds) boxes[id] = BoxOf(id);
        var profiles = new Dictionary<int, BuildingProfile>();
        BuildingProfile? Profile(int id) { if (profiles.TryGetValue(id, out var p)) return p; var np = ProfileOf(id); if (np != null) profiles[id] = np; return np; }
        var burning = Book.Buildings.Keys.ToList();
        var lit = new List<(int Target, ulong Fire, string Cause)>();
        foreach (var sid in burning)
        {
            var b = Book.Buildings[sid];
            if (Profile(sid) is not { } src) { Book.Buildings.Remove(sid); continue; }
            if (hour < b.StartHour) continue;
            b.BurnedHours++;
            ApplyDamage(src, b, FinalDamagePm(src) * b.BurnedHours / AbstractBurnHours, sec);
            foreach (var (tid, box) in boxes)
            {
                if (tid == sid || Book.Buildings.ContainsKey(tid) || lit.Any(l => l.Target == tid)) continue;
                if (GapM(boxes[sid], box) > SpreadRangeM) continue;
                if (Profile(tid) is not { } t) continue;
                int d = GapM(src, t);
                if (d > SpreadRangeM) continue;
                long p = SpreadBasePm(t) * 1000L;
                if (p == 0) { MarkStopped(b, t, sec, w); continue; }
                p = p * WindPm(w, t.CentreX - src.CentreX, t.CentreY - src.CentreY, false) / 1000;
                p = p * wet / 1000;
                int adults = AbleAdultsNear?.Invoke(t.CentreX, t.CentreY) ?? 0;
                if (adults >= ChainAdults) p /= 2;
                if (DetRng.Range(Seed, hour, (ulong)(uint)t.Structure | (1UL << 40), RngStreams.FireSpread, 20, 0, 1_000_000) < p) lit.Add((t.Structure, b.Fire, "spread"));
                else MarkStopped(b, t, sec, w);
            }
        }
        foreach (var (target, fire, cause) in lit)
        {
            if (Book.Buildings.ContainsKey(target)) continue;
            Book.Buildings[target] = new BuildingFire { Structure = target, StartHour = hour + 1, Fire = fire, CauseKind = cause, Spread = true, Flammable = Profile(target)?.FlammableCells ?? 0 };
        }
        // out after three hours
        foreach (var sid in Book.Buildings.Keys.ToList())
        {
            var b = Book.Buildings[sid];
            if (b.BurnedHours < AbstractBurnHours) continue;
            Book.Buildings.Remove(sid);
            if (Book.Incidents.TryGetValue(b.Fire, out var inc)) { inc.Structures.Add(sid); }
            if (!Book.Buildings.Values.Any(o => o.Fire == b.Fire) && !Book.Cells.Values.Any(c => c.Fire == b.Fire) && Book.Incidents.TryGetValue(b.Fire, out var done)) End(done, sec);
        }
        if (Book.Buildings.Count == 0 && Book.Cells.Count == 0) Book.HourDone = -1;
        else Book.HourDone = hour;
    }

    /// <summary>A neighbour in range that did not catch: where the fire stopped for the faith hook, only when it is a sacred building (stone walls, a change of wind or rain).</summary>
    private void MarkStopped(BuildingFire src, BuildingProfile t, long sec, in FireWeather w)
    {
        if (!t.Altar || !Book.Incidents.TryGetValue(src.Fire, out var inc) || inc.StoppedAtStructure == t.Structure) return;
        inc.StoppedBy = !t.FlammableWalls && !t.FlammableRoof ? "stone_wall" : w.Raining ? "rain" : "wind_change";
        inc.StoppedAtStructure = t.Structure;
    }

    private (int X0, int Y0, int X1, int Y1) BoxOf(int structure)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var k in Rooms.StructureCells(structure)) { var (x, y, _) = Unpack(k); if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        return (x0, y0, x1, y1);
    }

    private static int GapM((int X0, int Y0, int X1, int Y1) a, (int X0, int Y0, int X1, int Y1) b)
    {
        int gx = Math.Max(0, Math.Max(b.X0 - a.X1, a.X0 - b.X1)), gy = Math.Max(0, Math.Max(b.Y0 - a.Y1, a.Y0 - b.Y1));
        return (int)Math.Sqrt((double)gx * gx + (double)gy * gy);
    }

    private static int GapM(BuildingProfile a, BuildingProfile b)
    {
        int gx = Math.Max(0, Math.Max(b.MinX - a.MaxX, a.MinX - b.MaxX)), gy = Math.Max(0, Math.Max(b.MinY - a.MaxY, a.MinY - b.MaxY));
        return (int)Math.Sqrt((double)gx * gx + (double)gy * gy);
    }

    /// <summary>The damage a building takes when it has burned its three hours (4.5): thatch and timber 1000 (gutted); stone walls with a timber roof 400; nothing flammable, 0.</summary>
    public static int FinalDamagePm(BuildingProfile p) => p.ThatchRoof && p.FlammableWalls ? 1000 : p.FlammableWalls && p.FlammableRoof ? 1000 : p.FlammableRoof || p.FlammableWalls ? 400 : 0;

    /// <summary>Burns a building down to <paramref name="targetPm"/> of its damage: the flammable cells, roof first, in (z desc, key) order, proportionally to what was flammable when it caught. A damage already taken is not taken twice.</summary>
    private void ApplyDamage(BuildingProfile p, BuildingFire b, int targetPm, long sec)
    {
        targetPm = Math.Clamp(targetPm, 0, 1000);
        if (targetPm <= b.DamagePm) return;
        int goal = (int)((long)targetPm * b.Flammable / 1000);
        int take = Math.Max(0, goal - b.Taken);
        var flammable = new List<long>();
        foreach (var k in Rooms.StructureCells(p.Structure))
        {
            var (x, y, z) = Unpack(k);
            if (Fuels.ClassOf(Rooms.PieceAt(x, y, z)?.Id).Burns || Fuels.ClassOf(Rooms.FixtureAt(x, y, z)?.Id).Burns) flammable.Add(k);
        }
        flammable.Sort((a, c) => { var ua = Unpack(a); var uc = Unpack(c); int r = uc.Z.CompareTo(ua.Z); return r != 0 ? r : a.CompareTo(c); });
        var keys = flammable.Take(take).ToList();
        if (keys.Count > 0) { foreach (var k in keys) Book.Cells.Remove(k); BurnOutKeys(keys, sec, b.Fire); b.Taken += keys.Count; }
        if (StoresOfStructure != null && Book.Incidents.TryGetValue(b.Fire, out var inc))
            foreach (var st in StoresOfStructure(p.Structure)) BurnShare(st, b.DamagePm, targetPm, inc);
        b.DamagePm = targetPm;
    }

    private void BurnOutKeys(List<long> keys, long sec, ulong fire)
    {
        // goes through the same removal as a live cell: a scar, the host's destroy hook or the room world and the tiles
        foreach (var k in keys)
        {
            var (x, y, z) = Unpack(k);
            Book.Cells[k] = new FireCell { Key = k, State = CellFireState.Burning, Intensity = 1, StartS = sec, FuelLeftS = 0, Fire = fire, Class = (byte)FuelAt(x, y, z).Index };
        }
        BurnOut(keys, sec);
    }

    private void BurnShare(Store st, int fromPm, int toPm, FireIncident inc)
    {
        if (toPm <= fromPm || fromPm >= 1000) return;
        var rows = new List<(ushort Good, long Qty)>();
        for (int i = 0; i < st.RowCount; i++) rows.Add((st.GoodAt(i), st.QtyAt(i)));
        foreach (var (good, qty) in rows)
        {
            if (!Fuels.BurnableGoodClasses.Contains(st.Catalog.Def(good).Class)) continue;
            long take = qty * (toPm - fromPm) / (1000 - fromPm);
            long burnt = st.DestroyUpTo(good, take, DestroyReason.Burned);
            if (burnt <= 0) continue;
            inc.GoodsGrams += st.Catalog.MassG(good, burnt);
            inc.GoodsMites += ValueMites(st.Catalog, good, burnt);
        }
    }

    // ------------------------------------------------------------------ accidents and lightning (4.5, stream 0x6C3)

    /// <summary>The chance per game day, parts per billion, that a dwelling's hearth sets it alight: 0.2% a game year in a town of thatch, 0.1% in a village, x2 in winter, x3 in a drought.</summary>
    public static long AccidentPpbPerDay(bool town, bool winter, bool drought)
    {
        long yearly = town ? 2_000_000 : 1_000_000;                       // 0.2% and 0.1% of 1e9
        if (winter) yearly *= 2;
        if (drought) yearly *= 3;
        return yearly / TimeConst.DaysPerYear;
    }

    /// <summary>The day's accident rolls (at hour 0): every dwelling with a hearth and a roof that burns. Returns the structures set alight, in id order.</summary>
    public List<int> DayAccidents(long day, bool winter)
    {
        var started = new List<int>();
        var w = WeatherAt(day * 86400);
        foreach (var p in Profiles())
        {
            if (!p.Dwelling || !p.Hearth || !p.FlammableRoof || Book.Buildings.ContainsKey(p.Structure)) continue;
            long ppb = AccidentPpbPerDay(IsTown?.Invoke(p.CentreX, p.CentreY) ?? false, winter, w.Drought);
            if (!p.ThatchRoof) ppb /= 2;
            if (DetRng.Range(Seed, day, (ulong)p.Structure, RngStreams.FireAccidents, 0, 0, 1_000_000_000) < ppb && IgniteBuilding(p.Structure, day * 24, FireCause.Hearth)) started.Add(p.Structure);
        }
        return started;
    }

    /// <summary>A storm hour's lightning: 0.05% a thatched roof an hour (draw 1 of the accident stream). Returns the structures struck.</summary>
    public List<int> StormHour(long hour)
    {
        var hit = new List<int>();
        if (!WeatherAt(hour * 3600).Thunder) return hit;
        foreach (var p in Profiles())
            if (p.ThatchRoof && !Book.Buildings.ContainsKey(p.Structure) && DetRng.Range(Seed, hour, (ulong)p.Structure, RngStreams.FireAccidents, 1, 0, 10_000) < 5 && IgniteBuilding(p.Structure, hour, FireCause.Lightning)) hit.Add(p.Structure);
        return hit;
    }

    // ------------------------------------------------------------------ the bubble handoff (FB-13)

    /// <summary>
    /// A building burning in the abstract comes into the live bubble (the hero arrives): its remaining flammable cells at the front become live fire cells on the same pieces; the damage
    /// the abstract steps already did stays done (those pieces are gone), so it is not counted twice. The building fire is dropped; its incident continues with the live cells.
    /// </summary>
    public int EnterBubble(int structure, long sec)
    {
        if (!Book.Buildings.TryGetValue(structure, out var b)) return 0;
        Book.Buildings.Remove(structure);
        if (Book.Cells.Count == 0 && Book.Exposed.Count == 0) Book.SecDone = FireClock.SimS(sec) - 1;
        int n = 0;
        var keys = Rooms.StructureCells(structure).ToList();
        // the front: the flammable cells left, top down, a share of them by how long it has burned (a quarter at once, then half, then three quarters), the rest to catch from them
        var flam = new List<long>();
        foreach (var k in keys) { var (x, y, z) = Unpack(k); if (FuelAt(x, y, z).Burns && !Book.Cells.ContainsKey(k)) flam.Add(k); }
        flam.Sort((p1, p2) => { var u1 = Unpack(p1); var u2 = Unpack(p2); int r = u2.Z.CompareTo(u1.Z); return r != 0 ? r : p1.CompareTo(p2); });
        int share = Math.Max(1, flam.Count * (b.BurnedHours + 1) / (AbstractBurnHours + 1));
        foreach (var k in flam.Take(share))
        {
            var (x, y, z) = Unpack(k);
            var fuel = FuelAt(x, y, z);
            var c = Add(k, fuel, sec, b.Fire);
            c.Intensity = (byte)Math.Min(fuel.MaxIntensity, 1 + b.BurnedHours);
            c.StartS = sec - b.BurnedHours * 3600 / 3;
            c.State = CellFireState.Burning;
            n++;
        }
        return n;
    }

    /// <summary>The hero is gone and the live step has not run for a game hour: every structure with live cells goes back to a building fire (and cells outside any building burn out), so the abstract tick carries on.</summary>
    public int Retire(long hour)
    {
        if (Book.Cells.Count == 0) return 0;
        var byStructure = new SortedDictionary<int, (ulong Fire, int Cells)>();
        var loose = new List<long>();
        foreach (var c in Book.Cells.Values)
        {
            var (x, y, z) = Unpack(c.Key);
            int st = Rooms.StructureAt(x, y, z);
            if (st > 0) byStructure[st] = (c.Fire, byStructure.GetValueOrDefault(st).Cells + 1); else loose.Add(c.Key);
        }
        int n = 0;
        foreach (var (st, (fire, _)) in byStructure)
        {
            if (Book.Buildings.ContainsKey(st)) continue;
            Book.Buildings[st] = new BuildingFire { Structure = st, StartHour = hour, Fire = fire, CauseKind = "spread", BurnedHours = 1, Spread = true, Flammable = ProfileOf(st)?.FlammableCells ?? 0 };
            n++;
        }
        foreach (var k in byStructure.Keys) foreach (var ck in Rooms.StructureCells(k)) Book.Cells.Remove(ck);
        if (loose.Count > 0) BurnOut(loose, hour * 3600);
        Book.HourDone = hour;
        return n;
    }
}
