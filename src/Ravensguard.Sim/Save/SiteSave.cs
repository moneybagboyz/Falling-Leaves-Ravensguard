using System.Text.Json;
using MessagePack;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Sites;

namespace Ravensguard.Sim.Save;

/// <summary>One site of the optional "sites" section (P4-H, exploration-and-sites 3.2): the snapshot as first generated, the delta play made, the counters.</summary>
[MessagePackObject]
public sealed class SiteRecordSave
{
    [Key(0)] public ulong Site { get; set; }
    [Key(1)] public byte[] Snapshot { get; set; } = [];
    [Key(2)] public byte[] Delta { get; set; } = [];
    [Key(3)] public int Cleared { get; set; } = -1;
    [Key(4)] public int Reoccupied { get; set; }
    [Key(5)] public int LootPm { get; set; } = 1000;
    [Key(6)] public long FloodSince { get; set; } = -1;
    [Key(7)] public bool Curse { get; set; }
    [Key(8)] public int Visits { get; set; }
    [Key(9)] public byte[] Known { get; set; } = [];
    [Key(10)] public int[] Pins { get; set; } = [];
}

/// <summary>A person in a crawl in progress (the hero underground at the save).</summary>
[MessagePackObject]
public sealed class CrawlMemberSave
{
    [Key(0)] public ulong Person { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public string KitJson { get; set; } = "";
    [Key(3)] public int[] Pos { get; set; } = [];
    [Key(4)] public long LightLeft { get; set; }
    [Key(5)] public byte LightKind { get; set; }
    [Key(6)] public bool Lit { get; set; }
    [Key(7)] public int[] Ints { get; set; } = [];
    [Key(8)] public bool[] Flags { get; set; } = [];
    [Key(9)] public byte[] Loot { get; set; } = [];
    [Key(10)] public int[] Path { get; set; } = [];
    [Key(11)] public int[] Crumbs { get; set; } = [];
    [Key(12)] public string DeathCause { get; set; } = "";
}

/// <summary>The crawl in progress at the save: the site, the clock, the people, the auto-map, pending collapses and a fight.</summary>
[MessagePackObject]
public sealed class CrawlSave
{
    [Key(0)] public ulong Site { get; set; }
    [Key(1)] public long GameSec { get; set; }
    [Key(2)] public long Steps { get; set; }
    [Key(3)] public List<CrawlMemberSave> Members { get; set; } = [];
    [Key(4)] public long[] CaveIns { get; set; } = [];
    [Key(5)] public long FloodSince { get; set; } = -1;
    [Key(6)] public int ClearedDay { get; set; } = -1;
    [Key(7)] public bool[] Flags { get; set; } = [];
    [Key(8)] public string[] Slain { get; set; } = [];
    [Key(9)] public int[] FightMap { get; set; } = [];
    [Key(10)] public int[] FightAt { get; set; } = [];
    [Key(11)] public byte[] Fight { get; set; } = [];
    [Key(12)] public int Loud { get; set; } = -1;
    [Key(13)] public int[] Burning { get; set; } = [];
}

/// <summary>The optional "sites" section (P4-H; no format bump): every touched site, the crawl in progress, the weather ring, the renown stub. Encoded in id order.</summary>
[MessagePackObject]
public sealed class SiteSave
{
    public const string SectionName = "sites";
    [Key(0)] public List<SiteRecordSave> Sites { get; set; } = [];
    [Key(1)] public CrawlSave? Active { get; set; }
    [Key(2)] public uint HeavyHours { get; set; }
    [Key(3)] public long LastHour { get; set; } = -1;
    [Key(4)] public bool Legends { get; set; }
    [Key(5)] public int Renown { get; set; }
    [Key(6)] public string[] Slain { get; set; } = [];
    [Key(7)] public ulong ActiveSite { get; set; }

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Captures the book (and the crawl in progress, if the hero is underground): under the book's lock, so the tick worker and the crawl never see half of it.</summary>
    public static SiteSave Capture(SiteBook b, SiteCrawl? crawl = null)
    {
        lock (b.Sync)
        {
            var s = new SiteSave { HeavyHours = b.HeavyHours, LastHour = b.LastHour, Legends = b.LegendsSeeded, Renown = b.Renown, Slain = [.. b.Slain], ActiveSite = b.ActiveSite };
            foreach (var (id, r) in b.Records)
            {
                var known = r.Known;
                if (crawl != null && crawl.Map.SiteId == id) known = crawl.Known;
                s.Sites.Add(new SiteRecordSave
                {
                    Site = id, Snapshot = r.Snapshot, Delta = r.Base != null && r.Map != null ? SiteCodec.Delta(r.Base, r.Map) : [], Cleared = r.Cleared, Reoccupied = r.Reoccupied, LootPm = r.LootPm,
                    FloodSince = r.FloodSince, Curse = r.Curse, Visits = r.Visits, Known = known?.ToRle() ?? [],
                    Pins = known == null ? [] : [.. known.Pins.SelectMany(p => new[] { p.Level, p.Cell, (int)p.Kind })],
                });
            }
            s.Active = crawl != null && !crawl.Ended ? CaptureCrawl(crawl) : b.ActiveCrawl;
            return s;
        }
    }

