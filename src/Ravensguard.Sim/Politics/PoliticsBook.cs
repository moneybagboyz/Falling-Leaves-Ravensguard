using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Politics;

/// <summary>The kinds of treaty (diplomacy-and-ai-lords 3). Append only.</summary>
public enum PactKind : byte { Truce, Alliance, Vassalage, MarriagePact, TradePact }

/// <summary>Why a realm may fight another: the casus belli kinds (4.1). Append only.</summary>
public enum CbKind : byte { None, Insult, UnpaidDues, BrokenTreaty, SeizedFief, Raid, Excommunication, Claim }

/// <summary>A treaty between two realms (history realm indexes, or a founded realm's): <c>A</c> is the overlord of a vassalage. Terms: tribute mites a season paid by <see cref="TributeFrom"/> until a day.</summary>
public sealed class Pact
{
    public int Serial;
    public PactKind Kind;
    public int A, B;
    public int StartDay, ExpiryDay = -1;
    public long TributeMites;
    public int TributeUntilDay = -1;
    /// <summary>The realm that pays the tribute (-1 none).</summary>
    public int TributeFrom = -1;
    public ulong Cause;
    public bool Ended;
    public bool Involves(int r) => A == r || B == r;
    public bool Between(int x, int y) => (A == x && B == y) || (A == y && B == x);
}

/// <summary>A war between two realms, fought season by season by the realm-level battle resolver of Phase 6 (live armies are Phase 7). Score is the attacker's view, -100..100.</summary>
public sealed class War
{
    public int Serial;
    public int Attacker, Defender;
    public int StartDay, EndDay = -1;
    public CbKind Cb;
    public ulong CbEntry, DeclEntry;
    public int Score;
    public int Seasons, Battles;
    /// <summary>The war score each side has won in its own battles (the loser's gains are what his weariness is weighed against).</summary>
    public int GainA, GainD;
    /// <summary>Losses suffered in the war by each side, in permille of its strength, summed over the seasons (the dead as a share of its people is this over ten).</summary>
    public int LossA, LossD;
    public bool Alive => EndDay < 0;
    public bool Involves(int r) => Attacker == r || Defender == r;
}

/// <summary>A casus belli: a grievance <see cref="Holder"/> holds against <see cref="Against"/>, with the chronicle event that made it (4.1: it is an event, never a roll).</summary>
public sealed class CasusBelli
{
    public int Serial, Holder, Against;
    public CbKind Kind;
    public ulong Entry;
    public int Day;
    public bool Used;
}

/// <summary>One modifier of an opinion (3): its kind (a row of content), its present size, the day, the entry that explains it. The value they add up to lives in <c>RuleBook.Opinions</c>.</summary>
public sealed class OpinionMod
{
    public ulong A, B;
    public string Kind = "";
    public int Amount;
    public int Day;
    public ulong Cause;
}

/// <summary>What the book keeps of a realm beyond history: losses and recoveries of the host, abstract coin, a founded realm's name and ruler (a faction the hero or a lord made).</summary>
public sealed class RealmRow
{
    public int Realm;
    /// <summary>The ruler's id (a history notable's or a person's), 0 for history's own.</summary>
    public ulong Ruler;
    public int LossPm;
    public long TreasuryDelta;
    public bool Founded;
    public string Name = "";
    public int Banner;
    public int Parent = -1;
    public int FoundedDay;
    public bool Dead;
}

/// <summary>An oath (4.3): a person who is the liege's man since a day; the vassal's owed service lives in the rule book.</summary>
public sealed class Fealty
{
    public ulong Vassal, Liege;
    public int Day;
    public int Fief = -1;
}

/// <summary>A manor (4.3): a village and its demesne held as a sub-fief of a fief; the holder is a person. Never taken from a living holder.</summary>
public sealed class Manor
{
    public int Serial, Settlement, OverlordFief, Realm;
    public long Holder = -1;
    public int GrantedDay = -1;
}

