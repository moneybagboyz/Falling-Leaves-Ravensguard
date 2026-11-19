using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>Something left on the ground by the fight: a thrown weapon, a spent arrow, shards of a bottle (recoverable after the fight).</summary>
public readonly record struct GroundItem(string Shape, string Material, int X, int Y, bool Damaged, bool Ammo = false);

/// <summary>
/// The real-time fight (melee-and-ranged.md, P4-C): every fighter's action timeline at 20 Hz, block, parry, dodge and feint, stamina, facing and flanking, shield walls,
/// projectiles with real paths and thrown objects, every landed blow through <see cref="ResolverV2"/> and every wound into the person's <see cref="WoundBook"/>. Integer
/// and counter-RNG only, keyed on its own <see cref="Tick"/> (FightTick, overview 6.1), so a seeded fight is the same on every machine, at every speed, near or far.
/// <para>Order of a step (melee 5, 6): vitals, morale (2 Hz), commands and timelines in fighter id order, movement and shoves, bracing, then every blow that lands this
/// tick against the state at the start of the impact phase (ascending id for effects), then projectiles, then the one-shot inputs are cleared.</para>
/// </summary>
public sealed partial class FightWorld
{
    public FightContent Content { get; }
    public MeleeTuningDef K => Content.K;
    public CombatTablesV2 T => Content.T;
    public ResolverV2 Resolver { get; }
    public ulong Seed { get; }
    public ulong FightId { get; }
    /// <summary>The game second of FightTick 0: a fight tick is one game second (one live tick at 1x; a fight never lengthens a tick).</summary>
    public long GameSec0 { get; }
    public int Tick { get; private set; }
    public long GameSec => GameSec0 + Tick;
    public List<Fighter> Fighters { get; } = [];
    public List<Projectile> Projectiles { get; } = [];
    public List<FightEvent> Events { get; } = [];
    public List<GroundItem> Ground { get; } = [];
    /// <summary>The shared wound ledger (live fights); null: the fight keeps its own books (the harness, tests).</summary>
    public WoundLedger? Ledger { get; }
    /// <summary>Ammunition that can be picked up after the fight (not broken), by ammo id.</summary>
    public Dictionary<string, int> SpentAmmo { get; } = new(StringComparer.Ordinal);
    public bool RecordEvents = true;
    /// <summary>AI level of detail: null = automatic (20 Hz within <see cref="MeleeTuningDef.AiNearRadiusMm"/> of <see cref="LodCentre"/>, 5 Hz beyond); true all near, false all far (test ML-16).</summary>
    public bool? ForceNear;
    public (int X, int Y)? LodCentre;
    /// <summary>Called after every resolved blow (the calibration harness counts from it).</summary>
    public Action<Fighter, Fighter, BlowResult, StrikeV2>? OnBlow;
    /// <summary>P4-D hook: called for every event as it is emitted, whether or not <see cref="RecordEvents"/> keeps it (the flavour recorder listens; the sim never reads it).</summary>
    public Action<FightEvent>? OnEvent;

    private readonly BlowResult _r = new();
    private readonly List<int> _impacts = [];
    private FAct[] _snapAct = [];
    private int[] _snapStamina = [], _snapBlockSince = [];

    public FightWorld(FightContent content, ulong seed, ulong fightId = 1, long gameSec0 = 0, WoundLedger? ledger = null)
    {
        Content = content; Seed = seed; FightId = fightId; GameSec0 = gameSec0; Ledger = ledger;
        Resolver = new ResolverV2(content.T);
    }

    /// <summary>A fight restored from a save: the tick it was at.</summary>
    internal void RestoreTick(int tick) => Tick = tick;

    // ------------------------------------------------------------------ setup

