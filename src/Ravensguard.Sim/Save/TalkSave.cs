using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Rumours;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class RumourRowSave
{
    [Key(0)] public ulong Id { get; set; } [Key(1)] public ulong Source { get; set; } [Key(2)] public byte Kind { get; set; } [Key(3)] public string TypeId { get; set; } = ""; [Key(4)] public string TextKey { get; set; } = "";
    [Key(5)] public int Settlement { get; set; } [Key(6)] public string Place { get; set; } = ""; [Key(7)] public ulong[] Subjects { get; set; } = []; [Key(8)] public byte Severity { get; set; }
    [Key(9)] public int BornHour { get; set; } [Key(10)] public int ExpiresDay { get; set; } [Key(11)] public int LifeDays { get; set; } [Key(12)] public int Interest { get; set; } [Key(13)] public int Number { get; set; }
    [Key(14)] public string Who { get; set; } = ""; [Key(15)] public string About { get; set; } = ""; [Key(16)] public byte Facts { get; set; } [Key(17)] public int[] Witnesses { get; set; } = [];
    [Key(18)] public bool FromAfar { get; set; } [Key(19)] public bool Authored { get; set; }
}

[MessagePackObject]
public sealed class HearingRowSave
{
    [Key(0)] public ulong Rumour { get; set; } [Key(1)] public int Listener { get; set; } [Key(2)] public int Teller { get; set; } [Key(3)] public int Hour { get; set; } [Key(4)] public byte Hop { get; set; }
    [Key(5)] public byte Flags { get; set; } [Key(6)] public short PlaceAlt { get; set; } [Key(7)] public byte How { get; set; } [Key(8)] public bool Dropped { get; set; }
}

[MessagePackObject]
public sealed class HeroHearingSave
{
    [Key(0)] public ulong Rumour { get; set; } [Key(1)] public int Hour { get; set; } [Key(2)] public int Teller { get; set; } [Key(3)] public string TellerName { get; set; } = ""; [Key(4)] public byte How { get; set; }
    [Key(5)] public int Hop { get; set; } [Key(6)] public byte Flags { get; set; } [Key(7)] public short PlaceAlt { get; set; }
    [Key(8)] public int[] ChainPerson { get; set; } = []; [Key(9)] public int[] ChainHour { get; set; } = []; [Key(10)] public int[] ChainHop { get; set; } = []; [Key(11)] public byte[] ChainHow { get; set; } = []; [Key(12)] public int[] ChainTeller { get; set; } = [];
    [Key(13)] public string[] ChainNames { get; set; } = [];
}

[MessagePackObject]
public sealed class RelationRowSave { [Key(0)] public int A { get; set; } [Key(1)] public int B { get; set; } [Key(2)] public short Trust { get; set; } [Key(3)] public byte Familiarity { get; set; } [Key(4)] public int LastMet { get; set; } [Key(5)] public byte Flags { get; set; } [Key(6)] public int PerDayFor { get; set; } [Key(7)] public int PerDayCount { get; set; } }

/// <summary>The "talk" section: rumours and their hearings (in the order they were made), the relations between people, the hero's heard rumours, the dialogue flags and the seeder's place in the chronicle.</summary>
[MessagePackObject]
public sealed class TalkSave
{
    public const string SectionName = "talk";
    [Key(0)] public RumourRowSave[] Rumours { get; set; } = [];
    [Key(1)] public HearingRowSave[] Hearings { get; set; } = [];
    [Key(2)] public HeroHearingSave[] Heard { get; set; } = [];
    [Key(3)] public RelationRowSave[] Relations { get; set; } = [];
    [Key(4)] public string[] Flags { get; set; } = [];
    [Key(5)] public int Cursor { get; set; }
    [Key(6)] public long LastHour { get; set; }
    [Key(7)] public int LastDay { get; set; }
    [Key(8)] public ulong NextAuthored { get; set; }

