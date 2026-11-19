using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Economy.Markets;

/// <summary>
/// A caravan ambushed away from the player (caravans spec 3.4 through abstract-battles-v1 4.1, 5): the crew (guards kitted as the <c>guard</c> row, drivers as <c>traveller</c>)
/// against the band (<c>bandit_light</c>) in an abstract battle, instead of the strength-odds roll. The dead are the battle's dead (killed with the chain of how and where); the
/// wounded keep their wound books in the world's ledger (the hourly wound step bleeds and heals them); a won fight lets the caravan go on, a lost one is robbed as before. The
/// report goes to the chronicle as word that reached the caravan's town. Only where the world has the <see cref="AbstractBattleService"/>; elsewhere the older roll stands.
/// </summary>
public sealed partial class ContinentTrade
{
    /// <summary>The ambush fought out. Returns true when the caravan held the road.</summary>
    private bool AmbushBattle(TickContext ctx, ContinentState st, Caravan c, GameTime time, int band, AbstractBattleService ab)
    {
        var w = _ew.World; var pt = w.People;
        var fc = ab.Fight;
        var guard = FightSetup.KitDef(fc, "guard", "guard"); var driver = FightSetup.KitDef(fc, "traveller", "traveller"); var bandit = FightSetup.KitDef(fc, "bandit_light", "bandit_light");
        ulong seed = ctx.Seed ^ DetRng.Mix(c.Id.Value + 0x4A0UL);
        var victims = new List<FighterKit>();
        var rows = new List<int>();
        int drivers = c.Crew.Length - c.Guards;
        for (int i = 0; i < c.Crew.Length; i++)
        {
            int p = c.Crew[i];
            if (pt.Status[p] != People.PersonStatus.Alive) continue;
            var id = pt.IdOf(p);
            victims.Add(FightSetup.Roll(fc, i >= drivers ? guard : driver, seed, id.Value, pt.FullName(p), 0, (i % 4) * 1200, (i / 4) * 1500, FightMath.One, 0) with { Stance = "balanced" });
            rows.Add(p);
        }
        if (victims.Count == 0) return false;
        var robbers = new List<FighterKit>();
        for (int i = 0; i < band; i++) robbers.Add(FightSetup.Roll(fc, bandit, seed, 0x4A00_0000UL + (ulong)i, $"bandit {i + 1}", 1, 0, 0, -FightMath.One, 0) with { Person = 0 });
        ulong battleId = (ulong)ctx.Hour * 4096 + (c.Id.Value & 4095);
        string town = w.Population.Region.Settlements[c.Settlement].Name;
        var setup = FightSetup.Ambush(victims, robbers, seed, battleId, ctx.Hour * 3600, "caravan_ambush") with { Terrain = new FightTerrain { Base = Footing.Road } };
        var ledger = w.Get<WoundLedger>();
        WoundBook? BookOf(ulong person) { if (ledger == null) return null; lock (ledger.Sync) return ledger.Find(person); }
        var resolver = new AbstractBattles { BookOf = BookOf, Place = $"the road from {town}" };
        var rep = resolver.Resolve(setup, fc);
        var br = rep.Battle!;
        // the barrier's commands, in person order: wound books into the ledger, the dead killed with their chain
        var killed = new List<int>();
        string cause = $"bandits ({band}) ambushed a caravan on the road from {town}";
        foreach (var cmd in rep.Commands)
        {
            int p = pt.IndexOf(new EntityId(cmd.Person));
            if (p < 0) continue;
            if (cmd.Kind == BattleCommandKind.Wounds && cmd.Book != null && ledger != null && !cmd.Book.Dead)
                lock (ledger.Sync) ledger.Books[cmd.Person] = cmd.Book;
            if (cmd.Kind == BattleCommandKind.Status && cmd.Status == SoldierStatus.Dead && pt.Status[p] == People.PersonStatus.Alive)
            {
                var n = br.Notables.FirstOrDefault(x => x.Person == cmd.Person);
                var book = rep.Commands.FirstOrDefault(x => x.Person == cmd.Person && x.Kind == BattleCommandKind.Wounds)?.Book;
                var dc = book is { Dead: true } ? WoundSystem.CauseOf(book.Death) : People.DeathCause.Violence;
                Demography.Kill(w, p, dc, time, ctx.Outbox, BattleChronicle.DeathChain(n, br, cause));
                killed.Add(p);
            }
        }
        bool held = br.Winner == 0;
        BattleChronicle.Report(ctx.Outbox, time, br, c.Id, town, "the caravan's guards", "bandits", new Cause(CauseKind.Raid, null, cause));
        if (held)
        {
            ctx.Outbox.Count("caravan_ambush_won");
            int guards = c.Crew.Skip(drivers).Count(p => pt.Status[p] == People.PersonStatus.Alive);
            c.Crew = [.. c.Crew.Where(p => pt.Status[p] == People.PersonStatus.Alive)]; c.Guards = guards;
            if (c.Crew.Length == 0) { Lose(ctx, st, c, time); return false; }
            return true;
        }
        Robbed(ctx, st, c, time, band, killed);
        return false;
    }
}
