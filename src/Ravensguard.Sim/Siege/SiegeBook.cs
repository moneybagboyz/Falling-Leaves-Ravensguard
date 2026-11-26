using MessagePack;

namespace Ravensguard.Sim.Siege;

/// <summary>Where a siege stands (sieges-and-engines 3.4). Append only: the save stores the number.</summary>
public enum SiegeState : byte { Investing, Blockade, Bombardment, Assault, Surrendered, Stormed, Lifted, Abandoned }

/// <summary>
/// One siege (3.4): the place, the host that besieges it, the chronicle entry it stands on (<c>siege_begun</c>), the engines at its camp and what its end was. The record holds no object reference, so it saves as it stands
/// (the optional <c>sieges</c> section, keys 2 and 3).
/// </summary>
[MessagePackObject]
public sealed class SiegeRow
{
    [Key(0)] public int Id { get; set; }
    /// <summary>The besieged settlement's index in the home region.</summary>
    [Key(1)] public int Place { get; set; }
    /// <summary>The besieging <c>ArmyHost</c> id and its supply-book host id.</summary>
    [Key(2)] public int Host { get; set; }
    [Key(3)] public int Supply { get; set; }
    [Key(4)] public int Realm { get; set; }
    [Key(5)] public int StartDay { get; set; }
    [Key(6)] public int EndDay { get; set; } = -1;
    [Key(7)] public SiegeState State { get; set; }
    /// <summary>The <c>siege_begun</c> entry (the chain's root) and the entry of the siege's end (0 while it stands).</summary>
    [Key(8)] public ulong Entry { get; set; }
    [Key(9)] public ulong EndEntry { get; set; }
    /// <summary>The camp, in the walls' frame (millimetres): where the engines are laid and aim from.</summary>
    [Key(10)] public long CampXMm { get; set; }
    [Key(11)] public long CampYMm { get; set; }
    /// <summary>Vehicle ids of the engines laid at the camp (building or built).</summary>
    [Key(12)] public List<ulong> Engines { get; set; } = [];
    [Key(13)] public int TermsDay { get; set; } = -1;
    [Key(14)] public int TermsCount { get; set; }
    [Key(15)] public int BreachDay { get; set; } = -1;
    [Key(16)] public int AssaultDay { get; set; } = -1;
    [Key(17)] public int Assaults { get; set; }
    [Key(18)] public int MenAtStart { get; set; }
    [Key(19)] public int GarrisonAtStart { get; set; }
    /// <summary>What the end cost and took: the besiegers' and defenders' dead in assaults, the townsfolk killed in a sack, the mites and grams taken, the houses burned, the people taken.</summary>
    [Key(20)] public int LostAttackers { get; set; }
    [Key(21)] public int LostDefenders { get; set; }
    [Key(22)] public int SackDead { get; set; }
    [Key(23)] public long PlunderMites { get; set; }
    [Key(24)] public long PlunderG { get; set; }
    [Key(25)] public int Burned { get; set; }
    [Key(26)] public int Taken { get; set; }
    /// <summary>The note key of how the siege ended (<c>ch_note_sg_*</c>); empty while it stands.</summary>
    [Key(27)] public string Why { get; set; } = "";
    /// <summary>The place's walls were reported as breached once (the <c>wall_breached</c> entries stand on the siege).</summary>
    [Key(28)] public bool BreachNoted { get; set; }
    /// <summary>The live bubble has the assault (the book waits for <see cref="SiegeWorld.EndAssault"/>) since this game hour (-1 none).</summary>
    [Key(29)] public long LiveSinceHour { get; set; } = -1;
    [Key(30)] public int RefusedDay { get; set; } = -1;

    [IgnoreMember] public bool Open => State is SiegeState.Investing or SiegeState.Blockade or SiegeState.Bombardment or SiegeState.Assault;
}

/// <summary>The sieges of the world by id, the optional keys 2 and 3 of the <c>sieges</c> section. Empty (so absent from a save) while none was ever laid.</summary>
public sealed class SiegeBook
{
    public SortedDictionary<int, SiegeRow> Sieges { get; } = [];
    public int Next = 1;
    public bool IsEmpty => Sieges.Count == 0 && Next == 1;

    public SiegeRow? OfPlace(int place) { foreach (var s in Sieges.Values) if (s.Place == place && s.Open) return s; return null; }
    public SiegeRow? OfHost(int host) { foreach (var s in Sieges.Values) if (s.Host == host && s.Open) return s; return null; }

    public ulong Hash()
    {
        ulong h = 0xcbf29ce484222325UL;
        void M(long v) { h = (h ^ (ulong)v) * 0x100000001b3UL; h ^= h >> 29; }
        M(Next);
        foreach (var s in Sieges.Values)
        {
            M(s.Id); M(s.Place); M(s.Host); M(s.Supply); M(s.Realm); M(s.StartDay); M(s.EndDay); M((long)s.State); M((long)s.Entry); M((long)s.EndEntry); M(s.CampXMm); M(s.CampYMm);
            foreach (ulong v in s.Engines) M((long)v);
            M(s.TermsDay); M(s.TermsCount); M(s.BreachDay); M(s.AssaultDay); M(s.Assaults); M(s.MenAtStart); M(s.GarrisonAtStart); M(s.LostAttackers); M(s.LostDefenders); M(s.SackDead);
            M(s.PlunderMites); M(s.PlunderG); M(s.Burned); M(s.Taken); M(s.BreachNoted ? 1 : 0); M(s.LiveSinceHour); M(s.RefusedDay);
            foreach (char c in s.Why) M(c);
        }
        return h;
    }
}
