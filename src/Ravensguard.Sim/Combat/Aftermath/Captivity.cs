using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>
/// One captive (morale-capture-succession 3.2 <c>Captive</c>): who, held by whom and where, how restrained, since when, the ransom and who pays it, the guards, the captor's stash of
/// the captive's things, the care given, the next transfer, the attempts made, and the captive's skills as the escape rolls read them (copied at capture).
/// </summary>
public sealed class Captive
{
    public EntityId Person;
    public bool IsHero;
    public CaptorKind Captor;
    /// <summary>The band or detachment (its purse and its stash owner).</summary>
    public EntityId CaptorId;
    public PurseRef CaptorPurse;
    /// <summary>Where the captive was taken (the road spot an escape returns to), region millimetres.</summary>
    public long PlaceX, PlaceY;
    public EntityId Place;
    public Restraint Restraint;
    public int SinceDay;
    public long SinceSec;
    public CaptiveRank Rank;
    public long BaseRansom, RansomMites;
    public PayerKind PayerKind;
    public List<PurseRef> Payers = [];
    public int DueDay;
    public bool Negotiated, Paid;
    public int Guards, ShiftHours = 6;
    public EntityId Stash;
    public List<EntityId> StashItems = [];
    public CareLevel Care;
    public bool Cared;
    public int NextTransferDay;
    public int EscapeAttempts;
    public CaptiveState State;
    public int HoldUntilDay;
    public long EndedSec = -1;
    public bool HasPick, ImprovisedPick;
    public int Stealth, Athletics, Lockpicking, Perception, Speech;
    /// <summary>Free companions who would come for the captive (the abstract rescue party's members).</summary>
    public int RescueMembers;
    public int LastDay;
    /// <summary>The release's reason key ("sold" is the Phase 6 slavery stub: released with a rumour that the captive was taken south).</summary>
    public string Note = "";
    public bool Held => State == CaptiveState.Held;

    public bool Transferring(int day) => day >= NextTransferDay;

    public ulong Hash(ulong h)
    {
        void L(long v) => h = Melee2.FightMath.Mix(h, v);
        L((long)Person.Value); L(IsHero ? 1 : 0); L((long)Captor); L((long)CaptorId.Value); L((long)CaptorPurse.Kind); L((long)CaptorPurse.Owner.Value); L(PlaceX); L(PlaceY); L((long)Place.Value);
        L((long)Restraint); L(SinceDay); L(SinceSec); L((long)Rank); L(BaseRansom); L(RansomMites); L((long)PayerKind);
        foreach (var p in Payers) { L((long)p.Kind); L((long)p.Owner.Value); }
        L(DueDay); L(Negotiated ? 1 : 0); L(Paid ? 1 : 0); L(Guards); L(ShiftHours); L((long)Stash.Value); foreach (var i in StashItems) L((long)i.Value);
        L((long)Care); L(Cared ? 1 : 0); L(NextTransferDay); L(EscapeAttempts); L((long)State); L(HoldUntilDay); L(EndedSec); L(HasPick ? 1 : 0); L(ImprovisedPick ? 1 : 0);
        L(Stealth); L(Athletics); L(Lockpicking); L(Perception); L(Speech); L(RescueMembers); L(LastDay); L(Note.Length);
        foreach (char ch in Note) L(ch);
        return h;
    }
}

/// <summary>What the captive asks to do (the captivity screen's options, the Q-menu verbs of 7).</summary>
public enum CaptiveVerb : byte { Wait, WorkBonds, PickLock, Search, OfferRansom, TalkToGuard, Escape }

/// <summary>What an attempt came to: the verb, whether it worked, whether the guards caught it, the text key and the game second it ended.</summary>
public sealed record CaptiveResult(CaptiveVerb Verb, bool Ok, bool Caught, string Text, long EndSec, long Mites = 0);