    /// <summary>Adds a fighter from a kit (melee 3.3). Ids are the order added.</summary>
    public Fighter Add(FighterKit kit, bool player = false)
    {
        var plan = T.Plan(kit.Plan);
        WeaponV2 weapon;
        ImprovisedDef? imp = null;
        LauncherDef? launcher = kit.Launcher != null ? Content.Launchers[kit.Launcher] : null;
        if (kit.Weapon != null) weapon = T.Weapon(kit.Weapon.ShapeId, kit.Weapon.MaterialId, kit.Weapon.Quality);
        else if (launcher != null) weapon = T.Weapon(launcher.Shape, launcher.Shape == "crossbow" ? "steel" : "ash");
        else if (kit.Improvised != null) { imp = Content.Improvised[kit.Improvised]; weapon = Content.ImprovisedWeapon(imp); }
        else weapon = T.Weapon("fist", "bone");
        var shape = weapon.Shape;
        ShieldV2? shield = kit.Shield != null && !shape.TwoHanded && launcher == null ? T.Shield(kit.Shield) : null;
        var worn = new WornV2[kit.Worn.Count];
        int armourG = 0;
        for (int i = 0; i < worn.Length; i++)
        {
            var w = kit.Worn[i];
            var a = T.Armour(w.ShapeId, w.MaterialId);
            worn[i] = WornV2.Of(a, CombatTablesV2.QualityPm[(int)w.Quality], fitCm3: kit.SizeCm3);
            int defDensity = a.Shape.DefaultMaterial != null && T.HasMaterial(a.Shape.DefaultMaterial) ? T.Material(a.Shape.DefaultMaterial).DensityKgM3 : a.DensityKgM3;
            armourG += defDensity <= 0 ? a.Shape.MassG : (int)((long)a.Shape.MassG * a.DensityKgM3 / defDensity);
        }
        int carriedG = armourG + (int)(weapon.MassMg / 1000) + (shield != null ? shield.Def.MassG : 0);
        int capG = 15000 + 2500 * kit.Strength / 125;
        // the ledger holds the books of people (and of the hero, whose id is 0, the "nobody" of a nameless bandit: hence the player test; review, auto-dress follow-up)
        bool ledgerBook = Ledger != null && (kit.Person != 0 || player);
        var book = ledgerBook
            ? Ledger!.For(kit.Person, plan, GameSec, kit.SizeCm3, kit.Toughness)
            : new WoundBook(Seed, (FightId << 16) + (ulong)Fighters.Count + 1, (int)((long)plan.BloodMl * kit.SizeCm3 / plan.SizeCm3), kit.Toughness, plan.Id) { LastSec = GameSec };
        int id = Fighters.Count;
        var (fx, fy) = FightMath.Norm(kit.FaceX, kit.FaceY);
        if (fx == 0 && fy == 0) fx = FightMath.One;
        var f = new Fighter
        {
            Id = id, Kit = kit, Player = player, Weapon = weapon, Family = FamilyOf(shape), ReachMm = Math.Max(500, shape.ReachMm), StrikeTicks = Math.Max(1, shape.StrikeTicks),
            BaseWindTicks = TicksOf(shape.WindUpS), BaseRecoverTicks = TicksOf(shape.RecoverS), Shield = shield, Worn = worn, ArmourG = armourG,
            OverKg = Math.Max(0, (carriedG - capG) / 1000), Launcher = launcher, Plan = plan, Book = book, Stance = Content.Stance(kit.Stance),
            Skill = kit.Skill, ShieldSkill = kit.ShieldSkill >= 0 ? kit.ShieldSkill : kit.Skill, ThrowSkill = kit.ThrowSkill >= 0 ? kit.ThrowSkill : kit.Skill,
            ArcherySkill = kit.ArcherySkill >= 0 ? kit.ArcherySkill : kit.Skill, Improvised = imp,
            X = kit.X, Y = kit.Y, FX = fx, FY = fy, BraceX = kit.X, BraceY = kit.Y, BraceSince = Tick, Stamina = K.StaminaMaxX100, Morale = kit.Courage, LedgerBook = ledgerBook,
        };
        if (launcher != null && kit.Ammo != null && Content.Ammo.TryGetValue(kit.Ammo, out var ammo))
        {
            f.AmmoDef = ammo; f.AmmoWeapon = Content.AmmoWeapon(ammo); f.Ammo = kit.AmmoCount > 0 ? kit.AmmoCount : launcher.QuiverSize;
            if (launcher.Holds && f.Ammo > 0) { f.Loaded = true; f.Ammo--; }          // close-out item 3: a crossbow is carried into a fight spanned
        }
        foreach (var t in kit.Throwables) if (t.Count > 0) f.Throwables.Add(t);
        f.Brain.Target = -1; f.Brain.ReactSerialFoe = -1;
        Fighters.Add(f);
        RefreshBody(f);
        if (f.Stance.NoLethal) SheatheFor(f);                   // P4-F hook: a no-lethal stance fights with the flat, the butt, or bare hands (watch-mode 4.3)
        return f;
    }

    public static int TicksOf(double seconds) => Math.Max(1, (int)Math.Round(seconds * 20, MidpointRounding.AwayFromZero));

    public static string FamilyOf(WeaponShapeDef s) => s.Family switch
    {
        "blade" or "dagger" => "blades", "axe" => "axes", "hafted" => "hafted", "polearm" => "polearms", "ranged" => "archery", "unarmed" or "natural" => "melee", _ => s.Family,
    };

    public bool IsOver
    {
        get
        {
            if (Disengaged) return true;                // P4-D hook: both sides walked away for 15 s (morale 4.3)
            if (HitsWinner() >= 0) return true;         // P4-F hook: a practice bout ends at three clean hits
            int team = -1;
            foreach (var f in Fighters)
            {
                if (!f.Active) continue;
                if (team < 0) team = f.Team; else if (f.Team != team) return false;
            }
            return true;
        }
    }

    /// <summary>The surviving side, or -1 (everyone down, or still going).</summary>
    public int WinnerTeam
    {
        get
        {
            if (HitsWinner() is int hw and >= 0) return Fighters[hw].Team;     // P4-F hook: the practice bout's winner
            int team = -1;
            foreach (var f in Fighters) { if (!f.Active) continue; if (team < 0) team = f.Team; else if (f.Team != team) return -1; }
            return team;
        }
    }

    private void Emit(FightEventKind kind, int actor, int target, int part = -1, int a = 0, int b = 0)
    {
        if (kind is FightEventKind.Windup or FightEventKind.Shot or FightEventKind.Throw) _engageTick = Tick;   // P4-D hook: the walk-away clock (morale 4.3)
        var ev = new FightEvent(Tick, kind, actor, target, (short)part, a, b);
        if (RecordEvents) Events.Add(ev);
        OnEvent?.Invoke(ev);   // P4-D hook
    }

    // ------------------------------------------------------------------ player input (recorded by the session recorder)

