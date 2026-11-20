using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the Faction screen (P6-D2, diplomacy-and-ai-lords 6): F opens it; its <see cref="FactionView"/> is built at a barrier from the politics, rule and standing books and refreshed twice a second while it is up;
/// its actions (swear, ask for a manor or a fief, give a gift, renounce) are hero commands <c>dp:</c> whose answers are toasted once.
/// </summary>
public partial class StreetView
{
    FactionView? _factionView; Task<FactionView?>? _factionTask; long _factionSeen;
    const long FactionGiftMites = 10_000;                       // a gift from the screen is 100 crowns

    void RefreshFaction()
    {
        if (Runtime is not { } rt || rt.State.Party == null || rt.State.Content is not { } content) return;
        if (_factionTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _factionView = t.Result; _factionTask = null; }
        int day = (int)(HeroNow() / 86400);
        _factionTask ??= Bar(rt, () => (FactionView?)FactionView.Of(rt.State, DeedTable.From(content), day));
    }

    void OpenFaction(FactionScreen.Tab tab = FactionScreen.Tab.Realm)
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _factionView = null; _factionTask = null;
        RefreshFaction();
        _router.Push(new FactionScreen(_router, () => _factionView, RefreshFaction, new FactionActions { Do = FactionAct }) { ShotPath = _opt.Panel == "faction" ? _opt.ShotPath : null, StartTab = tab });
    }

    void FactionAct(ActionLine a)
    {
        if (_heroLink == null) return;
        long now = HeroNow();
        HeroCommand? c = a.Id switch
        {
            "swear" => PoliticsDesk.Swear(a.Target, now),
            "manor" => PoliticsDesk.Manor(a.Target, now),
            "fief" => PoliticsDesk.AskFief(a.Target, now),
            "gift" => PoliticsDesk.GiftLord(a.Target, FactionGiftMites, now),
            "giftr" => PoliticsDesk.GiftRealm(a.Target, FactionGiftMites, now),
            "renounce" => PoliticsDesk.Renounce(now),
            _ => null,
        };
        if (c == null) { _router?.ShowToast(L.T("dp_act_cannot")); return; }
        _heroLink.Issue(c);
    }

    /// <summary>Per frame: the answers to the hero's politics, toasted once.</summary>
    void FactionTick()
    {
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Diplomacy && r.Seq > _factionSeen)) { _router?.ShowToast(r.Text, 5f); _factionSeen = r.Seq; }
    }

    /// <summary>Dev (--ng-shot faction): the hero's home realm with a war on, a truce, the lords' opinions of him, a vacant fief and a record of service, then the Faction screen on the Actions tab.</summary>
    public void DebugFaction(FactionScreen.Tab tab = FactionScreen.Tab.Actions)
    {
        if (Runtime is not { } rt || _heroLink == null || rt.State.Rule is not { } rb || rt.State.HomeRecords is not { } home || rt.State.Party is not { } party) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        int day = (int)(HeroNow() / 86400);
        rt.AtBarrier(() =>
        {
            var st = rt.State; var book = st.Politics ??= new PoliticsBook(); PoliticsWiring.Bind(book, st.Content, st.Seed);
            var c = PoliticsEngine.Context(home.Sim, book, rb, RuleChron.Of(st.Chronicle, st.Ids), day);
            var hero = party.Hero; var lords = rb.Fiefs.Values.Where(f => f.Holder >= 0 && f.Realm >= 0).Take(3).ToList();
            int amount = 25;
            foreach (var f in lords) { Opinions.Add(book, rb, (ulong)f.Holder, hero.Value, "gift", day, 0, amount); Opinions.Add(book, rb, (ulong)f.Holder, hero.Value, "insult", day, 0, -15); amount -= 10; }
            if (lords.Count > 0) Road.RecordService(c, hero, lords[0].Realm);
            var realms = c.AliveRealms().ToList();
            if (realms.Count >= 3)
            {
                book.Wars.Add(new War { Serial = book.NextWar++, Attacker = realms[0], Defender = realms[1], StartDay = day - 45, Cb = CbKind.Raid, Score = 18, Seasons = 3, LossA = 90, LossD = 140, GainA = 18 });
                Diplomacy.Sign(c, PactKind.Truce, realms[1], realms[2], 2, 0);
            }
            var vacant = rb.Fiefs.Values.Where(f => f.Realm >= 0 && f.Holder >= 0).Skip(3).FirstOrDefault();
            if (vacant != null) { vacant.Holder = -1; vacant.PopLord = -1; }
            book.Note(day - 10, lords.Count > 0 ? lords[0].Fief : 0, lords.Count > 0 ? (ulong)lords[0].Holder : 0, "ch_dp_act_set_tax_up", "ch_note_dp_treasury_thin", 0);
            return 0;
        }).GetAwaiter().GetResult();
        OpenFaction(tab);
    }
}