    public static CrawlSave CaptureCrawl(SiteCrawl c)
    {
        var s = new CrawlSave
        {
            Site = c.Map.SiteId, GameSec = c.GameSec, Steps = c.Steps, FloodSince = c.FloodSince, ClearedDay = c.ClearedDay, Slain = [.. c.Slain],
            Flags = [c.TabooBroken, c.GraveTaken, c.Resting, c.HeroAi], CaveIns = [.. c.CaveIns.SelectMany(p => new[] { p.Zone, p.Cell, p.Radius, p.AtSec, (long)p.EventId })],
        };
        foreach (var m in c.Members)
            s.Members.Add(new CrawlMemberSave
            {
                Person = m.Person, Name = m.Name, KitJson = JsonSerializer.Serialize(m.Kit, Json), Pos = [m.X, m.Y, m.Level, m.FX, m.FY, m.Cell],
                LightLeft = m.Light.LeftMilliS, LightKind = (byte)m.Light.Kind, Lit = m.Light.Lit,
                Ints = [m.SpareTorches, m.SpareCandles, m.OilMl, m.Perception, m.Lockpicking, m.Athletics, m.Stealth, m.DigQuality, m.GearG, m.CapacityG, m.LootG, m.MoraleX100,
                    m.FoulS, m.ClearS, m.BreathS, m.HeldS, m.BusyS, m.SwimChecks, (int)m.BusyKind, m.BusyTarget, m.DragBy, (int)m.Mode, m.MoveDir, m.PathIdx, m.TorchesUsed, m.CandlesUsed, m.OilUsedMl, m.Crumbs.Capacity, m.Bandages, m.FirstAid],
                Flags = [m.Tinder, m.Rope, m.Superstitious, m.HasMap, m.Unconscious, m.Dead, m.Sunk, m.Swimming, m.WarnedAir, m.Lost, m.Sneak, m.Retracing, m.Path != null, m.AutoTreat],
                Loot = SiteCodec.Items(m.Loot), Path = m.Path?.ToArray() ?? [], Crumbs = m.Crumbs.ToArray(), DeathCause = m.DeathCause,
            });
        c.CaptureFight(s);
        return s;
    }

    /// <summary>The book as saved; maps are the snapshots with their deltas (never regenerated: a visited place keeps its look whatever the content does).</summary>
    public SiteBook Restore()
    {
        var b = new SiteBook { HeavyHours = HeavyHours, LastHour = LastHour, LegendsSeeded = Legends, Renown = Renown, ActiveSite = ActiveSite, ActiveCrawl = Active };
        b.Slain.AddRange(Slain);
        foreach (var r in Sites)
        {
            var rec = b.Record(r.Site);
            rec.Cleared = r.Cleared; rec.Reoccupied = r.Reoccupied; rec.LootPm = r.LootPm; rec.FloodSince = r.FloodSince; rec.Curse = r.Curse; rec.Visits = r.Visits;
            if (r.Snapshot.Length > 0)
            {
                rec.Snapshot = r.Snapshot;
                rec.Base = SiteCodec.Decode(r.Snapshot);
                rec.Map = r.Delta.Length > 0 ? SiteCodec.Apply(rec.Base, r.Delta) : SiteCodec.Clone(rec.Base);
                rec.Known = r.Known.Length > 0 ? AutoMap.FromRle(rec.Map.W, rec.Map.H, rec.Map.Levels, r.Known) : new AutoMap(rec.Map.W, rec.Map.H, rec.Map.Levels);
                for (int i = 0; i + 2 < r.Pins.Length; i += 3) rec.Known.Pins.Add(new MapPin(r.Pins[i], r.Pins[i + 1], (byte)r.Pins[i + 2]));
            }
        }
        return b;
    }

    public static FighterKit KitOf(string json) => JsonSerializer.Deserialize<FighterKit>(json, Json) ?? new FighterKit();
}
