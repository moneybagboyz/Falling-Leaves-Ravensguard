using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Rumours;

/// <summary>What the dialogue screen needs to know of the person it talks to, read at a barrier.</summary>
public sealed record SpeakerInfo(EntityId Person, int Index, string Name, string Job, bool Female, bool Child, int Age, int Mood, bool Shopkeeper, string Settlement, bool Hungry, IReadOnlySet<string> Needs);

/// <summary>The answer to "what is the news": the card the hero now holds, or null when the person had nothing new.</summary>
public sealed record NewsAnswer(JournalCard? Card, int Hop, bool Traveller)
{
    /// <summary>P6-C2 (legends fallback): when they had no fresh rumour, the legend of a place within ten kilometres they tell instead.</summary>
    public Ravensguard.Sim.Dialogue.LegendTold? Legend { get; init; }
}

/// <summary>
/// The talk systems' link to the running world, like the hero's desk (trade spec 4.7): hooks the hour barrier so rumours are seeded and spread, and runs the hero's asks, overhearings and dialogue
/// effects at a barrier (the records are only touched on the tick worker). The main thread reads <see cref="View"/> and the results of its calls.
/// </summary>
public sealed class TalkLink
{
    public WorldRuntime Runtime { get; }
    public LineBook Book { get; }
    /// <summary>P6-C2: the Codex the legends fallback draws on (set by the game once the world's codex is built); null: no legends told.</summary>
    public Ravensguard.Sim.Codex.CodexBook? Codex { get; set; }
    private volatile JournalView? _view;
    public JournalView? View => _view;

    private TalkLink(WorldRuntime rt, LineBook book) { Runtime = rt; Book = book; }

    public static TalkLink Attach(WorldRuntime rt, LineBook? book = null)
    {
        return rt.LinkOnce(() =>
        {
            var l = new TalkLink(rt, book ?? LineBook.Default);
            rt.AfterHour += l.OnHour;
            _ = rt.AtBarrier(() => { l.Publish(rt.HourDone); return 0; });
            return l;
        });
    }

    private TalkState State => Runtime.State.Talk ??= new TalkState();
    private TalkWorld World() => TalkWorld.From(Runtime.State);

    private void OnHour(long hour)
    {
        // the home region's chronicle and people are the ones this reads; a world without people yet has nothing to talk about
        if (Runtime.State.Population.People.Count == 0) return;
        State.AfterSpread = _standingHook ??= StandingHook;
        TalkWorld tw; using (Ravensguard.Sim.Diagnostics.PfClock.Span("talk.world")) tw = World();
        TalkSystem.AfterHour(tw, State, Book, hour);
        using (Ravensguard.Sim.Diagnostics.PfClock.Span("talk.carry")) CarryRumours(hour);
        using (Ravensguard.Sim.Diagnostics.PfClock.Span("talk.publish")) Publish(hour);
    }

    // ---- P6-A3: the standing book reads each hour's hearings (reputation 4.5); the deed table comes from the content, the groups from the region (rebuilt each day)
    private Action<TalkWorld, long>? _standingHook;
    private Ravensguard.Content.ContentDatabase? _deedContent;
    private Ravensguard.Sim.Society.DeedTable _deeds = Ravensguard.Sim.Society.DeedTable.Empty;
    private Ravensguard.Sim.Society.RegionGroups? _groups;
    private int _groupsDay = -1, _groupsPeople = -1;

    private void StandingHook(TalkWorld w, long hour)
    {
        var st = Runtime.State;
        if (!ReferenceEquals(st.Content, _deedContent)) { _deedContent = st.Content; _deeds = Ravensguard.Sim.Society.DeedTable.From(st.Content); }
        if (_deeds.Count == 0) { State.Rumours.DrainTouched(); return; }
        int day = (int)(hour / 24);
        if (_groups == null || _groupsDay != day || _groupsPeople != st.Population.People.Count) { _groups = new Ravensguard.Sim.Society.RegionGroups(st.Population.Region, st.Population.People, day, Ravensguard.Sim.Faith.FaithWiring.ReligionsOf(st)); _groupsDay = day; _groupsPeople = st.Population.People.Count; }
        Ravensguard.Sim.Society.StandingSystem.OnHour(() => st.Standing ??= new Ravensguard.Sim.Society.StandingBook(), st.Standing, _deeds, _groups, w, State, hour);
    }