/// <summary>A petition: a person asks his ruler for a vacant fief (4.2).</summary>
public sealed class Petition
{
    public ulong Person;
    public int Fief, Day;
}

/// <summary>What one realm last learned of another (4.2): the strength and the treasury as the rumour told them, and the day it was told.</summary>
public sealed class KnownFact
{
    public int Observer, About;
    public long Strength;
    public int TreasurySeasons;
    public int Day;
}

/// <summary>A line of the lords' log (the Faction screen's Actions tab, DA-5): who acted, what, why and the entry.</summary>
public sealed class LordLog
{
    public int Day, Fief;
    public ulong Lord;
    public string Action = "", Note = "";
    public ulong Entry;
}

/// <summary>
/// Diplomacy and the AI lords (the optional "diplomacy" section; no format bump, absent while empty): treaties and wars between realms, the casus belli that justify them, the modifiers behind the opinions (their sum is in
/// <c>RuleBook.Opinions</c>, the one opinion store), realms founded by a renunciation, oaths, manors, petitions, what realms last learned of each other and the lords' log. Drawn only with registered streams and written at
/// the tick's seasonal step or the barrier.
/// </summary>
public sealed class PoliticsBook
{
    public readonly List<Pact> Pacts = [];
    public readonly List<War> Wars = [];
    public readonly List<CasusBelli> Cbs = [];
    public readonly List<OpinionMod> Mods = [];
    public readonly SortedDictionary<int, RealmRow> Realms = [];
    public readonly List<Fealty> Oaths = [];
    public readonly List<Manor> Manors = [];
    public readonly List<Petition> Petitions = [];
    public readonly SortedDictionary<(int Observer, int About), KnownFact> Known = [];
    public readonly List<LordLog> Log = [];
    /// <summary>P7-0B: history rulers who died of old age in play (notable id to the day); history itself is not changed. Saved in the counters' tail.</summary>
    public readonly SortedDictionary<ulong, int> RulerDeaths = [];
    /// <summary>The day of the last season step settled, and of the last opinion decay.</summary>
    public int LastSeasonDay = -1, LastDecayDay = -1;
    public int NextPact = 1, NextWar = 1, NextCb = 1, NextManor = 1;
    /// <summary>History's live wars and treaties were taken in (once).</summary>
    public bool Imported;
    /// <summary>The keep of the log: only the latest rows are saved.</summary>
    public const int LogKeep = 64;

    public bool IsEmpty => Pacts.Count == 0 && Wars.Count == 0 && Cbs.Count == 0 && Mods.Count == 0 && Realms.Count == 0 && Oaths.Count == 0 && Manors.Count == 0 && Petitions.Count == 0 && Known.Count == 0
        && Log.Count == 0 && RulerDeaths.Count == 0 && LastSeasonDay < 0 && LastDecayDay < 0 && !Imported;

    public RealmRow RealmOf(int realm) { if (!Realms.TryGetValue(realm, out var r)) Realms[realm] = r = new RealmRow { Realm = realm }; return r; }
    public RealmRow? RealmIfAny(int realm) => Realms.GetValueOrDefault(realm);
    public War? WarOf(int a, int b) { foreach (var w in Wars) if (w.Alive && ((w.Attacker == a && w.Defender == b) || (w.Attacker == b && w.Defender == a))) return w; return null; }
    public War? WarOf(int realm) { foreach (var w in Wars) if (w.Alive && w.Involves(realm)) return w; return null; }
    public Pact? PactOf(PactKind kind, int a, int b, int day) { foreach (var p in Pacts) if (!p.Ended && p.Kind == kind && p.Between(a, b) && (p.ExpiryDay < 0 || p.ExpiryDay > day)) return p; return null; }
    public bool Allied(int a, int b, int day) => PactOf(PactKind.Alliance, a, b, day) != null;
    public bool TruceHolds(int a, int b, int day) => PactOf(PactKind.Truce, a, b, day) != null;
    public Fealty? OathOf(ulong vassal) { foreach (var o in Oaths) if (o.Vassal == vassal) return o; return null; }
    public Manor? ManorAt(int settlement) { foreach (var m in Manors) if (m.Settlement == settlement) return m; return null; }

