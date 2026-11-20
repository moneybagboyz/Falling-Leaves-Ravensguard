using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Medicine;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of treatment (medicine 4.1, P6-B1): Q beside a person who is downed, a captive, in the party or bleeding fast (within two metres) > Treat, the Treat button of the Character
/// screen for the hero himself, Q beside a physician > Treat me, and the after-fight report's call to action open the treatment screen over a <see cref="TreatView"/> built at a barrier. Every verb is
/// a recorded hero command (<see cref="HeroTreatDesk"/>) applied at once at a barrier and performed at the second its work ends, so the blood lost meanwhile is lost; moving more than a metre or
/// starting a fight cancels what is left of the queue (the pack was never charged for it). The Phase 4 auto-dress after a fight is gone: nothing here runs by itself.
/// </summary>
public partial class StreetView
{
    ulong _treatPatient, _treatPhysician;
    TreatView? _treatView; Task<TreatView?>? _treatTask;
    double _treatAcc; bool _treatOpen, _treatQueued;
    (long X, long Y) _treatAnchor;
    long _treatTickSec; Task<int>? _treatAdvance;

    ulong HeroPersonId => Runtime?.State.Party?.Hero.Value ?? 0;

    /// <summary>The wound ledger's book of a person has something to treat (read under the ledger's lock: a few fields).</summary>
    (bool Wounded, bool Downed, long RateMilli) WoundsOf(ulong person)
    {
        var l = Runtime?.State.Wounds; if (l == null) return (false, false, 0);
        lock (l.Sync)
        {
            if (l.Find(person) is not { Dead: false } b || b.Records.Count == 0) return (false, false, 0);
            return (true, b.State is Consciousness.Collapsed or Consciousness.Unconscious or Consciousness.Dying, b.CurrentRateMilli());
        }
    }

    string NameOfPerson(ulong id)
    {
        if (id == HeroPersonId) return L.T("tr_you");
        var p = _lod.People.FirstOrDefault(x => x.Id.Value == id);
        if (p.Name != null) return p.Name;
        var sq = _heroLink?.View?.Squad.FirstOrDefault(m => m.Person.Value == id);
        return sq?.Name ?? "?";
    }

    /// <summary>The people in reach of the screen, the hero first: the party, and anyone within two metres who has wounds on the books.</summary>
    List<(ulong Id, string Name)> TreatPatients()
    {
        var l = new List<(ulong, string)>();
        ulong hero = HeroPersonId;
        if (WoundsOf(hero).Wounded || _treatPatient == hero) l.Add((hero, L.T("tr_you")));
        foreach (var m in (_heroLink?.View?.Squad ?? []).Where(m => m.Active).OrderBy(m => m.Person.Value))
            if (WoundsOf(m.Person.Value).Wounded) l.Add((m.Person.Value, m.Name));
        foreach (var p in PeopleNear(2).OrderBy(p => p.Id.Value))
            if (l.All(x => x.Item1 != p.Id.Value) && WoundsOf(p.Id.Value).Wounded) l.Add((p.Id.Value, p.Name));
        if (l.All(x => x.Item1 != _treatPatient) && _treatPatient != 0) l.Add((_treatPatient, NameOfPerson(_treatPatient)));
        return l;
    }

    void RefreshTreat()
    {
        if (_heroLink == null) return;
        if (_treatTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _treatView = t.Result; _treatTask = null; }
        _treatTask ??= Watch(_heroLink.TreatView(_treatPatient, HeroNow(), _treatPhysician != 0 ? _treatPhysician : null), "treat view");
    }