    /// <summary>Applies one recorded player input to fighter <paramref name="fighter"/>'s command (the hero in the combat stance, plan 2.8).</summary>
    public void Input(int fighter, FightInput i)
    {
        if (fighter < 0 || fighter >= Fighters.Count) return;
        var f = Fighters[fighter]; var c = f.Cmd;
        switch (i.Kind)
        {
            case FightInputKind.Move: { var (x, y) = FightMath.Dir(i.A); c.MoveX = x; c.MoveY = y; c.Sprint = i.B != 0; break; }
            case FightInputKind.Face: { var (x, y) = FightMath.Dir(i.A); if (x != 0 || y != 0) { c.FaceX = x; c.FaceY = y; } break; }
            case FightInputKind.BlockDown:
                if (f.Act == FAct.Windup && f.WindElapsed * 2 < f.WindNeeded) c.Feint = true;     // RMB in the first half of one's own wind-up feints (plan 2.8)
                else c.Block = true;
                break;
            case FightInputKind.BlockUp: c.Block = false; break;
            case FightInputKind.AttackDown:
                c.Start = new AttackReq { Attack = i.A, Target = f.Brain.Target, Part = i.B, Gap = i.C, Auto = false };
                c.Hold = true;
                break;
            case FightInputKind.AttackUp: c.Hold = false; c.Release = true; break;
            case FightInputKind.Dodge: { var (x, y) = FightMath.Dir(i.A); c.DodgeX = x; c.DodgeY = y; break; }
            case FightInputKind.Reload: c.Reload = true; break;
            case FightInputKind.Throw: c.Throw = (i.A, i.B); break;
            case FightInputKind.Target: f.Brain.Target = i.A; break;
            case FightInputKind.Yield: Yield(f); break;       // P4-D hook: the Q-menu yield (4.2)
            case FightInputKind.CalledShot: c.Start = new AttackReq { Attack = i.A, Target = f.Brain.Target, Part = i.B, Gap = i.C, Auto = true }; break;
            case FightInputKind.Watch or FightInputKind.Stance or FightInputKind.Formation or FightInputKind.Order or FightInputKind.DeclineDanger: WatchInput(f, i); break;   // P4-F hook
        }
    }

    // ------------------------------------------------------------------ the step

    /// <summary>
    /// The live tier's step (phase-4 review finding 1): <see cref="StepWithAi"/> under the world ledger's lock when the fighters' books are the ledger's, so the tick worker (the hourly
    /// wound and captivity steps, the hero desk at the barrier, a save's capture) never reads or writes a book, the log or the fight half-way through a step. A fight of its own books
    /// needs no lock.
    /// </summary>
    public void StepShared()
    {
        if (Ledger is { } l) lock (l.Sync) StepWithAi();
        else StepWithAi();
    }

    /// <summary>The brain thinks for every fighter it drives, then one step.</summary>
    public void StepWithAi()
    {
        foreach (var f in Fighters) if (!f.Player) CombatBrain.Think(this, f);
        if (Mounts != null) MountedAfterThink();                    // [mounted-combat] horses, the cavalry brain, braced polearms (Combat/Mounted)
        Step();
    }

    /// <summary>Advances one 20 Hz fight tick. Controllers must have written each fighter's <see cref="Fighter.Cmd"/> first.</summary>
    public void Step()
    {
        Tick++;
        _impacts.Clear();
        foreach (var f in Fighters) UpdateVitals(f);
        MoraleStep();                                   // P4-D hook: morale at 2 Hz, staggered by id (Morale/FightWorld.Morale.cs)
        if (Mounts != null) MountedBeforeCommands();                // [mounted-combat] panic, falls, side-steps, braces
        foreach (var f in Fighters) { ApplyCommand(f); AdvanceState(f); }
        foreach (var f in Fighters) Move(f);
        if (Mounts != null) MountedAfterMove();                     // [mounted-combat] refusal, the braced strike, the trample
        Separate();
        foreach (var f in Fighters) UpdateBrace(f);
        if (_impacts.Count > 0)
        {
            int n = Fighters.Count;
            if (_snapAct.Length < n) { _snapAct = new FAct[n]; _snapStamina = new int[n]; _snapBlockSince = new int[n]; }
            for (int i = 0; i < n; i++) { _snapAct[i] = Fighters[i].Act; _snapStamina[i] = Fighters[i].Stamina; _snapBlockSince[i] = Fighters[i].BlockSince; }
            _impacts.Sort();
            foreach (int a in _impacts) ResolveImpact(Fighters[a]);
        }
        if (Mounts != null) MountedAfterImpacts();                  // [mounted-combat] unhorsings, shattered lances
        StepProjectiles();
        if (Rules.TreatInFight) TreatStep();                        // P4-F hook: pressure and bandages once clear of enemies (watch-mode 4.3)
        foreach (var f in Fighters) f.Cmd.ClearOneShots();
        if (Watch != null) WatchStep();                             // P4-F hook: odds, danger and the why line at 5 Hz; signals wait for the barrier
        if (Ledger != null) KeepBooksInLedger();
    }

    /// <summary>
    /// Phase 4 review finding 3: a fighter's book is the ledger's, but the ledger drops a book with nothing in it on its next advance, and every fighter starts unhurt, so the live tier's first
    /// advance left every fighter writing into a book the ledger no longer held: the fight's wounds were never saved, never bled or healed after the fight, and the defeat table and the abstract
    /// tier never saw them. After each step a hurt fighter's book goes back into the ledger (unless the ledger has since taken a hurt book of its own for that person).
    /// </summary>
    public void KeepBooksInLedger()
    {
        var l = Ledger!;
        lock (l.Sync)
            foreach (var f in Fighters)
            {
                ulong key = f.Kit.Person;
                if (!f.LedgerBook || f.Book.Empty) continue;
                if (l.Books.TryGetValue(key, out var held) && (ReferenceEquals(held, f.Book) || !held.Empty)) continue;
                l.Books[key] = f.Book;
            }
    }

    // ------------------------------------------------------------------ timings (4.1)