/// <summary>The captive's situation as the captivity screen shows it (a copy, swapped in whole after every change: the screen reads, never writes).</summary>
public sealed record CaptiveView(EntityId Person, CaptorKind Captor, Restraint Restraint, CaptiveState State, int Guards, int GuardsAwake, bool Dark, long RansomMites, PayerKind Payer,
    long PayableMites, int DueDay, int Day, int HoldUntilDay, bool HasPick, bool ImprovisedPick, int BondsPm, int LockPm, int NoticePm, bool TransferTonight, int RescueMembers,
    int EscapeAttempts, string Note, CaptiveResult? Last, long PlaceX, long PlaceY);

/// <summary>
/// Everyone held captive (4.5), by person id (a sorted map: every walk and save is in id order), with the live captivity rules for the hero and the hourly abstract step for everyone
/// (<see cref="StepHour"/>: care, ransoms due, renegotiation, transfers and their escape windows, the abstract rescue, the hold limits). Rolls key on game time (GameSec / 20 windows,
/// game day) and the captive (streams 0x453 to 0x456). Coin moves only through the ledger. The game holds one book in the world state; the abstract tick and the hero desk write it at
/// the barrier, the screen reads <see cref="View"/>.
/// </summary>
public sealed class CaptiveBook
{
    public readonly SortedDictionary<ulong, Captive> Captives = [];
    public ulong Seed;
    /// <summary>The person the player controls while the hero is held (a free companion on a rescue, plan 3.7), or None for the hero.</summary>
    public EntityId Controlled;
    public CaptiveResult? Last;
    private volatile CaptiveView? _view;
    public readonly object Sync = new();

    public CaptiveBook(ulong seed = 0) { Seed = seed; }

    // not saved: set when the book is attached to a running world (content and the item factory)
    public AftermathRules Rules { get; set; } = AftermathRules.Default;
    public Func<int, long>? ValueOf { get; set; }
    public Func<string, bool>? IsWeapon { get; set; }
    /// <summary>The hero's last defeat, as the defeat screen shows it (not saved: it is read once, right after the fight).</summary>
    public DefeatReport? LastDefeat { get; set; }

    public Captive? Find(EntityId person) => Captives.GetValueOrDefault(person.Value);
    public Captive? Hero => Captives.Values.FirstOrDefault(c => c.IsHero && c.Held) ?? Captives.Values.LastOrDefault(c => c.IsHero);
    public bool HeroHeld => Captives.Values.Any(c => c.IsHero && c.Held);
    /// <summary>The hero's situation as of the last change (null when the hero has never been taken).</summary>
    public CaptiveView? View => _view;

    // ------------------------------------------------------------------ capture (4.5)

