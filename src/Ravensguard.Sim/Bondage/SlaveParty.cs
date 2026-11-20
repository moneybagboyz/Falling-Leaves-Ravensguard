using Ravensguard.Content;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Bondage;

/// <summary>What a slave of the hero's party does when a fight begins (overview 10a): stands and fights (armed), stands back (unarmed), or bolts (unchained and under the flee morale).</summary>
public enum SlaveRole : byte { Fights, Stays, Flees }

/// <summary>One slave of the party as a fight sees him: the person, the weapon he was armed with (empty when none), his morale and his role.</summary>
public sealed record SlaveStand(EntityId Person, string Name, string WeaponShape, string WeaponMaterial, int Morale, SlaveRole Role);

/// <summary>
/// The hero's slaves in his party (overview 10a, slavery 3 "Own": a party member at follower morale, flees below 40 unless chained, fights only if armed by the owner). A slave whose bond says <see cref="BondWork.Follow"/> is a
/// <see cref="Follower"/> of the squad (origin <see cref="FollowerOrigin.Slave"/>, no wage), so the live street walks him behind the hero like any companion; <see cref="Sync"/> keeps the two in step. The rules here are pure
/// integer functions of the bond: <see cref="RoleOf"/> is the one place that says who fights and who runs. A slave who runs becomes a runaway through the daily pass's own path (<see cref="BondDaily.Flee"/>: the
/// bounty, the catchers, the free town), and leaves the squad. "Armed" means the slave owns a weapon item row (the owner hands one over with <see cref="Arm"/>; nothing is made or lost, the row changes owner).
/// </summary>
public static class SlaveParty
{
    /// <summary>The one rule: an unchained slave under the flee morale bolts (the same test the daily runaway roll uses); otherwise he fights if armed and stands back if not.</summary>
    public static SlaveRole RoleOf(Bond b, bool armed, int fleeBelow) =>
        b.Chain == Restraint.None && b.Morale < fleeBelow ? SlaveRole.Flees : armed ? SlaveRole.Fights : SlaveRole.Stays;

    /// <summary>The first weapon row the person owns (item row order): its shape and material; false when he carries none.</summary>
    public static bool WeaponOf(EconomyState e, ContentDatabase db, EntityId person, out EntityId item, out string shape, out string material)
    {
        item = EntityId.None; shape = ""; material = "";
        foreach (int i in e.Items.LiveRows())
        {
            ref var r = ref e.Items.At(i);
            if (r.Owner != person || !db.TryGet<WeaponShapeDef>(r.Shape, out _)) continue;
            item = r.Id; shape = r.Shape;
            material = r.ComponentCount > 0 ? e.Items.Component(r.ComponentStart).Material : Characters.HeroRecord.DefaultMaterial(r.Shape);
            return true;
        }
        return false;
    }

    /// <summary>Keeps the squad and the bonds in step (idempotent): every slave the hero owns whose work is Follow (not in a pen, not on the run, alive) is an active member; a slave member whose bond no longer says so leaves.
    /// Returns how many changed. A world without a bond book does nothing.</summary>
    public static int Sync(BondBook? book, Squad squad, PersonTable pt, HouseholdTable? hh, EntityId hero, int day)
    {
        int changed = 0;
        var want = new SortedDictionary<ulong, Bond>();
        if (book != null)
            foreach (var b in book.Bonds.Values)
            {
                if (b.Owner != hero.Value || b.Person == hero.Value || b.InPen || b.OnRun || b.Work != BondWork.Follow) continue;
                int r = pt.IndexOf(new EntityId(b.Person));
                if (r >= 0 && pt.IsAlive(r)) want[b.Person] = b;
            }
        foreach (var m in squad.Members.Where(m => m.Origin == FollowerOrigin.Slave && !want.ContainsKey(m.Person.Value)).ToList()) { squad.Members.Remove(m); changed++; }
        foreach (var (id, b) in want)
        {
            if (squad.Members.Any(m => m.Person.Value == id)) continue;
            int row = pt.IndexOf(new EntityId(id)); int h = pt.Household[row];
            squad.Members.Add(new Follower
            {
                Person = new EntityId(id), PersonIndex = row, Name = pt.FullName(row), Origin = FollowerOrigin.Slave, State = HireState.Active, Loyalty = b.Morale * 10, JoinedDay = day, PaidThroughDay = day,
                Order = SquadOrder.Follow, Household = hh != null && h >= 0 && h < hh.Count ? hh.IdOf(h) : EntityId.None,
            });
            changed++;
        }
        return changed;
    }

    /// <summary>The party's slaves as a fight sees them, in person id order: role by <see cref="RoleOf"/>, the weapon each owns. Read-only.</summary>
    public static List<SlaveStand> Muster(BondBook? book, Squad squad, EconomyState e, ContentDatabase db, BondRules rules)
    {
        var res = new List<SlaveStand>();
        if (book == null) return res;
        foreach (var m in squad.Members.Where(m => m.Active && m.Origin == FollowerOrigin.Slave).OrderBy(m => m.Person.Value))
        {
            if (book.Of(m.Person) is not { InPen: false, OnRun: false } b) continue;
            bool armed = WeaponOf(e, db, m.Person, out _, out var shape, out var mat);
            res.Add(new SlaveStand(m.Person, m.Name, armed ? shape : "", armed ? mat : "", b.Morale, RoleOf(b, armed, rules.D.FleeMorale)));
        }
        return res;
    }

