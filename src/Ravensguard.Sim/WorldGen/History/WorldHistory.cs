using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>Stage 2 output: the world's legends (plan §1A). Every ruin and war is linked to a chronicle cause.</summary>
public sealed class WorldHistory
{
    public ulong Seed { get; init; }
    public int Years { get; init; }
    /// <summary>The world's <c>aging_pace</c> (1 to 4) the history was simulated with: a notable is <c>(years since birth) x pace</c> years old.</summary>
    public int AgingPace { get; init; } = 1;
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
    /// <summary>The rules the history was made with (0 = Phase 1, frozen; 1 = Phase 6).</summary>
    public int RulesVersion { get; init; }
    /// <summary>History v1 only: the faiths, the heresies, the excommunications and the slave raids of the legends (empty under v0).</summary>
    public List<HistoryReligion> Religions { get; } = [];
    public List<Heresy> Heresies { get; } = [];
    public List<Excommunication> Excommunications { get; } = [];
    public List<SlaveRaid> SlaveRaids { get; } = [];
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

    /// <summary>The current name of a chronicle parameter's id in these legends (a notable's, a settlement's), "?" when it has none: what a keyed entry's sentence fills its holes with.</summary>
    public string NameOf(ChronParamKind kind, long id)
    {
        var eid = new EntityId((ulong)id);
        return kind switch
        {
            ChronParamKind.Person => GetNotable(eid)?.Full ?? Realms.FirstOrDefault(r => r.Id == eid)?.Name ?? "?",
            ChronParamKind.Place => GetSettlement(eid)?.Name ?? "?",
            _ => "?",
        };
    }

    /// <summary>The heading of an entry for a viewer: a keyed entry reads as its sentence through <paramref name="tr"/> (en.csv), a legacy entry its English title.</summary>
    public string Title(ChronicleEntry e, Func<string, string> tr) =>
        e.TextKey.Length > 0 ? ChronicleTypes.Format(e, tr, NameOf) : e.Data.GetValueOrDefault("title", e.TypeId);

    /// <summary>The body text of a legacy entry; a keyed entry's sentence is its title, so it has none.</summary>
    public string Text(ChronicleEntry e) => e.TextKey.Length > 0 ? "" : e.Data.GetValueOrDefault("text", "");

    /// <summary>A cause's note for a viewer: its key and parameters through <paramref name="tr"/>, else its legacy English note, else its kind.</summary>
    public string NoteText(Cause c, Func<string, string> tr)
    {
        string n = ChronicleTypes.FormatNote(c, tr, NameOf);
        return n.Length > 0 ? n : c.Kind.ToString();
    }

    /// <summary>One readable legends line: "Spring 266: Title — text. Cause: ... (event #123)".</summary>
    public string LegendLine(ChronicleEntry e)
    {
        var d = e.Data;
        string title = d.GetValueOrDefault("title", e.TextKey.Length > 0 ? e.TextKey : e.TypeId), text = d.GetValueOrDefault("text", "").TrimEnd('.');
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

        L((long)Seed); L(Years); if (AgingPace != 1) L(AgingPace);
        foreach (var e in Chronicle.All)
        {
            Id(e.Id); L(e.When.Seconds); S(e.TypeId);
            foreach (var s in e.Subjects) Id(s);
            foreach (var c in e.Causes) { L((long)c.Kind); if (c.EventId is { } x) Id(x); S(c.Note); }
            foreach (var kv in e.Data.OrderBy(k => k.Key, StringComparer.Ordinal)) { S(kv.Key); S(kv.Value); }
            if (e.TextKey.Length > 0) { S(e.TextKey); foreach (var p in e.Params ?? []) { S(p.Name); L((long)p.Kind); L(p.Value); S(p.Key); } }
            foreach (var c in e.Causes) if (c.NoteKey.Length > 0) { S(c.NoteKey); foreach (var p in c.NoteParams ?? []) { S(p.Name); L((long)p.Kind); L(p.Value); S(p.Key); } }
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
            Id(n.Id); S(n.Name); S(n.Epithet); L((long)n.Role); L(n.BirthYear); L(n.DeathYear); Id(n.Dynasty); Id(n.Father); Id(n.Mother); Id(n.Spouse); L(n.Realm); Id(n.DeathEvent); S(n.DeathCause); L(n.Female ? 1 : 0); L(n.Culture); L(n.Residence);
        }
        foreach (var d in Dynasties) { Id(d.Id); S(d.Name); Id(d.Founder); }
        foreach (var a in Artefacts) { Id(a.Id); S(a.Name); S(a.Kind); Id(a.Holder); L((long)a.Status); L(a.Cell); Id(a.LossEvent); S(a.LossNote); L(a.Transfers); Id(a.Creator); L(a.CreatedTick); Id(a.CreatedEvent); }
        foreach (var t in TradeRoutes) { Id(t.A); Id(t.B); L(t.Cost); L(t.Cells.Length); foreach (var c in t.Cells) L(c); L(t.Abandoned ? 1 : 0); }
        foreach (var b in Battlefields) { L(b.Cell); Id(b.Event); S(b.Name); L(b.Tick); L(b.Dead); }
        foreach (var g in Grudges) { L(g.From); L(g.To); L(g.Strength); Id(g.CauseEvent); }
        foreach (var w in Wars) { L(w.Attacker); L(w.Defender); L(w.StartTick); L(w.EndTick); L((long)w.Cb); Id(w.CbEvent); Id(w.DeclEvent); Id(w.EndEvent); L(w.Score); S(w.Outcome); }
        foreach (var m in MineCollapses) { L(m.Cell); Id(m.Event); }
        foreach (var t in Treaties) { L(t.A); L(t.B); L((long)t.Kind); L(t.StartTick); L(t.ExpiryTick); Id(t.Event); }
        foreach (var s in Snapshots) { L(s.Year); h.AppendData(s.Owner); }
        h.AppendData(FinalOwner);
        foreach (var p in FinalPop) { L(p); }
        L(StartPopulation); L(EndPopulation);
        if (RulesVersion >= 1)
        {
            L(RulesVersion);
            foreach (var r in Religions) { L(r.Index); S(r.Name); L(r.Culture); Id(r.FoundedEvent); Id(r.Founder); L(r.FoundedTick); L(r.SchismFrom); Id(r.HolySite); }
            foreach (var x in Heresies) { L(x.Religion); L(x.Realm); L(x.StartTick); L(x.EndTick); Id(x.Event); Id(x.CauseEvent); }
            foreach (var x in Excommunications) { L(x.Realm); L(x.Tick); L(x.WarTick); Id(x.Ruler); Id(x.Event); }
            foreach (var x in SlaveRaids) { L(x.Raider); L(x.Victim); L(x.Captives); L(x.Tick); L(x.Market); Id(x.Event); Id(x.MarketEvent); }
            foreach (var r in Realms) L(r.Religion);
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
