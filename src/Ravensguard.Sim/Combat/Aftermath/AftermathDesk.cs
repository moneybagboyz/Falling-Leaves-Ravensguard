using System.Globalization;
using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>
/// What the game knows of a lost road fight when it is over (morale-capture-succession 4.3), sent to the barrier in a hero command: the fight, who won, how the hero ended, whether the hero
/// fought to the end, the robbers' strength and size, the lead and where it happened. Encoded in the command's tag so it is saved with the pending commands and replayed.
/// </summary>
public sealed record DefeatRequest(ulong FightId, WinnerKind Winner, LoserState State, bool Wounded, bool Bleeding, bool Lone, bool Resisted, IReadOnlyList<Robber> Robbers,
    long PlaceX = 0, long PlaceY = 0, bool AllyStanding = false)
{
    public const string Prefix = "mc:defeat";

    public string Encode()
    {
        var sb = new StringBuilder(Prefix);
        sb.Append(CultureInfo.InvariantCulture, $"|f={FightId}|w={(int)Winner}|s={(int)State}|o={(Wounded ? 1 : 0)}{(Bleeding ? 1 : 0)}{(Lone ? 1 : 0)}{(Resisted ? 1 : 0)}{(AllyStanding ? 1 : 0)}|x={PlaceX}|y={PlaceY}|r=");
        sb.Append(string.Join(",", Robbers.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Strength}:{r.SizeCm3}:{(r.FirstAid ? 1 : 0)}"))));
        return sb.ToString();
    }

    public static DefeatRequest? Decode(string tag)
    {
        if (!tag.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var kv = tag.Split('|').Skip(1).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        long L(string k) => kv.TryGetValue(k, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        string o = kv.GetValueOrDefault("o", "00000").PadRight(5, '0');
        var robbers = new List<Robber>();
        foreach (var r in kv.GetValueOrDefault("r", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = r.Split(':');
            if (p.Length == 3 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sz))
                robbers.Add(new Robber(st, sz, p[2] == "1"));
        }
        return new DefeatRequest((ulong)L("f"), (WinnerKind)L("w"), (LoserState)L("s"), o[0] == '1', o[1] == '1', o[2] == '1', o[3] == '1', robbers, L("x"), L("y"), o[4] == '1');
    }

    /// <summary>The request of a lost fight: the hero is <paramref name="hero"/>; the winning side's standing fighters are the robbers (they have first aid between them: a bandage is kept).</summary>
    public static DefeatRequest Of(FightWorld w, int hero, WinnerKind winner, long placeX = 0, long placeY = 0)
    {
        var h = w.Fighters[hero];
        var state = h.Act switch
        {
            FAct.Dead => LoserState.Dead, FAct.Fled => LoserState.Fled, FAct.Surrendered => LoserState.Surrendered,
            FAct.Down => h.Book.State == Consciousness.Dying ? LoserState.Dying : LoserState.KnockedOut, _ => LoserState.Standing,
        };
        int win = w.DecidedWinner;
        var robbers = w.Fighters.Where(f => f.Team != h.Team && (win < 0 || f.Team == win) && f.Active).Select(f => new Robber(f.Kit.Strength, f.Kit.SizeCm3, winner == WinnerKind.Bandits)).ToList();
        bool ally = w.Fighters.Any(f => f.Team == h.Team && f.Id != hero && f.Active);
        return new DefeatRequest(w.FightId, winner, state, h.Book.Records.Count > 0, h.Book.RateMlPerS > 0 || h.Book.CurrentRateMilli() > 0, !ally, state != LoserState.Surrendered, robbers,
            placeX, placeY, ally);
    }
}

/// <summary>The defeat screen's facts (7): what happened, what was taken, what is left, the wounds and what to do first.</summary>
public sealed record DefeatReport(DefeatKind Kind, WinnerKind Winner, string Style, long CoinTaken, long CoinLeft, IReadOnlyList<string> ItemsTaken, IReadOnlyList<string> ItemsLeft,
    bool Mercy, int Wounds, bool Bleeding, bool NeedsDoctor, long Fine, long AtSec);

/// <summary>
/// The hero's side of the aftermath at the barrier (the hero desk calls it for <c>HeroCommandKind.Aftermath</c>): a lost fight's defeat table and its consequences (robbery through the
/// ledger and the item rows, capture into the <see cref="CaptiveBook"/>), and the captive's attempts. The main thread only encodes the request; every record is written here.
/// </summary>
public static class AftermathDesk
{
    public const string ActPrefix = "mc:act|";
    /// <summary>P4 close-out (4.7): the player takes control of a free companion ("mc:control|person"), gives it back ("mc:release|"), or the controlled companion reaches the cell
    /// ("mc:rescue|person|dx|dy": where it stands, millimetres from the cell) and the rest of the rescue is <see cref="RescueRun"/>'s creep past the guards.</summary>
    public const string ControlPrefix = "mc:control|", ReleasePrefix = "mc:release|", RescuePrefix = "mc:rescue|";
    /// <summary>P4 close-out: the fight's wear on the hero's worn pieces, written back to their item rows ("mc:wear|item:condition|..."; condition only, the rows keep the lower one).</summary>
    public const string WearPrefix = "mc:wear|";

    public static HeroCommand WearCommand(IEnumerable<(EntityId Item, int ConditionPm)> wear, long sec) =>
        new() { Kind = HeroCommandKind.Aftermath, IssuedSec = sec, Tag = WearPrefix + string.Join("|", wear.Select(w => string.Create(CultureInfo.InvariantCulture, $"{w.Item.Value}:{w.ConditionPm}"))), Origin = CommandOrigin.Human };

    private static HeroResult Wear(HeroContext ctx, HeroCommand c)
    {
        var items = ctx.Econ.Items; int n = 0;
        foreach (var part in c.Tag[WearPrefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length != 2 || !ulong.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cond)) continue;
            int row = items.IndexOf(new EntityId(id));
            if (row < 0 || items.At(row).Owner != ctx.Party.Hero) continue;                  // robbed or gone since: the robbers carry the dents
            ref var r = ref items.At(row);
            short to = (short)Math.Clamp(cond, 0, 1000);
            if (to < r.ConditionPermille) { r.ConditionPermille = to; n++; }
        }
        return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Empty, 0, n, c.IssuedSec);
    }

    public static HeroCommand ControlCommand(EntityId companion, long sec) =>
        new() { Kind = HeroCommandKind.Aftermath, IssuedSec = sec, Tag = ControlPrefix + companion.Value.ToString(CultureInfo.InvariantCulture), Origin = CommandOrigin.Human };
    public static HeroCommand ReleaseCommand(long sec) => new() { Kind = HeroCommandKind.Aftermath, IssuedSec = sec, Tag = ReleasePrefix, Origin = CommandOrigin.Human };
    public static HeroCommand RescueCommand(EntityId companion, long dx, long dy, long sec) =>
        new() { Kind = HeroCommandKind.Aftermath, IssuedSec = sec, Tag = string.Create(CultureInfo.InvariantCulture, $"{RescuePrefix}{companion.Value}|{dx}|{dy}"), Origin = CommandOrigin.Human };

    /// <summary>The guard posts round the cell (millimetres): the first <c>Guards</c> of a fixed ring, nearest first.</summary>
    public static IReadOnlyList<GuardPost> PostsOf(Captive c)
    {
        (int X, int Y)[] ring = [(8000, 0), (-8000, 0), (0, 8000), (0, -8000), (6000, 6000), (-6000, -6000), (6000, -6000), (-6000, 6000)];
        return [.. ring.Take(Math.Clamp(c.Guards, 1, ring.Length)).Select(p => new GuardPost(p.X, p.Y))];
    }

    private static HeroResult Rescue(HeroContext ctx, CaptiveBook book, HeroCommand c)
    {
        HeroResult Res(HeroResultCode code, string key) { book.Publish(book.Rules, ctx.Econ.Ledger, c.IssuedSec); return new HeroResult(c.Seq, c.Kind, code, HeroText.Of(key), 0, 0, c.IssuedSec); }
        if (c.Tag.StartsWith(ReleasePrefix, StringComparison.Ordinal)) { book.ReleaseControl(); return Res(HeroResultCode.Ok, "rs_released"); }
        var p = c.Tag[(c.Tag.StartsWith(ControlPrefix, StringComparison.Ordinal) ? ControlPrefix : RescuePrefix).Length..].Split('|');
        long P(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var who = new EntityId((ulong)P(0));
        if (c.Tag.StartsWith(ControlPrefix, StringComparison.Ordinal))
        {
            bool free = ctx.Party.Squad.Find(who) != null;
            return free && book.TakeControl(who) ? Res(HeroResultCode.Ok, "rs_control") : Res(HeroResultCode.Refused, "rs_control_refused");
        }
        if (book.Controlled != who || book.Hero is not { Held: true } cap) return Res(HeroResultCode.Refused, "rs_control_refused");
        var pt = ctx.State.Population.People; int row = pt.IndexOf(who);
        int stealth = row >= 0 ? pt.SkillLevelOf(row, Skill.Stealth) : 20, locks = row >= 0 ? pt.SkillLevelOf(row, Skill.Lockpicking) : 10;
        var o = RescueRun.Run(book.Rules, ctx.Seed, cap, who, stealth, locks, (int)Math.Clamp(P(1), -200_000, 200_000), (int)Math.Clamp(P(2), -200_000, 200_000), PostsOf(cap), c.IssuedSec);
        if (o.Freed) book.Rescued(cap.Person, c.IssuedSec + o.Seconds); else book.ReleaseControl();
        return Res(o.Freed ? HeroResultCode.Ok : HeroResultCode.Refused, o.Text);
    }

    public static HeroCommand DefeatCommand(DefeatRequest r, long sec) => new() { Kind = HeroCommandKind.Aftermath, IssuedSec = sec, Tag = r.Encode(), Origin = CommandOrigin.Human };
    public static HeroCommand ActCommand(CaptiveVerb v, long sec, CommandOrigin origin = CommandOrigin.Human) =>
        new() { Kind = HeroCommandKind.Aftermath, IssuedSec = sec, Tag = ActPrefix + ((int)v).ToString(CultureInfo.InvariantCulture), Origin = origin };

    /// <summary>The book of a world, made and wired on first use (rules, item values and weapon shapes from the content).</summary>
    public static CaptiveBook BookOf(Save.WorldState state, ContentDatabase content)
    {
        var b = state.Captivity ??= new CaptiveBook(state.Seed);
        Wire(b, content, state.Economy);
        return b;
    }

    public static void Wire(CaptiveBook b, ContentDatabase content, EconomyState? e)
    {
        b.Rules = AftermathRules.For(content);
        var weapons = content.All<WeaponShapeDef>().Select(w => w.Id).Concat(content.All<ShieldShapeDef>().Select(s => s.Id)).ToHashSet(StringComparer.Ordinal);
        b.IsWeapon = s => weapons.Contains(s);
        if (e != null)
        {
            var f = new Goods.ItemFactory(content, e.Goods);
            b.ValueOf = row => e.Items.ValueMites(row, m => { try { return f.ValueCrownsPerKg(m); } catch (KeyNotFoundException) { return 1.0; } });
        }
    }

    /// <summary>Applies an aftermath command at the barrier, under the wound ledger's lock (the mercy bandage, a beating and the rescue write the hero's book, which the live tier steps on
    /// the main thread meanwhile; review finding 1).</summary>
    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (ctx.State.Wounds is { } wl) lock (wl.Sync) return ApplyCore(ctx, c);
        return ApplyCore(ctx, c);
    }

    private static HeroResult ApplyCore(HeroContext ctx, HeroCommand c)
    {
        var book = BookOf(ctx.State, ctx.Content);
        if (c.Tag.StartsWith(WearPrefix, StringComparison.Ordinal)) return Wear(ctx, c);              // P4 close-out: the fight's armour wear onto the item rows
        if (c.Tag.StartsWith(ControlPrefix, StringComparison.Ordinal) || c.Tag.StartsWith(ReleasePrefix, StringComparison.Ordinal) || c.Tag.StartsWith(RescuePrefix, StringComparison.Ordinal))
            return Rescue(ctx, book, c);                                                   // P4 close-out: the rescue control switch
        if (c.Tag.StartsWith(ActPrefix, StringComparison.Ordinal))
        {
            if (!int.TryParse(c.Tag.AsSpan(ActPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < 0 || v > (int)CaptiveVerb.Escape)
                return new HeroResult(c.Seq, c.Kind, HeroResultCode.Invalid, HeroText.Of("MC_NOT_HELD"));
            var res = book.Attempt(book.Rules, ctx.Party.Hero, (CaptiveVerb)v, c.IssuedSec, ctx.State.Wounds?.Find(ctx.Party.Hero.Value), ctx.Econ.Ledger);
            book.Publish(book.Rules, ctx.Econ.Ledger, c.IssuedSec);
            return new HeroResult(c.Seq, c.Kind, res.Ok ? HeroResultCode.Ok : HeroResultCode.Refused, HeroText.Of(res.Text, Quotes.Money(res.Mites)), res.Mites, 0, c.IssuedSec);
        }
        var req = DefeatRequest.Decode(c.Tag);
        if (req == null) return new HeroResult(c.Seq, c.Kind, HeroResultCode.Invalid, HeroText.Of("MC_NOT_HELD"));
        var rep = Defeat(ctx.State, ctx.Content, ctx.Party, ctx.Purse, req, c.IssuedSec);
        return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("MC_DEFEAT_" + rep.Kind.ToString().ToUpperInvariant(), Quotes.Money(rep.CoinTaken)), rep.CoinTaken, 0, c.IssuedSec);
    }

    /// <summary>
    /// The hero lost a fight (4.4): the defeat table decides, then robbery moves the purse and the things to the band (the mercy bandage on a bleeding victim), capture puts the hero in the book
    /// (an army keeps the equipment in its stash and leaves the purse, which pays the ransom; bandits rob first), guards fine and release, locals take the purse. Coin only through the ledger.
    /// </summary>
    public static DefeatReport Defeat(Save.WorldState state, ContentDatabase content, HeroParty party, PurseId heroPurse, DefeatRequest req, long sec)
    {
        var e = state.Economy ?? throw new InvalidOperationException("the aftermath needs the world's economy");
        var book = BookOf(state, content);
        var R = book.Rules;
        var hero = party.Hero;
        var ledger = e.Ledger;
        var wb = state.Wounds?.Find(hero.Value);
        bool needsDoctor = wb != null && (wb.OpenCount > 0 && (wb.CurrentRateMilli() > 0 || wb.Records.Any(w => w.Open && w.BleedX10 > 0)) || wb.MaxInfection > 0);
        var friends = party.Squad.Active.Select(m => new PurseRef(PurseKind.Household, m.Household)).Where(p => p.Owner != EntityId.None).ToList();
        long coin = ledger.Balance(heroPurse).Mites;
        var payer = PayerKind.Friends;
        long payable = Ransom.Payable(R, ledger, payer, friends);
        long ransom = Ransom.ForWanderer(R, coin);
        var style = req.Winner == WinnerKind.Bandits ? R.StyleOf(state.Seed, req.FightId) : null;
        var facts = new LoserFacts(hero, LoserRole.Hero, req.State, req.Wounded, req.Bleeding, req.Lone, ransom, payable, req.AllyStanding);
        var outcome = DefeatTable.Decide(R, state.Seed, req.FightId, req.Winner, style, facts, sec);
        var names = new Func<EntityId, string>(id => { int r = e.Items.IndexOf(id); return r >= 0 ? e.Items.At(r).Shape : ""; });
        var taken = new List<string>(); var left = new List<string>();
        long coinTaken = 0, coinLeft = coin, fine = 0; bool mercy = false;
        var isWeapon = book.IsWeapon ?? (_ => false);

        void Rob(bool purseOnly)
        {
            var holdings = Robbery.Holdings(content, e, party.Carried, 70000, book.ValueOf);
            var plan = Robbery.Plan(R, ledger.Balance(heroPurse).Mites, needsDoctor, holdings, req.Resisted, req.Robbers.Count > 0 ? req.Robbers : [new Robber()], purseOnly);
            var band = ledger.Open(PurseKind.Household, AftermathCoin.BandId(req.FightId));
            Robbery.Apply(e, party.Carried, heroPurse, band, AftermathCoin.BandId(req.FightId), plan);
            coinTaken += plan.CoinTaken; coinLeft = ledger.Balance(heroPurse).Mites;
            taken.AddRange(plan.Taken.Select(i => i.Shape)); left.AddRange(plan.Left.Select(i => i.Shape));
        }

        switch (outcome.Kind)
        {
            case DefeatKind.Robbed:
                Rob(outcome.Style == "purse");
                mercy = DefeatTable.Mercy(R, state.Seed, req.FightId, hero, req.Bleeding, req.Robbers);
                if (mercy && wb != null) PressWound(wb, sec);
                break;
            case DefeatKind.Captured when req.Winner == WinnerKind.Bandits:
            {
                Rob(false);
                var band = AftermathCoin.BandId(req.FightId);
                book.Capture(R, CaptorKind.Bandits, hero, true, band, new PurseRef(PurseKind.Household, band), sec, CaptiveRank.Wanderer, ransom, payer, friends, [], Skills(state, party),
                    party.Squad.ActiveCount, req.PlaceX, req.PlaceY);
                break;
            }
            case DefeatKind.Captured:
            {
                // an army: weapons, armour and tools into the detachment's stash; the under layer stays; the purse stays and pays the ransom (with friends')
                var det = AftermathCoin.DetachmentId(req.FightId);
                var holdings = Robbery.Holdings(content, e, party.Carried, 70000, book.ValueOf);
                var stashPlan = new RobberyPlan(0, coin, [.. holdings.Where(i => i.Kind is LootKind.Weapon or LootKind.Armour or LootKind.Tool && i.Id != EntityId.None)], [], 0, 0);
                var moved = Robbery.Apply(e, party.Carried, heroPurse, heroPurse, det, stashPlan);
                taken.AddRange(stashPlan.Taken.Select(i => i.Shape));
                ledger.Open(PurseKind.Chest, det);
                var payers = new List<PurseRef> { new(PurseKind.Person, hero) };
                book.Capture(R, CaptorKind.Army, hero, true, det, new PurseRef(PurseKind.Chest, det), sec, CaptiveRank.Wanderer, ransom, PayerKind.Household, payers, moved, Skills(state, party),
                    party.Squad.ActiveCount, req.PlaceX, req.PlaceY);
                break;
            }
            case DefeatKind.Ignored:
            {
                // guards (Phase 6 stub): a fine paid at the gate to the town's toll purse, then released
                long want = Math.Min(coin, R.D.GuardFineMites);
                var toll = state.Markets?.AllBooks().FirstOrDefault()?.TollPurse;
                if (want > 0 && toll is { } to && to != heroPurse && ledger.Transfer(heroPurse, to, Coin.FromMites(want), AftermathCoin.Plunder)) fine = want;
                coinLeft = ledger.Balance(heroPurse).Mites;
                break;
            }
        }
        book.Publish(R, ledger, sec);
        var rep = new DefeatReport(outcome.Kind, req.Winner, outcome.Style, coinTaken, coinLeft, taken, left, mercy, wb?.Records.Count ?? 0, req.Bleeding, needsDoctor, fine, sec);
        book.LastDefeat = rep;
        return rep;
    }

    private static (int, int, int, int, int) Skills(Save.WorldState state, HeroParty party)
    {
        var h = state.Hero;
        int S(People.Skill s) => h?.Skill(Characters.HeroRecord.SkillId(s)) ?? 0;
        int per = h?.Attribute(Characters.HeroAttr.Perception) ?? 50;
        return (S(People.Skill.Stealth), S(People.Skill.Athletics), S(People.Skill.Lockpicking), per, S(People.Skill.Speech));
    }

    /// <summary>The mercy bandage: the worst bleeder dressed.</summary>
    private static void PressWound(WoundBook b, long sec)
    {
        b.AdvanceTo(sec);
        int i = Treatment.WorstBleeder(b, -1, sec);
        if (i < 0) return;
        var wd = b.Records[i];
        wd.Dressing = Dressing.Bandage; wd.DressFromS = sec / MedicineTables.TicksPerSimS; wd.DressedAt = sec; wd.State = WoundState.Dressed;
        WoundBook.Retime(ref wd); b.Records[i] = wd;
    }
}