    /// <summary>Opens the screen on a patient (the hero, a companion, a person beside him) or, with a physician, on his quote.</summary>
    void OpenTreatment(ulong patient, ulong physician = 0, int part = -1, string? shot = null)
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _treatPatient = patient; _treatPhysician = physician; _treatView = null; _treatTask = null; _treatOpen = true;
        _treatAnchor = _world.WorldMm;
        RefreshTreat();
        var link = new TreatmentLink
        {
            View = () => { RefreshTreat(); return _treatView?.Patient == _treatPatient ? _treatView : null; },
            Do = (k, p) => IssueTreat(HeroTreatDesk.Do(_treatPatient, k, p, HeroNow(), TreatLit())),
            DressAll = () => IssueTreat(HeroTreatDesk.DressAll(_treatPatient, FieldDressing.MaxSteps, HeroNow(), TreatLit())),
            Patients = TreatPatients,
            Select = id => { _treatPatient = id; _treatView = null; _treatTask = null; RefreshTreat(); },
            PatientName = () => NameOfPerson(_treatPatient),
            Pay = physician == 0 ? null : lines => { _heroLink.IssueTreat(HeroTreatDesk.Physician(_treatPhysician, _treatPatient, lines.Select(x => new QuoteLine(x.Kind, x.Part, x.FeeMites, x.ChancePct)), HeroNow()), HeroNow()); _treatQueued = true; },
            Coin = () => _heroLink?.View?.CoinMites ?? 0,
        };
        _router.Push(new TreatmentScreen(_router, link, part) { ShotPath = shot });
    }

    /// <summary>Whether the place is lit enough to operate (indoors he always has light; outdoors by day, or with a torch).</summary>
    bool TreatLit() => true;

    void IssueTreat(HeroCommand c)
    {
        if (_heroLink == null) return;
        _treatAnchor = _world.WorldMm; _treatQueued = true;
        _heroLink.IssueTreat(c, HeroNow());
    }

    /// <summary>Per frame: while work is queued the barrier performs the steps whose second has come; moving more than a metre or a fight cancels the rest.</summary>
    void TreatTick(double delta)
    {
        if (_heroLink == null) return;
        if (_treatAdvance is { IsCompleted: true }) _treatAdvance = null;
        if (!_treatQueued && !_treatOpen) return;
        bool queued = _treatView is { Queue.Count: > 0 };
        if (_treatQueued && !queued && _treatView != null && _treatView.Queue.Count == 0 && (_treatView.NowSec - _treatTickSec) > 60) _treatQueued = false;
        if (queued)
        {
            var (x, y) = _world.WorldMm;
            if (PathPlan.Dist(x - _treatAnchor.X, y - _treatAnchor.Y) > 1000) CancelTreat(L.T("tr_stop_moved"));
            else if (_world.Fight != null) CancelTreat(L.T("tr_stop_fight"));
        }
        _treatAcc += delta;
        if (_treatAdvance == null && (queued || _treatQueued) && _treatAcc >= 0.1)
        {
            _treatAcc = 0; _treatTickSec = HeroNow();
            _treatAdvance = Watch(_heroLink.TreatTick(_treatTickSec), "treat tick");
        }
        if (!_treatOpen) return;
        foreach (var r in _heroLink.View?.Results.Where(r => r.Kind == HeroCommandKind.Treat && r.Seq > _treatSeen) ?? [])
        {
            _treatSeen = r.Seq;
            if (!r.Ok || r.Mites > 0) _router?.ShowToast(r.Text, 4f);
        }
        if (_router?.Top is not TreatmentScreen) { _treatOpen = false; }
    }
    long _treatSeen;

    void CancelTreat(string why)
    {
        if (_heroLink == null) return;
        _heroLink.IssueTreat(HeroTreatDesk.Cancel(HeroNow()), HeroNow());
        _router?.ShowToast(why, 3f); _treatQueued = false;
    }

    /// <summary>The Q menu's lines for the wounded: yourself, a physician's "Treat me", and each person within two metres who may be treated (downed, a captive, in the party, bleeding fast).</summary>
    void TreatVerbs(List<QMenu.Verb> v)
    {
        if (Runtime == null || _heroLink == null) return;
        v.Add(new(L.T("tr_q_head"), () => { }, Heading: true));
        ulong hero = HeroPersonId;
        var me = WoundsOf(hero);
        v.Add(new(L.T("tr_q_self"), () => OpenTreatment(hero), me.Wounded ? null : L.T("tr_q_not_hurt")));
        var party = (_heroLink.View?.Squad ?? []).Where(m => m.Active).Select(m => m.Person.Value).ToHashSet();
        var phys = Runtime.State.Medicine;
        foreach (var p in PeopleNear(2).OrderBy(p => p.Id.Value).Take(4))
        {
            ulong id = p.Id.Value; var w = WoundsOf(id);
            if (phys != null && phys.TryGet(id, out var row))
            {
                // a physician or barber-surgeon: his quote for the hero's own wounds
                v.Add(new(L.F("tr_q_treat_me", p.Name), () => OpenTreatment(hero, id), me.Wounded ? null : L.T("tr_q_not_hurt")));
                continue;
            }
            bool consents = w.Downed || party.Contains(id) || w.RateMilli > 1000;      // a stranger consents when downed or bleeding over 1 ml/s (medicine 8.1)
            v.Add(new(L.F("tr_q_treat", p.Name), () => OpenTreatment(id), !w.Wounded ? L.T("tr_q_not_hurt") : !consents ? L.F("tr_q_refuses", p.Name) : null));
        }
    }

    /// <summary>The after-fight report's call to action and the Character screen's button: the screen on the hero with the worst bleeder selected (the view picks it).</summary>
    void OpenTreatmentSelf() => OpenTreatment(HeroPersonId);

    /// <summary>How many of the hero's wounds bleed undressed now (the after-fight report says so and offers the screen).</summary>
    int HeroBleedingWounds()
    {
        var l = Runtime?.State.Wounds; if (l == null) return 0;
        lock (l.Sync)
        {
            if (l.Find(HeroPersonId) is not { Dead: false } b) return 0;
            int n = 0; foreach (var w in b.Records) if (w.BleedX10 > 0 && !w.Internal && w.Dressing == Dressing.None && w.State != WoundState.Scar && !w.Is(Aid.Resolved)) n++;
            return n;
        }
    }

    // ------------------------------------------------------------------ dev (the smoke and the screenshot)

    /// <summary>
    /// Dev: a wounded hero for the screenshot and the smoke: a cut vein of the left forearm, a broken right thigh, a deep cut across the chest and a bruised ribcage are put on the hero's book at a
    /// barrier, three bandages and a splint in his pack; then the screen opens on him.
    /// </summary>
    public void DebugTreatment(string? shot = null)
    {
        if (Runtime is not { } rt || _heroLink == null || rt.State.Party is not { } party || rt.State.Content is not { } content) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        long now = HeroNow();
        rt.AtBarrier(() =>
        {
            var plan = MedicineWiring.PlanOf(content, "human"); if (plan == null) return 0;
            var l = rt.State.Wounds ??= new WoundLedger(rt.State.Seed, now);
            lock (l.Sync)
            {
                var b = l.For(party.Hero.Value, plan, now);
                if (b.Records.Count == 0)
                {
                    WoundV2 Cut(string part, string structure, WoundFlags fl, int rate10, int depth = 1000)
                    {
                        int p = plan.IndexOf(part); var pp = plan.Parts[p]; int si = Array.FindIndex(pp.Structures, s => s.Id == structure);
                        return new WoundV2 { Part = (short)p, Tissue = (short)(si >= 0 ? pp.Structures[si].Tissue.Index : pp.Layers[0].Tissue.Index), Structure = (short)si, Organ = -1, Kind = Ravensguard.Sim.Combat.WoundKind.Cut, DepthPm = depth, BleedX10 = rate10, Flags = fl };
                    }
                    b.Apply(plan, [Cut("lower_arm_l", "radial_vein", WoundFlags.VeinHit, 150), Cut("upper_body", "", WoundFlags.None, 20, 700)], now, now, 0x77, 0, false);
                }
            }
            var e = rt.State.Economy!; var pack = party.Carried.Pack(e);
            foreach (var (g, n) in new[] { ("bandage", 3L), ("splint", 1L) }) if (e.Goods.TryIdOf(g, out var gid) && pack.Qty(gid) < n) pack.Create(gid, n, Ravensguard.Sim.Goods.CreateReason.Seed);
            return 0;
        }).GetAwaiter().GetResult();
        OpenTreatment(party.Hero.Value, 0, -1, shot);
    }
}
