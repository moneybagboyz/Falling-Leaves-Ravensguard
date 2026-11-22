using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Save;

/// <summary>One fighter of a <see cref="FightRecord"/>: the kit it came in with, what it holds now, and every integer of its state (see <see cref="FightRecord.Pack"/> for the order).</summary>
[MessagePackObject]
public sealed class FighterRow
{
    [Key(0)] public string Name { get; set; } = ""; [Key(1)] public int Team { get; set; } [Key(2)] public ulong Person { get; set; } [Key(3)] public bool Player { get; set; }
    [Key(4)] public string WeaponShape { get; set; } = ""; [Key(5)] public string WeaponMaterial { get; set; } = ""; [Key(6)] public byte WeaponQuality { get; set; }
    [Key(7)] public string Shield { get; set; } = ""; [Key(8)] public string[] WornShapes { get; set; } = []; [Key(9)] public string[] WornMaterials { get; set; } = []; [Key(10)] public byte[] WornQualities { get; set; } = [];
    [Key(11)] public string Launcher { get; set; } = ""; [Key(12)] public string Ammo { get; set; } = ""; [Key(13)] public int AmmoCount { get; set; }
    [Key(14)] public string[] ThrowShapes { get; set; } = []; [Key(15)] public string[] ThrowMaterials { get; set; } = []; [Key(16)] public int[] ThrowCounts { get; set; } = []; [Key(17)] public bool[] ThrowImprovised { get; set; } = [];
    [Key(18)] public string Improvised { get; set; } = ""; [Key(19)] public int[] Stats { get; set; } = [];
    [Key(20)] public string Stance { get; set; } = ""; [Key(21)] public string Plan { get; set; } = ""; [Key(22)] public bool Blunted { get; set; }
    // what the fighter holds now (a broken stool, a thrown javelin, a broken shield change it)
    [Key(23)] public string NowShape { get; set; } = ""; [Key(24)] public string NowMaterial { get; set; } = ""; [Key(25)] public byte NowQuality { get; set; }
    [Key(26)] public string NowImprovised { get; set; } = ""; [Key(27)] public bool HasShield { get; set; }
    [Key(28)] public string[] NowThrowShapes { get; set; } = []; [Key(29)] public string[] NowThrowMaterials { get; set; } = []; [Key(30)] public int[] NowThrowCounts { get; set; } = []; [Key(31)] public bool[] NowThrowImprovised { get; set; } = [];
    [Key(32)] public long[] State { get; set; } = [];
    /// <summary>The fighter's own wound book (a fight without the world's ledger); null when the book is the ledger's (saved in the "wounds" section).</summary>
    [Key(33)] public WoundBookSave? Book { get; set; }
}

[MessagePackObject]
public sealed class ProjectileRow
{
    [Key(0)] public long[] State { get; set; } = [];
    [Key(1)] public string Ammo { get; set; } = ""; [Key(2)] public string Shape { get; set; } = ""; [Key(3)] public string Material { get; set; } = "";
}

[MessagePackObject]
public sealed class GroundRow
{
    [Key(0)] public string Shape { get; set; } = ""; [Key(1)] public string Material { get; set; } = ""; [Key(2)] public int X { get; set; } [Key(3)] public int Y { get; set; } [Key(4)] public bool Damaged { get; set; } [Key(5)] public bool Ammo { get; set; }
}

