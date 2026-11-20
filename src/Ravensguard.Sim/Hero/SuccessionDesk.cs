using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Succession;

/// <summary>
/// The hero's death settled in play (morale-capture-succession 4.8; P4 close-out), at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Succession"/>): "su:heir|person|x|y"
/// makes that candidate the hero, "su:end|x|y" ends the run (nobody is left; the world persists). The estate is the body's: every item row and stack the hero carried passes to an estate owner
/// that lies where the hero fell (<see cref="Estate"/>), the coin stays with the hero's purse less the 5% funeral to the lord of the nearest settlement (the heir takes the purse up, so no coin
/// moves but the fee). The heir's person row leaves the region's records (<see cref="Demography.Depart"/>: the heir is the hero now, not a villager), the record is built from the row
/// (<see cref="SuccessionOrder.HeroFrom"/>), the heir stands in the clothes of their class with a pouch and a satchel, kin and the loyal stay in the squad and the rest go, and the heir's own
/// kin become the hero's. Pure in the tables and the command (no draw: stream 0x457 stays unused).
/// </summary>
public static class SuccessionDesk
{
    public const string HeirPrefix = "su:heir|", EndPrefix = "su:end|";
    /// <summary>The ambition of an heir who is not kin (inheritance 4.3: "find your place").</summary>
    public const string DefaultAmbition = "find_your_place";
    /// <summary>Estate owners are ids of kind Other from this base (one per estate), clear of the Phase 2 business and market bases.</summary>
    public const ulong EstateBase = 0x0E57_0000_0000UL;

    public static EntityId EstateOwner(int estateId) => EntityId.Make(IdKind.Other, EstateBase + (ulong)estateId);

    public static HeroCommand HeirCommand(EntityId heir, long placeX, long placeY, long sec) =>
        new() { Kind = HeroCommandKind.Succession, IssuedSec = sec, Tag = string.Create(CultureInfo.InvariantCulture, $"{HeirPrefix}{heir.Value}|{placeX}|{placeY}"), Origin = CommandOrigin.Human };
    public static HeroCommand EndCommand(long placeX, long placeY, long sec) =>
        new() { Kind = HeroCommandKind.Succession, IssuedSec = sec, Tag = string.Create(CultureInfo.InvariantCulture, $"{EndPrefix}{placeX}|{placeY}"), Origin = CommandOrigin.Human };

    /// <summary>The squad as the order reads it.</summary>
    public static List<SquadMember> SquadOf(HeroParty party, PersonTable pt) =>
        [.. party.Squad.Active.Select(m => new SquadMember(m.Person, pt.IndexOf(m.Person), m.Loyalty, m.JoinedDay, m.Origin == FollowerOrigin.Recruited, m.Name))];