    /// <summary>
    /// Takes a person captive at <paramref name="sec"/>: bandits bind (2-3 guards, no care, ransom due in 3 days, held at most 10), an army cages (2 guards per tent, basic care, ransom due in 6
    /// days, held until the campaign ends, at most 40). The first transfer is 2-6 days on (stream 0x454). Weapons are never kept: the caller has moved them to <paramref name="stashItems"/>.
    /// </summary>
    public Captive Capture(AftermathRules r, CaptorKind captor, EntityId person, bool hero, EntityId captorId, PurseRef captorPurse, long sec, CaptiveRank rank, long ransom,
        PayerKind payer, IReadOnlyList<PurseRef> payers, IReadOnlyList<EntityId> stashItems, (int Stealth, int Athletics, int Lockpicking, int Perception, int Speech) skills,
        int rescueMembers = 0, long placeX = 0, long placeY = 0)
    {
        var D = r.D;
        int day = (int)(sec / 86400);
        var c = new Captive
        {
            Person = person, IsHero = hero, Captor = captor, CaptorId = captorId, CaptorPurse = captorPurse, PlaceX = placeX, PlaceY = placeY, Place = captorId,
            Restraint = captor == CaptorKind.Army ? Restraint.Caged : Restraint.Bound, SinceDay = day, SinceSec = sec, Rank = rank, BaseRansom = ransom, RansomMites = ransom,
            PayerKind = payer, Payers = [.. payers], DueDay = day + (captor == CaptorKind.Army ? D.ArmyDueDays : D.BanditDueDays),
            Guards = captor == CaptorKind.Army ? D.ArmyGuards : D.BanditGuardsMin + EscapeTable.Roll(Seed, day, person, RngStreams.CaptiveEvent, 1) * (D.BanditGuardsMax - D.BanditGuardsMin + 1) / 1000,
            ShiftHours = D.ShiftHours, Stash = captorId, StashItems = [.. stashItems], Care = captor == CaptorKind.Army ? CareLevel.Basic : CareLevel.None,
            NextTransferDay = day + D.TransferEveryMinDays + EscapeTable.Roll(Seed, day, person, RngStreams.EscapeWindow, 1) * (D.TransferEveryMaxDays - D.TransferEveryMinDays + 1) / 1000,
            HoldUntilDay = day + (captor == CaptorKind.Army ? D.ArmyHoldDays : D.BanditHoldDays), Stealth = skills.Stealth, Athletics = skills.Athletics, Lockpicking = skills.Lockpicking,
            Perception = skills.Perception, Speech = skills.Speech, RescueMembers = rescueMembers, LastDay = day,
        };
        Captives[person.Value] = c;
        return c;
    }

    /// <summary>
    /// P6-C1 (crime and justice 4.5): a person taken to a lord's gaol. Bound (chained for a murderer or a traitor), held by the fief (its treasury is the captor's purse), no ransom, no transfers and no hold limit: the
    /// court decides when he goes (<see cref="Release"/>). The captive's skills are copied as for any capture, so the escape rolls read them.
    /// </summary>
    public Captive CaptureGaol(AftermathRules r, EntityId person, bool hero, EntityId fief, PurseRef treasury, EntityId place, long sec, Restraint restraint, (int Stealth, int Athletics, int Lockpicking, int Perception, int Speech) skills, int guards = 2)
    {
        int day = (int)(sec / 86400);
        var c = new Captive
        {
            Person = person, IsHero = hero, Captor = CaptorKind.Gaol, CaptorId = fief, CaptorPurse = treasury, Place = place, Restraint = restraint, SinceDay = day, SinceSec = sec, Rank = CaptiveRank.Wanderer,
            PayerKind = PayerKind.None, DueDay = int.MaxValue, Guards = Math.Max(1, guards), ShiftHours = r.D.ShiftHours, Stash = fief, Care = CareLevel.None, NextTransferDay = int.MaxValue, HoldUntilDay = int.MaxValue,
            Stealth = skills.Stealth, Athletics = skills.Athletics, Lockpicking = skills.Lockpicking, Perception = skills.Perception, Speech = skills.Speech, LastDay = day,
        };
        lock (Sync) Captives[person.Value] = c;
        return c;
    }

    /// <summary>P6-C1: the court or the gaoler lets a held person go (served, acquitted, paid, bribed, unfed): his restraint is off and the note says how. False when he was not held.</summary>
    public bool Release(EntityId person, CaptiveState how, string note, long sec)
    {
        lock (Sync)
        {
            if (Find(person) is not { Held: true } c) return false;
            c.State = how; c.EndedSec = sec; c.Note = note; c.Restraint = Restraint.None;
            if (c.IsHero && Controlled != EntityId.None) Controlled = EntityId.None;
            return true;
        }
    }

    // ------------------------------------------------------------------ the captive's own attempts (4.7, live)

