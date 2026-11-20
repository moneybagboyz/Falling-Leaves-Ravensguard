using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Law;

/// <summary>Where a crime stands (crime-and-justice 3): seen, reported to the law, the accused named, held in a cell, tried, sentenced, served, fled or pardoned. Append only; later steps (gaol, courts) move it on.</summary>
public enum CrimeState : byte { Seen, Reported, Accused, Held, Tried, Sentenced, Served, Fled, Pardoned,
    /// <summary>P6-C1: the court found no proof and let him go. Appended.</summary>
    Acquitted }

/// <summary>One who saw: the person and flags (bit 0 he knew who it was, bit 1 he is the actor's kin or friend, bit 2 he fears the actor): what the report's draw reads later.</summary>
public readonly record struct WitnessRow(ulong Person, byte Flags)
{
    public const byte KnewIt = 1, FriendFlag = 2, FearFlag = 4;
    public bool Recognised => (Flags & KnewIt) != 0;
    public bool Friend => (Flags & FriendFlag) != 0;
    public bool Fears => (Flags & FearFlag) != 0;
}

/// <summary>
/// One crime that somebody saw (crime-and-justice 3). <see cref="Perpetrator"/> is the doer's id value when a witness recognised him, else -1 and <see cref="Look"/> holds what the witnesses described;
/// the hero's id is zero, so "unknown" is never an id test. The chain of chronicle entries (<see cref="Entry"/> the act, <see cref="Witnessed"/>, <see cref="Reported"/>) is how the law explains itself.
/// </summary>
public sealed class CrimeRecord
{
    public int Id;
    public string Kind = "";
    public long Perpetrator = -1;
    /// <summary>The doer even when the witnesses did not know him (the chronicle is omniscient; the law is not): used to attach the bounty to the right man once a guard matches the description.</summary>
    public ulong Doer;
    public ActorLook Look = ActorLook.None;
    /// <summary>The victim's id value (a person, a household, a fief, the church), 0: none.</summary>
    public ulong Victim;
    public int Settlement = -1;
    public int Day;
    public long Sec;
    public List<WitnessRow> Witnesses = [];
    public long ValueMites, Damage;
    public ulong Entry, Witnessed, Reported;
    /// <summary>The bounty job's id value, 0: none posted.</summary>
    public ulong BountyJob;
    /// <summary>The <c>bounty_posted</c> entry (the cause of a collection).</summary>
    public ulong BountyEntry;
    public CrimeState State;
    /// <summary>A witness raised the hue and cry.</summary>
    public bool Cried;
    /// <summary>The witness whose report made it known to the law (person id value), 0 while unreported.</summary>
    public ulong Reporter;

    public bool Known => Perpetrator >= 0;
}

/// <summary>
/// The records of the law (the optional "law" section, crime-and-justice 3): crimes by id, the count of convictions by (person, kind) for the escalation of sentences, and the day the daily pass last ran.
/// </summary>
public sealed class CrimeBook
{
    public readonly SortedDictionary<int, CrimeRecord> Crimes = [];
    public readonly SortedDictionary<(ulong Person, string Kind), int> Convictions = new(Comparer<(ulong Person, string Kind)>.Create((a, b) => a.Person != b.Person ? a.Person.CompareTo(b.Person) : string.CompareOrdinal(a.Kind, b.Kind)));
    public int NextId = 1;
    public int LastDay = -1;

    public bool IsEmpty => Crimes.Count == 0 && Convictions.Count == 0 && NextId == 1;

    public CrimeRecord Add(CrimeRecord r) { r.Id = NextId++; Crimes[r.Id] = r; return r; }
    public CrimeRecord? Get(int id) => Crimes.GetValueOrDefault(id);
    public int ConvictionsOf(EntityId person, string kind) => Convictions.GetValueOrDefault((person.Value, kind));

    /// <summary>The crimes still in the open for a person (known to be his, not yet served, pardoned or acquitted; one he ran from is still open), in id order.</summary>
    public IEnumerable<CrimeRecord> Open(EntityId person) => Crimes.Values.Where(c => c.Known && c.Perpetrator == (long)person.Value && (c.State < CrimeState.Served || c.State == CrimeState.Fled));

    /// <summary>The <c>LawHash</c> of the daily trail (crime 5): crimes, convictions and the bounty links in id order.</summary>
    public ulong Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        void S(string s) { foreach (char c in s) M(c); M(0xFF); }
        foreach (var (id, c) in Crimes)
        {
            M((ulong)(uint)id); S(c.Kind); M((ulong)c.Perpetrator); M(c.Doer); M(c.Victim); M((ulong)(uint)c.Settlement); M((ulong)(uint)c.Day); M((ulong)c.Sec);
            M((ulong)c.ValueMites); M((ulong)c.Damage); M(c.Entry); M(c.Witnessed); M(c.Reported); M(c.BountyJob); M(c.BountyEntry); M((ulong)c.State); M(c.Cried ? 1UL : 0); M(c.Reporter);
            foreach (var w in c.Witnesses) { M(w.Person); M(w.Flags); }
            foreach (uint col in c.Look.Colours) M(col); M(c.Look.Build);
        }
        foreach (var ((p, k), n) in Convictions) { M(p); S(k); M((ulong)(uint)n); }
        M((ulong)(uint)NextId); M((ulong)(uint)LastDay);
        return h;
    }
}
