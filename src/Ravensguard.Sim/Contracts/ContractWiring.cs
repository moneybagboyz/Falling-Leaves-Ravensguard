using Ravensguard.Content;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// Contracts on the running world: builds the <see cref="ContractWorld"/> from the world state (what the need scanner and the verifiers read), and the hour (<see cref="OnHour"/>: expiries and the company pass at a
/// day's start, the need scan an hour a settlement, verification, the tournament's days, slander at a season's start) that <see cref="ContractLink"/> runs on the tick worker after the hour's own passes.
/// </summary>
public static class ContractWiring
{
    /// <summary>The store an owner id keeps: a business's, another owner's, a household's.</summary>
    private static Store? StoreOf(WorldState st, EntityId owner)
    {
        var e = st.Economy; if (e == null) return null;
        if (st.Production?.ById(owner) is { } b) return b.Store;
        if (e.OtherStores.TryGetValue(owner, out var s)) return s;
        int h = st.Population.Households.IndexOf(owner);
        return h >= 0 && h < e.HouseholdCount ? e.HouseholdStore[h] : null;
    }

    private static PurseId? PurseOfOwner(WorldState st, EntityId owner)
    {
        var e = st.Economy; if (e == null) return null;
        if (st.Production?.ById(owner) is { } b) return b.Purse;
        if (owner.Kind == IdKind.Household && e.Ledger.TryFind(PurseKind.Household, owner, out var hp)) return hp;
        if (owner.Kind == IdKind.Person) return PersonPurse(st, owner);
        if (e.Ledger.TryFind(PurseKind.Treasury, owner, out var tp)) return tp;
        return null;
    }

    private static PurseId? PersonPurse(WorldState st, EntityId p)
    {
        var e = st.Economy; if (e == null) return null;
        var hero = st.Party?.Hero ?? EntityId.None;
        if (p == hero) return e.Ledger.TryFind(PurseKind.Person, EconomyState.HeroOwner, out var hp) ? hp : null;
        var pop = st.Population; int row = pop.People.IndexOf(p); int hh = row >= 0 ? pop.People.Household[row] : -1;
        return hh >= 0 && hh < e.HouseholdCount ? e.HouseholdPurse[hh] : null;
    }

