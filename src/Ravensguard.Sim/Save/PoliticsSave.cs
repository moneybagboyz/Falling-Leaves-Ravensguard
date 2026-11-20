using MessagePack;
using Ravensguard.Sim.Politics;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "diplomacy" section (P6-D2, diplomacy-and-ai-lords 3; no format bump, absent while the book has nothing, so a world that never had politics keeps its bytes): treaties and wars, casus belli, the modifiers behind
/// the opinions (their sum is in the "fiefs" section's opinions), realms founded in play, oaths, manors, petitions, what realms last learned of each other and the latest of the lords' log. Rows are plain number arrays in
/// order so the bytes are deterministic.
/// </summary>
[MessagePackObject]
public sealed class PoliticsSave
{
    public const string SectionName = "diplomacy";

    /// <summary>Per pact: serial, kind, a, b, start, expiry, tribute mites, tribute until, tribute from, cause, ended.</summary>
    [Key(0)] public long[][] Pacts { get; set; } = [];
    /// <summary>Per war: serial, attacker, defender, start, end, cb, cb entry, decl entry, score, seasons, battles, loss a, loss d, gain a, gain d.</summary>
    [Key(1)] public long[][] Wars { get; set; } = [];
    /// <summary>Per casus belli: serial, holder, against, kind, entry, day, used.</summary>
    [Key(2)] public long[][] Cbs { get; set; } = [];
    /// <summary>Per modifier: a, b, amount, day, cause.</summary>
    [Key(3)] public long[][] Mods { get; set; } = [];
    [Key(4)] public string[] ModKinds { get; set; } = [];
    /// <summary>Per realm row: realm, ruler, loss pm, treasury delta, founded, banner, parent, founded day, dead.</summary>
    [Key(5)] public long[][] Realms { get; set; } = [];
    [Key(6)] public string[] RealmNames { get; set; } = [];
    /// <summary>Per oath: vassal, liege, day, fief.</summary>
    [Key(7)] public long[][] Oaths { get; set; } = [];
    /// <summary>Per manor: serial, settlement, overlord fief, realm, holder, granted day.</summary>
    [Key(8)] public long[][] Manors { get; set; } = [];
    /// <summary>Per petition: person, fief, day.</summary>
    [Key(9)] public long[][] Petitions { get; set; } = [];
    /// <summary>Per known fact: observer, about, strength, treasury seasons, day.</summary>
    [Key(10)] public long[][] Known { get; set; } = [];
    /// <summary>Per log line: day, fief, lord, entry.</summary>
    [Key(11)] public long[][] Log { get; set; } = [];
    [Key(12)] public string[] LogText { get; set; } = [];
    /// <summary>Counters: last season day, last decay day, next pact, war, cb, manor, imported.</summary>
    [Key(13)] public long[] Counters { get; set; } = [];

    public static PoliticsSave Capture(PoliticsBook b)
    {
        var log = b.Log.Count > PoliticsBook.LogKeep ? b.Log.GetRange(b.Log.Count - PoliticsBook.LogKeep, PoliticsBook.LogKeep) : b.Log;
        return new PoliticsSave
        {
            Pacts = [.. b.Pacts.Select(p => new long[] { p.Serial, (long)p.Kind, p.A, p.B, p.StartDay, p.ExpiryDay, p.TributeMites, p.TributeUntilDay, p.TributeFrom, (long)p.Cause, p.Ended ? 1 : 0 })],
            Wars = [.. b.Wars.Select(w => new long[] { w.Serial, w.Attacker, w.Defender, w.StartDay, w.EndDay, (long)w.Cb, (long)w.CbEntry, (long)w.DeclEntry, w.Score, w.Seasons, w.Battles, w.LossA, w.LossD, w.GainA, w.GainD })],
            Cbs = [.. b.Cbs.Select(x => new long[] { x.Serial, x.Holder, x.Against, (long)x.Kind, (long)x.Entry, x.Day, x.Used ? 1 : 0 })],
            Mods = [.. b.Mods.Select(m => new long[] { (long)m.A, (long)m.B, m.Amount, m.Day, (long)m.Cause })],
            ModKinds = [.. b.Mods.Select(m => m.Kind)],
            Realms = [.. b.Realms.Values.Select(r => new long[] { r.Realm, (long)r.Ruler, r.LossPm, r.TreasuryDelta, r.Founded ? 1 : 0, r.Banner, r.Parent, r.FoundedDay, r.Dead ? 1 : 0 })],
            RealmNames = [.. b.Realms.Values.Select(r => r.Name)],
            Oaths = [.. b.Oaths.Select(o => new long[] { (long)o.Vassal, (long)o.Liege, o.Day, o.Fief })],
            Manors = [.. b.Manors.Select(m => new long[] { m.Serial, m.Settlement, m.OverlordFief, m.Realm, m.Holder, m.GrantedDay })],
            Petitions = [.. b.Petitions.Select(p => new long[] { (long)p.Person, p.Fief, p.Day })],
            Known = [.. b.Known.Values.Select(k => new long[] { k.Observer, k.About, k.Strength, k.TreasurySeasons, k.Day })],
            Log = [.. log.Select(l => new long[] { l.Day, l.Fief, (long)l.Lord, (long)l.Entry })],
            LogText = [.. log.Select(l => l.Action + "|" + l.Note)],
            Counters = [b.LastSeasonDay, b.LastDecayDay, b.NextPact, b.NextWar, b.NextCb, b.NextManor, b.Imported ? 1 : 0],
        };
    }