    /// <summary>
    /// One attempt by the captive at game second <paramref name="sec"/>. Working the bonds (Bound) takes 120 s, a lock (Chained or Caged, with a pick) 90 s; each rolls the guards' notice
    /// first (stream 0x453): caught is a beating (pain 20, no wound) and the restraint one step worse; else the skill roll (stream 0x455) frees the captive, who slips out to the road.
    /// Searching for a pick takes an hour; offering a ransom to a guard is a Speech check (0x456) that brings the payment day to tomorrow; at a transfer's night the captive can try to
    /// slip away (0x454). Keys are GameSec / 20 windows and the game day, so the same attempt at the same moment always ends the same way.
    /// </summary>
    public CaptiveResult Attempt(AftermathRules r, EntityId person, CaptiveVerb verb, long sec, WoundBook? book = null, Ledger? ledger = null)
    {
        lock (Sync)
        {
            var c = Find(person);
            if (c == null || !c.Held) return Done(new CaptiveResult(verb, false, false, "MC_NOT_HELD", sec));
            var D = r.D;
            long key = sec / 20; int day = (int)(sec / 86400);
            bool dark = EscapeTable.Dark(sec);
            uint n = (uint)c.EscapeAttempts;
            CaptiveResult res;
            switch (verb)
            {
                case CaptiveVerb.WorkBonds or CaptiveVerb.PickLock:
                {
                    bool bonds = verb == CaptiveVerb.WorkBonds;
                    if (bonds && c.Restraint != Restraint.Bound) { res = new(verb, false, false, "MC_NOT_BOUND", sec); break; }
                    if (!bonds && c.Restraint is not (Restraint.Chained or Restraint.Caged)) { res = new(verb, false, false, "MC_NO_LOCK", sec); break; }
                    if (!bonds && !c.HasPick) { res = new(verb, false, false, "MC_NO_PICK", sec); break; }
                    long end = sec + (bonds ? D.BondsWorkS : D.LockWorkS);
                    c.EscapeAttempts++;
                    int notice = EscapeTable.GuardsAwake(c.Captor, c.Guards, sec) > 0 ? EscapeTable.NoticePm(r, c.Stealth, dark) : 0;
                    if (EscapeTable.Roll(Seed, key, person, RngStreams.CaptiveEvent, 2 + n * 4) < notice)
                    {
                        Caught(r, c, book, sec);
                        res = new(verb, false, true, "MC_CAUGHT", end);
                        break;
                    }
                    int pm = bonds ? EscapeTable.BondsPm(r, c.Athletics, c.Stealth) : EscapeTable.LockPm(r, c.Lockpicking, c.Captor, c.ImprovisedPick);
                    if (EscapeTable.Roll(Seed, key, person, RngStreams.LockPick, n) < pm)
                    {
                        c.State = CaptiveState.Escaped; c.EndedSec = end; c.Note = bonds ? "bonds" : "lock";
                        res = new(verb, true, false, bonds ? "MC_SLIPPED_BONDS" : "MC_PICKED_LOCK", end);
                    }
                    else res = new(verb, false, false, bonds ? "MC_BONDS_HOLD" : "MC_LOCK_HOLDS", end);
                    break;
                }
                case CaptiveVerb.Search:
                    if (c.HasPick) { res = new(verb, true, false, "MC_HAVE_PICK", sec); break; }
                    if (EscapeTable.Roll(Seed, key, person, RngStreams.CaptiveEvent, 3 + n * 4) < EscapeTable.SearchPm(c.Perception))
                    { c.HasPick = true; c.ImprovisedPick = true; res = new(verb, true, false, "MC_FOUND_NAIL", sec + 3600); }
                    else res = new(verb, false, false, "MC_FOUND_NOTHING", sec + 3600);
                    c.EscapeAttempts++;
                    break;
                case CaptiveVerb.OfferRansom:
                {
                    long payable = ledger != null ? Ransom.Payable(r, ledger, c.PayerKind, c.Payers) : 0;
                    long ask = Ransom.Current(r, c.BaseRansom, c.DueDay, day);
                    if (c.PayerKind == PayerKind.None || payable < ask) { res = new(verb, false, false, "MC_OFFER_CANNOT", sec, ask); break; }
                    if (EscapeTable.Roll(Seed, day, person, RngStreams.RansomNegotiation, 1 + n) < EscapeTable.OfferPm(c.Speech))
                    { c.Negotiated = true; c.DueDay = Math.Min(c.DueDay, day + 1); res = new(verb, true, false, "MC_OFFER_TAKEN", sec + 600, ask); }
                    else res = new(verb, false, false, "MC_OFFER_REFUSED", sec + 600, ask);
                    c.EscapeAttempts++;
                    break;
                }
                case CaptiveVerb.TalkToGuard:
                    res = new(verb, true, false, c.Captor == CaptorKind.Army ? "MC_GUARD_NEWS_ARMY" : c.Captor == CaptorKind.Gaol ? "MC_GUARD_NEWS_GAOL" : "MC_GUARD_NEWS_BANDIT", sec + 300, Ransom.Current(r, c.BaseRansom, c.DueDay, day));
                    break;
                case CaptiveVerb.Escape:
                {
                    if (!c.Transferring(day) || !dark) { res = new(verb, false, false, "MC_NO_WINDOW", sec); break; }
                    c.EscapeAttempts++;
                    if (EscapeTable.Roll(Seed, day, person, RngStreams.EscapeWindow, 2 + n) < EscapeTable.TransferEscapePm(r, c.Stealth))
                    { c.State = CaptiveState.Escaped; c.EndedSec = sec + 60; c.Note = "transfer"; res = new(verb, true, false, "MC_SLIPPED_AWAY", sec + 60); }
                    else { Caught(r, c, book, sec); res = new(verb, false, true, "MC_CAUGHT", sec + 60); }
                    break;
                }
                default:
                    res = new(verb, true, false, "MC_WAITED", sec);
                    break;
            }
            return Done(res);
        }
    }

