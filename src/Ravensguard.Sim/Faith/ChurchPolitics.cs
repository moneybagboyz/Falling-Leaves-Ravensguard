using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Faith;

/// <summary>A heresy in a settlement (faith-and-church 3, 4.5): the faith it broke from, the one doctrine it changed, its preacher, its adherents (people), and what the bishop has done about it.</summary>
public sealed class HeresyRow
{
    public int Settlement;
    /// <summary>The religion's template id.</summary>
    public string Religion = "";
    /// <summary>The doctrine changed: <c>tithe_poor</c>, <c>slavery_forbid</c>, <c>usury_forbid</c>, <c>fasting_strict</c> (the text key is <c>fa_heresy_</c> + this).</summary>
    public string Change = "";
    public long Preacher;
    public int StartDay;
    public ulong Entry;
    /// <summary>The adherents, by person id (ascending).</summary>
    public readonly SortedSet<ulong> Adherents = [];
    /// <summary>What the bishop did: 0 nothing yet, 1 preached against it, 2 had the preacher tried, 3 laid an interdict, 4 let it be.</summary>
    public byte Response;
    public int ResponseDay = -1;
    public bool Interdict;
    /// <summary>Adults of the settlement on the day of its last count (for the share).</summary>
    public int Adults;
}

/// <summary>A person's excommunication (faith 4.6): by whom, on what cause, at whose asking, and how many pilgrimages the hero had made before it (penance asks for one more).</summary>
public sealed class ExcommRow
{
    public ulong Person;
    public string Religion = "";
    public int Day;
    public ulong Entry, Cause, By, Requester;
    public int PilgrimsBefore;
    /// <summary>-1 while it stands, else the day of the penance or the bishop's grace.</summary>
    public int LiftedDay = -1;
    public bool Active => LiftedDay < 0;
}

/// <summary>A relic's state where it differs from the derived default (the reliquary on its altar): in a person's hands, for sale, lost, or a pardoner's fake (faith 4.4).</summary>
public sealed class RelicRow
{
    public ulong Key;
    public string Name = "", Religion = "";
    /// <summary>0 on the altar of <see cref="At"/>, 1 in a person's pack, 2 for sale, 3 gone.</summary>
    public byte Holder;
    public int At = -1;
    public ulong Person;
    public bool Fake;
    /// <summary>Believed miracles that have made it famous.</summary>
    public int Fame;
    public int SinceDay;
}

/// <summary>A witch hunt (faith 4.7): a scapegoat accused in a settlement of high superstition; the bishop may stop it.</summary>
public sealed class HuntRow
{
    public int Settlement, Day;
    public ulong Accused, Entry;
    /// <summary>0 under way, 1 stopped by the bishop, 2 the accused was handed to the court.</summary>
    public byte State;
}

/// <summary>A believed miracle (overview 10a): what the folk call one, and the natural explanation always set beside it. It may be disputed.</summary>
public sealed class MiracleRow
{
    public int Day, Settlement;
    /// <summary>cure, fire_stopped.</summary>
    public string Kind = "";
    /// <summary>The natural explanation's note key.</summary>
    public string Natural = "";
    public ulong Entry, Person;
    public bool Disputed;
}

/// <summary>A pilgrim in pain, watched for a natural recovery (the cure a miracle may follow).</summary>
public sealed class CureWatch
{
    public ulong Person;
    public int Day, Settlement;
    public ulong Entry;
}