/// <summary>
/// A fight in progress (overview 8.3, the "fight" section, saved only mid-fight): the fight's id, seed, start second and <c>FightTick</c>, every fighter (kit, holdings, position in mm,
/// facing Q10, action and timers, request, stamina, morale, stance, the AI's memory and the held inputs) and the projectiles in flight. The RNG needs nothing (counter based), so a
/// load resumes the fight at the same tick and its <c>FightHash</c> goes on exactly as it would have (test OV-3); a save, a load and a save again give the same bytes.
/// </summary>
[MessagePackObject]
public sealed class FightRecord
{
    public const string SectionName = "fight";
    [Key(0)] public ulong Seed { get; set; } [Key(1)] public ulong FightId { get; set; } [Key(2)] public long GameSec0 { get; set; } [Key(3)] public int Tick { get; set; }
    [Key(4)] public FighterRow[] Fighters { get; set; } = [];
    [Key(5)] public ProjectileRow[] Projectiles { get; set; } = [];
    [Key(6)] public GroundRow[] Ground { get; set; } = [];
    [Key(7)] public string[] SpentAmmoIds { get; set; } = []; [Key(8)] public int[] SpentAmmoCounts { get; set; } = [];
    [Key(9)] public bool UsesLedger { get; set; }
    /// <summary>What started it (FightSetup kind) and the encounter id, so the game can settle it when it ends.</summary>
    [Key(10)] public string Kind { get; set; } = ""; [Key(11)] public string Cause { get; set; } = "";
    /// <summary>P4-D (save v7): each fighter's morale state in id order (<see cref="Ravensguard.Sim.Combat.Morale.MoraleState.Pack"/>); null in a v6 record (the model starts afresh).</summary>
    [Key(20)] public long[][]? Morale { get; set; }
    /// <summary>P4-F (watch mode, no format bump): the watch state, the sides' orders and formations, the always-near fighter, and per fighter the guarded person, the treat timer
    /// and the sheathed flag; null in a fight nobody watched, ordered or gave a stance (the extras are then all default).</summary>
    [Key(21)] public WatchRow? Watch { get; set; }
    /// <summary>Close-out item 4 (no format bump): the worn pieces' damage, per fighter in id order: the piece count, then condition, breach mask and broken strap of each piece;
    /// null when every piece of every fighter is untouched.</summary>
    [Key(22)] public long[]? Armour { get; set; }
    /// <summary>Close-out item 1 (no format bump): per fighter in id order, the tick it stays inside a longer weapon's reach and whose; null when nobody is or was.</summary>
    [Key(23)] public long[]? Inside { get; set; }
    /// <summary>P4 close-out (no format bump): the fight's weather row by its state name; "" for a fight without one (older records).</summary>
    [Key(24)] public string Weather { get; set; } = "";
    /// <summary>P7 BB-B (no format bump): the fight's formation book (<see cref="Ravensguard.Sim.Battle.FormationBook.Pack"/>); null for a fight with no formations, so a fight that never formed one keeps its bytes.</summary>
    [Key(25)] public long[]? Formations { get; set; }

    // ---------------------------------------------------------------- capture

    /// <summary>The record of a fight as it stands. A fight on the world's ledger is read under the ledger's lock, which the live step holds (<see cref="FightWorld.StepShared"/>), so a
    /// save made on the tick worker mid-fight reads one whole tick (review finding 1).</summary>
    public static FightRecord Capture(FightWorld w, string kind = "", string cause = "")
    {
        if (w.Ledger is { } l) lock (l.Sync) return CaptureCore(w, kind, cause);
        return CaptureCore(w, kind, cause);
    }