    /// <summary>Caught at it: beaten (pain 20, no wounds) and the restraint one step worse (Bound, Chained, Caged).</summary>
    private static void Caught(AftermathRules r, Captive c, WoundBook? book, long sec)
    {
        if (book != null) { book.AdvanceTo(sec); book.PainX100 += r.D.CaughtPainX100; }
        if (c.Restraint < Restraint.Caged) c.Restraint++;
        if (c.Restraint != Restraint.Bound) { c.HasPick = c.HasPick && c.ImprovisedPick ? false : c.HasPick; c.ImprovisedPick = false; }   // the guards take the nail
    }

    private CaptiveResult Done(CaptiveResult res) { Last = res; return res; }

    // ------------------------------------------------------------------ the hourly step (5: the abstract tick)

    /// <summary>
    /// The captives' hour <paramref name="hour"/>: the army medic dresses a new captive's wounds in the first hour (70%); at each day boundary the ransom falls due and is paid from the payer's purse
    /// if it can be (renegotiated: halved after 5 days, quartered after 10), a transfer opens a night's escape window (an NPC tries it; the hero is offered it), an abstract rescue party
    /// frees with 5% a day per member, and the hold limits end it: bandits after 10 days (40% released stripped, 40% the ransom handled, 20% "sold", the Phase 6 stub), an army at the
    /// campaign's end (40 days). Freed captives' stashed things come back from an army (less a 10% keep of the valuables), never from bandits.
    /// </summary>
    public void StepHour(AftermathRules r, Ledger? ledger, long hour, Func<ulong, WoundBook?>? books = null, Action<Captive, IReadOnlyList<EntityId>>? returnItems = null)
    {
        lock (Sync)
        {
            int day = (int)(hour / 24);
            foreach (var c in Captives.Values)
            {
                if (!c.Held) continue;
                if (c.Care == CareLevel.Basic && !c.Cared && hour * 3600 >= c.SinceSec)
                {
                    c.Cared = true;
                    if (EscapeTable.Roll(Seed, hour, c.Person, RngStreams.CaptiveEvent, 5) < r.D.ArmyCareDressPm && books?.Invoke(c.Person.Value) is { } b) Dress(b, hour * 3600);
                }
                if (day <= c.LastDay) continue;
                for (int d = c.LastDay + 1; d <= day && c.Held; d++) Day(r, ledger, c, d, returnItems);
                c.LastDay = day;
            }
            Publish(r, ledger, hour * 3600);
        }
    }

