using MessagePack;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class WoundRecSave
{
    [Key(0)] public uint Id { get; set; } [Key(1)] public short Part { get; set; } [Key(2)] public short Tissue { get; set; } [Key(3)] public short Structure { get; set; } [Key(4)] public short Organ { get; set; }
    [Key(5)] public short Root { get; set; } [Key(6)] public byte Kind { get; set; } [Key(7)] public byte Cat { get; set; } [Key(8)] public byte Grade { get; set; } [Key(9)] public byte Grade0 { get; set; }
    [Key(10)] public byte Count { get; set; } [Key(11)] public byte Residual { get; set; } [Key(12)] public ushort DepthPm { get; set; } [Key(13)] public int BleedX10 { get; set; } [Key(14)] public int Flags { get; set; }
    [Key(15)] public byte State { get; set; } [Key(16)] public byte Dressing { get; set; } [Key(17)] public ushort Aids { get; set; } [Key(18)] public byte Attr { get; set; }
    [Key(19)] public bool Internal { get; set; } [Key(20)] public bool Clots { get; set; } [Key(21)] public long Born { get; set; } [Key(22)] public long DressFromS { get; set; }
    [Key(23)] public int HealX10 { get; set; } [Key(24)] public int NeedX10 { get; set; } [Key(25)] public int PainC { get; set; } [Key(26)] public byte InfectionLevel { get; set; }
    [Key(27)] public int SepsisDay { get; set; } [Key(28)] public int DeathDay { get; set; } [Key(29)] public long StitchedAt { get; set; } [Key(30)] public long DressedAt { get; set; } [Key(31)] public ulong Cause { get; set; }

    public static WoundRecSave Of(in WoundRecord w) => new()
    {
        Id = w.Id, Part = w.Part, Tissue = w.Tissue, Structure = w.Structure, Organ = w.Organ, Root = w.Root, Kind = (byte)w.Kind, Cat = (byte)w.Cat, Grade = w.Grade, Grade0 = w.Grade0, Count = w.Count,
        Residual = w.Residual, DepthPm = w.DepthPm, BleedX10 = w.BleedX10, Flags = (int)w.Flags, State = (byte)w.State, Dressing = (byte)w.Dressing, Aids = (ushort)w.Aids, Attr = (byte)w.Attr,
        Internal = w.Internal, Clots = w.Clots, Born = w.Born, DressFromS = w.DressFromS, HealX10 = w.HealX10, NeedX10 = w.NeedX10, PainC = w.PainC, InfectionLevel = w.InfectionLevel,
        SepsisDay = w.SepsisDay, DeathDay = w.DeathDay, StitchedAt = w.StitchedAt, DressedAt = w.DressedAt, Cause = w.Cause,
    };

    public WoundRecord To() => new()
    {
        Id = Id, Part = Part, Tissue = Tissue, Structure = Structure, Organ = Organ, Root = Root, Kind = (WoundKind)Kind, Cat = (WoundCat)Cat, Grade = Grade, Grade0 = Grade0, Count = Count,
        Residual = Residual, DepthPm = DepthPm, BleedX10 = BleedX10, Flags = (WoundFlags)Flags, State = (WoundState)State, Dressing = (Dressing)Dressing, Aids = (Aid)Aids, Attr = (AttrLoss)Attr,
        Internal = Internal, Clots = Clots, Born = Born, DressFromS = DressFromS, HealX10 = HealX10, NeedX10 = NeedX10, PainC = PainC, InfectionLevel = InfectionLevel,
        SepsisDay = SepsisDay, DeathDay = DeathDay, StitchedAt = StitchedAt, DressedAt = DressedAt, Cause = Cause,
    };
}

[MessagePackObject]
public sealed class WoundBookSave
{
    [Key(0)] public ulong Key { get; set; } [Key(1)] public string PlanId { get; set; } = ""; [Key(2)] public long BloodUl { get; set; } [Key(3)] public long BloodMaxUl { get; set; }
    [Key(4)] public int PainX100 { get; set; } [Key(5)] public int Toughness { get; set; } [Key(6)] public byte State { get; set; } [Key(7)] public byte Down { get; set; } [Key(8)] public long LastSec { get; set; }
    [Key(9)] public long UnconsciousUntilSec { get; set; } [Key(10)] public long HeartAtSec { get; set; } [Key(11)] public long BreathStopSec { get; set; } [Key(12)] public long CalmSince { get; set; }
    [Key(13)] public long DeathSec { get; set; } [Key(14)] public int ConcussedUntilDay { get; set; } [Key(15)] public byte Death { get; set; } [Key(16)] public ulong DeathWound { get; set; }
    [Key(17)] public byte Latch { get; set; } [Key(18)] public uint NextId { get; set; } [Key(19)] public int RegenPm { get; set; }
    [Key(20)] public byte StrengthLoss { get; set; } [Key(21)] public byte DexterityLoss { get; set; } [Key(22)] public byte WitsLoss { get; set; } [Key(23)] public byte SpeechCap { get; set; }
    [Key(24)] public byte Malunions { get; set; } [Key(25)] public byte Concussions { get; set; } [Key(26)] public ulong AmputatedMask { get; set; }
    [Key(27)] public int Conc0 { get; set; } [Key(28)] public int Conc1 { get; set; } [Key(29)] public int Conc2 { get; set; }
    [Key(30)] public WoundRecSave[] Records { get; set; } = [];
}