    private static FightRecord CaptureCore(FightWorld w, string kind, string cause)
    {
        if (kind.Length == 0 && w.SetupKind is { } sk) kind = FightSetup.NameOf(sk);    // P4-F: the setup's kind, so the rules come back (the session recorder captures without one)
        var r = new FightRecord { Seed = w.Seed, FightId = w.FightId, GameSec0 = w.GameSec0, Tick = w.Tick, UsesLedger = w.Ledger != null, Kind = kind, Cause = cause, Weather = w.WeatherRow?.State ?? "" };
        r.Fighters = [.. w.Fighters.Select(f => Row(w, f))];
        r.Projectiles = [.. w.Projectiles.Select(p => new ProjectileRow
        {
            State = [p.Serial, p.Shooter, p.Team, p.X0, p.Y0, p.Z0, p.DirX, p.DirY, p.SlopePpm, p.StepMm, p.RangeMm, p.Flown, p.ApexMm, p.PlanMm, p.Thrown ? 1 : 0, p.Improvised ? 1 : 0,
                p.V0Mmps, p.PowerPm, p.MassG, p.AimPart, p.Done ? 1 : 0],
            Ammo = p.Ammo, Shape = p.Shape, Material = p.Material,
        })];
        r.Ground = [.. w.Ground.Select(g => new GroundRow { Shape = g.Shape, Material = g.Material, X = g.X, Y = g.Y, Damaged = g.Damaged, Ammo = g.Ammo })];
        r.Morale = w.CaptureMorale();                                         // P4-D hook
        r.Formations = w.Formations?.Pack();                                  // P7 BB-B hook
        r.Watch = WatchRow.Capture(w);                                        // P4-F hook
        if (w.Fighters.Any(f => f.InsideUntil != -1 || f.InsideOf != -1)) r.Inside = [.. w.Fighters.SelectMany(f => new long[] { f.InsideUntil, f.InsideOf })];
        if (w.Fighters.Any(f => f.Worn.Any(x => !x.Pristine)))
            r.Armour = [.. w.Fighters.SelectMany(f => f.Worn.SelectMany(x => new long[] { x.ConditionPm, (long)x.BreachMask, x.Slipped ? 1 : 0 }).Prepend(f.Worn.Length))];
        var spent = w.SpentAmmo.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray();
        r.SpentAmmoIds = [.. spent.Select(kv => kv.Key)]; r.SpentAmmoCounts = [.. spent.Select(kv => kv.Value)];
        return r;
    }

    private static FighterRow Row(FightWorld w, Fighter f)
    {
        var k = f.Kit;
        bool ownBook = w.Ledger == null || !f.LedgerBook;
        return new FighterRow
        {
            Name = k.Name, Team = k.Team, Person = k.Person, Player = f.Player,
            WeaponShape = k.Weapon?.ShapeId ?? "", WeaponMaterial = k.Weapon?.MaterialId ?? "", WeaponQuality = (byte)(k.Weapon?.Quality ?? Goods.ItemQuality.Common), Shield = k.Shield ?? "",
            WornShapes = [.. k.Worn.Select(x => x.ShapeId)], WornMaterials = [.. k.Worn.Select(x => x.MaterialId)], WornQualities = [.. k.Worn.Select(x => (byte)x.Quality)],
            Launcher = k.Launcher ?? "", Ammo = k.Ammo ?? "", AmmoCount = k.AmmoCount,
            ThrowShapes = [.. k.Throwables.Select(t => t.Shape)], ThrowMaterials = [.. k.Throwables.Select(t => t.Material)], ThrowCounts = [.. k.Throwables.Select(t => t.Count)], ThrowImprovised = [.. k.Throwables.Select(t => t.Improvised)],
            Improvised = k.Improvised ?? "", Stats = k.Tactics >= 0 ? [k.Skill, k.ShieldSkill, k.ThrowSkill, k.ArcherySkill, k.Strength, k.Toughness, k.SizeCm3, k.Courage, k.X, k.Y, k.FaceX, k.FaceY, k.Tactics]
                : [k.Skill, k.ShieldSkill, k.ThrowSkill, k.ArcherySkill, k.Strength, k.Toughness, k.SizeCm3, k.Courage, k.X, k.Y, k.FaceX, k.FaceY],
            Stance = k.Stance, Plan = k.Plan, Blunted = k.Blunted,
            NowShape = f.Weapon.Shape.Id, NowMaterial = f.Weapon.Head.Id, NowQuality = (byte)f.Weapon.Quality, NowImprovised = f.Improvised?.Id ?? "", HasShield = f.Shield != null,
            NowThrowShapes = [.. f.Throwables.Select(t => t.Shape)], NowThrowMaterials = [.. f.Throwables.Select(t => t.Material)], NowThrowCounts = [.. f.Throwables.Select(t => t.Count)], NowThrowImprovised = [.. f.Throwables.Select(t => t.Improvised)],
            State = w.Mounts == null ? Pack(f) : [.. Pack(f), .. w.MountedTail(f)],      // [mounted-combat] a mounted fight's layer rides at the end of each row
            Book = ownBook ? WoundSave.Of(f.Book.Key, f.Book) : null,
        };
    }