    private void Day(AftermathRules r, Ledger? ledger, Captive c, int d, Action<Captive, IReadOnlyList<EntityId>>? returnItems)
    {
        if (c.Captor == CaptorKind.Gaol) return;                                  // P6-C1: the court, not the abstract step, decides when a prisoner of the lord goes
        var D = r.D;
        c.RansomMites = Ransom.Current(r, c.BaseRansom, c.DueDay, d);
        if (d >= c.DueDay && !c.Paid && ledger != null && c.CaptorPurse.Find(ledger) is { } to && Ransom.TryPay(r, ledger, c.PayerKind, c.Payers, to, c.RansomMites))
        {
            c.Paid = true; Free(r, c, CaptiveState.Ransomed, (long)d * 86400, "ransomed", returnItems);
            return;
        }
        if (c.Transferring(d))
        {
            if (!c.IsHero && EscapeTable.Roll(Seed, d, c.Person, RngStreams.EscapeWindow, 3) < EscapeTable.TransferEscapePm(r, c.Stealth))
            { Free(r, c, CaptiveState.Escaped, (long)d * 86400 + 22 * 3600, "transfer", null); return; }
            if (!c.IsHero || d > c.NextTransferDay)
                c.NextTransferDay = d + D.TransferEveryMinDays + EscapeTable.Roll(Seed, d, c.Person, RngStreams.EscapeWindow, 1) * (D.TransferEveryMaxDays - D.TransferEveryMinDays + 1) / 1000;
        }
        if (c.RescueMembers > 0 && Controlled == EntityId.None && EscapeTable.Roll(Seed, d, c.Person, RngStreams.EscapeWindow, 4) < EscapeTable.RescueDailyPm(r, c.RescueMembers))
        { Free(r, c, CaptiveState.Rescued, (long)d * 86400 + 2 * 3600, "rescued", null); return; }
        if (d >= c.HoldUntilDay)
        {
            if (c.Captor == CaptorKind.Army) { Free(r, c, CaptiveState.Released, (long)d * 86400, "exchanged", returnItems); return; }
            if (c.Captor == CaptorKind.Slavers) { Free(r, c, CaptiveState.Released, (long)d * 86400, "sold", null); return; }       // P6-D1: a slaver's hold ends in a sale (the bondage sweep stands him in a pen), never in a release
            int fate = EscapeTable.Roll(Seed, d, c.Person, RngStreams.CaptiveEvent, 6);
            if (fate < D.BanditReleasePm) { Free(r, c, CaptiveState.Released, (long)d * 86400, "stripped", null); return; }
            if (fate < D.BanditReleasePm + D.BanditRansomPm && ledger != null && c.CaptorPurse.Find(ledger) is { } to2 && Ransom.TryPay(r, ledger, c.PayerKind, c.Payers, to2, c.RansomMites))
            { c.Paid = true; Free(r, c, CaptiveState.Ransomed, (long)d * 86400, "ransomed", null); return; }
            Free(r, c, CaptiveState.Released, (long)d * 86400, fate < D.BanditReleasePm + D.BanditRansomPm ? "stripped" : "sold", null);
        }
    }

    private void Free(AftermathRules r, Captive c, CaptiveState how, long sec, string note, Action<Captive, IReadOnlyList<EntityId>>? returnItems)
    {
        c.State = how; c.EndedSec = sec; c.Note = note; c.Restraint = Restraint.None;
        if (c.IsHero && Controlled != EntityId.None) Controlled = EntityId.None;
        if (c.Captor == CaptorKind.Army && returnItems != null && how is CaptiveState.Ransomed or CaptiveState.Released) returnItems(c, c.StashItems);
    }

