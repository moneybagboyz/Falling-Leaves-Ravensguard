using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of contracts and rumours v2 (P6-C2, contracts-and-rumours-v2 4.2 to 4.5, 6): the contract hook on the world's hours, the board's view built at a barrier (never read off the sim's own
/// state from the main thread), the hero's takes, hand-overs and pins as recorded commands (<see cref="ContractDesk"/>), the Journal's Contracts tab and the Q verb, and the mood the barks read (needs, the house's
/// grievances, the weather: a barrier task keeps a map for the people near). No state here but the last views.
/// </summary>
public partial class StreetView
{
    ContractsView? _wbView; Task<ContractsView?>? _wbTask; double _wbAcc; long _wbSeen;
    readonly Dictionary<ulong, (int Mood, bool Hungry)> _moods = [];
    /// <summary>Rumours shown false, by id, with the name of whoever told them (read by the Journal's cards; refreshed at a barrier).</summary>
    Dictionary<ulong, string> _lies = [];
    Task<Dictionary<ulong, string>>? _liesTask; double _liesAcc;
    Task<Dictionary<ulong, (int, bool)>?>? _moodTask; double _moodAcc;

    /// <summary>Attaches the contracts' hour to the running world and hands the world's Codex to the talk link (the legends a villager tells when he has no news).</summary>
    void AttachWorkBoard()
    {
        if (Runtime is not { } rt) return;
        ContractLink.Attach(rt);
        if (_talk != null) _talk.Codex = _source?.Codex;
    }

    ContractsView? BuildWorkBoard(Ravensguard.Sim.Save.WorldState st, int settlement, int day, EntityId hero)
        => st.Economy == null ? null : ContractsView.Build(st, st.Content, settlement, day, hero, ContractWiring.From(st, st.Content));

    void RefreshWorkBoard()
    {
        if (Runtime is not { } rt || rt.State.Party is not { } party) return;
        if (_wbTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _wbView = t.Result; _wbTask = null; }
        int day = (int)(HeroNow() / 86400), s = HeroSettlement(); var hero = party.Hero;
        _wbTask ??= Bar(rt, () => BuildWorkBoard(rt.State, s, day, hero));
    }

    void OpenWorkBoard() => PushWorkBoard(false, null);

    void PushWorkBoard(bool mine, string? shot)
    {
        if (_router == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        if (_router.Top is ContractsScreen) return;
        RefreshWorkBoard();
        long now() => HeroNow();
        var act = new ContractActions(
            id => { _heroLink.Issue(ContractDesk.Take(id, now())); _wbAcc = 99; },
            id => { _heroLink.Issue(ContractDesk.Abandon(id, now())); _wbAcc = 99; },
            (id, on) => { _heroLink.Issue(ContractDesk.Pin(id, on, now())); _wbAcc = 99; },
            id => { var r = _wbView?.Mine.FirstOrDefault(x => x.Id == id); if (r != null) _heroLink.Issue(ContractDesk.Deliver(id, Math.Max(0, r.Grams - r.Delivered), now())); _wbAcc = 99; },
            id => { _heroLink.Issue(ContractDesk.Report(id, now())); _wbAcc = 99; },
            tid => { _heroLink.Issue(ContractDesk.Enter(tid, now())); _wbAcc = 99; });
        _router.Push(new ContractsScreen(_router, () => _wbView, act, () => { var (x, y) = HeroRegionMm(); return (x, y); }) { StartMine = mine, ShotPath = shot });
    }

    /// <summary>Per frame: the answers to contract commands toasted, the board's view kept fresh while it is up, the people's moods for the barks.</summary>
    void WorkBoardTick(double delta)
    {
        if (Runtime == null) return;
        _wbAcc += delta; _moodAcc += delta; _liesAcc += delta;
        if (_liesTask is { IsCompleted: true } lt) { if (lt.IsCompletedSuccessfully) _lies = lt.Result; _liesTask = null; }
        if (_liesAcc >= 6 && _liesTask == null && Runtime.State.Contracts is { Slanders.Count: > 0 })
        {
            _liesAcc = 0; var rt0 = Runtime;
            _liesTask = Bar(rt0, () =>
            {
                var st = rt0.State; var map = new Dictionary<ulong, string>();
                if (st.Contracts != null) foreach (var s in st.Contracts.Slanders.Where(s => s.Exposed)) map[s.Rumour] = st.Population.People.IndexOf(new EntityId(s.Author)) is var r and >= 0 ? st.Population.People.FullName(r) : "";
                return map;
            });
        }
        if (_heroLink?.View is { } v)
            foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Contract && r.Seq > _wbSeen)) { _router?.ShowToast(r.Text, 5f); _wbSeen = r.Seq; _wbAcc = 99; }
        bool up = _router?.Top is ContractsScreen;
        if (_wbAcc >= (up ? 1.0 : 20.0) && (up || _wbView != null)) { _wbAcc = 0; RefreshWorkBoard(); }
        if (_moodTask is { IsCompleted: true } mt)
        {
            if (mt.IsCompletedSuccessfully && mt.Result != null) { _moods.Clear(); foreach (var (k, m) in mt.Result) _moods[k] = m; }
            _moodTask = null;
        }
    }

    /// <summary>The mood and hunger of the people near, read at a barrier every few seconds (their day's mood from needs, the grievances of their house and the weather); the barks read the map.</summary>
    void RefreshMoods(IReadOnlyCollection<EntityId> ids)
    {
        if (Runtime is not { } rt || _moodTask != null || _moodAcc < 4 || _talk == null) return;
        _moodAcc = 0; string weather = _wx.State.ToString(); var set = _talk.Book.Settings;
        _moodTask = Bar(rt, () =>
        {
            var st = rt.State; var map = new Dictionary<ulong, (int, bool)>(); int day = (int)(rt.HourDone / 24);
            foreach (var id in ids)
            {
                int row = st.Population.People.IndexOf(id); if (row < 0 || !st.Population.People.IsAlive(row)) continue;
                map[id.Value] = MoodSystem.ForBark(st.Population, st.Economy, row, day, weather, set);
            }
            return (Dictionary<ulong, (int, bool)>?)map;
        });
    }

    /// <summary>The Q menu's contract verb: what is posted where you stand and what you have taken.</summary>
    void WorkBoardVerbs(List<QMenu.Verb> v) => v.Add(new(L.T("ct_q_board"), OpenWorkBoard));

    // ------------------------------------------------------------------ dev (--panel contracts, smoke)

    /// <summary>Dev: the contracts screen over a made-up board (on the Taken tab when asked).</summary>
    public void DebugWorkBoard(string? shot = null, bool mine = false)
    {
        if (_router == null) return;
        var view = ContractsScreen.Demo();
        _router.Push(new ContractsScreen(_router, () => view, new ContractActions(_ => { }, _ => { }, (_, _) => { }, _ => { }, _ => { }, _ => { }), () => (390_000, 290_000)) { StartMine = mine, ShotPath = shot });
    }
}