    /// <summary>Every integer of a fighter's live state, in one fixed order (<see cref="Unpack"/> reads it back).</summary>
    public static long[] Pack(Fighter f)
    {
        var c = f.Cmd; var m = f.Brain; var q = f.Req;
        long b = (f.Heavy ? 1 : 0) | (f.Riposte ? 2 : 0) | (f.Loaded ? 4 : 0) | (f.WeaponArm ? 8 : 0) | (f.ShieldArm ? 16 : 0) | (f.CanStand ? 32 : 0) | (f.OneEye ? 64 : 0) | (f.Crawling ? 128 : 0)
            | (f.Routing ? 256 : 0) | (f.Announced ? 512 : 0) | (q.Auto ? 1024 : 0) | (c.Sprint ? 2048 : 0) | (c.Block ? 4096 : 0) | (c.Hold ? 8192 : 0) | (f.FmSteer ? 16384 : 0);   // P7 BB-C: the formation steering flag of the last think
        return
        [
            b, f.ShieldPm, f.ArmourG, f.OverKg, f.Ammo, f.ImprovisedPm, f.ImprovisedBlows, f.Skill, f.X, f.Y, f.FX, f.FY, f.BraceX, f.BraceY, f.BraceSince, f.UnbracedUntil, f.LastMoveMm,
            (long)f.Act, f.ActTicks, f.WindNeeded, f.WindElapsed, f.WindStart, q.Attack, q.Target, q.Part, q.Gap, (long)q.Style, (long)q.Kind, f.Serial, f.ImpactAt, f.Stamina,
            f.DodgeReady, f.DodgeX, f.DodgeY, f.BlockSince, f.RiposteUntil, f.FeintReady, f.ParryDeadUntil, f.DrawElapsed, f.AimSince, f.ReloadLeft, f.ShotSerial, f.ThrowTargetX, f.ThrowTargetY,
            f.WalkPm, f.StrengthPm, f.BlowPm, (long)f.MissingMask, f.WoundsSeen, f.Morale, f.MoralePenalty, f.FlankedUntil, f.EndedAt,
            f.Thrown, f.Landed, f.BlockedBy, f.ParriedBy, f.DodgedBy, f.Whiffs,
            m.Target, m.NextThink, m.AttackAfter, m.ReactSerialFoe, m.ReactSerial, m.ReactAt, m.HoldBlockUntil, m.FeintChecked, m.React, m.PatienceSet,
            c.MoveX, c.MoveY, c.FaceX, c.FaceY, f.Player ? 1 : 0,
        ];
    }

