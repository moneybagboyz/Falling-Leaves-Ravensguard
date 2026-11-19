using System.Text;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// The site's bytes (exploration-and-sites 3.2, 4.1): the generated map as first visited (the snapshot, written once and never regenerated, so a visited place does not change
/// when content changes, plan 2.2) and the delta of what play changed since (tiles, water, air, containers, traps, ladders, collapses, the people). Little-endian, varints,
/// stable order: the same map gives the same bytes (save, load, save is byte-identical).
/// </summary>
public static class SiteCodec
{
    private const int Magic = 0x53495445, Version = 1;

    public static byte[] Encode(SiteMap m)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(Magic); w.Write(Version);
        w.Write(m.SiteId); w.Write(m.Seed); w.Write(m.Template); w.Write(m.ContentHash); w.Write(m.W); w.Write(m.H); w.Write(m.Levels);
        w.Write(m.Mouth); foreach (int a in m.Arrival) w.Write(a); foreach (int f in m.FloodCm) w.Write(f);
        w.Write(m.LootBudgetMites); w.Write(m.LootPlacedMites); w.Write(m.Taboo); w.Write(m.Lair); w.Write(m.Walls);
        foreach (var t in m.Tiles) w.Write((byte)t);
        foreach (var a in m.Air) w.Write(a);
        foreach (var x in m.WaterCm) w.Write(x);
        foreach (var r in m.Room) w.Write(r);
        w.Write(m.Rooms.Count);
        foreach (var r in m.Rooms) { w.Write(r.Template); w.Write(r.Level); w.Write(r.X); w.Write(r.Y); w.Write(r.W); w.Write(r.H); w.Write(r.Purposes.Count); foreach (var p in r.Purposes) w.Write(p); }
        w.Write(m.Portals.Count); foreach (var p in m.Portals) WritePortal(w, p);
        w.Write(m.Containers.Count); foreach (var c in m.Containers) WriteContainer(w, c);
        w.Write(m.Traps.Count); foreach (var t in m.Traps) WriteTrap(w, t);
        w.Write(m.Unstable.Count); foreach (var z in m.Unstable) { w.Write(z.Id); w.Write(z.Level); w.Write(z.X0); w.Write(z.Y0); w.Write(z.X1); w.Write(z.Y1); w.Write(z.InstabilityPm); w.Write(z.Collapsed); }
        w.Write(m.Inhabitants.Count); foreach (var n in m.Inhabitants) WriteFolk(w, n);
        w.Write(m.Sconces.Count); foreach (int s in m.Sconces) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    public static SiteMap Decode(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        if (r.ReadInt32() != Magic || r.ReadInt32() != Version) throw new InvalidDataException("not a site snapshot");
        ulong id = r.ReadUInt64(), seed = r.ReadUInt64(); string tpl = r.ReadString(), hash = r.ReadString(); int W = r.ReadInt32(), H = r.ReadInt32(), L = r.ReadInt32();
        int mouth = r.ReadInt32(); var arrival = new int[L]; for (int i = 0; i < L; i++) arrival[i] = r.ReadInt32(); var flood = new int[L]; for (int i = 0; i < L; i++) flood[i] = r.ReadInt32();
        int n = W * H * L;
        var m = new SiteMap
        {
            SiteId = id, Seed = seed, Template = tpl, ContentHash = hash, W = W, H = H, Levels = L, Arrival = arrival, FloodCm = flood,
            Tiles = new SiteTile[n], Air = new ushort[n], WaterCm = new ushort[n], Room = new short[n],
        };
        m.Mouth = mouth; m.LootBudgetMites = r.ReadInt32(); m.LootPlacedMites = r.ReadInt32(); m.Taboo = r.ReadBoolean(); m.Lair = r.ReadBoolean(); m.Walls = r.ReadString();
        for (int i = 0; i < n; i++) m.Tiles[i] = (SiteTile)r.ReadByte();
        for (int i = 0; i < n; i++) m.Air[i] = r.ReadUInt16();
        for (int i = 0; i < n; i++) m.WaterCm[i] = r.ReadUInt16();
        for (int i = 0; i < n; i++) m.Room[i] = r.ReadInt16();
        int rooms = r.ReadInt32();
        for (int i = 0; i < rooms; i++)
        {
            string t = r.ReadString(); int l = r.ReadInt32(), x = r.ReadInt32(), y = r.ReadInt32(), w = r.ReadInt32(), h = r.ReadInt32(); int pc = r.ReadInt32();
            var ps = new List<string>(); for (int k = 0; k < pc; k++) ps.Add(r.ReadString());
            m.Rooms.Add(new PlacedRoom(i, t, l, x, y, w, h, ps));
        }
        int pn = r.ReadInt32(); for (int i = 0; i < pn; i++) m.Portals.Add(ReadPortal(r));
        int cn = r.ReadInt32(); for (int i = 0; i < cn; i++) m.Containers.Add(ReadContainer(r));
        int tn = r.ReadInt32(); for (int i = 0; i < tn; i++) m.Traps.Add(ReadTrap(r));
        int zn = r.ReadInt32();
        for (int i = 0; i < zn; i++) m.Unstable.Add(new UnstableZone { Id = r.ReadInt32(), Level = r.ReadInt32(), X0 = r.ReadInt32(), Y0 = r.ReadInt32(), X1 = r.ReadInt32(), Y1 = r.ReadInt32(), InstabilityPm = r.ReadInt32(), Collapsed = r.ReadBoolean() });
        int fn = r.ReadInt32(); for (int i = 0; i < fn; i++) m.Inhabitants.Add(ReadFolk(r));
        int sn = r.ReadInt32(); for (int i = 0; i < sn; i++) m.Sconces.Add(r.ReadInt32());
        return m;
    }