    /// <summary>Skill and encumbrance scaling: <c>t x (400 - skill) / 400</c>, +1% per kg over, rounded up, at least <paramref name="min"/>.</summary>
    public int Scaled(int ticks, int skill, int overKg, int min)
    {
        long t = (long)ticks * (400 - Math.Clamp(skill, 0, 100)) * (1000 + K.OverKgTimingPm * overKg);
        long r = (t + 400_000 - 1) / 400_000;
        return (int)Math.Max(min, r);
    }

    public int WindupTicks(Fighter f, bool riposte)
    {
        int t = Scaled(f.BaseWindTicks, f.Skill, f.OverKg, K.MinWindupTicks);
        long pm = 1000;
        if (f.Stamina < K.ExhaustedBelowX100) pm = pm * K.ExhaustedWindupPm / 1000;
        if (f.Book.Weakened) pm = pm * K.WeakenedWindupPm / 1000;
        if (riposte) pm = pm * K.RiposteWindupPm / 1000;
        return (int)Math.Max(riposte ? 2 : K.MinWindupTicks, (t * pm + 999) / 1000);
    }

    public int RecoverTicks(Fighter f)
    {
        int t = Scaled(f.BaseRecoverTicks, f.Skill, f.OverKg, K.MinRecoverTicks);
        if (f.Book.Weakened) t = (t * K.WeakenedWindupPm + 999) / 1000;
        return t;
    }

    /// <summary>Stamina of a swing, x100: <c>4 + 2 x kg</c>, x0.6 quick, x1.5 heavy.</summary>
    public int SwingCost(Fighter f, SwingStyle style = SwingStyle.Normal)
    {
        long c = (K.SwingBaseX100 * 1_000_000L + K.SwingPerKgX100 * f.Weapon.MassMg) / 1_000_000;
        int pm = style == SwingStyle.Quick ? K.QuickStaminaPm : style == SwingStyle.Heavy ? K.HeavyStaminaPm : 1000;
        return (int)WeatherSwing(f, c * pm / 1000);                        // [P4 close-out] weather hook
    }

    // ------------------------------------------------------------------ commands and the state machine

    private void ApplyCommand(Fighter f)
    {
        if (!f.CanAct) return;
        var c = f.Cmd;
        if (c.FaceX != 0 || c.FaceY != 0)
        {
            int steps = f.Act is FAct.Strike or FAct.Recover or FAct.Dodge ? 1 : Math.Max(1, K.TurnDegPerS * 64 / 360 / 20);
            (f.FX, f.FY) = FightMath.TurnToward(f.FX, f.FY, c.FaceX, c.FaceY, steps);
        }

        // dodge (Ctrl + direction): not while winding up or striking (4.2)
        if (c.HasDodge && Tick >= f.DodgeReady && f.CanStand && f.WalkPm > 0 && f.Stamina >= K.DodgeX100 && f.Act is FAct.Idle or FAct.Block or FAct.Aiming)
        {
            f.Act = FAct.Dodge; f.ActTicks = K.DodgeTicks;
            (f.DodgeX, f.DodgeY) = FightMath.Norm(c.DodgeX, c.DodgeY);
            f.DodgeReady = Tick + K.DodgeTicks + K.DodgeCooldownTicks + WeatherDodgeTicks();      // the cooldown counts from the end of the dodge; [P4 close-out] weather hook
            f.Stamina -= K.DodgeX100;
            return;
        }

        // feint: RMB in the first half of one's own wind-up (4.2), at most one per 30 ticks
        if (c.Feint && f.Act == FAct.Windup && f.WindElapsed * 2 < f.WindNeeded && Tick >= f.FeintReady && f.Req.Kind == BlowKind.Melee)
        {
            f.Act = FAct.Recover; f.ActTicks = K.FeintRecoverTicks; f.FeintReady = Tick + K.FeintEveryTicks;
            f.Stamina = Math.Max(0, f.Stamina - K.FeintX100);
            // a defender who began a parry against this wind-up has spent it
            foreach (var d in Fighters)
                if (d.Team != f.Team && d.Act == FAct.Block && Tick - d.BlockSince < K.ParryWindowTicks + 4) d.ParryDeadUntil = Tick + K.FeintParryDeadTicks;
            Emit(FightEventKind.Feint, f.Id, f.Req.Target);
            return;
        }

        if (c.Reload && f.Launcher != null)
        {
            if (f.Act == FAct.Reloading) { f.Act = FAct.Idle; f.ActTicks = 0; }
            else if (!f.Loaded && f.Ammo > 0 && f.Act is FAct.Idle or FAct.Block) StartReload(f);
        }

        if (c.Throw is { } tp && f.Act is FAct.Idle or FAct.Block && f.WeaponArm && CanThrow(f))
        {
            StartThrow(f, tp.X, tp.Y);
            return;
        }

        if (c.Start is { } req && f.Act is FAct.Idle or FAct.Block or FAct.Aiming)
        {
            if (f.Launcher != null) { StartDraw(f, req); return; }
            if (!f.WeaponArm || f.Stamina <= 0) return;
            if (req.Attack < 0 || req.Attack >= f.Weapon.Attacks.Length) req.Attack = CombatBrain.AutoAttack(this, f, req.Target >= 0 ? Fighters[req.Target] : null, req.Part);
            f.Req = req;
            f.Riposte = Tick <= f.RiposteUntil;
            f.Act = FAct.Windup; f.WindElapsed = 0; f.WindStart = Tick; f.Heavy = false;
            f.WindNeeded = WindupTicks(f, f.Riposte);
            if (req.Style == SwingStyle.Quick) f.WindNeeded = Math.Max(2, (f.WindNeeded * K.QuickWindupPm + 999) / 1000);
            f.ImpactAt = f.Req.Auto ? Tick + f.WindNeeded - 1 : -1;
            f.Serial++;
            f.Stamina = Math.Max(0, f.Stamina - SwingCost(f, req.Style));
            f.Thrown++;
            if (Mounts != null) MountedSwingStart(f);                // [mounted-combat] the couched lance, a lance on foot
            Emit(FightEventKind.Windup, f.Id, f.Req.Target, f.Req.Part, f.WindNeeded);
            return;
        }

        if (f.Act == FAct.Idle && c.Block) { f.Act = FAct.Block; f.BlockSince = Tick; }
        else if (f.Act == FAct.Block && !c.Block) f.Act = FAct.Idle;
        else if (f.Act == FAct.Aiming && c.Release && f.Launcher != null) Loose(f);
    }

