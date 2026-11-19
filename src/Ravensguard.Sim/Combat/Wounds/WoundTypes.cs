using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Wounds;

// Wounds, bleeding, pain, healing (docs/specs/phase4/wounds-bleeding-pain-healing.md). All integer. Time: a GAME SECOND is the unit of every stored
// timestamp; one sim-second is 20 game seconds (one real second at 1x), one live tick at 1x is one game second, one game hour is 180 sim-seconds.
// Blood is held in milli-millilitres (ml x 1000), a wound's rate in ml/s x 10, pain x 100.

/// <summary>The wound lifecycle (wounds 4.10): open, dressed, stitched, healing, scar; <see cref="Infected"/> is reported by the infection level, kept for the spec's list.</summary>
public enum WoundState : byte { Open, Dressed, Stitched, Healing, Scar, Infected }

public enum Dressing : byte { None, Pressure, Bandage, Tourniquet, Splint }

/// <summary>How the person is: awake, dazed (concussion days), collapsed (30-40% blood lost: down, can crawl and talk), unconscious, dying (unconscious and still bleeding or collapsed by the heart or breath), dead.</summary>
public enum Consciousness : byte { Awake, Dazed, Collapsed, Unconscious, Dying, Dead }

public enum DeathKind : byte { None, BloodLoss, Organ, Brain, Breath, Infection, Ischaemia }

[Flags] public enum DownReason : byte { None = 0, Blood = 1, Pain = 2, Heart = 4, Concussion = 8, Breath = 16 }

/// <summary>What sort of wound it is, for healing and treatment (derived once at creation).</summary>
public enum WoundCat : byte { Skin, Muscle, Bone, Ligament, Tendon, Nerve, Spine, Vessel, Organ, Severed, Concussion, Inset }

/// <summary>Bits of <see cref="WoundRecord.Aids"/>.</summary>
[Flags] public enum Aid : ushort
{
    None = 0, Set = 1, SetFailed = 2, Splinted = 4, TendonTried = 8, Repaired = 16, Dirty = 32, Resolved = 64, Numb = 128, HalfDone = 256, Cauterised = 512, Retried = 1024,
}

/// <summary>Which attribute a malunion lowers (wounds 4.5).</summary>
public enum AttrLoss : byte { None, Strength, Dexterity, Speech }

/// <summary>One wound (wounds 3.1). Mutated in place inside its <see cref="WoundBook"/>.</summary>
public struct WoundRecord
{
    public uint Id;
    public short Part, Tissue, Structure, Organ;
    /// <summary>For a tourniquet: the limb part it was tied on.</summary>
    public short Root;
    public WoundKind Kind;
    public WoundCat Cat;
    /// <summary>A <see cref="MuscleGrade"/>, <see cref="BoneGrade"/> or <see cref="JointGrade"/> by category.</summary>
    public byte Grade, Grade0;
    public byte Count, Residual;
    public ushort DepthPm;
    /// <summary>The rate at the moment of the blow, ml/s x 10 (an internal wound's too).</summary>
    public int BleedX10;
    public WoundFlags Flags;
    public WoundState State;
    public Dressing Dressing;
    public Aid Aids;
    public AttrLoss Attr;
    /// <summary>Internal bleeding (organs): cannot be dressed and never clots. Clots: capillary and muscle cuts.</summary>
    public bool Internal, Clots;
    /// <summary>Game second of the blow.</summary>
    public long Born;
    /// <summary>The sim-second a dressing starts to count from (a pressure hold takes 4 sim-s to bite).</summary>
    public long DressFromS;
    /// <summary>Healing progress and the days needed, both days x 10.</summary>
    public int HealX10, NeedX10;
    /// <summary>Pain at creation, x100.</summary>
    public int PainC;
    public byte InfectionLevel;
    /// <summary>Game day the infection reached sepsis (level 3), and the day it will kill (0 = not set).</summary>
    public int SepsisDay, DeathDay;
    /// <summary>The game second the wound was stitched.</summary>
    public long StitchedAt, DressedAt;
    public ulong Cause;

    public readonly bool Has(WoundFlags f) => (Flags & f) == f;
    public readonly bool Is(Aid a) => (Aids & a) != 0;
    /// <summary>Counts for bleeding: not healed past bleeding, with a rate.</summary>
    public readonly bool Open => BleedX10 > 0 && State is not (WoundState.Scar);
}

/// <summary>Permanent facts of a person (wounds 3.1 <c>Permanent</c>): attribute losses from malunion and repeated concussion, the limbs lost; paralysis and numbness are derived from the nerve wounds, which never heal.</summary>
public struct PermanentInfo
{
    public byte StrengthLoss, DexterityLoss, WitsLoss, SpeechCap;
    public byte Malunions;
    public byte Concussions;
    /// <summary>Bit per part index of limbs the person lost to a tourniquet kept on too long or to a surgeon.</summary>
    public ulong AmputatedMask;
    /// <summary>The game days of the last three concussions.</summary>
    public int Conc0, Conc1, Conc2;
}

/// <summary>A change a caller may want to log (the flavour log, the chronicle).</summary>
public readonly record struct BookEvent(long Sec, BookEventKind Kind, int Part = -1, string Note = "");
public enum BookEventKind : byte { Collapsed, Unconscious, Woke, Died, Infected, InfectionCleared, Scar, Malunion, PermanentLoss, Dazed, Recovered, Amputated, Sepsis }