    public static SiteMap Clone(SiteMap m) => Decode(Encode(m));

    private static void WritePortal(BinaryWriter w, SitePortal p) { w.Write(p.Id); w.Write((byte)p.Kind); w.Write(p.UpperCell); w.Write(p.LowerCell); w.Write(p.Enabled); }
    private static SitePortal ReadPortal(BinaryReader r) => new() { Id = r.ReadInt32(), Kind = (PortalKindSite)r.ReadByte(), UpperCell = r.ReadInt32(), LowerCell = r.ReadInt32(), Enabled = r.ReadBoolean() };

    private static void WriteItems(BinaryWriter w, List<LootItem> items)
    {
        w.Write(items.Count);
        foreach (var it in items) { w.Write(it.Item); w.Write(it.Material); w.Write(it.ValueMites); w.Write(it.MassG); w.Write(it.ConditionPm); w.Write(it.Named); w.Write(it.Dirty); w.Write(it.Grave); }
    }

    public static List<LootItem> ReadItems(BinaryReader r)
    {
        int n = r.ReadInt32(); var l = new List<LootItem>(n);
        for (int i = 0; i < n; i++) l.Add(new LootItem(r.ReadString(), r.ReadString(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadString(), r.ReadBoolean(), r.ReadBoolean()));
        return l;
    }

    public static byte[] Items(List<LootItem> items) { using var ms = new MemoryStream(); using var w = new BinaryWriter(ms); WriteItems(w, items); w.Flush(); return ms.ToArray(); }
    public static List<LootItem> Items(byte[] b) { using var r = new BinaryReader(new MemoryStream(b)); return ReadItems(r); }

    private static void WriteContainer(BinaryWriter w, SiteContainer c) { w.Write(c.Id); w.Write(c.Cell); w.Write((byte)c.Kind); w.Write(c.Room); w.Write(c.Dead); w.Write(c.Opened); w.Write(c.Grave); WriteItems(w, c.Items); }
    private static SiteContainer ReadContainer(BinaryReader r) => new() { Id = r.ReadInt32(), Cell = r.ReadInt32(), Kind = (ContainerKind)r.ReadByte(), Room = r.ReadInt32(), Dead = r.ReadString(), Opened = r.ReadBoolean(), Grave = r.ReadBoolean(), Items = ReadItems(r) };

    private static void WriteTrap(BinaryWriter w, SiteTrap t) { w.Write(t.Id); w.Write(t.Def); w.Write(t.Cell); w.Write(t.Armed); w.Write(t.Detected); w.Write(t.Triggered); w.Write(t.Owners); }
    private static SiteTrap ReadTrap(BinaryReader r) => new() { Id = r.ReadInt32(), Def = r.ReadString(), Cell = r.ReadInt32(), Armed = r.ReadBoolean(), Detected = r.ReadBoolean(), Triggered = r.ReadInt32(), Owners = r.ReadBoolean() };

    private static void WriteFolk(BinaryWriter w, Inhabitant n)
    {
        w.Write(n.Id); w.Write(n.Role); w.Write(n.Kit); w.Write(n.Monster); w.Write(n.Name); w.Write(n.Home); w.Write(n.Bed); w.Write(n.X); w.Write(n.Y); w.Write(n.Level); w.Write((byte)n.State);
        w.Write(n.Skill); w.Write(n.TriggerS); w.Write(n.LungeS); w.Write(n.LostS); w.Write(n.InvestigateS); w.Write(n.StayS); w.Write(n.FledDay); w.Write(n.LossPm); w.Write(n.Hostile);
        w.Write(n.TargetX); w.Write(n.TargetY); w.Write(n.TargetLevel); w.Write(n.Legend);
        w.Write(n.Path?.Count ?? -1);
        if (n.Path != null) foreach (var c in n.Path) { w.Write(c.X); w.Write(c.Y); w.Write(c.Z); }
        w.Write(n.PathIdx);
    }

    private static Inhabitant ReadFolk(BinaryReader r)
    {
        var n = new Inhabitant
        {
            Id = r.ReadUInt64(), Role = r.ReadString(), Kit = r.ReadString(), Monster = r.ReadString(), Name = r.ReadString(), Home = r.ReadInt32(), Bed = r.ReadInt32(), X = r.ReadInt32(), Y = r.ReadInt32(),
            Level = r.ReadInt32(), State = (InhabitantState)r.ReadByte(), Skill = r.ReadInt32(), TriggerS = r.ReadInt32(), LungeS = r.ReadInt32(), LostS = r.ReadInt32(), InvestigateS = r.ReadInt32(),
            StayS = r.ReadInt32(), FledDay = r.ReadInt32(), LossPm = r.ReadInt32(), Hostile = r.ReadBoolean(), TargetX = r.ReadInt32(), TargetY = r.ReadInt32(), TargetLevel = r.ReadInt32(), Legend = r.ReadBoolean(),
        };
        int pc = r.ReadInt32();
        if (pc >= 0) { n.Path = new List<Cell3>(pc); for (int i = 0; i < pc; i++) n.Path.Add(new Cell3(r.ReadInt32(), r.ReadInt32(), r.ReadInt32())); }
        n.PathIdx = r.ReadInt32();
        return n;
    }

    // ------------------------------------------------------------------ the delta (what play changed since the snapshot)

    /// <summary>What <paramref name="live"/> changed against <paramref name="baseMap"/>: changed cells, the flood, changed containers, every trap, ladder and zone flag, the people.</summary>
    public static byte[] Delta(SiteMap baseMap, SiteMap live)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(Version);
        void Cells<T>(T[] a, T[] b, Action<T> write) where T : IEquatable<T>
        {
            int n = 0; for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) n++;
            w.Write(n);
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) { w.Write(i); write(b[i]); }
        }
        var bt = Array.ConvertAll(baseMap.Tiles, t => (byte)t); var lt = Array.ConvertAll(live.Tiles, t => (byte)t);
        Cells(bt, lt, w.Write);
        Cells(baseMap.Air, live.Air, w.Write);
        Cells(baseMap.WaterCm, live.WaterCm, w.Write);
        foreach (int f in live.FloodCm) w.Write(f);
        var changed = new List<SiteContainer>();
        for (int i = 0; i < live.Containers.Count; i++)
        {
            var a = i < baseMap.Containers.Count ? baseMap.Containers[i] : null; var b = live.Containers[i];
            if (a == null || a.Opened != b.Opened || !a.Items.SequenceEqual(b.Items)) changed.Add(b);
        }
        w.Write(changed.Count); foreach (var c in changed) WriteContainer(w, c);
        w.Write(live.Traps.Count); foreach (var t in live.Traps) WriteTrap(w, t);
        w.Write(live.Portals.Count); foreach (var p in live.Portals) w.Write(p.Enabled);
        w.Write(live.Unstable.Count); foreach (var z in live.Unstable) w.Write(z.Collapsed);
        w.Write(live.Inhabitants.Count); foreach (var n in live.Inhabitants) WriteFolk(w, n);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>The snapshot with the delta applied: the site as play left it.</summary>
    public static SiteMap Apply(SiteMap baseMap, byte[] delta)
    {
        var m = Clone(baseMap);
        using var r = new BinaryReader(new MemoryStream(delta), Encoding.UTF8);
        if (r.ReadInt32() != Version) throw new InvalidDataException("unknown site delta");
        int n = r.ReadInt32(); for (int i = 0; i < n; i++) { int c = r.ReadInt32(); m.Tiles[c] = (SiteTile)r.ReadByte(); }
        n = r.ReadInt32(); for (int i = 0; i < n; i++) { int c = r.ReadInt32(); m.Air[c] = r.ReadUInt16(); }
        n = r.ReadInt32(); for (int i = 0; i < n; i++) { int c = r.ReadInt32(); m.WaterCm[c] = r.ReadUInt16(); }
        for (int l = 0; l < m.Levels; l++) m.FloodCm[l] = r.ReadInt32();
        n = r.ReadInt32(); for (int i = 0; i < n; i++) { var c = ReadContainer(r); int k = m.Containers.FindIndex(x => x.Id == c.Id); if (k >= 0) m.Containers[k] = c; else m.Containers.Add(c); }
        n = r.ReadInt32(); m.Traps.Clear(); for (int i = 0; i < n; i++) m.Traps.Add(ReadTrap(r));
        n = r.ReadInt32(); for (int i = 0; i < n; i++) { bool e = r.ReadBoolean(); if (i < m.Portals.Count) m.Portals[i].Enabled = e; }
        n = r.ReadInt32(); for (int i = 0; i < n; i++) { bool c = r.ReadBoolean(); if (i < m.Unstable.Count) m.Unstable[i].Collapsed = c; }
        n = r.ReadInt32(); m.Inhabitants.Clear(); for (int i = 0; i < n; i++) m.Inhabitants.Add(ReadFolk(r));
        return m;
    }
}
