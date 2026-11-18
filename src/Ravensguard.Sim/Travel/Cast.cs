using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Travel;

/// <summary>A traveller the director made because no record fitted: a real person row of the travel book, kept until the encounter is far behind (spec 3.1: no one appears from nothing).</summary>
public sealed class Traveller
{
    public EntityId Id;
    public string Archetype = "";
    public string Encounter = "";
    public long Sec;
    public long XMm, YMm;
}

/// <summary>Who plays an encounter's groups: real records first (caravan crews, vagrants), else generated travellers (the fallback below). The game supplies the first.</summary>
public interface IEncounterCast
{
    /// <summary>Up to <paramref name="count"/> existing people for the archetype near the point, or fewer (the director fills the rest).</summary>
    IReadOnlyList<EntityId> Existing(string archetype, int count, long sec, long xMm, long yMm);
}

/// <summary>Places the groups of an encounter and picks its people (stream 0x341).</summary>
public static class EncounterPlacement
{
    /// <summary>The spawn point of a group: <paramref name="aheadMm"/> along the path, a little to a side. Returns distance in metres.</summary>
    public static int Distance(TravelState st, long sec, int slotKey, int group, EncounterGroupDef g)
        => DetRng.Range(st.Seed, sec / TimeConst.SecondsPerHour, st.HeroId.Value, RngStreams.EncounterPlace, (uint)(slotKey * 16 + group * 4), g.MinM, g.MaxM + 1);

    public static int Count(TravelState st, long sec, int slotKey, int group, EncounterGroupDef g)
        => g.Count.Count < 2 ? 1 : DetRng.Range(st.Seed, sec / TimeConst.SecondsPerHour, st.HeroId.Value, RngStreams.EncounterPlace, (uint)(slotKey * 16 + group * 4 + 1), g.Count[0], g.Count[1] + 1);

    /// <summary>The people of the group: existing records first, then generated ones. Returns their ids.</summary>
    public static List<EntityId> Cast(TravelState st, EncounterDef d, EncounterGroupDef g, int count, long sec, long xMm, long yMm)
    {
        var ids = new List<EntityId>();
        if (st.CastSource != null) foreach (var id in st.CastSource.Existing(g.Archetype, count, sec, xMm, yMm)) { if (ids.Count < count) ids.Add(id); }
        while (ids.Count < count)
        {
            var t = new Traveller { Id = EntityId.MakeIn(IdKind.Person, 254, ++st.NextTraveller), Archetype = g.Archetype, Encounter = d.Id, Sec = sec, XMm = xMm, YMm = yMm };
            st.Travellers.Add(t); ids.Add(t.Id);
        }
        return ids;
    }
}