    // ---- caravans carry rumours between regions (Phase 3 cleanup B, item 7)
    private readonly Dictionary<EntityId, Ravensguard.Sim.Caravans.CaravanState> _caravanSeen = [];
    private readonly Dictionary<int, (TalkWorld W, TalkState S)> _abroad = [];

    private (TalkWorld W, TalkState S)? Abroad(int region, long hour)
    {
        var rs = Runtime.State.Regions; if (rs == null) return null;
        var rw = rs.TryGet(new Ravensguard.Sim.WorldGen.Region.RegionCoord(region % Ravensguard.Sim.WorldGen.Region.RegionCoord.Grid, region / Ravensguard.Sim.WorldGen.Region.RegionCoord.Grid));
        if (rw == null) return null;
        if (!_abroad.TryGetValue(region, out var a))
        {
            var pop = rw.Population;
            var w = new TalkWorld { People = pop.People, Households = pop.Households, SettlementNames = [.. pop.Region.Settlements.Select(x => x.Name)], SettlementIds = [.. pop.Region.Settlements.Select(x => x.Id)], Chronicle = rw.Chronicle, Seed = rw.Services.SeedFor(rw.Coord) };
            _abroad[region] = a = (w, new TalkState());
        }
        RumourSeeder.Seed(a.W, a.S, Book, hour);          // what the neighbour's own chronicle gives its people to talk of
        return a;
    }

    /// <summary>A caravan reaching the gate tells its crew's rumours in the neighbour's market town; one coming home brings what the neighbour's innkeepers and merchants told its crew.</summary>
    private void CarryRumours(long hour)
    {
        var cont = Runtime.State.Regions?.Services.AbstractContinent; if (cont == null) return;
        int home = Runtime.State.Regions!.Home.Id; var hw = World(); var set = Book.Settings;
        foreach (var car in cont.Caravans.All)
        {
            if (car.Home != home) continue;
            _caravanSeen.TryGetValue(car.Id, out var prev); _caravanSeen[car.Id] = car.State;
            if (car.Neighbour < 0 || car.Crew.Length == 0) continue;
            if (prev != Ravensguard.Sim.Caravans.CaravanState.AtGate && car.State == Ravensguard.Sim.Caravans.CaravanState.AtGate && Abroad(car.Neighbour, hour) is { } ab)
                RumourCarriers.Deliver(hw, State, ab.W, ab.S, car.Crew, LargestSettlementOf(ab.W, ab.S, hour), hour, set);
            else if (prev == Ravensguard.Sim.Caravans.CaravanState.Inbound && car.State == Ravensguard.Sim.Caravans.CaravanState.Idle && Abroad(car.Neighbour, hour) is { } nb)
            {
                var tellers = RumourCarriers.TravellerHearers(nb.W, nb.S, LargestSettlementOf(nb.W, nb.S, hour), hour);
                RumourCarriers.Deliver(nb.W, nb.S, hw, State, tellers, [.. car.Crew.Where(r => r >= 0 && r < hw.People.Count && hw.People.IsAlive(r))], hour, set);
                RumourCarriers.Deliver(hw, State, hw, State, car.Crew, car.Settlement, hour, set);        // and the crew tell it at home, to the travellers' ears of their own town
            }
        }
    }