    /// <summary>The kits of the slaves who stand and fight (armed, steady or chained): the weapon they own, the courage of their morale; the caller places them (<c>FightSetup.Encounter</c> puts the party on team 0).</summary>
    public static List<Combat.Melee2.FighterKit> Kits(IEnumerable<SlaveStand> stands) =>
        [.. stands.Where(s => s.Role == SlaveRole.Fights).Select(s => new Combat.Melee2.FighterKit
        {
            Name = s.Name, Person = s.Person.Value, Weapon = new Combat.WeaponInstance(s.WeaponShape, s.WeaponMaterial), Skill = 25, Courage = Math.Clamp(s.Morale, 1, 100),
            Worn = [new("tunic", "wool")],
        })];

    /// <summary>The owner hands a weapon to a slave of his party (the item named, else the spare weapon he carries unequipped, lowest row first): the row changes owner and leaves the hero's slots. A slave holds one weapon.</summary>
    public static SlaveResult Arm(EconomyState e, ContentDatabase db, HeroParty party, BondBook book, EntityId hero, EntityId slave, EntityId item)
    {
        if (book.Of(slave) is not { InPen: false } b || b.Owner != hero.Value || b.Work != BondWork.Follow) return SlaveResult.No("sl_not_in_party");
        if (WeaponOf(e, db, slave, out _, out _, out _)) return SlaveResult.No("sl_already_armed");
        if (item == EntityId.None)
            foreach (int i in e.Items.LiveRows())
            {
                ref var r = ref e.Items.At(i);
                if (r.Owner == hero && !party.Carried.Equip.ContainsKey(r.Id) && db.TryGet<WeaponShapeDef>(r.Shape, out _)) { item = r.Id; break; }
            }
        if (item == EntityId.None || !e.Items.IsLive(item)) return SlaveResult.No("sl_no_spare_weapon");
        ref var row = ref e.Items.Get(item);
        if (row.Owner != hero || !db.TryGet<WeaponShapeDef>(row.Shape, out _)) return SlaveResult.No("sl_no_spare_weapon");
        row.Owner = slave; row.Parent = -1;
        party.Carried.Equip.Remove(item);
        for (int q = 0; q < party.Carried.QuickBar.Length; q++) if (party.Carried.QuickBar[q] == item) party.Carried.QuickBar[q] = EntityId.None;
        return new SlaveResult(true, "sl_armed");
    }

    /// <summary>Takes the slave's weapon back into the hero's pack.</summary>
    public static SlaveResult Disarm(EconomyState e, ContentDatabase db, BondBook book, EntityId hero, EntityId slave)
    {
        if (book.Of(slave) is not { InPen: false } b || b.Owner != hero.Value) return SlaveResult.No("sl_not_yours");
        if (!WeaponOf(e, db, slave, out var item, out _, out _)) return SlaveResult.No("sl_not_armed");
        ref var row = ref e.Items.Get(item); row.Owner = hero; row.Parent = -1;
        return new SlaveResult(true, "sl_disarmed");
    }

    /// <summary>
    /// A slave of the party runs: before the first blow (<paramref name="inFight"/> false: the rule must hold, an unchained slave under the flee morale) or routed in the fight (true: any unchained slave who broke). He leaves the
    /// squad and the daily runaway path takes him (<see cref="BondDaily.Flee"/>). A chained slave cannot run. Returns whether he ran.
    /// </summary>
    public static bool Flee(BondWorld bw, BondBook book, Squad squad, Law.CrimeBook? crimes, EntityId hero, EntityId slave, bool inFight, int day, long sec)
    {
        if (book.Of(slave) is not { InPen: false, OnRun: false } b || b.Owner != hero.Value || b.Work != BondWork.Follow) return false;
        if (b.Chain != Restraint.None) return false;
        if (!inFight && b.Morale >= bw.D.FleeMorale) return false;
        BondDaily.Flee(bw, book, crimes, b, day, sec);
        b.Work = BondWork.Idle; b.Morale = Math.Max(0, b.Morale - 5);
        squad.Members.RemoveAll(m => m.Person == slave && m.Origin == FollowerOrigin.Slave);
        return true;
    }

    /// <summary>What a fight does to the nerve of the party's slaves: a held field steadies them (+3), a lost one shakes them (-10).</summary>
    public static void AfterFight(BondBook book, Squad squad, bool heroSideWon)
    {
        foreach (var m in squad.Members.Where(m => m.Active && m.Origin == FollowerOrigin.Slave))
            if (book.Of(m.Person) is { } b) b.Morale = Math.Clamp(b.Morale + (heroSideWon ? 3 : -10), 0, 100);
    }
}