    private void AdvanceState(Fighter f)
    {
        switch (f.Act)
        {
            case FAct.Windup:
                f.WindElapsed++;
                if (f.Req.Kind == BlowKind.Throw)
                {
                    if (f.WindElapsed >= f.WindNeeded) ReleaseThrow(f);
                    break;
                }
                if (!f.Req.Auto && f.Cmd.Release && f.WindElapsed < f.WindNeeded)
                {
                    // released inside the wind-up: a quick swing as soon as its shorter wind-up is done
                    f.Req.Style = SwingStyle.Quick;
                    f.WindNeeded = Math.Max(f.WindElapsed, Math.Max(2, (f.WindNeeded * K.QuickWindupPm + 999) / 1000));
                }
                if (f.WindElapsed >= f.WindNeeded)
                {
                    bool holding = !f.Req.Auto && f.Cmd.Hold && f.WindElapsed < f.WindNeeded + K.MaxHoldTicks;
                    if (holding)
                    {
                        if (!f.Heavy && f.WindElapsed >= f.WindNeeded + K.HeavyHoldTicks) { f.Heavy = true; f.Req.Style = SwingStyle.Heavy; f.Stamina = Math.Max(0, f.Stamina - SwingCost(f) / 2); }
                        break;
                    }
                    if (f.Heavy && f.WindElapsed < f.WindNeeded + K.HeavyHoldTicks) break;
                    f.Act = FAct.Strike; f.ActTicks = f.StrikeTicks;
                    _impacts.Add(f.Id);
                }
                break;
            case FAct.Strike:
                if (--f.ActTicks <= 0) { f.Act = FAct.Recover; f.ActTicks = RecoverTicks(f); }
                break;
            case FAct.Recover:
            case FAct.Stagger:
                // a guard held through the recover comes up the tick the recover ends (that counts as the press, for a parry)
                if (--f.ActTicks <= 0) { if (f.Cmd.Block && f.Launcher == null) { f.Act = FAct.Block; f.BlockSince = Tick; } else f.Act = FAct.Idle; }
                break;
            case FAct.Dodge:
                if (f.ActTicks <= 0) f.Act = FAct.Idle;          // counted down by the dodge's own steps (Move): 7 ticks of evasion and of movement
                break;
            case FAct.Reloading:
                if (--f.ActTicks <= 0) { f.Act = FAct.Idle; f.Loaded = true; f.Ammo--; Emit(FightEventKind.Reloaded, f.Id, -1, -1, f.Ammo); }
                break;
            case FAct.Drawing:
                f.DrawElapsed++;
                if (f.DrawElapsed >= DrawTicks(f)) { f.Act = FAct.Aiming; f.AimSince = Tick; }
                else if (f.Cmd.Release || (!f.Req.Auto && !f.Cmd.Hold)) Loose(f);
                break;
            case FAct.Aiming:
                if (f.Launcher is { Holds: false } && Tick - f.AimSince > 24 && (Tick - f.AimSince) % 20 == 0) f.Stamina = Math.Max(0, f.Stamina - K.BowHoldPerSX100);
                if (f.Req.Auto && Tick - f.AimSince >= AiAimTicks(f)) Loose(f);
                else if (!f.Req.Auto && !f.Cmd.Hold) Loose(f);
                break;
        }
    }

    // ------------------------------------------------------------------ movement

    private void Move(Fighter f)
    {
        if (Mounts != null && MountedMove(f)) return;               // [mounted-combat] the horse carries its rider
        f.LastMoveMm = 0;
        if (f.Act is FAct.Dead or FAct.Fled or FAct.Surrendered or FAct.Stagger) return;
        if (f.Act == FAct.Down)
        {
            if (!f.Crawling || (f.Cmd.MoveX == 0 && f.Cmd.MoveY == 0)) return;
            Step(f, f.Cmd.MoveX, f.Cmd.MoveY, 300 / 20);                     // crawling, 0.3 m/s
            return;
        }
        if (f.Act == FAct.Dodge)
        {
            Step(f, f.DodgeX, f.DodgeY, K.DodgeMmps / 20);
            f.ActTicks--;
            return;
        }
        var c = f.Cmd;
        if (c.MoveX == 0 && c.MoveY == 0) return;
        int speed = K.WalkMmps;
        bool sprint = c.Sprint && f.Act == FAct.Idle && f.Stamina > 0 && f.Shield?.Def.Id != "tower";
        if (sprint) { speed = K.SprintMmps; f.Stamina = Math.Max(0, f.Stamina - K.SprintPerSX100 / 20); }
        int pm = f.Act switch { FAct.Windup or FAct.Strike or FAct.Recover => K.AttackMovePm, FAct.Block or FAct.Aiming or FAct.Drawing => K.GuardMovePm, _ => 1000 };
        speed = (int)((long)speed * pm / 1000 * f.WalkPm / 1000);
        if (f.Book.Weakened) speed = speed * 800 / 1000;
        speed = WeatherSpeed(speed);                                       // [P4 close-out] weather hook (FightWorld.Weather.cs)
        int perTick = speed / 20;
        if (f.Act == FAct.Reloading && f.Launcher is { Holds: true } && perTick > K.MoveCancelMmps / 20) { f.Act = FAct.Idle; f.ActTicks = 0; }   // spanning needs standing
        Step(f, c.MoveX, c.MoveY, perTick);
    }

