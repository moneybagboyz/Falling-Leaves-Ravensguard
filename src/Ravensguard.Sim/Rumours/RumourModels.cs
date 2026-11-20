using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Rumours;

/// <summary>What a rumour is about (dialogue spec 3.5).</summary>
public enum RumourKind : byte { Fire, Raid, Battle, Crime, Death, BirthWedding, Famine, Disease, FloodWeather, TaxLevy, War, RoadBandits, Omen, Landmark, PriceSpike }

/// <summary>How a retelling went wrong (spec 4.6): flags only, never new facts; the rumour keeps the truth.</summary>
[Flags] public enum Distortion : byte { None = 0, Vague = 1, Exaggerated = 2, Misattributed = 4 }

/// <summary>How a person came to hold a rumour.</summary>
public enum HearHow : byte { Witnessed, Told, Overheard, Traveller, Asked }

/// <summary>One rumour: a chronicle entry that people talk about, with what is true of it. <see cref="Id"/> is the source entry's id (one rumour per entry).</summary>
public sealed class Rumour
{
    public EntityId Id;
    /// <summary>The chronicle entry it came from (<see cref="EntityId.None"/> only for a rumour an authored dialogue made up).</summary>
    public EntityId Source;
    public RumourKind Kind;
    public string TypeId = "";
    /// <summary>The en.csv key of the sentence (R_*).</summary>
    public string Key = "";
    /// <summary>The settlement the news is about (an index of the home region), -1 when elsewhere or unknown.</summary>
    public int Settlement = -1;
    /// <summary>The place named in the sentence (a settlement or, for word from afar, the far region).</summary>
    public string Place = "";
    public EntityId[] Subjects = [];
    public byte Severity;
    public int BornHour;
    public int ExpiresDay;
    public int LifeDays;
    public int InterestPm;
    public int Number;
    public string Who = "", About = "";
    /// <summary>Which details are true as told: bit 0 who, 1 where, 2 when, 3 how many.</summary>
    public byte FactsMask = 0xF;
    /// <summary>The people who saw it first (or first heard it from a traveller): the roots of every trace.</summary>
    public int[] Witnesses = [];
    public bool FromAfar;
    /// <summary>Told by a dialogue's <c>add_rumour</c> with no entry behind it.</summary>
    public bool Authored;
    /// <summary>P6-C2 (rumours v2, slander): a lie. The rumour spreads like any other; it moves its victim's standing while believed and is shown false when exposed. Saved in the high bit of the facts byte, so older saves keep their bytes.</summary>
    public bool False;
    public int BornDay => BornHour / 24;
}

/// <summary>One person's holding of a rumour (spec 3.5): who told them, when, how many retellings it had passed through, and what went wrong on the way.</summary>
public struct Hearing
{
    public int Listener;
    /// <summary>The person index who told it, -1 for a witness.</summary>
    public int Teller;
    public int Hour;
    public byte Hop;
    public Distortion Flags;
    /// <summary>The settlement a misattributed retelling names instead, -1 none.</summary>
    public short PlaceAlt;
    public HearHow How;
    /// <summary>Held but no longer told (the person's six best were kept); still part of every trace.</summary>
    public bool Dropped;
    public readonly int Day => Hour / 24;
}

/// <summary>One step of a trace, newest first: a person, the hour, the hop, and how they came to know.</summary>
public readonly record struct TraceStep(int Person, int Hour, int Hop, HearHow How, int Teller);

/// <summary>A rumour the hero holds (the Journal's card): who told them, the chain behind it at the time, and what was said of it.</summary>
public sealed class HeroHearing
{
    public EntityId Rumour;
    public int Hour;
    public int Teller = -1;
    public string TellerName = "";
    public HearHow How;
    public int Hop;
    public Distortion Flags;
    public short PlaceAlt = -1;
    /// <summary>The chain from the teller back to the root, as it stood when the hero heard (names fixed at that time).</summary>
    public List<TraceStep> Chain = [];
    public List<string> ChainNames = [];
    public int Day => Hour / 24;
}

/// <summary>A rumour passed between two people in a gathering this hour (what the hero may overhear).</summary>
public readonly record struct Pass(int Hour, int Settlement, int Teller, int Listener, EntityId Rumour, int Venue);