    /// <summary>Who would continue, in order (the character screen's line, the death screen's list).</summary>
    public static List<SuccessionCandidate> Candidates(Save.WorldState st, HeroParty party, int day)
    {
        var pt = st.Population.People;
        return HeroKin.OrderForHero(pt, st.Succession?.Kin ?? [], day, SquadOf(party, pt), (i, id) => st.Captivity?.Find(id) != null);
    }

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0, long qty = 0) => new(c.Seq, c.Kind, code, text, mites, qty, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        bool end = c.Tag.StartsWith(EndPrefix, StringComparison.Ordinal);
        if (!end && !c.Tag.StartsWith(HeirPrefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var parts = c.Tag[(end ? EndPrefix : HeirPrefix).Length..].Split('|');
        long P(int i) => i < parts.Length && long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var st = ctx.State; var e = ctx.Econ; var pt = st.Population.People;
        var book = st.Succession ??= new SuccessionBook();
        if (book.RunEnded) return R(c, HeroResultCode.Refused, HeroText.Of("su_run_over"));
        int day = (int)(c.IssuedSec / 86400);
        SuccessionCandidate? heir = null;
        if (!end)
        {
            var who = new EntityId((ulong)P(0));
            heir = Candidates(st, ctx.Party, day).FirstOrDefault(x => x.Person == who);
            if (heir == null) return R(c, HeroResultCode.Refused, HeroText.Of("su_not_heir"));
        }
        long x = P(end ? 0 : 1), y = P(end ? 1 : 2);
        // the funeral: to the treasury of the lord of the settlement nearest where the hero fell (region millimetres)
        var region = st.Population.Region;
        int near = -1; long bd = long.MaxValue;
        for (int s = 0; s < region.Settlements.Count; s++) { long dx = region.Settlements[s].CenterX * 1000L - x, dy = region.Settlements[s].CenterY * 1000L - y; long d = dx / 1000 * (dx / 1000) + dy / 1000 * (dy / 1000); if (d < bd) { bd = d; near = s; } }
        int fief = near >= 0 ? region.Settlements[near].FiefIndex : -1;
        var funeral = fief >= 0 && fief < region.Fiefs.Count ? e.Ledger.Open(PurseKind.Treasury, region.Fiefs[fief].Id) : ctx.Purse;
        // what the body carried, in row order; the best weapon left is the heir's quest hint
        var rows = ctx.Party.Carried.Rows(e).ToList();
        var items = rows.Select(r => e.Items.At(r).Id).ToList();
        string recover = "";
        long bestValue = -1;
        foreach (int r in rows)
        {
            var shape = e.Items.At(r).Shape;
            if (!ctx.Content.TryGet<WeaponShapeDef>(shape, out _)) continue;
            long v = ctx.Factory.ValueMites(e.Items, e.Items.At(r).Id);
            if (v > bestValue) { bestValue = v; recover = shape; }
        }
        var (est, rec) = SuccessionOrder.Settle(book, e.Ledger, ctx.Party.Hero, ctx.Purse, heir, ctx.Purse, funeral, items, [], day, x, y, recover);
        // the estate lies where the hero fell: the rows and the stacks pass to its owner
        var owner = EstateOwner(est.Id);
        foreach (int r in rows) e.Items.At(r).Owner = owner;
        var pack = ctx.Pack; var chest = e.OpenStore(StoreKind.Chest, owner, 0, 0, false);
        foreach (var (g, q) in pack.Rows()) if (q > 0) Store.Move(pack, chest, g, q);
        var carried = ctx.Party.Carried;
        carried.Equip.Clear(); carried.GoodIn.Clear(); Array.Clear(carried.QuickBar);
        string deadName = st.Hero?.Name ?? "";
        // P5-A4: the dead hero's person row is dead (the row stays, as every dead person's does); the household the hero headed passes to its eldest member
        int deadRow = HeroPerson.RowOf(pt, st.Hero, ctx.Party.Hero);
        if (deadRow >= 0)
        {
            pt.Status[deadRow] = PersonStatus.Dead; pt.DeathDay[deadRow] = day; pt.DeathCause[deadRow] = DeathCause.Other;
            int sp = pt.Spouse[deadRow]; if (sp >= 0 && sp < pt.Count && pt.Spouse[sp] == deadRow) pt.Spouse[sp] = -1;
            var hht = st.Population.Households; int hhd = pt.Household[deadRow];
            if (hhd >= 0 && hhd < hht.Count && hht.Head[hhd] == deadRow)
                hht.Head[hhd] = hht.Members(hhd).Where(m => m >= 0 && m < pt.Count && pt.Status[m] == PersonStatus.Alive).OrderBy(m => pt.BirthDay[m]).ThenBy(m => m).DefaultIfEmpty(-1).First();
            st.Population.ResetKin();
        }
        // a hero who died held (wounds bleed on in captivity, review finding 3): the captive is dead, so the heir, who takes up the same hero id, is not held
        if (st.Captivity is { } cb && cb.Find(ctx.Party.Hero) is { Held: true } cap)
        {
            cap.State = Combat.Aftermath.CaptiveState.Dead; cap.EndedSec = c.IssuedSec;
            if (cb.Controlled != EntityId.None) cb.ReleaseControl();
            cb.Publish(cb.Rules, e.Ledger, c.IssuedSec);
        }
        // P5-H (inheritance 4.2): the estate settles: debts from the coin, then the chest, then the body's goods at bid; every holding to its heir by the will and the law (the play heir keeps
        // the hero's id, so what he inherits stays put), the rest to kin or the lord; the deeds the play heir holds are on the record
        if (deadRow >= 0 || heir != null)
        {
            bool kin = heir != null && heir.Rule is SuccessionRule.Child or SuccessionRule.Spouse or SuccessionRule.Sibling or SuccessionRule.YoungChild or SuccessionRule.Parent;
            var ew = EstateWiring.For(st, ctx.Content);
            var deeds = ew.SettleHero(est, ctx.Party.Hero, deadRow, heir, kin, ctx.Purse, day, c.IssuedSec, heir?.PersonIndex ?? -1, [chest],
                i => i >= 0 && i < pt.Count && st.Captivity?.Find(pt.IdOf(i)) is { Held: true });
            if (rec != null) { rec.Deeds.Clear(); rec.Deeds.AddRange(deeds); rec.Coin = e.Ledger.Balance(ctx.Purse).Mites; }
        }
        if (heir == null)
        {
            st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(c.IssuedSec), "hero_line_ended", [ctx.Party.Hero], [],
                new Dictionary<string, string> { ["name"] = deadName }));
            return R(c, HeroResultCode.Ok, HeroText.Of("su_ended", deadName));
        }
        // the heir becomes the hero
        int row = heir.PersonIndex;
        HeroRecord hero;
        // the heir's ambition (4.3): the dead hero's goes on with kin (the family's goal), else the heir's own default
        bool kinHeir = heir.Rule is SuccessionRule.Child or SuccessionRule.Spouse or SuccessionRule.Sibling or SuccessionRule.YoungChild or SuccessionRule.Parent;
        string ambition = kinHeir && st.Hero is { AmbitionId.Length: > 0 } old ? old.AmbitionId : DefaultAmbition;
        if (row >= 0 && row < pt.Count) hero = SuccessionOrder.HeroFrom(pt, row, heir.Name, ambition);
        else hero = new HeroRecord { Name = heir.Name, Origin = "heir", Attributes = [50, 50, 50, 50, 50], Skills = HeroRecord.SkillIds.ToDictionary(s => s, _ => 0) };
        // P6-A3 (reputation 4.4): a kin heir starts at half of the dead hero's name (every standing row and renown), a companion at nothing, and marks never pass; the record lists the groups carried
        if (st.Standing is { } standing) rec!.ReputationPending = standing.Inherit(ctx.Party.Hero, heir.Person, kinHeir);
        bool heroRows = deadRow >= 0;
        var stay = SuccessionOrder.Stay(SquadOf(ctx.Party, pt), HeroKin.People(book.Kin), heir.Person);
        foreach (var m in ctx.Party.Squad.Members)
            if (m.Active && (m.Person == heir.Person || !stay.Contains(m.Person))) m.State = m.Person == heir.Person ? HireState.Ended : HireState.Dismissed;
        book.Kin.Clear();
        if (row >= 0 && row < pt.Count)
        {
            book.Kin.AddRange(HeroKin.FromRow(pt, row));
            if (st.HomeRecords?.Sim is { } sim && pt.Status[row] == PersonStatus.Alive)
            {
                var ob = new RegionOutbox(st.Ids);
                Demography.Depart(sim, row, new GameTime(c.IssuedSec), ob);
                ob.ApplyTo(st.Chronicle, null, new Dictionary<string, long>(StringComparer.Ordinal));
            }
            // P5-A4: in a world where the hero is a row, the heir's row is the hero's now (not a villager who left: never brought back by migration)
            if (heroRows && pt.Status[row] is PersonStatus.Alive or PersonStatus.Departed) { pt.Status[row] = PersonStatus.Hero; hero.PersonIndex = row; HeroPerson.Mirror(pt, row, hero); }
        }
        st.Hero = hero;
        if (st.Crafting is { } cr) { cr.Book.Forget(ctx.Party.Hero); cr.Know.Forget(ctx.Party.Hero); }      // P5-I: the heir takes up the id, not the dead hero's craft: his skills come from his own record
        Array.Clear(ctx.Party.AttrMilli);
        if (st.Wounds is { } wl) lock (wl.Sync) wl.Books.Remove(ctx.Party.Hero.Value);      // the dead body's wounds are not the heir's
        foreach (var (shape, material) in HeroOutfit.For(ctx.Content, hero, c.IssuedSec))
            carried.EquipItem(e, ctx.Rules, carried.MakeItem(e, ctx.Content, ctx.Rules, ctx.Factory, st.Ids, shape, material));
        foreach (var shape in new[] { "belt_pouch", "satchel" }) carried.EquipItem(e, ctx.Rules, carried.MakeItem(e, ctx.Content, ctx.Rules, ctx.Factory, st.Ids, shape));
        st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(c.IssuedSec), "hero_succession", [ctx.Party.Hero, heir.Person], [],
            new Dictionary<string, string> { ["name"] = deadName, ["heir"] = heir.Name, ["rule"] = heir.Rule.ToString(), ["coin"] = rec!.Coin.ToString(CultureInfo.InvariantCulture) }));
        return R(c, HeroResultCode.Ok, HeroText.Of("su_continues", heir.Name), rec.Coin, 1);
    }

    /// <summary>
    /// The estate as the succession screen shows it before it settles (inheritance 4.3, 6): what <paramref name="heir"/> (none: the line ends) would receive, what goes to other kin and to the lord,
    /// the coin after the funeral and the debts. A pure read of the tables (IN-11: the same after a save and a load).
    /// </summary>
    public static EstateSummary Estate(Save.WorldState st, HeroParty party, ContentDatabase content, SuccessionCandidate? heir, int day)
    {
        if (st.Economy is not { } e) return EstateSummary.Empty;
        var pt = st.Population.People;
        int row = HeroPerson.RowOf(pt, st.Hero, party.Hero);
        if (row < 0 && heir == null) return EstateSummary.Empty;
        bool kin = heir != null && heir.Rule is SuccessionRule.Child or SuccessionRule.Spouse or SuccessionRule.Sibling or SuccessionRule.YoungChild or SuccessionRule.Parent;
        var purse = e.Ledger.TryFind(PurseKind.Person, party.Hero, out var p) ? p : (PurseId?)null;
        return EstateWiring.For(st, content).Summary(party.Hero, row, true, heir == null ? null : new Recipient(heir.PersonIndex, party.Hero, party.Hero, PurseKind.Person, true, false, heir.Name), kin, purse, day,
            unavailable: i => i >= 0 && i < pt.Count && st.Captivity?.Find(pt.IdOf(i)) is { Held: true });
    }

    /// <summary>The Ledger with the last estate the hero's line settled (P5-H, inheritance 6: the dead's name, the holdings it passed, its debts and what they still lack).</summary>
    public static Ravensguard.Sim.Production.LedgerView WithEstate(Save.WorldState st, Ravensguard.Sim.Production.LedgerView v)
    {
        if (st.Succession is not { } b || b.Records.Count == 0) return v;
        var rec = b.Records[^1];
        var est = b.Estates.FirstOrDefault(e => e.Id == rec.EstateId);
        if (est == null) return v;
        int row = st.Population.People.IndexOf(est.Dead);
        string name = row >= 0 ? st.Population.People.FullName(row) : "";
        return v with { EstateOf = name, EstateLines = est.Holdings.Count, EstateDebts = est.DebtsMites, EstateUnpaid = est.UnpaidMites };
    }

    /// <summary>Where an heir who was a person row lives (region millimetres: the centre of their home settlement), for the game to put the hero there; null when unknown.</summary>
    public static (long X, long Y)? HomeOf(Save.WorldState st, int row)
    {
        var pt = st.Population.People; var region = st.Population.Region;
        if (row < 0 || row >= pt.Count) return null;
        int s = pt.HomeSettlement[row];
        if (s < 0 || s >= region.Settlements.Count) return null;
        return (region.Settlements[s].CenterX * 1000L, region.Settlements[s].CenterY * 1000L);
    }
}
