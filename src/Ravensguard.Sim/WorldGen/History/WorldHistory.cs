using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>Stage 2 output: the world's legends (plan §1A). Every ruin and war is linked to a chronicle cause.</summary>
public sealed class WorldHistory
{
    public ulong Seed { get; init; }
    public int Years { get; init; }
    public Chronicle Chronicle { get; } = new();
    public List<Settlement> Settlements { get; } = [];
    public List<RealmState> Realms { get; } = [];
    public List<Notable> Notables { get; } = [];
    public List<Dynasty> Dynasties { get; } = [];
    public List<Artefact> Artefacts { get; } = [];
    public List<TradeRoute> TradeRoutes { get; } = [];
    public List<Battlefield> Battlefields { get; } = [];
    public List<Grudge> Grudges { get; } = [];
    public List<WarRecord> Wars { get; } = [];
    public List<Snapshot> Snapshots { get; } = [];
    public List<Treaty> Treaties { get; } = [];
    /// <summary>Mine collapse sites: (vein cell, event).</summary>
    public List<(int Cell, EntityId Event)> MineCollapses { get; } = [];
    /// <summary>Final realm index per continent cell (255 = none).</summary>
    public byte[] FinalOwner { get; set; } = [];
    public int[] FinalPop { get; set; } = [];
    public long StartPopulation { get; set; }
    public long EndPopulation { get; set; }

    private readonly Dictionary<EntityId, Notable> _notables = [];
    private readonly Dictionary<EntityId, Settlement> _settlements = [];
    private readonly Dictionary<EntityId, Dynasty> _dynasties = [];
    private readonly Dictionary<EntityId, Artefact> _artefacts = [];

    internal void Add(Notable n) { Notables.Add(n); _notables[n.Id] = n; }
    internal void Add(Settlement s) { Settlements.Add(s); _settlements[s.Id] = s; }
    internal void Add(Dynasty d) { Dynasties.Add(d); _dynasties[d.Id] = d; }
    internal void Add(Artefact a) { Artefacts.Add(a); _artefacts[a.Id] = a; }

    public Notable? GetNotable(EntityId id) => _notables.GetValueOrDefault(id);
    public Settlement? GetSettlement(EntityId id) => _settlements.GetValueOrDefault(id);
    public Dynasty? GetDynasty(EntityId id) => _dynasties.GetValueOrDefault(id);
    public Artefact? GetArtefact(EntityId id) => _artefacts.GetValueOrDefault(id);
    public IEnumerable<RealmState> AliveRealms => Realms.Where(r => r.Alive);

    /// <summary>One readable legends line: "Spring 266: Title — text. Cause: ... (event #123)".</summary>
    public string LegendLine(ChronicleEntry e)
    {
        var d = e.Data;
        string title = d.GetValueOrDefault("title", e.TypeId), text = d.GetValueOrDefault("text", "").TrimEnd('.');
        var sb = new StringBuilder();
        sb.Append(e.When.Season).Append(' ').Append(e.When.Year).Append(": ").Append(title);
        if (text.Length > 0) sb.Append(" — ").Append(text);
        sb.Append(". Cause: ");
        for (int i = 0; i < e.Causes.Count; i++)
        {
            var c = e.Causes[i];
            if (i > 0) sb.Append("; ");
            sb.Append(c.Note.Length > 0 ? c.Note : c.Kind.ToString().ToLowerInvariant());
            if (c.EventId is { } cid) sb.Append(" [#").Append(cid.Counter).Append(']');
        }
        sb.Append(" (event #").Append(e.Id.Counter).Append(')');
        return sb.ToString();
    }

    public IEnumerable<string> Legends() => Chronicle.All.Select(LegendLine);

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) { L(s.Length); h.AppendData(Encoding.UTF8.GetBytes(s)); }
        void Id(EntityId id) => L((long)id.Value);

        L((long)Seed); L(Years);
        foreach (var e in Chronicle.All)
        {
            Id(e.Id); L(e.When.Seconds); S(e.TypeId);
            foreach (var s in e.Subjects) Id(s);
            foreach (var c in e.Causes) { L((long)c.Kind); if (c.EventId is { } x) Id(x); S(c.Note); }
            foreach (var kv in e.Data.OrderBy(k => k.Key, StringComparer.Ordinal)) { S(kv.Key); S(kv.Value); }
        }
        foreach (var s in Settlements)
        {
            Id(s.Id); S(s.Name); L(s.Cell); L(s.Pop); L(s.PeakPop); L(s.Realm); L(s.FoundedTick); L(s.RuinTick); S(s.RuinKind); Id(s.RuinEvent); Id(s.RuinCauseEvent);
        }
        foreach (var r in Realms)
        {
            Id(r.Id); S(r.Name); L(r.CultureId); L(r.Capital); Id(r.Ruler); Id(r.Dynasty); L(r.Alive ? 1 : 0); L(r.EndTick); S(r.EndKind); L(r.Cells); L(r.Pop); L(r.Treasury);
        }
        foreach (var n in Notables)
        {
            Id(n.Id); S(n.Name); S(n.Epithet); L((long)n.Role); L(n.BirthYear); L(n.DeathYear); Id(n.Dynasty); Id(n.Father); Id(n.Mother); Id(n.Spouse); L(n.Realm); Id(n.DeathEvent); S(n.DeathCause);
        }
        foreach (var d in Dynasties) { Id(d.Id); S(d.Name); Id(d.Founder); }
        foreach (var a in Artefacts) { Id(a.Id); S(a.Name); S(a.Kind); Id(a.Holder); L((long)a.Status); L(a.Cell); Id(a.LossEvent); S(a.LossNote); L(a.Transfers); }
        foreach (var t in TradeRoutes) { Id(t.A); Id(t.B); L(t.Cost); L(t.Cells.Length); L(t.Abandoned ? 1 : 0); }
        foreach (var b in Battlefields) { L(b.Cell); Id(b.Event); S(b.Name); L(b.Tick); L(b.Dead); }
        foreach (var g in Grudges) { L(g.From); L(g.To); L(g.Strength); Id(g.CauseEvent); }
        foreach (var w in Wars) { L(w.Attacker); L(w.Defender); L(w.StartTick); L(w.EndTick); L((long)w.Cb); Id(w.CbEvent); Id(w.DeclEvent); Id(w.EndEvent); L(w.Score); S(w.Outcome); }
        foreach (var m in MineCollapses) { L(m.Cell); Id(m.Event); }
        foreach (var t in Treaties) { L(t.A); L(t.B); L((long)t.Kind); L(t.StartTick); L(t.ExpiryTick); Id(t.Event); }
        foreach (var s in Snapshots) { L(s.Year); h.AppendData(s.Owner); }
        h.AppendData(FinalOwner);
        foreach (var p in FinalPop) { L(p); }
        L(StartPopulation); L(EndPopulation);
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
