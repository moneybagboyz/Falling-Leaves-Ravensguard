using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// The running world as the Phase 5 crafting rules see it (crafting-full 4.6 to 4.8, P5-E): a person's purse is his household's (the hero's his own), a maker's store and site are those of the
/// business he owns or works in (its stations by the unified vocabulary, the trade's tools), the people of a place are its living adults, a guild town is a <see cref="SettlementClass.Town"/>,
/// and the witnesses of an offence are up to three of the trades' people there. Built once per hero context on the tick worker; every list is in id order.
/// </summary>
public static class CraftWiring
{
    public static CraftContext For(WorldState state, EconomyState e, CraftRunner runner, EntityId hero, PurseId heroPurse, Func<Store> pack)
    {
        var pop = state.Population; var people = pop.People; var region = pop.Region;
        int Day() => (int)(state.Clock.Now.Seconds / 86400);
        Business? WorkplaceOf(int i)
        {
            if (state.Production is not { } prod) return null;
            foreach (var b in prod.Businesses) if (b.Owner == i || b.Staff.Contains(i)) return b;
            return null;
        }
        CraftSite? Site(EntityId p, CraftRecipe r)
        {
            int i = people.IndexOf(p); if (i < 0 || p == hero) return null;
            var b = WorkplaceOf(i); if (b == null || state.Production is not { } prod) return null;
            var kind = prod.Content.Kinds[b.Kind];
            var site = new CraftSite { Light = 255, Water = true };
            foreach (var (k, _) in kind.Stations)
            {
                string st = RecipeUnifier.StationOfKind.GetValueOrDefault(k, k);
                if (runner.Db.Stations.ContainsKey(st)) site.AddStation(runner.Db, st);
            }
            // a workshop has its trade's tools: every ordinary tool (not the field kits)
            foreach (var (t, def) in runner.Db.Tools) if (!def.FieldKit) site.AddTool(runner.Db, t);
            if (r.Workstation != null && !site.Stations.Contains(r.Workstation)) return null;
            return site;
        }
        return new CraftContext
        {
            Econ = e, Runner = runner,
            PurseOf = p =>
            {
                if (p == hero) return heroPurse;
                int i = people.IndexOf(p);
                if (i >= 0 && people.Household[i] >= 0 && people.Household[i] < e.HouseholdCount) return e.HouseholdPurse[people.Household[i]];
                return e.Ledger.TryFind(PurseKind.Person, p, out var pp) ? pp : null;
            },
            StoreOf = p =>
            {
                if (p == hero) return pack();
                int i = people.IndexOf(p); if (i < 0) return e.OtherStores.GetValueOrDefault(p);
                if (WorkplaceOf(i) is { } b) return b.Store;
                return people.Household[i] >= 0 && people.Household[i] < e.HouseholdCount ? e.HouseholdStore[people.Household[i]] : null;
            },
            SiteOf = Site,
            ActorOf = p => new CraftActor(p),
            SettlementOf = p => { int i = people.IndexOf(p); return i >= 0 ? people.HomeSettlement[i] : -1; },
            PeopleOf = s =>
            {
                var list = new List<EntityId>(); int day = Day();
                for (int i = 0; i < people.Count; i++)
                    if (people.IsAlive(i) && people.HomeSettlement[i] == s && people.AgeYears(i, day) >= 16) list.Add(people.IdOf(i));
                list.Sort();
                return list;
            },
            IsTown = s => s >= 0 && s < region.Settlements.Count && region.Settlements[s].Class == SettlementClass.Town,
            TradeOf = p => { int i = people.IndexOf(p); return i >= 0 ? people.Occupation[i].ToString() : ""; },
            BusinessesOf = (s, trade) =>
            {
                if (state.Production is not { } prod) return 0;
                int n = 0;
                foreach (var b in prod.Businesses) if (b.Settlement == s && b.State == BusinessState.Open && prod.Content.Kinds[b.Kind].OwnerOccupation.ToString() == trade) n++;
                return n;
            },
            WitnessesOf = (s, offender) =>
            {
                var list = new List<EntityId>(); int day = Day();
                for (int i = 0; i < people.Count && list.Count < 3; i++)
                    if (people.IsAlive(i) && people.HomeSettlement[i] == s && people.AgeYears(i, day) >= 16 && OccupationInfo.IsTrade(people.Occupation[i]) && people.IdOf(i) != offender) list.Add(people.IdOf(i));
                return list;
            },
        };
    }
}