    public static void Unpack(Fighter f, long[] s)
    {
        int i = 0;
        long b = s[i++];
        f.Heavy = (b & 1) != 0; f.Riposte = (b & 2) != 0; f.Loaded = (b & 4) != 0; f.WeaponArm = (b & 8) != 0; f.ShieldArm = (b & 16) != 0; f.CanStand = (b & 32) != 0; f.OneEye = (b & 64) != 0;
        f.Crawling = (b & 128) != 0; f.Routing = (b & 256) != 0; f.Announced = (b & 512) != 0;
        f.ShieldPm = (int)s[i++]; f.ArmourG = (int)s[i++]; f.OverKg = (int)s[i++]; f.Ammo = (int)s[i++]; f.ImprovisedPm = (int)s[i++]; f.ImprovisedBlows = (int)s[i++]; f.Skill = (int)s[i++];
        f.X = (int)s[i++]; f.Y = (int)s[i++]; f.FX = (int)s[i++]; f.FY = (int)s[i++]; f.BraceX = (int)s[i++]; f.BraceY = (int)s[i++]; f.BraceSince = (int)s[i++]; f.UnbracedUntil = (int)s[i++]; f.LastMoveMm = (int)s[i++];
        f.Act = (FAct)s[i++]; f.ActTicks = (int)s[i++]; f.WindNeeded = (int)s[i++]; f.WindElapsed = (int)s[i++]; f.WindStart = (int)s[i++];
        f.Req = new AttackReq { Attack = (int)s[i++], Target = (int)s[i++], Part = (int)s[i++], Gap = (int)s[i++], Style = (SwingStyle)s[i++], Kind = (BlowKind)s[i++], Auto = (b & 1024) != 0 };
        f.Serial = (uint)s[i++]; f.ImpactAt = (int)s[i++]; f.Stamina = (int)s[i++];
        f.DodgeReady = (int)s[i++]; f.DodgeX = (int)s[i++]; f.DodgeY = (int)s[i++]; f.BlockSince = (int)s[i++]; f.RiposteUntil = (int)s[i++]; f.FeintReady = (int)s[i++]; f.ParryDeadUntil = (int)s[i++];
        f.DrawElapsed = (int)s[i++]; f.AimSince = (int)s[i++]; f.ReloadLeft = (int)s[i++]; f.ShotSerial = (uint)s[i++]; f.ThrowTargetX = (int)s[i++]; f.ThrowTargetY = (int)s[i++];
        f.WalkPm = (int)s[i++]; f.StrengthPm = (int)s[i++]; f.BlowPm = (int)s[i++]; f.MissingMask = (ulong)s[i++]; f.WoundsSeen = (int)s[i++];
        f.Morale = (int)s[i++]; f.MoralePenalty = (int)s[i++]; f.FlankedUntil = (int)s[i++]; f.EndedAt = (int)s[i++];
        f.Thrown = (int)s[i++]; f.Landed = (int)s[i++]; f.BlockedBy = (int)s[i++]; f.ParriedBy = (int)s[i++]; f.DodgedBy = (int)s[i++]; f.Whiffs = (int)s[i++];
        f.Brain = new BrainMemory
        {
            Target = (int)s[i++], NextThink = (int)s[i++], AttackAfter = (int)s[i++], ReactSerialFoe = (int)s[i++], ReactSerial = (int)s[i++], ReactAt = (int)s[i++], HoldBlockUntil = (int)s[i++],
            FeintChecked = (int)s[i++], React = (byte)s[i++], PatienceSet = (int)s[i++],
        };
        var c = f.Cmd; c.ClearAll();
        c.MoveX = (int)s[i++]; c.MoveY = (int)s[i++]; c.FaceX = (int)s[i++]; c.FaceY = (int)s[i++]; f.Player = s[i++] != 0;
        c.Sprint = (b & 2048) != 0; c.Block = (b & 4096) != 0; c.Hold = (b & 8192) != 0;
        f.FmSteer = (b & 16384) != 0;
    }

    // ---------------------------------------------------------------- restore

