using MessagePack;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Save;

/// <summary>One unit of a side's reserve or of the people entering at the edge (see <see cref="BattleUnit"/>): the kits of its soldiers as rows (the fight's own rows hold the state of those already in it) and the fighters entered so far.</summary>
[MessagePackObject]
public sealed class BattleUnitRow
{
    [Key(0)] public int Kind { get; set; } [Key(1)] public int Shape { get; set; } [Key(2)] public int Ranks { get; set; } [Key(3)] public int Drill { get; set; }
    [Key(4)] public FighterRow[] Soldiers { get; set; } = [];
    [Key(5)] public int[] Officers { get; set; } = []; [Key(6)] public int Banner { get; set; } = -1; [Key(7)] public int[] Leaders { get; set; } = [];
    [Key(8)] public int Wave { get; set; } [Key(9)] public int AnchorX { get; set; } [Key(10)] public int AnchorY { get; set; } [Key(11)] public int FormId { get; set; }
    [Key(12)] public int[] Ids { get; set; } = [];
}

[MessagePackObject]
public sealed class BattleSideRow
{
    [Key(0)] public int Team { get; set; } [Key(1)] public int Realm { get; set; } [Key(2)] public ulong Commander { get; set; } [Key(3)] public ulong HostId { get; set; } [Key(4)] public int Kind { get; set; }
    /// <summary>LiveCap, Strength, WavesSent, EntryX, EntryY, FaceX, FaceY, NextForm, LastWaveTick, Human.</summary>
    [Key(5)] public long[] State { get; set; } = [];
    [Key(6)] public BattleUnitRow[] Reserve { get; set; } = [];
    [Key(7)] public BattleUnitRow[] Pending { get; set; } = [];
}

/// <summary>
/// The optional <c>battle</c> section (battle-bubble-and-formations 7; no version bump): written only while a battle bubble is open, so a world that never fought keeps its bytes. It holds what the fight record
/// does not: the box, the sides with their reserves and waves, the people entering, the close bookkeeping and the named captains. The fighters, their wounds, the formation book and its orders are the fight's own
/// record (the <c>fight</c> section). A save, a load and a continue equal the uninterrupted battle; a save, a load and a save give the same bytes.
/// </summary>
[MessagePackObject]
public sealed class BattleRecord
{
    public const string SectionName = "battle";
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public long OriginX { get; set; } [Key(2)] public long OriginY { get; set; }
    /// <summary>The box: x0, y0, x1, y1 (mm).</summary>
    [Key(3)] public long[] Box { get; set; } = [];
    [Key(4)] public int CapTotal { get; set; }
    [Key(5)] public int CauseKind { get; set; } [Key(6)] public ulong CauseEntry { get; set; } [Key(7)] public string CauseNote { get; set; } = ""; [Key(8)] public ulong Place { get; set; }
    [Key(9)] public BattleSideRow[] Sides { get; set; } = [];
    /// <summary>StartTick, ContactSeen, LastEngaged, Hero, then the AI level-of-detail centre (has, x, y).</summary>
    [Key(10)] public long[] State { get; set; } = [];
    [Key(11)] public int[] NamedCaptains { get; set; } = [];

    public byte[] ToBytes() => SaveCodec.SerializeCompressed(this);
    public static BattleRecord FromBytes(byte[] b) => SaveCodec.DeserializeCompressed<BattleRecord>(b);

    // ---- a kit as a row and back (the fight record does the same for fighters; a reserve soldier is only a kit) ----

    public static FighterRow RowOf(FighterKit k) => new()
    {
        Name = k.Name, Team = k.Team, Person = k.Person, Player = false,
        WeaponShape = k.Weapon?.ShapeId ?? "", WeaponMaterial = k.Weapon?.MaterialId ?? "", WeaponQuality = (byte)(k.Weapon?.Quality ?? Goods.ItemQuality.Common), Shield = k.Shield ?? "",
        WornShapes = [.. k.Worn.Select(x => x.ShapeId)], WornMaterials = [.. k.Worn.Select(x => x.MaterialId)], WornQualities = [.. k.Worn.Select(x => (byte)x.Quality)],
        Launcher = k.Launcher ?? "", Ammo = k.Ammo ?? "", AmmoCount = k.AmmoCount,
        ThrowShapes = [.. k.Throwables.Select(t => t.Shape)], ThrowMaterials = [.. k.Throwables.Select(t => t.Material)], ThrowCounts = [.. k.Throwables.Select(t => t.Count)], ThrowImprovised = [.. k.Throwables.Select(t => t.Improvised)],
        Improvised = k.Improvised ?? "",
        Stats = [k.Skill, k.ShieldSkill, k.ThrowSkill, k.ArcherySkill, k.Strength, k.Toughness, k.SizeCm3, k.Courage, k.X, k.Y, k.FaceX, k.FaceY, k.Tactics],
        Stance = k.Stance, Plan = k.Plan, Blunted = k.Blunted,
    };