    private static void Step(Fighter f, int dx, int dy, int mm)
    {
        var (nx, ny) = FightMath.Norm(dx, dy);
        int mx = (int)((long)nx * mm >> 10), my = (int)((long)ny * mm >> 10);
        f.X += mx; f.Y += my;
        f.LastMoveMm = (int)FightMath.Len(mx, my);
    }

    /// <summary>Bodies keep apart (2 x body radius); a downed body is pushed, not pushing. A sprinting fighter running into a shield bearer of the other side shoves it (4.4).</summary>
    private void Separate()
    {
        int min = 2 * K.BodyRadiusMm;
        for (int i = 0; i < Fighters.Count; i++)
        {
            var a = Fighters[i];
            if (a.Act is FAct.Dead or FAct.Fled or FAct.Surrendered) continue;
            for (int j = i + 1; j < Fighters.Count; j++)
            {
                var b = Fighters[j];
                if (b.Act is FAct.Dead or FAct.Fled or FAct.Surrendered) continue;
                if (Mounts != null && MountedSkipPair(a, b)) continue;      // [mounted-combat] horses do not push; a horse at speed runs through
                long dx = b.X - a.X, dy = b.Y - a.Y;
                if (dx > min || dx < -min || dy > min || dy < -min) continue;
                long len = FightMath.Len(dx, dy);
                if (len >= min) continue;
                int ux, uy;
                if (len == 0) { ux = FightMath.One; uy = 0; } else { ux = (int)(dx * FightMath.One / len); uy = (int)(dy * FightMath.One / len); }
                int push = (int)((min - len) / 2);
                bool aFixed = a.Act == FAct.Down, bFixed = b.Act == FAct.Down;
                if (a.Team != b.Team && !aFixed && !bFixed) Shove(a, b, ux, uy);
                if (aFixed && !bFixed) { b.X += ux * push * 2 / FightMath.One; b.Y += uy * push * 2 / FightMath.One; }
                else if (bFixed && !aFixed) { a.X -= ux * push * 2 / FightMath.One; a.Y -= uy * push * 2 / FightMath.One; }
                else { a.X -= ux * push / FightMath.One; a.Y -= uy * push / FightMath.One; b.X += ux * push / FightMath.One; b.Y += uy * push / FightMath.One; }
            }
        }
    }

    private void Shove(Fighter a, Fighter b, int ux, int uy)
    {
        // the runner is whichever closed at sprint speed into a shield bearer
        Fighter? runner = null, bearer = null;
        int sprintTick = K.SprintMmps / 20 - 10;
        if (a.LastMoveMm >= sprintTick && b.Shield != null) { runner = a; bearer = b; }
        else if (b.LastMoveMm >= sprintTick && a.Shield != null) { runner = b; bearer = a; ux = -ux; uy = -uy; }
        if (runner == null || bearer == null || Tick < bearer.UnbracedUntil) return;
        long massR = 70_000 + runner.ArmourG, massB = 70_000 + bearer.ArmourG;
        long d = K.ShoveMm * massR / massB;
        if (BracedNeighbours(bearer) >= 2) d /= 3;
        bearer.X += (int)(ux * d / FightMath.One); bearer.Y += (int)(uy * d / FightMath.One);
        bearer.UnbracedUntil = Tick + K.ShoveUnbraceTicks;
        bearer.BraceX = bearer.X; bearer.BraceY = bearer.Y; bearer.BraceSince = Tick;
        Emit(FightEventKind.Shove, runner.Id, bearer.Id, -1, (int)d);
    }

    // ------------------------------------------------------------------ shield walls (4.4)

    private void UpdateBrace(Fighter f)
    {
        long dx = f.X - f.BraceX, dy = f.Y - f.BraceY;
        if (dx * dx + dy * dy > (long)K.BraceMoveMm * K.BraceMoveMm) { f.BraceX = f.X; f.BraceY = f.Y; f.BraceSince = Tick; }
    }

    /// <summary>Moved under 1 m in the last 2 s, not winding up, recovering or staggered, and not shoved just now.</summary>
    public bool Braced(Fighter f) => f.Shield != null && f.Active && Tick - f.BraceSince >= K.BraceTicks && Tick >= f.UnbracedUntil
        && f.Act is not (FAct.Windup or FAct.Recover or FAct.Stagger or FAct.Strike or FAct.Dodge);

    private int AdjacentBraced(Fighter f, Fighter? except = null)
    {
        int n = 0;
        foreach (var o in Fighters)
        {
            if (o.Id == f.Id || o == except || o.Team != f.Team || !Braced(o)) continue;
            long dx = o.X - f.X, dy = o.Y - f.Y;
            if (dx * dx + dy * dy > (long)K.WallSpacingMm * K.WallSpacingMm) continue;
            if (FightMath.Dot(f.FX, f.FY, o.FX, o.FY) < K.WallFacingQ10) continue;
            // side by side: the neighbour lies to the left or right, not in front or behind
            var (ux, uy) = FightMath.Norm(dx, dy);
            if (Math.Abs(FightMath.Dot(f.FX, f.FY, ux, uy)) > 512) continue;
            n++;
        }
        return Math.Min(2, n);
    }