    /// <summary>Rebuilds the fight: fighters from their kits, then their saved state over the top; the books from the record (or the world's ledger).</summary>
    public FightWorld Restore(FightContent content, WoundLedger? ledger = null)
    {
        var w = new FightWorld(content, Seed, FightId, GameSec0, UsesLedger ? ledger : null);
        w.RestoreTick(Tick);
        if (FightSetup.KindOf(Kind) is { } fk) { w.Rules = FightRules.For(fk); w.SetupKind = fk; }   // P4-F: the kind's rules (practice hits, the tournament yield, treating)
        foreach (var r in Fighters)
        {
            var st = r.Stats;
            var kit = new FighterKit
            {
                Name = r.Name, Team = r.Team, Person = r.Person,
                Weapon = r.WeaponShape.Length > 0 ? new WeaponInstance(r.WeaponShape, r.WeaponMaterial, (Goods.ItemQuality)r.WeaponQuality) : null,
                Shield = r.Shield.Length > 0 ? r.Shield : null,
                Worn = [.. r.WornShapes.Select((s, i) => new WornItem(s, r.WornMaterials[i], (Goods.ItemQuality)r.WornQualities[i]))],
                Launcher = r.Launcher.Length > 0 ? r.Launcher : null, Ammo = r.Ammo.Length > 0 ? r.Ammo : null, AmmoCount = r.AmmoCount,
                Throwables = [.. r.ThrowShapes.Select((s, i) => new ThrowStock(s, r.ThrowMaterials[i], r.ThrowCounts[i], r.ThrowImprovised[i]))],
                Improvised = r.Improvised.Length > 0 ? r.Improvised : null,
                Skill = st[0], ShieldSkill = st[1], ThrowSkill = st[2], ArcherySkill = st[3], Strength = st[4], Toughness = st[5], SizeCm3 = st[6], Courage = st[7],
                X = st[8], Y = st[9], FaceX = st[10], FaceY = st[11], Stance = r.Stance, Plan = r.Plan, Blunted = r.Blunted, Tactics = st.Length > 12 ? st[12] : -1,
            };
            var f = w.Add(kit, r.Player);
            if (r.Book != null) { f.Book = WoundSave.ToBook(r.Book, Seed); f.LedgerBook = false; }
            else if (ledger != null && UsesLedger && !f.LedgerBook)
            {
                // a ledger book saved in the "wounds" section for a fighter the kit alone does not mark (the hero, id 0, handed to the AI by watch mode when saved)
                f.Book = ledger.For(kit.Person, f.Plan, w.GameSec, kit.SizeCm3, kit.Toughness); f.LedgerBook = true;
            }
            // what is in hand now
            if (r.NowImprovised.Length > 0) { f.Improvised = content.Improvised[r.NowImprovised]; f.Weapon = content.ImprovisedWeapon(f.Improvised); }
            else { f.Improvised = null; f.Weapon = content.T.Weapon(r.NowShape, r.NowMaterial, (Goods.ItemQuality)r.NowQuality); }
            var sh = f.Weapon.Shape;
            f.Family = FightWorld.FamilyOf(sh); f.ReachMm = Math.Max(500, sh.ReachMm); f.StrikeTicks = Math.Max(1, sh.StrikeTicks);
            f.BaseWindTicks = FightWorld.TicksOf(sh.WindUpS); f.BaseRecoverTicks = FightWorld.TicksOf(sh.RecoverS);
            if (!r.HasShield) f.Shield = null;
            f.Throwables.Clear();
            for (int i = 0; i < r.NowThrowShapes.Length; i++) f.Throwables.Add(new ThrowStock(r.NowThrowShapes[i], r.NowThrowMaterials[i], r.NowThrowCounts[i], r.NowThrowImprovised[i]));
            Unpack(f, r.State);
        }
        w.RestoreMounted([.. Fighters.Select(r => r.State)]);          // [mounted-combat] nothing for a foot fight
        foreach (var r in Projectiles)
        {
            var s = r.State;
            w.Projectiles.Add(new Projectile
            {
                Serial = (uint)s[0], Shooter = (int)s[1], Team = (int)s[2], X0 = (int)s[3], Y0 = (int)s[4], Z0 = (int)s[5], DirX = (int)s[6], DirY = (int)s[7], SlopePpm = (int)s[8], StepMm = (int)s[9],
                RangeMm = (int)s[10], Flown = s[11], ApexMm = (int)s[12], PlanMm = (int)s[13], Thrown = s[14] != 0, Improvised = s[15] != 0, V0Mmps = (int)s[16], PowerPm = (int)s[17], MassG = (int)s[18],
                AimPart = (int)s[19], Done = s[20] != 0, Ammo = r.Ammo, Shape = r.Shape, Material = r.Material,
            });
        }
        foreach (var g in Ground) w.Ground.Add(new GroundItem(g.Shape, g.Material, g.X, g.Y, g.Damaged, g.Ammo));
        for (int i = 0; i < SpentAmmoIds.Length; i++) w.SpentAmmo[SpentAmmoIds[i]] = SpentAmmoCounts[i];
        w.RestoreMorale(Morale);                                              // P4-D hook
        w.RestoreFormations(Formations);                                      // P7 BB-B hook
        Watch?.Restore(w);                                                    // P4-F hook
        if (Inside != null)
            for (int i = 0; i < w.Fighters.Count && 2 * i + 1 < Inside.Length; i++) { w.Fighters[i].InsideUntil = (int)Inside[2 * i]; w.Fighters[i].InsideOf = (int)Inside[2 * i + 1]; }
        if (Armour != null)
        {
            int i = 0;
            foreach (var f in w.Fighters)
            {
                if (i >= Armour.Length) break;
                int n = (int)Armour[i++];
                for (int k = 0; k < n && i + 2 < Armour.Length; k++, i += 3)
                    if (k < f.Worn.Length) { f.Worn[k].ConditionPm = (int)Armour[i]; f.Worn[k].BreachMask = (ulong)Armour[i + 1]; f.Worn[k].Slipped = Armour[i + 2] != 0; }
            }
        }
        w.WeatherRow = FightWorld.WeatherRowOf(content.Db, Weather);          // P4 close-out: the weather row
        return w;
    }

