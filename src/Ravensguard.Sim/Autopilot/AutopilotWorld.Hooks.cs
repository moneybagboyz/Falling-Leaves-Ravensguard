using Ravensguard.Content;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Autopilot;

/// <summary>
/// P8-AT-2 wiring: the game's own <see cref="MeansHook"/> and <see cref="FactsHook"/>. The facts are read from the books (trust from the people the hero knows, reputation from the standing book, rank and fief rank from the
/// bond, charter and rule books); the means issue the commands of the desks (talk, buy an item at the market, order a piece at a maker, petition for freedom or a charter) with origin Autopilot, applied at the next barrier.
/// Everything is read in stable-id order, so it gives the same answers at any speed and across a save.
/// </summary>
public sealed partial class AutopilotWorld
{
    /// <summary>Installs the game's two hooks (a hook already set, by a test or a mod, is kept).</summary>
    public void InstallAmbitionHooks()
    {
        FactsHook ??= ReadWorldFacts;
        MeansHook ??= DoDeskMeans;
    }

    /// <summary>The person kinds the ambition content names, from a person's row: the occupation's name, with the lord, the liege and the abbot read off class and office.</summary>
    private static IEnumerable<string> KindsOf(PersonTable p, int row)
    {
        var occ = p.Occupation[row];
        yield return occ.ToString().ToLowerInvariant();
        if (p.Class[row] == SocialClass.Gentry || occ is Occupation.Lord) { yield return "lord"; yield return "liege"; }
        if (occ is Occupation.Priest or Occupation.Monk) yield return "abbot";
    }

    private void ReadWorldFacts(EntityId person, AmbitionFacts f)
    {
        if (person != HeroId) return;
        var pt = State.Population.People;
        if (State.Party is { } party)
            foreach (var (id, k) in party.Known)                                                            // sorted by id
            {
                int row = pt.IndexOf(id); if (row < 0) continue;
                foreach (var kind in KindsOf(pt, row)) f.Trust[kind] = Math.Max(f.Trust.GetValueOrDefault(kind, int.MinValue), k.TrustPm / 10);
            }
        if (State.Standing is { } sb)
            foreach (var (g, r) in sb.RowsOf(HeroId))
            {
                int v = r.ValuePm / 10; string scope = g.Kind == GroupKind.Realm ? "region" : "district";
                f.Reputation[scope] = Math.Max(f.Reputation.GetValueOrDefault(scope, int.MinValue), v);
                if (scope == "district") f.Reputation["region"] = Math.Max(f.Reputation.GetValueOrDefault("region", int.MinValue), v / 2);
            }
        if (State.Bonds?.Of(HeroId) == null) f.Ranks.Add("free_person");
        int fief = 0;
        if (State.Land is { } land) foreach (var c in land.Charters) if (c.Grantee == HeroId && c.State != CharterState.Lapsed) fief = Math.Max(fief, 1);
        if (State.Rule is { } rule) foreach (var fr in rule.Fiefs.Values) if (fr.Holder == (long)HeroId.Value) fief = Math.Max(fief, fr.OverlordFief >= 0 ? 1 : 2);
        f.FiefRank = fief;
        if (fief >= 1) f.Ranks.Add("landholder");
        if (fief >= 2) f.Ranks.Add("baron");
    }

    private ConditionRefDef? StepCondition(MeansDef m)
    {
        var h = Ctx.Hero; if (h == null || h.AmbitionId.Length == 0) return null;
        var step = h.CurrentStep(Tree.Get(h.AmbitionId)); if (step == null) return null;
        return step.Conditions.FirstOrDefault(c => c.Id == m.Condition);
    }

    private bool DoDeskMeans(EntityId person, MeansDef m)
    {
        if (person != HeroId || StepCondition(m) is not { } cond) return false;
        var st = Book.Get(person); if (st == null) return false;
        long at = MeansAt;
        int here = NearestSettlement(st, PlaceRadiusMm);
        var pt = State.Population.People;
        switch (m.Directive)
        {
            case "talk":
            {
                if (here < 0 || !cond.Args.TryGetValue("kind", out var kind)) return false;
                int min = int.TryParse(cond.Args.GetValueOrDefault("min_trust", "0"), out int mt) ? mt : 0;
                for (int row = 0; row < pt.Count; row++)
                {
                    if (!pt.IsAlive(row) || pt.HomeSettlement[row] != here || !KindsOf(pt, row).Contains(kind)) continue;
                    var id = pt.IdOf(row);
                    if (State.Party != null && State.Party.Known.TryGetValue(id, out var k) && min > 0 && k.TrustPm / 10 >= min) continue;
                    IssueCmd(new HeroCommand { Kind = HeroCommandKind.Talk, Person = id, Settlement = here, IssuedSec = at, Origin = CommandOrigin.Autopilot });
                    return true;
                }
                return false;
            }
            case "buy":
            {
                if (here < 0 || !cond.Args.TryGetValue("item", out var item) || Ctx.Book(here) is not { } book) return false;
                var items = Ctx.Econ.Items;
                foreach (int row in items.LiveRows())
                {
                    var (shape, owner, rid) = (items.At(row).Shape, items.At(row).Owner, items.At(row).Id);
                    if (shape != item || book.Merchants.All(x => x.Id != owner)) continue;
                    IssueCmd(new HeroCommand { Kind = HeroCommandKind.BuyItem, Item = rid, Settlement = here, IssuedSec = at, Origin = CommandOrigin.Autopilot });
                    return true;
                }
                return false;
            }
            case "craft":
            {
                if (here < 0 || !cond.Args.TryGetValue("item", out var item)) return false;
                var w = Ctx.CraftContext;
                foreach (var r in Ctx.Recipes.Recipes)
                {
                    if (!r.Results.Any(x => x.Shape == item)) continue;
                    var maker = Commissions.Makers(w, r, here).FirstOrDefault(); if (maker == EntityId.None) continue;
                    IssueCmd(CraftDesk.Order(r, maker, true, 1, [], here, 0, at) with { Origin = CommandOrigin.Autopilot });
                    return true;
                }
                return false;
            }
            case "petition":
            {
                if (cond.Id == "has_rank" && cond.Args.GetValueOrDefault("rank") == "free_person")
                {
                    if (State.Bonds?.Of(HeroId) == null) return false;
                    IssueCmd(BondDesk.SelfBuy(at) with { Origin = CommandOrigin.Autopilot }); return true;
                }
                var fiefs = State.Population.Region.Fiefs;                                                   // a charter, petitioned at the seat of the fief whose seat the hero stands in
                if (here < 0 || State.Land == null) return false;
                for (int fi = 0; fi < fiefs.Count; fi++)
                    if (fiefs[fi].SeatSettlement == here)
                    {
                        IssueCmd(LandDesk.Petition(GrantorKind.Fief, fi, here, (int)(st.PosX / 1000), (int)(st.PosY / 1000), at) with { Origin = CommandOrigin.Autopilot });
                        return true;
                    }
                return false;
            }
        }
        return false;
    }
}