    private static int LargestSettlementOf(TalkWorld w, TalkState ts, long hour)
    {
        var res = RumourSeeder.Residents(w, ts, (int)(hour / 24)); int best = -1, bn = 0;
        for (int i = 0; i < res.Length; i++) if (res[i].Length > bn) { bn = res[i].Length; best = i; }
        return best;
    }

    /// <summary>Rebuilds the Journal view at the next barrier (after something outside the link changed the records: a dev shot, a test).</summary>
    public Task Refresh() => Runtime.AtBarrier(() => { Publish(Runtime.HourDone); return 0; });

    private void Publish(long hour) => _view = TalkSystem.Journal(World(), State, hour);

    /// <summary>Who this person is, for the dialogue screen. Null when they are gone.</summary>
    public Task<SpeakerInfo?> Speaker(EntityId person) => Runtime.AtBarrier<SpeakerInfo?>(() =>
    {
        var st = Runtime.State; var pop = st.Population; var pt = pop.People; int i = pt.IndexOf(person);
        if (i < 0 || !pt.IsAlive(i)) return null;
        int day = (int)(Runtime.HourDone / 24); int age = pt.AgeYears(i, day);
        var set = Book.Settings; int mood = MoodSystem.Of(pop, st.Economy, i, day, set);
        string job = pt.Occupation[i].ToString().ToLowerInvariant();
        var needs = new HashSet<string>(StringComparer.Ordinal);
        if (st.Economy != null && pt.Household[i] >= 0 && pt.Household[i] < st.Economy.FoodCoverDaysX10.Length && st.Economy.FoodCoverDaysX10[pt.Household[i]] < 10) needs.Add("hungry");
        int hod = (int)(Runtime.HourDone % 24); if (hod is >= 22 or < 5) needs.Add("tired");
        int s = pt.HomeSettlement[i];
        return new SpeakerInfo(person, i, pt.FullName(i), job, pt.Sex[i] == Sex.Female, age < RumourSeeder.AdultAge, age, mood, pt.Occupation[i] is Occupation.Merchant or Occupation.Innkeeper or Occupation.Baker or Occupation.Butcher or Occupation.Brewer,
            s >= 0 && s < pop.Region.Settlements.Count ? pop.Region.Settlements[s].Name : "", needs.Contains("hungry"), needs);
    });

    private Ravensguard.Sim.Dialogue.LegendTold? LegendFor(int row)
    {
        var st = Runtime.State; int s = st.Population.People.HomeSettlement[row];
        if (Codex == null || s < 0 || s >= st.Population.Region.Settlements.Count) return null;
        var c = st.Population.Region.Settlements[s];
        return Ravensguard.Sim.Dialogue.Legends.Tell(Codex, c.CenterX, c.CenterY, row);
    }

    /// <summary>The hero asks the person for the news: the best rumour they hold that the hero does not. The hero's Journal gains a card.</summary>
    public Task<NewsAnswer> AskNews(EntityId person) => Runtime.AtBarrier(() =>
    {
        var w = World(); var ts = State; int i = w.People.IndexOf(person); long hour = Runtime.HourDone;
        if (i < 0) return new NewsAnswer(null, 0, false);
        var told = TalkSystem.WhatTheyTell(ts, i, hour);
        if (told == null) return new NewsAnswer(null, 0, false) { Legend = LegendFor(i) };
        var hh = TalkSystem.HeroHears(w, ts, told.Rumour, i, hour, HearHow.Told);
        Publish(hour);
        var card = _view!.Cards.FirstOrDefault(c => c.Rumour == told.Rumour);
        return new NewsAnswer(card, told.Hearing.Hop, told.Hearing.How == HearHow.Traveller && told.Hearing.Hop >= 2 && told.Hearing.Teller < 0);
    });

