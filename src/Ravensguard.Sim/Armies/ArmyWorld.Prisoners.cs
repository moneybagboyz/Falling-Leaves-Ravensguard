using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Soldiers;

namespace Ravensguard.Sim.Armies;

public sealed partial class ArmyWorld
{
    /// <summary>What a captor of the armies is like when it comes to the men he holds (live-war 3.4); the world has no trait for it yet, so every captor is just and paroles the common prisoners. A test or the game may install another.</summary>
    public Func<ulong, Commander> TemperOf { get; set; } = _ => Commander.Just;

    /// <summary>The rank class of a captive for his ransom (4.7): a knight's row, a man-at-arms' or sergeant's, a gentleman or burgher by class, else a levy.</summary>
    private CaptiveRank RankOf(ulong person, int row)
    {
        var r = Soldiers?.Book.Of(person)?.Role;
        if (r == "knight") return CaptiveRank.Knight;
        if (r is "man_at_arms" or "sergeant") return CaptiveRank.ManAtArms;
        if (r != null) return CaptiveRank.Levy;
        return People.Class[row] switch { SocialClass.Gentry => CaptiveRank.Knight, SocialClass.Burgher => CaptiveRank.ManAtArms, _ => CaptiveRank.Levy };
    }

    /// <summary>
    /// P8-LW-4 (live-war 3.4): every battle's result calls <see cref="PrisonerPens.Take"/> for the yielded and the captured, with their rank class: the pen of <paramref name="pen"/> (a settlement) holds up to a guard to ten (<paramref name="guards"/> men of the
    /// captor's side stand guard), the ransom class asked of the payer's real purse (the household's; a lord's fief is ceded when he cannot pay), the rest let go. The common prisoners of an AI captor are given their fate at once by his temper; the hero's
    /// stay in the pen for him to decide. Nothing is made: a prisoner is a person who was already in the world. Returns null when nobody is taken.
    /// </summary>
    public TakeReport? TakePrisoners(ArmyEnv e, ulong battle, ulong captor, int pen, int guards, IEnumerable<ulong> captives, int war = -1)
    {
        var sw = Soldiers; var cc = Content; if (sw == null || cc == null) return null;
        var pt = People; var eco = W.Economy; var rules = AftermathRules.For(cc); var rb = W.Get<Ravensguard.Sim.Rule.RuleBook>();
        var taken = new List<Taken>();
        foreach (ulong p in captives.Distinct().OrderBy(x => x))
        {
            int r = RowOf(p); if (r < 0 || pt.Status[r] != PersonStatus.Alive || p == captor || sw.Book.Prisoners.ContainsKey(p)) continue;
            var rank = RankOf(p, r); int hh = pt.Household[r];
            var purse = eco != null && hh >= 0 && hh < eco.HouseholdPurse.Length ? eco.HouseholdPurse[hh] : default;
            bool lord = rb != null && rb.Fiefs.Values.Any(f => f.Holder == (long)p && !f.Forfeited);
            long value = PrisonerPens.ValueOf(rules, new Taken(p, rank));
            bool means = !purse.IsNone && eco!.Ledger.Balance(purse).Mites >= value;
            taken.Add(new Taken(p, rank, means, purse.IsNone ? -1 : purse.Index, lord));
        }
        if (taken.Count == 0) return null;
        int day = (int)e.Day; var chron = e.Chronicler;
        var rep = PrisonerPens.Take(sw, rules, chron, W.Seed, battle, day, (long)captor, -1, pen, sw.PurseOf(captor), guards, TemperOf(captor), taken);
        if (rep.Held > 0 && !IsHero(captor)) PrisonerPens.Settle(sw, chron, W.Seed, day, (long)captor, -1, TemperOf(captor), saleLegal: false, exchangeWar: war);
        Book.Touch();
        return rep;
    }

    /// <summary>The persons of a battle's commands who yielded or were captured, among <paramref name="men"/> (the loser's roll).</summary>
    internal static List<ulong> Yielded(IEnumerable<Ravensguard.Sim.Combat.Abstract.BattleCommand> cmds, IReadOnlyCollection<ulong> men)
    {
        var set = men as ISet<ulong> ?? men.ToHashSet();
        return [.. cmds.Where(c => c.Kind == Ravensguard.Sim.Combat.Abstract.BattleCommandKind.Status && c.Status is Ravensguard.Sim.Combat.Abstract.SoldierStatus.Captured or Ravensguard.Sim.Combat.Abstract.SoldierStatus.Surrendered && set.Contains(c.Person)).Select(c => c.Person).Distinct().OrderBy(x => x)];
    }
}
