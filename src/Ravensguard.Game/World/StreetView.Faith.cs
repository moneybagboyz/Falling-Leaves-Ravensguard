using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of faith (P6-B4, faith-and-church 6): the Faith panel of the character screen over a <see cref="FaithView"/> built at a barrier, and Q beside a priest: Attend the service (a holy
/// day), Give alms, Pilgrimage (the panel's vows). Every act is a recorded hero command (<see cref="FaithDesk"/>) applied at the barrier; its answer is toasted when it lands. A pilgrim's vow is kept by the
/// street: when the hero stands in the shrine's town the arrival command goes in on its own.
/// </summary>
public partial class StreetView
{
    FaithView? _faithView; Task<FaithView?>? _faithTask; double _faithAcc, _faithPoll; long _faithSeen; bool _arrivalSent;

    void RefreshFaith()
    {
        if (Runtime is not { } rt || rt.State.Party is not { } party || rt.State.Church == null) return;
        if (_faithTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _faithView = t.Result; _faithTask = null; }
        int day = (int)(HeroNow() / 86400), at = HeroSettlement(); var hero = party.Hero;
        _faithTask ??= Bar(rt, () => (FaithView?)FaithView.Of(rt.State, hero, at, day));
    }

    /// <summary>A priest within ten metres (the church's verbs need one), or null.</summary>
    (EntityId Id, string Name)? PriestNear()
    {
        foreach (var p in PeopleNear(10).Where(p => p.Occupation == Occupation.Priest).OrderBy(p => p.Id.Value)) return (p.Id, p.Name);
        return null;
    }

    void OpenFaith()
    {
        if (_router == null || Runtime == null || _heroLink == null || Runtime.State.Church == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        if (_router.Top is FaithScreen) return;                                               // already up (a modal never stacks on itself)
        _faithView = null; _faithTask = null;
        RefreshFaith();
        _router.Push(new FaithScreen(_router, FaithLinkFor()));
    }

    FaithLink FaithLinkFor() => new()
    {
        View = () => _faithView, Refresh = RefreshFaith,
        Attend = () =>
        {
            int s = HeroSettlement();
            if (s < 0) { _router?.ShowToast(L.T("fa_not_in_town")); return; }
            _heroLink?.Issue(FaithDesk.Attend(s, HeroNow()));
        },
        Alms = mites =>
        {
            int s = HeroSettlement();
            if (s < 0) { _router?.ShowToast(L.T("fa_not_in_town")); return; }
            _heroLink?.Issue(FaithDesk.Alms(s, mites, HeroNow()));
        },
        Vow = target => { _arrivalSent = false; _heroLink?.Issue(FaithDesk.Vow(HeroSettlement(), target, HeroNow())); },
        Abandon = () => _heroLink?.Issue(FaithDesk.Abandon(HeroNow())),
        Penance = () => _heroLink?.Issue(FaithDesk.Penance(HeroNow())),
        Venerate = key => { int s = HeroSettlement(); if (s >= 0) _heroLink?.Issue(FaithDesk.Venerate(s, key, HeroNow())); },
        SellRelic = key => { int s = HeroSettlement(); if (s >= 0) _heroLink?.Issue(FaithDesk.SellRelic(s, key, HeroNow())); },
        BuyRelic = key => { int s = HeroSettlement(); if (s >= 0) _heroLink?.Issue(FaithDesk.BuyRelic(s, key, HeroNow())); },
        ShowHead = () => { int s = HeroSettlement(); if (s >= 0) _heroLink?.Issue(FaithDesk.ShowHead(s, HeroNow())); },
        Charm = () => { int s = HeroSettlement(); if (s >= 0) _heroLink?.Issue(FaithDesk.Charm(s, HeroNow())); },
    };

    /// <summary>Per frame: the answers to faith commands toasted, the view kept fresh while the panel is up (and now and then otherwise), a pilgrim's arrival in the shrine's town.</summary>
    void FaithTick(double delta)
    {
        if (Runtime?.State.Church == null) return;
        _faithAcc += delta; _faithPoll += delta;
        if (_faithAcc >= 2.0 && _router?.Top is FaithScreen) { _faithAcc = 0; RefreshFaith(); }
        if (_faithPoll >= 6.0) { _faithPoll = 0; RefreshFaith(); }
        if (_heroLink?.View is { } v)
            foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Faith && r.Seq > _faithSeen)) { _router?.ShowToast(r.Text, 5f); _faithSeen = r.Seq; _faithPoll = 6.0; }
        if (_faithView is { OnPilgrimage: true } fv)
        {
            if (!_arrivalSent && fv.PilgrimSettlement >= 0 && HeroSettlement() == fv.PilgrimSettlement) { _arrivalSent = true; _heroLink?.Issue(FaithDesk.Arrive(fv.PilgrimSettlement, HeroNow())); }
        }
        else _arrivalSent = false;
    }

    /// <summary>The Q menu's church verbs: beside a priest, Attend the service, Give alms, Pilgrimage; the Faith panel from anywhere.</summary>
    void FaithVerbs(List<QMenu.Verb> v)
    {
        if (Runtime?.State.Church == null || _heroLink == null) return;
        v.Add(new(L.T("fa_q_head"), () => { }, Heading: true));
        var p = PriestNear();
        string? need = p == null ? L.T("fa_find_priest") : null;
        v.Add(new(L.T("fa_q_panel"), OpenFaith));
        v.Add(new(p is { } pp ? L.F("fa_q_attend_with", pp.Name) : L.T("fa_q_attend"), () => { if (PriestNear() != null) FaithLinkFor().Attend(); }, need));
        v.Add(new(L.T("fa_q_alms"), () => { if (PriestNear() != null) FaithLinkFor().Alms(100); }, need));
        v.Add(new(L.T("fa_q_pilgrimage"), () => { if (PriestNear() != null) OpenFaith(); }, need));
    }

    // ------------------------------------------------------------------ dev shot (--ng-shot faith)

    /// <summary>Dev: the hero gives three crowns in alms and takes a pilgrim's vow (applied at the barrier as the desk does), then the Faith panel opens over him.</summary>
    public void DebugFaith(string? shot = null)
    {
        if (Runtime is not { } rt || _heroLink == null || rt.State.Party is not { } party || rt.State.Content is not { } content || rt.State.Church == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        long now = HeroNow(); int where = HeroSettlement(); int at = Math.Max(0, where);
        rt.AtBarrier(() =>
        {
            var ctx = new HeroContext(rt.State, party, content);
            var book = rt.State.Church!;
            int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(rt.State.Population.People, rt.State.Hero, party.Hero);
            int ri = row >= 0 ? FaithWiring.ReligionsOf(rt.State).OfCulture(rt.State.Population.People.Culture[row]) : -1;
            int parish = book.Parishes.Keys.FirstOrDefault(s => FaithWiring.ReligionsOf(rt.State).OfSettlement(rt.State.Population.Region, s) == ri, at);
            FaithDesk.Apply(ctx, FaithDesk.Alms(parish, 300, now) with { Seq = 9001 });
            var site = ri >= 0 ? FaithWiring.ReligionsOf(rt.State).HolySiteIn(rt.State.Population.Region, ri) : -1;
            if (site >= 0) FaithDesk.Apply(ctx, FaithDesk.Vow(parish, site, now) with { Seq = 9002 });
            // P6-E2: a heresy in the parish, fear of the dark, a relic on the altar and one in the pack, a believed miracle with its natural reason, a pardoner's offer
            int day = (int)(now / 86400); var pol = rt.State.FaithPol ??= new ChurchPolitics(); var religions = FaithWiring.ReligionsOf(rt.State);
            parish = where >= 0 ? where : row >= 0 ? rt.State.Population.People.HomeSettlement[row] : parish;      // staged where the panel looks: the parish he stands in
            if (book.ParishAt(parish) is { Priest: >= 0 } par)
            {
                var h = Heresy.Start(rt.State, content, parish, new EntityId((ulong)par.Priest), "tithe_poor", day);
                if (h != null) { for (int i = 0; i < rt.State.Population.People.Count && h.Adherents.Count < 30; i++) if (rt.State.Population.People.IsAlive(i) && rt.State.Population.People.HomeSettlement[i] == parish) h.Adherents.Add(rt.State.Population.People.IdOf(i).Value); h.Adults = 90; }
            }
            if (FaithOps.Of(rt.State, content) is { } fp)
            {
                Superstition.Add(fp, parish, 55);
                string rid = ri >= 0 ? religions.Get(ri)!.Id : "";
                pol.Relics[91] = new RelicRow { Key = 91, Name = "Bone of Saint Aldric", Religion = rid, Holder = 0, At = parish };
                pol.Relics[92] = new RelicRow { Key = 92, Name = "Finger of Saint Wend", Religion = rid, Holder = 1, Person = party.Hero.Value };
                pol.Relics[93] = new RelicRow { Key = 93, Name = "Tooth of Saint Orm", Religion = rid, Holder = 2, At = parish, Fake = true };
                pol.Miracles.Add(new MiracleRow { Day = day, Settlement = parish, Kind = "fire_stopped", Natural = "ch_note_miracle_stone_wall", Disputed = true });
                var see = book.SeeOf(religions.Get(ri)?.Id ?? "", ChurchSeeding.RealmOf(rt.State.Population.Region, parish));
                if (see is { Bishop: >= 0 } && ri >= 0) Excommunication.Impose(rt.State, content, fp, null, party.Hero, ri, new EntityId((ulong)see.Bishop), null, EntityId.None, day);
            }
            return 0;
        }).GetAwaiter().GetResult();
        OpenFaith();
    }
}