    /// <summary>The hero stands within earshot of these people: any rumour passed among them in the last hour is heard.</summary>
    public Task<JournalCard?> Overhear(IReadOnlyCollection<EntityId> nearby, int settlement) => Runtime.AtBarrier<JournalCard?>(() =>
    {
        var w = World(); var set = nearby.Select(id => w.People.IndexOf(id)).Where(i => i >= 0).ToHashSet();
        var h = TalkSystem.Overhear(w, State, settlement, set, Runtime.HourDone);
        if (h == null) return null;
        Publish(Runtime.HourDone);
        return _view!.Cards.FirstOrDefault(c => c.Rumour == h.Rumour);
    });

    /// <summary>An authored dialogue's effect, applied at the barrier. Returns true when it did something.</summary>
    public Task<bool> Apply(string effect, IReadOnlyDictionary<string, string> args, EntityId speaker) => Runtime.AtBarrier(() =>
    {
        var w = World(); var ts = State; int i = w.People.IndexOf(speaker); long hour = Runtime.HourDone;
        string A(string k) => args.TryGetValue(k, out var v) ? v : "";
        // the first hour's mentor (onboarding spec 3.5): their contract, the loan and the "you have met" flag go to the script
        bool script = false;
        if (Runtime.State.Onboarding is { IsEmpty: false } ob && i >= 0 && i == ob.MentorPerson && Runtime.State.Content is { } db)
        {
            var rb = Runtime.State.Mounts; long hx = rb != null && (rb.HeroXMm != 0 || rb.HeroYMm != 0) ? rb.HeroXMm : ob.MentorX, hy = rb != null && (rb.HeroXMm != 0 || rb.HeroYMm != 0) ? rb.HeroYMm : ob.MentorY;
            script = Ravensguard.Sim.Onboarding.OnboardingSetup.Apply(Runtime.State, db, ob, effect, args, hour * 3600, hx, hy);
        }
        switch (effect)
        {
            case "set_flag": return (A("flag").Length > 0 && ts.Flags.Add(A("flag"))) | script;
            case "start_contract": return script || (!script && A("kind") is not ("first" or "loan") && Runtime.State.Content is { } cdb && Ravensguard.Sim.Dialogue.DialogueEffects.StartContract(Runtime.State, cdb, i, args, hour * 3600) != null);
            case "give_item": return script || (Runtime.State.Content is { } gdb && Ravensguard.Sim.Dialogue.DialogueEffects.GiveItem(Runtime.State, gdb, i, args) > 0);
            case "add_rumour": { var h = TalkSystem.AddAuthored(w, ts, A("kind"), A("place"), i, hour); Publish(hour); return h != null; }
            case "add_trust":
            {
                var party = Runtime.State.Party; if (party == null || !int.TryParse(A("pm"), out int pm)) return false;
                var k = party.Know(speaker); k.TrustPm = Math.Clamp(k.TrustPm + pm, -1000, 1000); return true;
            }
            default: return false;
        }
    });

    /// <summary>What the street grumbles about, read from the world: a staple dear against its usual price in this settlement's market, and unrest at 400 permille or more.</summary>
    public Task<(bool Dear, bool Unrest)> Climate(int settlement) => Runtime.AtBarrier(() =>
    {
        bool dear = false, unrest = false; var st = Runtime.State;
        if (settlement < 0) return (false, false);
        if (st.Markets != null && st.Economy != null && settlement < st.Population.Region.Settlements.Count)
        {
            var b = st.Markets.BookForSettlement(settlement);
            foreach (var g in new[] { "bread", "flour", "wheat", "barley" })
                if (st.Economy.Goods.TryIdOf(g, out var id) && b.Goods[id].BaseRefMicro > 0 && b.MidMicro(id) * 1000 > b.Goods[id].BaseRefMicro * 1300) dear = true;
        }
        if (Runtime.Records.HomeWorld?.Economy.Taxes.State.Unrest is { } u && settlement < u.Length) unrest = u[settlement] >= 400;
        return (dear, unrest);
    });

    public Task<HashSet<string>> Flags() => Runtime.AtBarrier(() => State.Flags.ToHashSet(StringComparer.Ordinal));
}