    /// <summary>The world as contracts read it at a barrier.</summary>
    public static ContractWorld From(WorldState st, ContentDatabase? db)
    {
        var pop = st.Population; var region = pop.Region; var e = st.Economy!;
        var hero = st.Party?.Hero ?? EntityId.None;
        var fief = region.Settlements.Select(s => s.FiefIndex >= 0 && s.FiefIndex < region.Fiefs.Count ? s.FiefIndex : -1).ToArray();
        var fiefIds = region.Fiefs.Select(f => f.Id).ToArray();
        var content = ContractContent.From(db);
        var fairs = db != null ? Ravensguard.Sim.Travel.FairBook.From(db) : null;
        long Dist(int a, int b) => a < 0 || b < 0 || a >= region.Settlements.Count || b >= region.Settlements.Count ? 0 :
            IntMath.Isqrt((long)(region.Settlements[a].CenterX - region.Settlements[b].CenterX) * (region.Settlements[a].CenterX - region.Settlements[b].CenterX) + (long)(region.Settlements[a].CenterY - region.Settlements[b].CenterY) * (region.Settlements[a].CenterY - region.Settlements[b].CenterY));
        var talk = st.Talk; List<CompanyInfo>? companies = null;       // the guards of every settlement are walked once per world view, not once per call
        return new ContractWorld
        {
            Chronicle = st.Chronicle, Ids = st.Ids, Ledger = e.Ledger, People = pop.People, SettlementNames = [.. region.Settlements.Select(s => s.Name)], SettlementIds = [.. region.Settlements.Select(s => s.Id)],
            SettlementFief = fief, FiefIds = fiefIds, Seed = st.Seed, Content = content, HeroId = hero,
            PurseOfOwner = o => PurseOfOwner(st, o), PurseOf = p => PersonPurse(st, p),
            TreasuryOf = s => { int f = s >= 0 && s < fief.Length ? fief[s] : -1; return f >= 0 && e.Ledger.TryFind(PurseKind.Treasury, fiefIds[f], out var p) ? p : null; },
            DistanceM = Dist,
            PlaceOf = p => { int r = pop.People.IndexOf(p); return r >= 0 ? pop.People.HomeSettlement[r] : -1; },
            StockNeeds = () => StockNeeds(st, content),
            MerchantHolds = (s, good, grams) => st.Markets?.Books is { } bs && s >= 0 && s < bs.Length && st.Markets.BookOf[s] is var bi and >= 0 && bi < bs.Length && bs[bi] is { } book && good < book.Goods.Length && book.Goods[good].StockUnits > 0,
            StoreQty = (owner, good) => StoreOf(st, owner)?.Qty(good) ?? 0,
            IsDead = p => { int r = pop.People.IndexOf(p); return r >= 0 && pop.People.Status[r] == PersonStatus.Dead; },
            IsHeld = p => (st.Justice?.Gaol.ContainsKey(p.Value) ?? false) || st.Captivity?.Find(p) is { Held: true },
            CaptiveFree = p => st.Captivity?.Find(p) is { } c
                ? new CaptiveEnding(true, c.Held, c.Held ? "" : c.State.ToString().ToLowerInvariant(), c.EndedSec >= 0 ? (int)(c.EndedSec / 86_400) : -1, c.State == CaptiveState.Rescued && c.Captor != CaptorKind.Gaol)   // a bandit's captive is only ever rescued by the hero's side (heist, live rescue, his party's abstract rescue)
                : default,
            HeldSince = p => st.Justice?.Gaol.TryGetValue(p.Value, out var gr) == true ? gr.SinceDay : st.Captivity?.Find(p) is { Held: true } hc ? hc.SinceDay : -1,
            DeadSince = p => { int r = pop.People.IndexOf(p); return r >= 0 && pop.People.Status[r] == PersonStatus.Dead ? pop.People.DeathDay[r] : -1; },
            Captives = () => Captives(st),
            Bands = () => Bands(st, content),
            BandGone = band => st.Chronicle.All.Any(en => en.TypeId == "bandit_cleared" && en.Data.TryGetValue("band", out var b) && b == band.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Companies = () => companies ??= [.. CompaniesOf(st)],
            CompanyDone = c =>
            {
                if (c.Kind == "soil_improvement") { Ravensguard.Sim.Holdings.SoilWorld.For(st)?.CompanyDone(c); return; }      // P6-D4: the crew's marling or manuring is done
                if (c.Kind is "clear_bandit_cave" or "clear_lair")
                    st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(c.TakenDay * 86_400L + 43_200), "bandit_cleared", [], [new Cause(CauseKind.Battle, new EntityId(c.PostedEntry))],
                        new Dictionary<string, string> { ["band"] = c.Target.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ["by"] = "company" }, TextKey: "ch_bandit_cleared", Params: [ChronParam.Place("place", c.Settlement >= 0 && c.Settlement < region.Settlements.Count ? region.Settlements[c.Settlement].Id : EntityId.None)]));
            },
            CharterTowns = () => region.Towns.Select(t => region.Settlements.IndexOf(t)).Where(i => i >= 0),
            FairInDays = day => fairs?.Next((int)(day % TimeConst.DaysPerYear) + 1) is { } n ? n.DaysUntil : -1,
            Fighters = s => Fighters(st, s),
            RatingOf = p => p == hero ? 130 : 100,
            IsRival = (a, b) => talk != null && pop.People.IndexOf(a) is var ia and >= 0 && pop.People.IndexOf(b) is var ib and >= 0 && (talk.Relations.Rows.TryGetValue(ia <= ib ? (ia, ib) : (ib, ia), out var r) && (r.Flags & Ravensguard.Sim.Dialogue.RelationFlags.Rival) != 0),
            SpeechOf = p => p == hero ? st.Hero?.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(Skill.Speech)) ?? 0 : pop.People.IndexOf(p) is var r and >= 0 ? pop.People.SkillLevelOf(r, Skill.Speech) : 0,
            TrustPm = (a, b) =>
            {
                if (b == hero) return st.Party != null && st.Party.Known.TryGetValue(a, out var k) ? k.TrustPm : 0;
                int ia = pop.People.IndexOf(a), ib = pop.People.IndexOf(b);
                return talk != null && ia >= 0 && ib >= 0 ? talk.Relations.Of(ia, ib).TrustPm : 0;
            },
            StandingWithFief = (p, f) => Standing.Of(st.Standing, p, GroupRef.Fief(f)),
            DailyWageMites = db != null ? Ravensguard.Sim.Behaviour.BehaviourContent.From(db).OddJobs.UnskilledWageMitesPerDay : 150,
            NameOf = p => p == hero ? st.Hero?.Name ?? "" : "",
            GoodName = g => e.Goods.StringId(g),
        };
    }

    private static IEnumerable<StockNeed> StockNeeds(WorldState st, ContractContent content)
    {
        var prod = st.Production; if (prod == null) yield break;
        foreach (var b in prod.Businesses)
        {
            if (b.State != BusinessState.Open || b.Store == null || b.IdleDays < 3) continue;
            var kind = prod.KindOf(b); var need = new SortedDictionary<ushort, long>();
            // a real deficit: the workshop has stood idle three days or more and a batch of one of its recipes lacks an input the store should hold (a shop that is merely between orders posts nothing)
            foreach (var r in kind.Recipes) foreach (var (g, q) in r.Inputs) { if (b.Store.Qty(g) >= q) continue; long perDay = Math.Max(1, q / Math.Max(1, r.DurationDays)); if (perDay > need.GetValueOrDefault(g)) need[g] = perDay; }
            foreach (var (g, perDay) in need)
            {
                long have = b.Store.Qty(g); int days = (int)Math.Min(1000, have / perDay);
                if (days < content.S.StockDaysMin) yield return new StockNeed(b.Id, b.Settlement, g, Math.Max(perDay * 6, 1000), days, kind.Id);
            }
        }
    }

    private static IEnumerable<CaptiveHeld> Captives(WorldState st)
    {
        if (st.Captivity == null) yield break;
        var pop = st.Population;
        foreach (var c in st.Captivity.Captives.Values)
        {
            if (!c.Held || c.IsHero || c.Captor != CaptorKind.Bandits) continue;
            int r = pop.People.IndexOf(c.Person); if (r < 0) continue;
            int hh = pop.People.Household[r]; if (hh < 0) continue;
            yield return new CaptiveHeld(c.Person, pop.Households.IdOf(hh), pop.People.HomeSettlement[r], c.RansomMites);
        }
    }

    /// <summary>The bands a place believes in: a road-bandit rumour, still fresh, held by the settlement's people (there are no band objects yet; the rumour's id stands for the band).</summary>
    private static IEnumerable<BandSeen> Bands(WorldState st, ContractContent content)
    {
        var ts = st.Talk; if (ts == null) yield break;
        foreach (var r in ts.Rumours.Rumours.Values)
            if (r.Kind == RumourKind.RoadBandits && r.Settlement >= 0 && !r.FromAfar && ts.Rumours.HearingsOf(r.Id).Count >= 3) yield return new BandSeen(r.Id, r.Settlement, r.BornDay, 6);
    }

    private static IEnumerable<CompanyInfo> CompaniesOf(WorldState st)
    {
        var pop = st.Population; var e = st.Economy; if (e == null) yield break;
        for (int s = 0; s < pop.Region.Settlements.Count; s++)
        {
            var g = Guards.Of(pop.People, s);
            if (g.Count < 4) continue;
            int hh = pop.People.Household[g[0]]; if (hh < 0 || hh >= e.HouseholdCount) continue;
            yield return new CompanyInfo(EntityId.Make(IdKind.Faction, 0x4A00_0000UL + (ulong)s), pop.Region.Settlements[s].Name + " company", s, g.Count * 12, g.Count, e.HouseholdPurse[hh].Index, "mercenary");
        }
        // P6-D4: the people without a trade in a place (four or more) work as a crew of farmhands on the soil jobs the holders post (SoilWorld hands them the job after a week); they take no other contract
        var hands = new List<int>[pop.Region.Settlements.Count];
        var pt = pop.People; var hht = pop.Households; int day = (int)st.Clock.Now.TotalDays;
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.Status[i] != PersonStatus.Alive || pt.Occupation[i] is not (Occupation.None or Occupation.Labourer)) continue;       // the day labourers of the place: whoever has no trade, hired out today or not
            int hh = pt.Household[i]; if (hh < 0 || hh >= hht.Count || hh >= e.HouseholdCount || hht.Class[hh] == SocialClass.Gentry) continue;
            int age = pt.AgeYears(i, day); if (age < 16 || age >= 60) continue;
            int s = pt.HomeSettlement[i]; if ((uint)s < (uint)hands.Length) (hands[s] ??= []).Add(i);
        }
        for (int s = 0; s < hands.Length; s++)
        {
            if (hands[s] is not { Count: >= 4 } h) continue;
            int hh = pt.Household[h[0]];
            yield return new CompanyInfo(Ravensguard.Sim.Holdings.SoilWorld.CrewId(s), pop.Region.Settlements[s].Name + " farmhands", s, 120, h.Count, e.HouseholdPurse[hh].Index, "farmhands");
        }
    }

    private static IEnumerable<(EntityId, int)> Fighters(WorldState st, int s)
    {
        var pop = st.Population;
        foreach (int i in Guards.Of(pop.People, s).Take(6)) yield return (pop.People.IdOf(i), 80 + pop.People.SkillLevelOf(i, Skill.Melee));
    }

    // ------------------------------------------------------------------ the hour

    /// <summary>The contracts' hour. Hour 0: expiries, the companies' pass, the tournament's announcement, slander at a season's start; every hour: one settlement's needs, verification, the fair day's bouts. The book is made when first needed and dropped while empty.</summary>
    public static void OnHour(WorldState st, long hour)
    {
        if (st.Economy == null || st.Population.People.Count == 0) return;
        var w = From(st, st.Content);
        var book = st.Contracts ?? new ContractBook();
        int day = (int)(hour / 24), hod = (int)(hour % 24);
        if (hod == 0)
        {
            ContractSystem.Expiries(w, book, day);
            Companies_(w, book, day);
        }
        Tournaments.OnHour(w, book, hour);
        NeedScanner.Hour(w, book, hour);
        ContractSystem.Verify(w, book, day);
        Ravensguard.Sim.Holdings.SoilWorld.For(st)?.OnHour(w, book, day, hod);          // P6-D4 (holdings 3.5): soil jobs posted on a season's first day, taken by crews, improvements aged
        if (hod == 0 && st.Talk is { } ts)
        {
            Slander.Season(w, book, ts, day);
            Slander.Scan(w, book, ts, st.Standing, null, day);
        }
        st.Contracts = book.IsEmpty ? null : book;
    }

    private static void Companies_(ContractWorld w, ContractBook book, int day)
    {
        Companies.Resolve(w, book, day);
        Companies.TakeAll(w, book, day);
    }
}
