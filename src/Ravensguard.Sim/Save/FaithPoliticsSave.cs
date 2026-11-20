using MessagePack;
using Ravensguard.Sim.Faith;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "faith_politics" section (P6-E2, faith-and-church 3; no format bump, absent while the faith's politics hold nothing, so a world that never had a heresy or a ban keeps its bytes):
/// heresies with their adherents, excommunications, relics away from their altars, superstition that differs from its base, witch hunts, believed miracles and the small markers of the daily passes.
/// </summary>
[MessagePackObject]
public sealed class FaithPoliticsSave
{
    public const string SectionName = "faith_politics";

    /// <summary>Per heresy: settlement, preacher, start day, entry, response, response day, interdict, adults.</summary>
    [Key(0)] public long[][] Heresies { get; set; } = [];
    /// <summary>Per heresy: religion, change.</summary>
    [Key(1)] public string[] HeresyText { get; set; } = [];
    /// <summary>Per heresy: the adherents' person ids.</summary>
    [Key(2)] public long[][] Adherents { get; set; } = [];
    /// <summary>Per ban: person, day, entry, cause, by, requester, pilgrims before, lifted day.</summary>
    [Key(3)] public long[][] Excomms { get; set; } = [];
    [Key(4)] public string[] ExcommReligions { get; set; } = [];
    /// <summary>Per relic: key, holder, at, person, fake, fame, since.</summary>
    [Key(5)] public long[][] Relics { get; set; } = [];
    /// <summary>Per relic: name, religion.</summary>
    [Key(6)] public string[] RelicText { get; set; } = [];
    [Key(7)] public long[][] Superstition { get; set; } = [];
    /// <summary>Per hunt: settlement, day, accused, entry, state.</summary>
    [Key(8)] public long[][] Hunts { get; set; } = [];
    /// <summary>Per miracle: day, settlement, entry, person, disputed.</summary>
    [Key(9)] public long[][] Miracles { get; set; } = [];
    /// <summary>Per miracle: kind, natural explanation.</summary>
    [Key(10)] public string[] MiracleText { get; set; } = [];
    /// <summary>Per cure watch: person, day, settlement, entry.</summary>
    [Key(11)] public long[][] Cures { get; set; } = [];
    [Key(12)] public long[] CountedRumours { get; set; } = [];
    [Key(13)] public long[] HeadsShown { get; set; } = [];
    /// <summary>Fire stops seen, venerate season, last day, last season.</summary>
    [Key(14)] public long[] Marks { get; set; } = [];

    public static FaithPoliticsSave Capture(ChurchPolitics b) => new()
    {
        Heresies = [.. b.Heresies.Values.Select(h => new long[] { h.Settlement, h.Preacher, h.StartDay, (long)h.Entry, h.Response, h.ResponseDay, h.Interdict ? 1 : 0, h.Adults })],
        HeresyText = [.. b.Heresies.Values.SelectMany(h => new[] { h.Religion, h.Change })],
        Adherents = [.. b.Heresies.Values.Select(h => h.Adherents.Select(a => (long)a).ToArray())],
        Excomms = [.. b.Excomms.Select(x => new long[] { (long)x.Person, x.Day, (long)x.Entry, (long)x.Cause, (long)x.By, (long)x.Requester, x.PilgrimsBefore, x.LiftedDay })],
        ExcommReligions = [.. b.Excomms.Select(x => x.Religion)],
        Relics = [.. b.Relics.Values.Select(r => new long[] { (long)r.Key, r.Holder, r.At, (long)r.Person, r.Fake ? 1 : 0, r.Fame, r.SinceDay })],
        RelicText = [.. b.Relics.Values.SelectMany(r => new[] { r.Name, r.Religion })],
        Superstition = [.. b.Superstition.Select(kv => new long[] { kv.Key, kv.Value })],
        Hunts = [.. b.Hunts.Select(x => new long[] { x.Settlement, x.Day, (long)x.Accused, (long)x.Entry, x.State })],
        Miracles = [.. b.Miracles.Select(x => new long[] { x.Day, x.Settlement, (long)x.Entry, (long)x.Person, x.Disputed ? 1 : 0 })],
        MiracleText = [.. b.Miracles.SelectMany(x => new[] { x.Kind, x.Natural })],
        Cures = [.. b.Cures.Select(x => new long[] { (long)x.Person, x.Day, x.Settlement, (long)x.Entry })],
        CountedRumours = [.. b.CountedRumours.Select(x => (long)x)],
        HeadsShown = [.. b.HeadsShown.Select(x => (long)x)],
        Marks = [b.FireStopsSeen, b.VenerateSeason, b.LastDay, b.LastSeason],
    };

    public ChurchPolitics Restore()
    {
        if (HeresyText.Length != Heresies.Length * 2 || Adherents.Length != Heresies.Length || ExcommReligions.Length != Excomms.Length || RelicText.Length != Relics.Length * 2 || MiracleText.Length != Miracles.Length * 2 || Marks.Length < 4)
            throw new SaveException("Corrupt 'faith_politics' section in save.");
        var b = new ChurchPolitics { FireStopsSeen = (int)Marks[0], VenerateSeason = (int)Marks[1], LastDay = (int)Marks[2], LastSeason = (int)Marks[3] };
        for (int i = 0; i < Heresies.Length; i++)
        {
            var v = Heresies[i]; if (v.Length < 8) throw new SaveException("Corrupt heresy in save.");
            var h = new HeresyRow { Settlement = (int)v[0], Preacher = v[1], StartDay = (int)v[2], Entry = (ulong)v[3], Response = (byte)v[4], ResponseDay = (int)v[5], Interdict = v[6] != 0, Adults = (int)v[7], Religion = HeresyText[i * 2], Change = HeresyText[i * 2 + 1] };
            foreach (var a in Adherents[i]) h.Adherents.Add((ulong)a);
            b.Heresies[h.Settlement] = h;
        }
        for (int i = 0; i < Excomms.Length; i++)
        {
            var v = Excomms[i]; if (v.Length < 8) throw new SaveException("Corrupt excommunication in save.");
            b.Excomms.Add(new ExcommRow { Person = (ulong)v[0], Day = (int)v[1], Entry = (ulong)v[2], Cause = (ulong)v[3], By = (ulong)v[4], Requester = (ulong)v[5], PilgrimsBefore = (int)v[6], LiftedDay = (int)v[7], Religion = ExcommReligions[i] });
        }
        for (int i = 0; i < Relics.Length; i++)
        {
            var v = Relics[i]; if (v.Length < 7) throw new SaveException("Corrupt relic in save.");
            b.Relics[(ulong)v[0]] = new RelicRow { Key = (ulong)v[0], Holder = (byte)v[1], At = (int)v[2], Person = (ulong)v[3], Fake = v[4] != 0, Fame = (int)v[5], SinceDay = (int)v[6], Name = RelicText[i * 2], Religion = RelicText[i * 2 + 1] };
        }
        foreach (var v in Superstition) { if (v.Length < 2) throw new SaveException("Corrupt superstition in save."); b.Superstition[(int)v[0]] = (byte)v[1]; }
        foreach (var v in Hunts) { if (v.Length < 5) throw new SaveException("Corrupt witch hunt in save."); b.Hunts.Add(new HuntRow { Settlement = (int)v[0], Day = (int)v[1], Accused = (ulong)v[2], Entry = (ulong)v[3], State = (byte)v[4] }); }
        for (int i = 0; i < Miracles.Length; i++)
        {
            var v = Miracles[i]; if (v.Length < 5) throw new SaveException("Corrupt miracle in save.");
            b.Miracles.Add(new MiracleRow { Day = (int)v[0], Settlement = (int)v[1], Entry = (ulong)v[2], Person = (ulong)v[3], Disputed = v[4] != 0, Kind = MiracleText[i * 2], Natural = MiracleText[i * 2 + 1] });
        }
        foreach (var v in Cures) { if (v.Length < 4) throw new SaveException("Corrupt cure watch in save."); b.Cures.Add(new CureWatch { Person = (ulong)v[0], Day = (int)v[1], Settlement = (int)v[2], Entry = (ulong)v[3] }); }
        foreach (var r in CountedRumours) b.CountedRumours.Add((ulong)r);
        foreach (var r in HeadsShown) b.HeadsShown.Add((ulong)r);
        return b;
    }
}
