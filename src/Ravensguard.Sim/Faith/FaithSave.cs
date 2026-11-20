using MessagePack;
using Ravensguard.Sim.Faith;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "faith" section (P6-B4, faith-and-church 3, overview 7; no format bump, absent while the church has nothing to say, so a world that never had one keeps its bytes): the parishes and sees with
/// their holders and vacancies, the piety that differs from its default, the hero's pilgrimage and services. Religions are derived, never saved; a see names its religion by the template's id.
/// </summary>
[MessagePackObject]
public sealed class FaithSave
{
    public const string SectionName = "faith";

    /// <summary>Per parish: settlement, priest (-1 vacant), vacant since, fabric mites, poor mites, service day, attended, adults.</summary>
    [Key(0)] public long[][] Parishes { get; set; } = [];
    /// <summary>Per see: index, realm, seat, bishop, vacant since, election day, taken mites.</summary>
    [Key(1)] public long[][] Sees { get; set; } = [];
    [Key(2)] public string[] SeeReligions { get; set; } = [];
    /// <summary>Per person with a piety that differs from the default: person id, piety.</summary>
    [Key(3)] public long[][] Piety { get; set; } = [];
    /// <summary>The hero's side: last service day, comfort until day, pilgrimages done, the last daily pass (-1 each when never), the hero's id.</summary>
    [Key(4)] public long[] Hero { get; set; } = [];
    /// <summary>A pilgrimage on the road: the religion's template id (empty: none), the target settlement and the start day.</summary>
    [Key(5)] public string PilgrimReligion { get; set; } = "";
    [Key(6)] public int PilgrimTarget { get; set; } = -1;
    [Key(7)] public int PilgrimStart { get; set; }
    [Key(8)] public ulong PilgrimEntry { get; set; }

    public static FaithSave Capture(ChurchBook b) => new()
    {
        Parishes = [.. b.Parishes.Values.Select(p => new long[] { p.Settlement, p.Priest, p.VacantSince, p.FabricMites, p.PoorMites, p.ServiceDay, p.Attended, p.Adults })],
        Sees = [.. b.Sees.Values.Select(s => new long[] { s.Index, s.Realm, s.Seat, s.Bishop, s.VacantSince, s.ElectionDay, s.TakenMites })],
        SeeReligions = [.. b.Sees.Values.Select(s => s.Religion)],
        Piety = [.. b.Piety.Select(kv => new long[] { (long)kv.Key, kv.Value })],
        Hero = [b.LastServiceDay, b.ComfortUntilDay, b.Pilgrimages, b.LastDay, b.HeroId],
        PilgrimReligion = b.Pilgrim?.Religion ?? "", PilgrimTarget = b.Pilgrim?.Target ?? -1, PilgrimStart = b.Pilgrim?.StartDay ?? 0, PilgrimEntry = b.Pilgrim?.Entry ?? 0,
    };

    public ChurchBook Restore()
    {
        if (Sees.Length != SeeReligions.Length || Hero.Length < 5) throw new SaveException("Corrupt 'faith' section in save.");
        var b = new ChurchBook { LastServiceDay = (int)Hero[0], ComfortUntilDay = (int)Hero[1], Pilgrimages = (int)Hero[2], LastDay = (int)Hero[3], HeroId = Hero[4] };
        foreach (var v in Parishes)
        {
            if (v.Length < 8) throw new SaveException("Corrupt parish in save.");
            b.Parishes[(int)v[0]] = new ParishRow { Settlement = (int)v[0], Priest = v[1], VacantSince = (int)v[2], FabricMites = v[3], PoorMites = v[4], ServiceDay = (int)v[5], Attended = (int)v[6], Adults = (int)v[7] };
        }
        for (int i = 0; i < Sees.Length; i++)
        {
            var v = Sees[i]; if (v.Length < 7) throw new SaveException("Corrupt see in save.");
            b.Sees[(int)v[0]] = new SeeRow { Index = (int)v[0], Religion = SeeReligions[i], Realm = (int)v[1], Seat = (int)v[2], Bishop = v[3], VacantSince = (int)v[4], ElectionDay = (int)v[5], TakenMites = v[6] };
        }
        foreach (var v in Piety) { if (v.Length < 2) throw new SaveException("Corrupt piety in save."); b.Piety[(ulong)v[0]] = (byte)v[1]; }
        if (PilgrimReligion.Length > 0) b.Pilgrim = new PilgrimRow { Religion = PilgrimReligion, Target = PilgrimTarget, StartDay = PilgrimStart, Entry = PilgrimEntry };
        return b;
    }
}
