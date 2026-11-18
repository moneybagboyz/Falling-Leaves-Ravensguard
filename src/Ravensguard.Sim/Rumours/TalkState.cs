using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Rumours;

/// <summary>What the talk systems read of the world: the people, the places, the chronicle and the seed. Built from a <c>WorldState</c> by <see cref="From"/>, or by hand for a test.</summary>
public sealed class TalkWorld
{
    public required PersonTable People { get; init; }
    public HouseholdTable? Households { get; init; }
    public required IReadOnlyList<string> SettlementNames { get; init; }
    public IReadOnlyList<EntityId> SettlementIds { get; init; } = [];
    public required Chronicle Chronicle { get; init; }
    public required ulong Seed { get; init; }
    public Func<int, bool>? Away { get; init; }

    public static TalkWorld From(Ravensguard.Sim.Save.WorldState st) => new()
    {
        People = st.Population.People, Households = st.Population.Households, Chronicle = st.Chronicle, Seed = st.Seed,
        SettlementNames = [.. st.Population.Region.Settlements.Select(s => s.Name)], SettlementIds = [.. st.Population.Region.Settlements.Select(s => s.Id)],
    };

    public int SettlementOf(string name) { for (int i = 0; i < SettlementNames.Count; i++) if (SettlementNames[i] == name) return i; return -1; }
    public int SettlementOf(EntityId id) { for (int i = 0; i < SettlementIds.Count; i++) if (SettlementIds[i] == id) return i; return -1; }
}

/// <summary>
/// The talk records of a world (saved in the "talk" section): the rumours and hearings, the relations between people, what the hero has heard, the dialogue flags, and the places in the chronicle
/// the seeder has read up to. The pass log and the resident index are views made from the rest and are not saved.
/// </summary>
public sealed class TalkState
{
    public RumourTable Rumours { get; } = new();
    public RelationTable Relations { get; } = new();
    public List<HeroHearing> Heard { get; } = [];
    public SortedSet<string> Flags { get; } = new(StringComparer.Ordinal);
    /// <summary>How many chronicle entries have been read for rumours.</summary>
    public int Cursor;
    public long LastHour = -1;
    public int LastDay = -1;
    public ulong NextAuthored = 1;
    public List<Pass> Passes { get; } = [];
    internal int[][]? Residents;
    internal int ResidentsDay = -1, ResidentsCount = -1;

    public bool IsEmpty => Rumours.Count == 0 && Heard.Count == 0 && Flags.Count == 0 && Relations.Count == 0 && Cursor == 0;
    public bool HeroHolds(EntityId rumour) { foreach (var h in Heard) if (h.Rumour == rumour) return true; return false; }

    /// <summary>The digest of the talk records (the spec's <c>TalkHash</c>): rumours, hearings, relations, the hero's cards.</summary>
    public ulong Hash()
    {
        ulong h = Rumours.Hash();
        static ulong M(ulong a, ulong b) => DetRng.Mix(a ^ (b + 0x9E3779B97F4A7C15UL));
        foreach (var (k, r) in Relations.Rows) { h = M(h, (ulong)(uint)k.Item1); h = M(h, (ulong)(uint)k.Item2); h = M(h, (ulong)(ushort)r.TrustPm); h = M(h, r.Familiarity); h = M(h, (ulong)(uint)r.LastMetDay); }
        foreach (var c in Heard) { h = M(h, c.Rumour.Value); h = M(h, (ulong)(uint)c.Hour); h = M(h, (ulong)(uint)c.Teller); h = M(h, (ulong)c.Hop); }
        foreach (var f in Flags) foreach (var ch in f) h = M(h, ch);
        return M(h, (ulong)(uint)Cursor);
    }
}