/// <summary>The "wounds" section: the ledger of wounded people (wounds 3.1). Absent while nobody is hurt; a book is 24-odd bytes a wound before compression.</summary>
[MessagePackObject]
public sealed class WoundSave
{
    public const string SectionName = "wounds";
    [Key(0)] public ulong Seed { get; set; }
    [Key(1)] public long LastDay { get; set; }
    [Key(2)] public WoundBookSave[] Books { get; set; } = [];

    public static WoundSave Capture(WoundLedger l)
    {
        lock (l.Sync)
            return new WoundSave { Seed = l.Seed, LastDay = l.LastDay, Books = [.. l.Books.Select(kv => Of(kv.Key, kv.Value))] };
    }

    /// <summary>One book's save form (also used by the "fight" section for a fight's own books).</summary>
    public static WoundBookSave Of(ulong key, WoundBook b) => new()
    {
        Key = key, PlanId = b.PlanId, BloodUl = b.BloodUl, BloodMaxUl = b.BloodMaxUl, PainX100 = b.PainX100, Toughness = b.Toughness, State = (byte)b.State, Down = (byte)b.Down, LastSec = b.LastSec,
        UnconsciousUntilSec = b.UnconsciousUntilSec, HeartAtSec = b.HeartAtSec, BreathStopSec = b.BreathStopSec, CalmSince = b.CalmSince, DeathSec = b.DeathSec, ConcussedUntilDay = b.ConcussedUntilDay,
        Death = (byte)b.Death, DeathWound = b.DeathWound, Latch = b.Latch, NextId = b.NextId, RegenPm = b.RegenPm,
        StrengthLoss = b.Permanent.StrengthLoss, DexterityLoss = b.Permanent.DexterityLoss, WitsLoss = b.Permanent.WitsLoss, SpeechCap = b.Permanent.SpeechCap, Malunions = b.Permanent.Malunions,
        Concussions = b.Permanent.Concussions, AmputatedMask = b.Permanent.AmputatedMask, Conc0 = b.Permanent.Conc0, Conc1 = b.Permanent.Conc1, Conc2 = b.Permanent.Conc2,
        Records = [.. b.Records.Select(r => WoundRecSave.Of(r))],
    };

    public static WoundBook ToBook(WoundBookSave s, ulong seed)
    {
        var b = new WoundBook(seed, s.Key, 0, s.Toughness, s.PlanId)
        {
            BloodUl = s.BloodUl, BloodMaxUl = s.BloodMaxUl, PainX100 = s.PainX100, State = (Consciousness)s.State, Down = (DownReason)s.Down, LastSec = s.LastSec, UnconsciousUntilSec = s.UnconsciousUntilSec,
            HeartAtSec = s.HeartAtSec, BreathStopSec = s.BreathStopSec, CalmSince = s.CalmSince, DeathSec = s.DeathSec, ConcussedUntilDay = s.ConcussedUntilDay, Death = (DeathKind)s.Death,
            DeathWound = s.DeathWound, Latch = s.Latch, NextId = s.NextId, RegenPm = s.RegenPm,
            Permanent = new PermanentInfo
            {
                StrengthLoss = s.StrengthLoss, DexterityLoss = s.DexterityLoss, WitsLoss = s.WitsLoss, SpeechCap = s.SpeechCap, Malunions = s.Malunions, Concussions = s.Concussions,
                AmputatedMask = s.AmputatedMask, Conc0 = s.Conc0, Conc1 = s.Conc1, Conc2 = s.Conc2,
            },
        };
        foreach (var r in s.Records) b.Records.Add(r.To());
        return b;
    }

    public WoundLedger Restore()
    {
        var l = new WoundLedger(Seed) { LastDay = LastDay };
        foreach (var s in Books) l.Books[s.Key] = ToBook(s, Seed);
        return l;
    }
}