    public PoliticsBook Restore()
    {
        if (Counters.Length < 7 || Mods.Length != ModKinds.Length || Realms.Length != RealmNames.Length || Log.Length != LogText.Length) throw new SaveException("Corrupt 'diplomacy' section in save.");
        var b = new PoliticsBook { LastSeasonDay = (int)Counters[0], LastDecayDay = (int)Counters[1], NextPact = (int)Counters[2], NextWar = (int)Counters[3], NextCb = (int)Counters[4], NextManor = (int)Counters[5], Imported = Counters[6] != 0 };
        foreach (var v in Pacts)
        {
            if (v.Length < 11) throw new SaveException("Corrupt pact in save.");
            b.Pacts.Add(new Pact { Serial = (int)v[0], Kind = (PactKind)v[1], A = (int)v[2], B = (int)v[3], StartDay = (int)v[4], ExpiryDay = (int)v[5], TributeMites = v[6], TributeUntilDay = (int)v[7], TributeFrom = (int)v[8], Cause = (ulong)v[9], Ended = v[10] != 0 });
        }
        foreach (var v in Wars)
        {
            if (v.Length < 15) throw new SaveException("Corrupt war in save.");
            b.Wars.Add(new War { Serial = (int)v[0], Attacker = (int)v[1], Defender = (int)v[2], StartDay = (int)v[3], EndDay = (int)v[4], Cb = (CbKind)v[5], CbEntry = (ulong)v[6], DeclEntry = (ulong)v[7], Score = (int)v[8], Seasons = (int)v[9], Battles = (int)v[10],
                LossA = (int)v[11], LossD = (int)v[12], GainA = (int)v[13], GainD = (int)v[14] });
        }
        foreach (var v in Cbs)
        {
            if (v.Length < 7) throw new SaveException("Corrupt casus belli in save.");
            b.Cbs.Add(new CasusBelli { Serial = (int)v[0], Holder = (int)v[1], Against = (int)v[2], Kind = (CbKind)v[3], Entry = (ulong)v[4], Day = (int)v[5], Used = v[6] != 0 });
        }
        for (int i = 0; i < Mods.Length; i++)
        {
            var v = Mods[i]; if (v.Length < 5) throw new SaveException("Corrupt opinion modifier in save.");
            b.Mods.Add(new OpinionMod { A = (ulong)v[0], B = (ulong)v[1], Amount = (int)v[2], Day = (int)v[3], Cause = (ulong)v[4], Kind = ModKinds[i] });
        }
        for (int i = 0; i < Realms.Length; i++)
        {
            var v = Realms[i]; if (v.Length < 9) throw new SaveException("Corrupt realm in save.");
            b.Realms[(int)v[0]] = new RealmRow { Realm = (int)v[0], Ruler = (ulong)v[1], LossPm = (int)v[2], TreasuryDelta = v[3], Founded = v[4] != 0, Banner = (int)v[5], Parent = (int)v[6], FoundedDay = (int)v[7], Dead = v[8] != 0, Name = RealmNames[i] };
        }
        foreach (var v in Oaths) { if (v.Length < 4) throw new SaveException("Corrupt oath in save."); b.Oaths.Add(new Fealty { Vassal = (ulong)v[0], Liege = (ulong)v[1], Day = (int)v[2], Fief = (int)v[3] }); }
        foreach (var v in Manors) { if (v.Length < 6) throw new SaveException("Corrupt manor in save."); b.Manors.Add(new Manor { Serial = (int)v[0], Settlement = (int)v[1], OverlordFief = (int)v[2], Realm = (int)v[3], Holder = v[4], GrantedDay = (int)v[5] }); }
        foreach (var v in Petitions) { if (v.Length < 3) throw new SaveException("Corrupt petition in save."); b.Petitions.Add(new Petition { Person = (ulong)v[0], Fief = (int)v[1], Day = (int)v[2] }); }
        foreach (var v in Known) { if (v.Length < 5) throw new SaveException("Corrupt knowledge in save."); b.Known[((int)v[0], (int)v[1])] = new KnownFact { Observer = (int)v[0], About = (int)v[1], Strength = v[2], TreasurySeasons = (int)v[3], Day = (int)v[4] }; }
        for (int i = 0; i < Log.Length; i++)
        {
            var v = Log[i]; if (v.Length < 4) throw new SaveException("Corrupt log line in save.");
            string t = LogText[i]; int bar = t.IndexOf('|');
            b.Log.Add(new LordLog { Day = (int)v[0], Fief = (int)v[1], Lord = (ulong)v[2], Entry = (ulong)v[3], Action = bar < 0 ? t : t[..bar], Note = bar < 0 ? "" : t[(bar + 1)..] });
        }
        return b;
    }
}