    public void Note(int day, int fief, ulong lord, string action, string note, ulong entry)
    {
        Log.Add(new LordLog { Day = day, Fief = fief, Lord = lord, Action = action, Note = note, Entry = entry });
        if (Log.Count > LogKeep) Log.RemoveRange(0, Log.Count - LogKeep);
    }

    /// <summary>The <c>PoliticsHash</c> of the daily trail (5): modifiers, treaties, wars, casus belli, realms, oaths, manors, petitions, what was learned and the lords' log, in order.</summary>
    public ulong Hash()
    {
        ulong h = 0xD1710AC70A5E0001UL;
        void M(long v) => h = DetRng.Mix(h ^ ((ulong)v + 0x9E3779B97F4A7C15UL));
        void S(string s) { foreach (char c in s) M(c); M(0xFF); }
        foreach (var p in Pacts) { M(p.Serial); M((long)p.Kind); M(p.A); M(p.B); M(p.StartDay); M(p.ExpiryDay); M(p.TributeMites); M(p.TributeUntilDay); M(p.TributeFrom); M((long)p.Cause); M(p.Ended ? 1 : 0); }
        foreach (var w in Wars) { M(w.Serial); M(w.Attacker); M(w.Defender); M(w.StartDay); M(w.EndDay); M((long)w.Cb); M((long)w.CbEntry); M((long)w.DeclEntry); M(w.Score); M(w.Seasons); M(w.Battles); M(w.LossA); M(w.LossD); M(w.GainA); M(w.GainD); }
        foreach (var c in Cbs) { M(c.Serial); M(c.Holder); M(c.Against); M((long)c.Kind); M((long)c.Entry); M(c.Day); M(c.Used ? 1 : 0); }
        foreach (var o in Mods) { M((long)o.A); M((long)o.B); S(o.Kind); M(o.Amount); M(o.Day); M((long)o.Cause); }
        foreach (var (k, r) in Realms) { M(k); M((long)r.Ruler); M(r.LossPm); M(r.TreasuryDelta); M(r.Founded ? 1 : 0); S(r.Name); M(r.Banner); M(r.Parent); M(r.FoundedDay); M(r.Dead ? 1 : 0); }
        foreach (var o in Oaths) { M((long)o.Vassal); M((long)o.Liege); M(o.Day); M(o.Fief); }
        foreach (var m in Manors) { M(m.Serial); M(m.Settlement); M(m.OverlordFief); M(m.Realm); M(m.Holder); M(m.GrantedDay); }
        foreach (var p in Petitions) { M((long)p.Person); M(p.Fief); M(p.Day); }
        foreach (var ((a, b), k) in Known) { M(a); M(b); M(k.Strength); M(k.TreasurySeasons); M(k.Day); }
        foreach (var l in Log) { M(l.Day); M(l.Fief); M((long)l.Lord); S(l.Action); S(l.Note); M((long)l.Entry); }
        foreach (var (k, d) in RulerDeaths) { M((long)k); M(d); }
        M(LastSeasonDay); M(LastDecayDay); M(NextPact); M(NextWar); M(NextCb); M(NextManor); M(Imported ? 1 : 0);
        return h;
    }

    // ---- not saved: the numbers the systems read, set when the book is attached
    public PoliticsSettingsDef Settings { get; set; } = new() { Id = "politics" };
    public IReadOnlyDictionary<string, OpinionKindDef> OpinionKinds { get; set; } = new Dictionary<string, OpinionKindDef>();
    public IReadOnlyList<LordActionDef> Actions { get; set; } = [];
    public ulong Seed { get; set; }
}