    /// <summary>Braced shield-bearing neighbours of a braced bearer in a wall of at least three (0 to 2, +100 permille cover each).</summary>
    public int BracedNeighbours(Fighter f)
    {
        if (!Braced(f)) return 0;
        int n = AdjacentBraced(f);
        if (n >= 2) return n;
        if (n == 0) return 0;
        // one neighbour: the wall counts only if that neighbour has another (three in a row)
        foreach (var o in Fighters)
        {
            if (o.Id == f.Id || o.Team != f.Team || !Braced(o)) continue;
            long dx = o.X - f.X, dy = o.Y - f.Y;
            if (dx * dx + dy * dy > (long)K.WallSpacingMm * K.WallSpacingMm) continue;
            if (AdjacentBraced(o, f) >= 1) return 1;
        }
        return 0;
    }

    /// <summary>A polearm behind a braced friendly shield bearer (within the wall spacing, in front of it) strikes over the wall (4.4).</summary>
    public bool OverWall(Fighter a)
    {
        if (a.Family != "polearms") return false;
        foreach (var o in Fighters)
        {
            if (o.Id == a.Id || o.Team != a.Team || !Braced(o)) continue;
            long dx = o.X - a.X, dy = o.Y - a.Y;
            if (dx * dx + dy * dy > (long)K.WallSpacingMm * K.WallSpacingMm * 2) continue;
            var (ux, uy) = FightMath.Norm(dx, dy);
            if (FightMath.Dot(a.FX, a.FY, ux, uy) >= 700) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ geometry (4.1)

    /// <summary>Where <paramref name="attacker"/> stands relative to <paramref name="defender"/>'s facing: front 100 degrees, rear 90 degrees, flank otherwise.</summary>
    public Facing FacingOf(Fighter defender, int ax, int ay)
    {
        var (ux, uy) = FightMath.Norm(ax - defender.X, ay - defender.Y);
        int dot = FightMath.Dot(defender.FX, defender.FY, ux, uy);
        return dot >= K.FrontArcQ10 ? Facing.Front : dot <= K.RearArcQ10 ? Facing.Rear : Facing.Flank;
    }

    public long DistMm(Fighter a, Fighter b) => FightMath.Len(b.X - a.X, b.Y - a.Y);

    /// <summary>The target's centre within reach + 400 mm and inside the attacker's 60 degree cone.</summary>
    public bool InStrike(Fighter a, Fighter t, int reach)
    {
        long dx = t.X - a.X, dy = t.Y - a.Y;
        long len = FightMath.Len(dx, dy);
        if (len > reach + K.ReachSlackMm) return false;
        if (len == 0) return true;
        int dot = (int)((a.FX * dx + a.FY * dy) / len);
        return dot >= K.AttackConeQ10;
    }

    // ------------------------------------------------------------------ vitals (from the wound book)

    private void UpdateVitals(Fighter f)
    {
        if (f.Act == FAct.Dead) return;
        if (_burning != null || f.Plan.Id == "draugr") { SiteVitals(f); if (f.Act == FAct.Dead) return; }   // [exploration] hook: the draugr's fire and core (Sites/FightWorld.Site.cs)
        var b = f.Book;
        b.AdvanceTo(GameSec);
        if (b.Records.Count != f.WoundsSeen) RefreshBody(f);
        if (b.Dead) { Kill(f); return; }
        if (YieldAtPain(f)) return;                                 // P4-F hook: a tournament bout is yielded at the pain knock-out
        bool down = b.State is Consciousness.Unconscious or Consciousness.Dying or Consciousness.Collapsed || !f.CanStand;
        f.Crawling = b.State == Consciousness.Collapsed || (!f.CanStand && b.Conscious);
        if (down && f.Act is not (FAct.Down or FAct.Fled or FAct.Surrendered)) GoDown(f);
        else if (!down && f.Act == FAct.Down)
        {
            f.Act = FAct.Idle; f.EndedAt = -1;
            f.Stamina = Math.Max(0, f.Stamina - K.StandUpX100);
            Emit(FightEventKind.Wake, f.Id, -1);
        }
        // stamina (4.3): rest 12/s, guarding or heavy armour 5/s, weakened x0.7
        if (f.Act is FAct.Idle or FAct.Recover or FAct.Stagger or FAct.Down or FAct.Block or FAct.Aiming or FAct.Reloading)
        {
            bool sprinting = f.Act == FAct.Idle && f.Cmd.Sprint && (f.Cmd.MoveX != 0 || f.Cmd.MoveY != 0);
            if (!sprinting)
            {
                int r = f.Act == FAct.Block || f.ArmourG > K.HeavyArmourKg * 1000 ? K.RegenGuardPerSX100 : K.RegenRestPerSX100;
                if (b.Weakened) r = r * K.WeakenedRegenPm / 1000;
                r = WeatherRegen(f, r);                                    // [P4 close-out] weather hook
                f.Stamina = Math.Min(K.StaminaMaxX100, f.Stamina + r / 20);
            }
        }
    }

    /// <summary>Re-derives what the body can do from the wound book (arms, legs, sight, strength) once per new wound.</summary>
    public void RefreshBody(Fighter f)
    {
        f.WoundsSeen = f.Book.Records.Count;
        var bs = f.Book.Function(f.Plan);
        bool hadArm = f.WeaponArm, hadShield = f.ShieldArm;
        // side index 1 is the right (weapon) hand, 0 the left (shield) hand
        f.WeaponArm = bs.WeaponHand[1] || (bs.WeaponHand[0] && !f.Weapon.Shape.TwoHanded && f.Shield == null);
        if (f.Weapon.Shape.TwoHanded) f.WeaponArm = bs.WeaponHand[0] && bs.WeaponHand[1] || (bs.Grip[1] >= 400 && bs.Arm[1] >= 400 && bs.Grip[0] >= 200);
        if (f.Weapon.Shape.Family == "natural") f.WeaponArm = true;        // jaws and claws: no hand needed
        f.ShieldArm = bs.Arm[0] >= 400;
        f.WalkPm = Math.Clamp(bs.WalkSpeedPm, 0, 1000);
        f.CanStand = bs.WalkSpeedPm > 0 || bs.Balance > 0;
        f.BlowPm = bs.BlowMomentumPm[1];
        f.StrengthPm = f.Book.StrengthPm;
        f.OneEye = bs.Sight > 0 && bs.Sight < 1000;
        ulong missing = 0;
        for (int i = 0; i < bs.Parts.Length && i < 64; i++) if (bs.Parts[i].Severed || bs.Parts[i].Lost > 0 && !f.Plan.Parts[i].IsHosted) missing |= 1UL << i;
        f.MissingMask = missing;
        if (hadArm && !f.WeaponArm && f.Active)
        {
            if (f.Act is FAct.Windup or FAct.Drawing or FAct.Aiming) { f.Act = FAct.Idle; }
            MoraleOnLimbLost(f);                        // P4-D hook
            Emit(FightEventKind.Disarmed, f.Id, -1);
        }
        if (hadShield && !f.ShieldArm && f.Shield != null) Emit(FightEventKind.Disarmed, f.Id, -1, -1, 1);
    }

    private void Kill(Fighter f)
    {
        if (f.Act == FAct.Dead) return;
        f.Act = FAct.Dead; f.EndedAt = Tick;
        Emit(FightEventKind.Death, f.Id, -1, -1, (int)f.Book.Death);
        Announce(f);
    }

    private void GoDown(Fighter f)
    {
        if (f.Act is FAct.Dead or FAct.Fled or FAct.Surrendered) return;
        if (Rules.YieldAtPainKo && f.Book.PainX100 >= f.Book.KnockoutPainX100 && YieldAtPain(f)) return;      // P4-F: the marshal stops a tournament bout at the pain knock-out
        f.Act = FAct.Down; f.EndedAt = Tick; f.Loaded = f.Loaded && f.Launcher is { Holds: true };
        Emit(FightEventKind.Down, f.Id, -1, -1, (int)f.Book.Down);
        Announce(f);
    }

    /// <summary>A friend falling costs morale of teammates within 10 m (once per fighter).</summary>
    private void Announce(Fighter f)
    {
        if (f.Announced) return;
        f.Announced = true;
        MoraleOnFallen(f);                              // P4-D hook: friends within 10 m count the fall at their next update
    }

    // morale: morale-capture-succession 4.1 (P4-D) lives in Combat/Morale/FightWorld.Morale.cs

    // ------------------------------------------------------------------ hash

    /// <summary>The <c>FightHash</c> (overview 11): every fighter's state, wound book, the projectiles and the tick. Events and text are not hashed.</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        h = FightMath.Mix(h, Tick); h = FightMath.Mix(h, (long)FightId);
        foreach (var f in Fighters)
        {
            h = FightMath.Mix(h, f.Id); h = FightMath.Mix(h, f.X); h = FightMath.Mix(h, f.Y); h = FightMath.Mix(h, f.FX); h = FightMath.Mix(h, f.FY);
            h = FightMath.Mix(h, (long)f.Act | (long)f.ActTicks << 8 | (long)f.WindNeeded << 24 | (long)f.WindElapsed << 40);
            h = FightMath.Mix(h, f.Stamina); h = FightMath.Mix(h, f.Serial); h = FightMath.Mix(h, f.Req.Attack | f.Req.Part << 8 | f.Req.Target << 16 | (int)f.Req.Style << 28);
            h = FightMath.Mix(h, f.Ammo | (f.Loaded ? 1 << 20 : 0)); h = FightMath.Mix(h, f.ShieldPm); h = FightMath.Mix(h, f.ImprovisedPm);
            h = FightMath.Mix(h, f.Morale | f.MoralePenalty << 8 | (f.Routing ? 1 << 20 : 0));
            if (f.InsideUntil != -1) h = FightMath.Mix(h, (uint)f.InsideUntil | (long)f.InsideOf << 32);                                     // close-out item 1
            foreach (var wv in f.Worn) if (!wv.Pristine) { h = FightMath.Mix(h, wv.ConditionPm | (wv.Slipped ? 1 << 16 : 0)); h = FightMath.Mix(h, (long)wv.BreachMask); }   // close-out item 4: armour damage
            h = f.Book.Hash(h);
            h = f.M.Hash(h);                            // P4-D hook: the MoraleHash joins the FightHash
        }
        foreach (var p in Projectiles) h = p.Hash(h);
        if (Mounts != null) h = MountedHash(h);                     // [mounted-combat] MountedHash joins the trail
        h = WeatherHash(h);                                         // [P4 close-out] the weather row, when set (FightWorld.Weather.cs)
        h = FightMath.Mix(h, _engageTick);              // P4-D hook: the walk-away clock
        return h;
    }
}
