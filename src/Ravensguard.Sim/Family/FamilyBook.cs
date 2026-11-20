using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Family;

/// <summary>Where a courtship stands (family-and-lifespan 3). A betrothal that waits for a dwelling is <see cref="Betrothed"/> with no wedding day yet.</summary>
public enum CourtState : byte { Courting, Betrothed, Wed, Broken }

/// <summary>The courted's household head's answer (4.1).</summary>
public enum ConsentState : byte { Pending, Yes, No }

/// <summary>One courtship of the hero (3): who, the gifts and meetings so far, the consent, the state and the wedding day.</summary>
public sealed class Courtship
{
    public int Id;
    public ulong Suitor, Courted;
    public int StartDay;
    /// <summary>Mites given as gifts.</summary>
    public long Gifts;
    public int Meetings;
    public ConsentState Consent;
    public CourtState State;
    /// <summary>The wedding day (a holy day at least 5 days after the betrothal), -1 while the betrothal waits for a dwelling.</summary>
    public int WeddingDay = -1;
    public int LastMeetingDay = -1;
    /// <summary>The last day the courted was asked (a plea is answered once a day: the same day gives the same answer).</summary>
    public int AskedDay = -1;
    /// <summary>The score of the last answer and who gave it (the courted's own, then the head of the house).</summary>
    public int Score;
}

/// <summary>What a child is being taught (4.4).</summary>
public enum Course : byte { None, Arms, Letters, Trade, Church, Household }

/// <summary>A child of the hero's household and what he has had taught: the tutor (a person, 0 none), the course (and the skill for a trade), the lesson hours and the thousandths gained (skills and attributes).</summary>
public sealed class Upbringing
{
    public ulong Child, Tutor;
    /// <summary>Whether a tutor is set (the hero's own person id is 0, so zero cannot mean none).</summary>
    public bool HasTutor;
    public Course Course;
    public Skill TradeSkill;
    public int Hours;
    /// <summary>Thousandths of a point gained, by skill id (0..99) and by attribute (200 + attribute index).</summary>
    public SortedDictionary<int, long> Milli { get; } = [];
    public const int AttrBase = 200;
}

/// <summary>The grievances a kin member holds against the hero (3): bit flags, with the loyalty they cost (4.5).</summary>
[Flags] public enum KinGrievance : byte { None = 0, Neglected = 1, Punished = 2, PassedOver = 4, MarriedOff = 8, Favoured = 16 }

/// <summary>The hero's ageing state (3, 4.6): the last age whose birthday has been applied and what age has taken from each attribute.</summary>
public sealed class HeroAgeing
{
    public ulong Hero;
    public int Row = -1;
    /// <summary>The age whose birthday was last applied.</summary>
    public int LastAge;
    public int[] Lost = new int[PersonTable.AttrCount];
    public int LastRollYear = -1, WarnedYear = -1;
}

/// <summary>The hero's death of old age or in childbed, waiting for the heir to take up (the game puts the succession screen up on it; the run's tests issue the heir's command).</summary>
public sealed class PendingDeath
{
    public int Day, Row, Age;
    public DeathCause Cause;
}

/// <summary>
/// The family state of the running world (the optional "family" section, absent while nothing is in it): the hero's courtships, the children's upbringing, the kin's grievances, the hero's ageing and the
/// children already noticed. Pregnancies are the person table's own columns. Everything is keyed by ids and walked in id order.
/// </summary>
public sealed class FamilyBook
{
    public SortedDictionary<int, Courtship> Courtships { get; } = [];
    public int NextCourtship = 1;
    public SortedDictionary<ulong, Upbringing> Upbringings { get; } = [];
    public SortedDictionary<ulong, KinGrievance> Grievances { get; } = [];
    public HeroAgeing? Ageing;
    public PendingDeath? Death;
    /// <summary>Children of the hero noticed and given their start (attributes, kin link, the chronicle).</summary>
    public SortedSet<ulong> Born { get; } = [];
    public int LastDay = -1, LastKidnapSeason = -1;
    public int Kidnaps;
    /// <summary>Arranged matches (P6-E3, 4.2): the hero's for his household's members and the lords' own; kept while they are open and for a season after.</summary>
    public SortedDictionary<int, Match> Matches { get; } = [];
    public int NextMatch = 1;
    /// <summary>Kin who have already contested the hero's will (one dispute each).</summary>
    public SortedSet<ulong> Contested { get; } = [];

    public bool IsEmpty => Courtships.Count == 0 && Upbringings.Count == 0 && Grievances.Count == 0 && Ageing == null && Death == null && Born.Count == 0 && LastDay < 0 && Kidnaps == 0 && Matches.Count == 0 && Contested.Count == 0;

    public Courtship? CourtshipOf(ulong courted) => Courtships.Values.FirstOrDefault(c => c.Courted == courted && c.State is CourtState.Courting or CourtState.Betrothed);

    /// <summary>The kin member's loyalty: trust in the hero (permille) less 150 per grievance (4.5).</summary>
    public static int Loyalty(int trustPm, KinGrievance g, int weight = 150) => trustPm - weight * System.Numerics.BitOperations.PopCount((uint)g);

    public ulong Hash()
    {
        ulong h = 0xFA17_5EED_0000_0001UL;
        void M(long v) => h = DetRng.Mix(h ^ (ulong)v);
        M(NextCourtship); M(LastDay); M(LastKidnapSeason); M(Kidnaps);
        foreach (var c in Courtships.Values) { M(c.Id); M((long)c.Suitor); M((long)c.Courted); M(c.StartDay); M(c.Gifts); M(c.Meetings); M((long)c.Consent); M((long)c.State); M(c.WeddingDay); M(c.LastMeetingDay); M(c.AskedDay); M(c.Score); }
        foreach (var u in Upbringings.Values)
        {
            M((long)u.Child); M((long)u.Tutor); M(u.HasTutor ? 1 : 0); M((long)u.Course); M((long)u.TradeSkill); M(u.Hours);
            foreach (var (k, v) in u.Milli) { M(k); M(v); }
        }
        foreach (var (k, v) in Grievances) { M((long)k); M((long)v); }
        foreach (var b in Born) M((long)b);
        if (Ageing is { } a) { M((long)a.Hero); M(a.Row); M(a.LastAge); foreach (int l in a.Lost) M(l); M(a.LastRollYear); M(a.WarnedYear); }
        if (Death is { } d) { M(d.Day); M(d.Row); M(d.Age); M((long)d.Cause); }
        // P6-E3: mixed only once something is there, so a book that never met a match keeps its hash
        if (Matches.Count > 0 || Contested.Count > 0)
        {
            M(NextMatch);
            foreach (var m in Matches.Values) { M(m.Id); M((long)m.Groom); M((long)m.Bride); M(m.DowryMites); M(m.Alliance ? 1 : 0); M(m.WeddingDay); M((long)m.State); M(m.Fief); M(m.Consent ? 1 : 0); M(m.Defied ? 1 : 0); M(m.ProposedDay); M(m.ByHero ? 1 : 0); M(m.Score); M(m.DueSerial); M(m.Lord); }
            foreach (var c in Contested) M((long)c);
        }
        return h;
    }
}