    /// <summary>The captive's stashed things an army hands back (4.6): all of them but a 10% "keep" of the non-weapon valuables, the most valuable first.</summary>
    public static List<EntityId> Returned(AftermathRules r, IReadOnlyList<(EntityId Id, long Value, bool Weapon)> stash)
    {
        long total = stash.Where(s => !s.Weapon).Sum(s => s.Value), keep = total * r.D.KeepPm / 1000, kept = 0;
        var back = new List<EntityId>();
        foreach (var s in stash.OrderByDescending(s => s.Value).ThenBy(s => s.Id.Value))
        {
            if (!s.Weapon && kept + s.Value <= keep && s.Value > 0) { kept += s.Value; continue; }
            back.Add(s.Id);
        }
        return back;
    }

    /// <summary>The army medic's basic care: the worst bleeders bandaged (as the abstract tier's first aid).</summary>
    private static void Dress(WoundBook b, long sec)
    {
        b.AdvanceTo(sec);
        for (int k = 0; k < 4; k++)
        {
            int i = Treatment.WorstBleeder(b, -1, sec);
            if (i < 0) break;
            var wd = b.Records[i];
            wd.Dressing = Dressing.Bandage; wd.DressFromS = sec / MedicineTables.TicksPerSimS; wd.DressedAt = sec; wd.State = WoundState.Dressed;
            WoundBook.Retime(ref wd); b.Records[i] = wd;
        }
    }

    // ------------------------------------------------------------------ rescue control (4.7), the view, the hash

    /// <summary>The player takes control of a free companion for a rescue (Tab, "Take control of {name}?"); false when the hero is not held or the person is held too.</summary>
    public bool TakeControl(EntityId companion)
    {
        lock (Sync)
        {
            if (!HeroHeld || companion == EntityId.None || Find(companion) is { Held: true }) return false;
            Controlled = companion;
            return true;
        }
    }

    public void ReleaseControl() { lock (Sync) Controlled = EntityId.None; }

    /// <summary>A live rescue that reached the cell and opened it (<see cref="RescueRun"/>): the hero is free.</summary>
    public bool Rescued(EntityId person, long sec)
    {
        lock (Sync)
        {
            if (Find(person) is not { Held: true } c) return false;
            c.State = CaptiveState.Rescued; c.EndedSec = sec; c.Note = "rescued"; c.Restraint = Restraint.None;
            Controlled = EntityId.None;
            return true;
        }
    }

    /// <summary>Swaps in a new view of the hero's captivity (called after every change, on the thread that made it).</summary>
    public void Publish(AftermathRules r, Ledger? ledger, long sec)
    {
        var c = Hero;
        if (c == null) { _view = null; return; }
        int day = (int)(sec / 86400);
        long payable = ledger != null ? Ransom.Payable(r, ledger, c.PayerKind, c.Payers) : 0;
        _view = new CaptiveView(c.Person, c.Captor, c.Restraint, c.State, c.Guards, EscapeTable.GuardsAwake(c.Captor, c.Guards, sec), EscapeTable.Dark(sec), c.RansomMites, c.PayerKind, payable,
            c.DueDay, day, c.HoldUntilDay, c.HasPick, c.ImprovisedPick, EscapeTable.BondsPm(r, c.Athletics, c.Stealth), EscapeTable.LockPm(r, c.Lockpicking, c.Captor, c.ImprovisedPick),
            EscapeTable.NoticePm(r, c.Stealth, EscapeTable.Dark(sec)), c.Transferring(day) && EscapeTable.Dark(sec), c.RescueMembers, c.EscapeAttempts, c.Note, Last, c.PlaceX, c.PlaceY);
    }

    /// <summary>The <c>CaptiveHash</c> of the daily trail (6): every captive in id order and the control switch.</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        foreach (var (k, c) in Captives) h = c.Hash(h ^ k);
        return Melee2.FightMath.Mix(h, (long)Controlled.Value);
    }
}
