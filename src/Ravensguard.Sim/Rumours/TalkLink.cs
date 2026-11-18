using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Rumours;

/// <summary>What the dialogue screen needs to know of the person it talks to, read at a barrier.</summary>
public sealed record SpeakerInfo(EntityId Person, int Index, string Name, string Job, bool Female, bool Child, int Age, int Mood, bool Shopkeeper, string Settlement, bool Hungry, IReadOnlySet<string> Needs);

/// <summary>The answer to "what is the news": the card the hero now holds, or null when the person had nothing new.</summary>
public sealed record NewsAnswer(JournalCard? Card, int Hop, bool Traveller);

/// <summary>
/// The talk systems' link to the running world, like the hero's desk (trade spec 4.7): hooks the hour barrier so rumours are seeded and spread, and runs the hero's asks, overhearings and dialogue
/// effects at a barrier (the records are only touched on the tick worker). The main thread reads <see cref="View"/> and the results of its calls.
/// </summary>
public sealed class TalkLink
{
    public WorldRuntime Runtime { get; }
    public LineBook Book { get; }
    private volatile JournalView? _view;
    public JournalView? View => _view;

    private TalkLink(WorldRuntime rt, LineBook book) { Runtime = rt; Book = book; }

    public static TalkLink Attach(WorldRuntime rt, LineBook? book = null)
    {
        var l = new TalkLink(rt, book ?? LineBook.Default);
        rt.AfterHour += l.OnHour;
        _ = rt.AtBarrier(() => { l.Publish(rt.HourDone); return 0; });
        return l;
    }

    private TalkState State => Runtime.State.Talk ??= new TalkState();
    private TalkWorld World() => TalkWorld.From(Runtime.State);

    private void OnHour(long hour)
    {
        // the home region's chronicle and people are the ones this reads; a world without people yet has nothing to talk about
        if (Runtime.State.Population.People.Count == 0) return;
        TalkSystem.AfterHour(World(), State, Book, hour);
        Publish(hour);
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

    /// <summary>The hero asks the person for the news: the best rumour they hold that the hero does not. The hero's Journal gains a card.</summary>
    public Task<NewsAnswer> AskNews(EntityId person) => Runtime.AtBarrier(() =>
    {
        var w = World(); var ts = State; int i = w.People.IndexOf(person); long hour = Runtime.HourDone;
        if (i < 0) return new NewsAnswer(null, 0, false);
        var told = TalkSystem.WhatTheyTell(ts, i, hour);
        if (told == null) return new NewsAnswer(null, 0, false);
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
        switch (effect)
        {
            case "set_flag": return A("flag").Length > 0 && ts.Flags.Add(A("flag"));
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