    public static FighterKit KitOf(FighterRow r)
    {
        var st = r.Stats;
        return new FighterKit
        {
            Name = r.Name, Team = r.Team, Person = r.Person,
            Weapon = r.WeaponShape.Length > 0 ? new WeaponInstance(r.WeaponShape, r.WeaponMaterial, (Goods.ItemQuality)r.WeaponQuality) : null,
            Shield = r.Shield.Length > 0 ? r.Shield : null,
            Worn = [.. r.WornShapes.Select((s, i) => new WornItem(s, r.WornMaterials[i], (Goods.ItemQuality)r.WornQualities[i]))],
            Launcher = r.Launcher.Length > 0 ? r.Launcher : null, Ammo = r.Ammo.Length > 0 ? r.Ammo : null, AmmoCount = r.AmmoCount,
            Throwables = [.. r.ThrowShapes.Select((s, i) => new ThrowStock(s, r.ThrowMaterials[i], r.ThrowCounts[i], r.ThrowImprovised[i]))],
            Improvised = r.Improvised.Length > 0 ? r.Improvised : null,
            Skill = st[0], ShieldSkill = st[1], ThrowSkill = st[2], ArcherySkill = st[3], Strength = st[4], Toughness = st[5], SizeCm3 = st[6], Courage = st[7],
            X = st[8], Y = st[9], FaceX = st[10], FaceY = st[11], Stance = r.Stance, Plan = r.Plan, Blunted = r.Blunted, Tactics = st[12],
        };
    }

    public static BattleUnitRow RowOf(BattleUnit u) => new()
    {
        Kind = (int)u.Kind, Shape = (int)u.Shape, Ranks = u.Ranks, Drill = u.Drill, Soldiers = [.. u.Soldiers.Select(RowOf)], Officers = u.Officers, Banner = u.Banner, Leaders = u.Leaders,
        Wave = u.Wave, AnchorX = u.AnchorX, AnchorY = u.AnchorY, FormId = u.FormId, Ids = [.. u.Ids],
    };

    public static BattleUnit UnitOf(BattleUnitRow r) => new()
    {
        Kind = (FormationKind)r.Kind, Shape = (Formation)r.Shape, Ranks = r.Ranks, Drill = r.Drill, Soldiers = [.. r.Soldiers.Select(KitOf)], Officers = r.Officers, Banner = r.Banner, Leaders = r.Leaders,
        Wave = r.Wave, AnchorX = r.AnchorX, AnchorY = r.AnchorY, FormId = r.FormId, Ids = [.. r.Ids],
    };

    /// <summary>The record of an open battle bubble (null once it has closed).</summary>
    public static BattleRecord? Capture(BattleBubble b)
    {
        if (b.Closed) return null;
        if (b.W.Ledger is { } l) lock (l.Sync) return CaptureCore(b);
        return CaptureCore(b);
    }

    private static BattleRecord CaptureCore(BattleBubble b) => new()
    {
        Id = b.Id, OriginX = b.OriginMm.X, OriginY = b.OriginMm.Y, Box = [b.Box.X0, b.Box.Y0, b.Box.X1, b.Box.Y1], CapTotal = b.CapTotal,
        CauseKind = (int)b.CauseKind, CauseEntry = b.CauseEntry.Value, CauseNote = b.CauseNoteKey, Place = b.Place.Value,
        Sides = [.. b.Sides.Select(s => new BattleSideRow
        {
            Team = s.Team, Realm = s.Realm, Commander = s.Commander, HostId = s.HostId, Kind = (int)s.Kind,
            State = [s.LiveCap, s.Strength, s.WavesSent, s.EntryX, s.EntryY, s.FaceX, s.FaceY, s.NextForm, s.LastWaveTick, s.Human ? 1 : 0],
            Reserve = [.. s.Reserve.Select(RowOf)], Pending = [.. s.Pending.Select(RowOf)],
        })],
        State = [b.StartTick, b.ContactSeen ? 1 : 0, b.LastEngaged, b.Hero, b.W.LodCentre.HasValue ? 1 : 0, b.W.LodCentre?.X ?? 0, b.W.LodCentre?.Y ?? 0, .. (b.WaveUnderPm != 0 ? new long[] { b.WaveUnderPm } : [])],
        NamedCaptains = [.. b.NamedCaptains],
    };
}