    public byte[] ToBytes() => SaveCodec.SerializeCompressed(this);
    public static FightRecord FromBytes(byte[] b) => SaveCodec.DeserializeCompressed<FightRecord>(b);
}

/// <summary>The watch extras of a saved fight (P4-F): see <see cref="FightRecord.Watch"/>.</summary>
[MessagePackObject]
public sealed class WatchRow
{
    [Key(0)] public long[]? State { get; set; }
    [Key(1)] public long[] Orders { get; set; } = [];
    [Key(2)] public int LodFighter { get; set; } = -1;
    /// <summary>Per fighter in id order: the guarded fighter + 1, the treat timer, sheathed (three longs each).</summary>
    [Key(3)] public long[] Fighters { get; set; } = [];
    /// <summary>Per fighter: the stance the brain reads, packed (<see cref="Ravensguard.Sim.Combat.Watch.WatchStance.PackA"/> of its row, custom bit set when it is not the kit's).</summary>
    [Key(4)] public long[] Stances { get; set; } = [];

    public static WatchRow? Capture(FightWorld w)
    {
        var orders = w.PackOrders();
        bool extras = w.Fighters.Any(f => f.ProtectFighter >= 0 || f.TreatBusyUntil != 0 || f.Sheathed || !ReferenceEquals(f.Stance, w.Content.Stance(f.Kit.Stance)));
        if (w.Watch == null && orders.Length == 0 && w.LodFighter < 0 && !extras) return null;
        var row = new WatchRow { State = w.Watch?.Pack(), Orders = orders, LodFighter = w.LodFighter };
        if (extras)
        {
            row.Fighters = [.. w.Fighters.SelectMany(f => new long[] { f.ProtectFighter + 1, f.TreatBusyUntil, f.Sheathed ? 1 : 0 })];
            row.Stances = [.. w.Fighters.Select(f => ReferenceEquals(f.Stance, w.Content.Stance(f.Kit.Stance)) ? 0L : StanceOf(w, f).PackA())];
        }
        return row;
    }

    private static Ravensguard.Sim.Combat.Watch.WatchStance StanceOf(FightWorld w, Fighter f)
    {
        var p = Ravensguard.Sim.Combat.Watch.WatchStance.PresetOf(f.Stance.Id);
        return Ravensguard.Sim.Combat.Watch.WatchStance.FromDef(f.Stance, p) with { Flags = Ravensguard.Sim.Combat.Watch.WatchStance.FromDef(f.Stance, p).Flags | Ravensguard.Sim.Combat.Watch.StanceFlags.Custom };
    }

    public void Restore(FightWorld w)
    {
        w.LodFighter = LodFighter;
        w.RestoreOrders(Orders);
        if (State != null) w.RestoreWatch(Ravensguard.Sim.Combat.Watch.WatchState.Unpack(State));
        for (int i = 0; i < w.Fighters.Count; i++)
        {
            var f = w.Fighters[i];
            if (i < Stances.Length && Stances[i] != 0) f.Stance = Ravensguard.Sim.Combat.Watch.WatchStance.Unpack(Stances[i], 0).ToDef(w.Content);
            if (3 * i + 2 < Fighters.Length) { f.ProtectFighter = (int)Fighters[3 * i] - 1; f.TreatBusyUntil = (int)Fighters[3 * i + 1]; f.Sheathed = Fighters[3 * i + 2] != 0; }
        }
    }
}