/// <summary>
/// The politics of the faith (P6-E2; faith-and-church 3, 4.5 to 4.7; the optional "faith_politics" section, written only once it holds something, no format bump): heresies with their adherents, excommunications,
/// relics away from their altars, superstition where it differs from what the history gives, witch hunts, believed miracles. No draw lives here; <see cref="Heresy"/>, <see cref="Excommunication"/>,
/// <see cref="Relics"/>, <see cref="Superstition"/> and <see cref="Miracles"/> draw with the registered streams and write the results in.
/// </summary>
public sealed class ChurchPolitics
{
    public readonly SortedDictionary<int, HeresyRow> Heresies = [];
    public readonly List<ExcommRow> Excomms = [];
    public readonly SortedDictionary<ulong, RelicRow> Relics = [];
    /// <summary>Superstition per settlement where it differs from its derived base (0..100).</summary>
    public readonly SortedDictionary<int, byte> Superstition = [];
    public readonly List<HuntRow> Hunts = [];
    public readonly List<MiracleRow> Miracles = [];
    public readonly List<CureWatch> Cures = [];
    /// <summary>Rumours already counted toward a settlement's superstition (by rumour id).</summary>
    public readonly SortedSet<ulong> CountedRumours = [];
    /// <summary>Fire stops already read for a believed miracle (the count of <c>FireBook.Stops</c> seen).</summary>
    public int FireStopsSeen;
    /// <summary>The season (day / 15) the hero last venerated a relic in, -1 never; the day of the last daily pass, the last season pass.</summary>
    public int VenerateSeason = -1, LastDay = -1, LastSeason = -1;
    /// <summary>Monster kills whose heads the hero has shown (the entry of the kill): never twice for one kill.</summary>
    public readonly SortedSet<ulong> HeadsShown = [];

    public bool IsEmpty => Heresies.Count == 0 && Excomms.Count == 0 && Relics.Count == 0 && Superstition.Count == 0 && Hunts.Count == 0 && Miracles.Count == 0 && Cures.Count == 0 && CountedRumours.Count == 0
        && FireStopsSeen == 0 && VenerateSeason < 0 && HeadsShown.Count == 0;

    public ExcommRow? ActiveFor(ulong person) { foreach (var x in Excomms) if (x.Person == person && x.Active) return x; return null; }
    public bool IsExcommunicated(ulong person) => ActiveFor(person) != null;

    /// <summary>The <c>FaithHash</c> part for the politics: heresies, bans, relics, superstition, hunts, miracles, in ascending order (joins the trail only once non-empty).</summary>
    public ulong Hash()
    {
        ulong h = 0x9E3779B97F4A7C15UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        void S(string s) { foreach (char c in s) M(c); M(0xFF); }
        foreach (var (k, r) in Heresies) { M((ulong)(uint)k); S(r.Religion); S(r.Change); M((ulong)r.Preacher); M((ulong)(uint)r.StartDay); M(r.Entry); M(r.Response); M((ulong)(uint)r.ResponseDay); M(r.Interdict ? 1UL : 0UL); foreach (var a in r.Adherents) M(a); }
        foreach (var x in Excomms) { M(x.Person); S(x.Religion); M((ulong)(uint)x.Day); M(x.Entry); M(x.Cause); M(x.By); M(x.Requester); M((ulong)(uint)x.PilgrimsBefore); M((ulong)(uint)x.LiftedDay); }
        foreach (var (k, r) in Relics) { M(k); S(r.Name); S(r.Religion); M(r.Holder); M((ulong)(uint)r.At); M(r.Person); M(r.Fake ? 1UL : 0UL); M((ulong)(uint)r.Fame); M((ulong)(uint)r.SinceDay); }
        foreach (var (k, v) in Superstition) { M((ulong)(uint)k); M(v); }
        foreach (var x in Hunts) { M((ulong)(uint)x.Settlement); M((ulong)(uint)x.Day); M(x.Accused); M(x.Entry); M(x.State); }
        foreach (var x in Miracles) { M((ulong)(uint)x.Day); M((ulong)(uint)x.Settlement); S(x.Kind); S(x.Natural); M(x.Entry); M(x.Person); M(x.Disputed ? 1UL : 0UL); }
        foreach (var x in Cures) { M(x.Person); M((ulong)(uint)x.Day); M((ulong)(uint)x.Settlement); M(x.Entry); }
        foreach (var r in CountedRumours) M(r);
        foreach (var r in HeadsShown) M(r);
        M((ulong)(uint)FireStopsSeen); M((ulong)(uint)VenerateSeason); M((ulong)(uint)LastDay); M((ulong)(uint)LastSeason);
        return h;
    }
}