    public static TalkSave Capture(TalkState t)
    {
        var rt = t.Rumours; var hearings = new List<HearingRowSave>();
        foreach (var (id, _) in rt.Rumours)
            foreach (var h in rt.HearingsOf(id))
                hearings.Add(new HearingRowSave { Rumour = id.Value, Listener = h.Listener, Teller = h.Teller, Hour = h.Hour, Hop = h.Hop, Flags = (byte)h.Flags, PlaceAlt = h.PlaceAlt, How = (byte)h.How, Dropped = h.Dropped });
        return new TalkSave
        {
            Rumours = [.. rt.Rumours.Values.Select(r => new RumourRowSave
            {
                Id = r.Id.Value, Source = r.Source.Value, Kind = (byte)r.Kind, TypeId = r.TypeId, TextKey = r.Key, Settlement = r.Settlement, Place = r.Place, Subjects = [.. r.Subjects.Select(s => s.Value)], Severity = r.Severity,
                BornHour = r.BornHour, ExpiresDay = r.ExpiresDay, LifeDays = r.LifeDays, Interest = r.InterestPm, Number = r.Number, Who = r.Who, About = r.About, Facts = r.FactsMask, Witnesses = r.Witnesses, FromAfar = r.FromAfar, Authored = r.Authored,
            })],
            Hearings = [.. hearings],
            Heard = [.. t.Heard.Select(h => new HeroHearingSave
            {
                Rumour = h.Rumour.Value, Hour = h.Hour, Teller = h.Teller, TellerName = h.TellerName, How = (byte)h.How, Hop = h.Hop, Flags = (byte)h.Flags, PlaceAlt = h.PlaceAlt,
                ChainPerson = [.. h.Chain.Select(c => c.Person)], ChainHour = [.. h.Chain.Select(c => c.Hour)], ChainHop = [.. h.Chain.Select(c => c.Hop)], ChainHow = [.. h.Chain.Select(c => (byte)c.How)], ChainTeller = [.. h.Chain.Select(c => c.Teller)],
                ChainNames = [.. h.ChainNames],
            })],
            Relations = [.. t.Relations.Rows.Select(kv => new RelationRowSave { A = kv.Key.Item1, B = kv.Key.Item2, Trust = kv.Value.TrustPm, Familiarity = kv.Value.Familiarity, LastMet = kv.Value.LastMetDay, Flags = (byte)kv.Value.Flags, PerDayFor = kv.Value.PerDayFor, PerDayCount = kv.Value.PerDayCount })],
            Flags = [.. t.Flags], Cursor = t.Cursor, LastHour = t.LastHour, LastDay = t.LastDay, NextAuthored = t.NextAuthored,
        };
    }

    public TalkState Restore()
    {
        var t = new TalkState { Cursor = Cursor, LastHour = LastHour, LastDay = LastDay, NextAuthored = NextAuthored };
        t.Rumours.Restore(Rumours.Select(r => new Rumour
        {
            Id = new EntityId(r.Id), Source = new EntityId(r.Source), Kind = (RumourKind)r.Kind, TypeId = r.TypeId, Key = r.TextKey, Settlement = r.Settlement, Place = r.Place, Subjects = [.. r.Subjects.Select(s => new EntityId(s))],
            Severity = r.Severity, BornHour = r.BornHour, ExpiresDay = r.ExpiresDay, LifeDays = r.LifeDays, InterestPm = r.Interest, Number = r.Number, Who = r.Who, About = r.About, FactsMask = r.Facts, Witnesses = r.Witnesses, FromAfar = r.FromAfar, Authored = r.Authored,
        }), Hearings.Select(h => (new EntityId(h.Rumour), new Hearing { Listener = h.Listener, Teller = h.Teller, Hour = h.Hour, Hop = h.Hop, Flags = (Distortion)h.Flags, PlaceAlt = h.PlaceAlt, How = (HearHow)h.How, Dropped = h.Dropped })));
        foreach (var h in Heard)
        {
            var c = new HeroHearing { Rumour = new EntityId(h.Rumour), Hour = h.Hour, Teller = h.Teller, TellerName = h.TellerName, How = (HearHow)h.How, Hop = h.Hop, Flags = (Distortion)h.Flags, PlaceAlt = h.PlaceAlt, ChainNames = [.. h.ChainNames] };
            for (int i = 0; i < h.ChainPerson.Length; i++) c.Chain.Add(new TraceStep(h.ChainPerson[i], h.ChainHour[i], h.ChainHop[i], (HearHow)h.ChainHow[i], h.ChainTeller[i]));
            t.Heard.Add(c);
        }
        foreach (var r in Relations) t.Relations.Set((r.A, r.B), new Relation { TrustPm = r.Trust, Familiarity = r.Familiarity, LastMetDay = r.LastMet, Flags = (RelationFlags)r.Flags, PerDayFor = r.PerDayFor, PerDayCount = r.PerDayCount });
        foreach (var f in Flags) t.Flags.Add(f);
        return t;
    }
}
