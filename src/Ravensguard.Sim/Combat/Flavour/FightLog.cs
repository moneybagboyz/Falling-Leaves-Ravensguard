using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>
/// The fight's record for the log, the tooltip, the after-fight report and the chronicle (feedback-flavour-log 3.1, 4.2, 10): lines (events with their severity), the layer traces
/// and wound lists of the blows they tell, the people, and an interned string table so the text of any line can be built later without the content tables. Presentation only: the
/// sim never reads it and it is not hashed into the <c>FightHash</c>.
/// </summary>
public sealed class FightLog
{
    /// <summary>Lines held in the ring before older ones that are not serious fall away (3.1: 4,096 per fight).</summary>
    public const int RingCap = 4096;
    /// <summary>Kills and serious wounds are spilled and kept up to this many per fight (3.1: 20,000 per battle).</summary>
    public const int SpillCap = 20_000;
    /// <summary>A hero fight keeps this many recent lines when it is saved (F7).</summary>
    public const int SaveRecent = 2000;

    public ulong Seed { get; set; }
    public ulong FightId { get; set; }
    /// <summary>What began the fight, as a localisable key and one parameter ("fl_trigger_ambush", "the bandits"); the last link of a cause chain.</summary>
    public string TriggerKey { get; set; } = "fl_trigger_fight";
    public string TriggerArg { get; set; } = "";
    /// <summary>The place or occasion (the ford), the second last link; empty when unknown.</summary>
    public string Place { get; set; } = "";
    /// <summary>The hero's fighter id (the one driven by the player), or -1 (watch mode, the abstract tier).</summary>
    public int Hero { get; set; } = -1;
    public int HeroTeam { get; set; } = -1;
    public int StartTick { get; set; }
    public int EndTick { get; set; }
    /// <summary>The team that held the field, or -1 (set by <see cref="FlavourRecorder.Finish"/>).</summary>
    public int Winner { get; set; } = -1;
    public bool Finished { get; set; }
    /// <summary>The game second of tick 0 (the chronicle's date).</summary>
    public long GameSec0 { get; set; }
    /// <summary>Lines that fell out of the ring (counted, not kept).</summary>
    public int Dropped { get; set; }
    public int NextSerial { get; set; }
    /// <summary>The cursor of the formatter's picks: every line below it has its templates chosen.</summary>
    public int PickCursor { get; set; }

    /// <summary>The formatter's picks write the lines and the strings on whichever thread formats (the log panel on the main thread, a save's capture on the tick worker): both take this
    /// lock (review, game finding 8).</summary>
    public object Sync { get; } = new();

    public List<FightLine> Lines { get; } = new(RingCap * 2 + 16);
    public Dictionary<int, BlowSnap> Blows { get; } = [];
    public SortedDictionary<int, FighterInfo> Fighters { get; } = [];
    private int _nextBlow;

    private readonly List<string> _strings = [""];
    private readonly Dictionary<string, ushort> _ids = new(StringComparer.Ordinal) { [""] = 0 };
    public IReadOnlyList<string> Strings => _strings;

    /// <summary>Binds the formatter's picker so a compaction that drops lines picks them first (the picks must not depend on who looked).</summary>
    public Action<FightLog, int>? Picker { get; set; }

    public ushort Intern(string s)
    {
        if (_ids.TryGetValue(s, out var id)) return id;
        id = (ushort)_strings.Count;
        _strings.Add(s); _ids[s] = id;
        return id;
    }

    public string Str(ushort id) => id < _strings.Count ? _strings[id] : "";

    internal void SetStrings(IEnumerable<string> strings)
    {
        _strings.Clear(); _ids.Clear();
        foreach (var s in strings) { if (_ids.TryAdd(s, (ushort)_strings.Count)) _strings.Add(s); else _strings.Add(s); }
        if (_strings.Count == 0 || _strings[0].Length != 0) { _strings.Insert(0, ""); _ids.Clear(); for (int i = 0; i < _strings.Count; i++) _ids.TryAdd(_strings[i], (ushort)i); }
    }

    public int AddBlow(BlowSnap b) { b.Serial = _nextBlow; Blows[_nextBlow] = b; return _nextBlow++; }
    internal void RestoreBlow(int id, BlowSnap b) { b.Serial = id; Blows[id] = b; _nextBlow = Math.Max(_nextBlow, id + 1); }
    public int NextBlowId => _nextBlow;
    internal void SetNextBlow(int n) => _nextBlow = n;

    public FighterInfo Info(int id)
    {
        if (!Fighters.TryGetValue(id, out var f)) Fighters[id] = f = new FighterInfo { Id = id, Name = "#" + id };
        return f;
    }

    public bool TryInfo(int id, out FighterInfo f) => Fighters.TryGetValue(id, out f!);

    /// <summary>Appends a line (the recorder's write: one struct copy). Returns its serial.</summary>
    public int Add(in FightLine line)
    {
        var l = line;
        l.Serial = NextSerial++;
        Lines.Add(l);
        if (Lines.Count >= RingCap * 2) Compact();
        return l.Serial;
    }

    /// <summary>Keeps the newest <see cref="RingCap"/> lines and, of the older ones, the serious ones up to <see cref="SpillCap"/>; their blows go with the lines that fall away.</summary>
    public void Compact()
    {
        int keepFrom = Lines.Count - RingCap;
        if (keepFrom <= 0) return;
        Picker?.Invoke(this, keepFrom);
        // in place: the serious older lines first (the oldest of them fall away past the spill cap), then the newest ring
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Lines);
        int serious = 0;
        for (int i = 0; i < keepFrom; i++) if (span[i].IsSerious) serious++;
        int skip = Math.Max(0, serious - (SpillCap - RingCap));
        int w = 0, cursor = 0;
        for (int i = 0; i < keepFrom; i++)
        {
            if (!span[i].IsSerious) { Dropped++; continue; }
            if (skip > 0) { skip--; Dropped++; continue; }
            span[w++] = span[i];
            if (i < PickCursor) cursor++;
        }
        for (int i = keepFrom; i < span.Length; i++) { span[w++] = span[i]; if (i < PickCursor) cursor++; }
        Lines.RemoveRange(w, Lines.Count - w);
        PickCursor = cursor;
        if (Blows.Count > 0)
        {
            var live = new HashSet<int>();
            foreach (var l in Lines) if (l.Blow >= 0) live.Add(l.Blow);
            foreach (var k in Blows.Keys.ToList()) if (!live.Contains(k)) Blows.Remove(k);
        }
    }

    /// <summary>Index of the line with this serial (lines are in serial order), or -1.</summary>
    public int IndexOfSerial(int serial)
    {
        int lo = 0, hi = Lines.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1, s = Lines[mid].Serial;
            if (s == serial) return mid;
            if (s < serial) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    public BlowSnap? BlowOf(in FightLine l) => l.Blow >= 0 && Blows.TryGetValue(l.Blow, out var b) ? b : null;

    /// <summary>The ticks since the start as mm:ss (the fight tick is 1/20 s).</summary>
    public static string Clock(int tick) { int s = Math.Max(0, tick) / 20; return $"{s / 60:00}:{s % 60:00}"; }
}
