using Ravensguard.Game.UI.NewGame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>What the Q menu reads of the soil and the house at the hero's feet, built at a barrier: his soil jobs, his fields, and the deed of the house here with the least its holder takes.</summary>
public sealed record SoilHere(IReadOnlyList<SoilRow> Jobs, IReadOnlyList<SoilFieldRow> Fields, EntityId HouseDeed, long OfferFloor, string HouseWhy);

/// <summary>
/// The street view's side of soil improvement and offers for houses (P6-D4, holdings-and-works-v2 3.3, 3.5 and 6): the Q menu's Marl this field and Manure this field (on a field of the hero's household's,
/// no contract), working the soil job by hand (his own, or a contract he took on the Journal's Contracts tab: every half game minute the attended time goes to the field, as felling does) and Offer for this house.
/// Everything reads a <see cref="SoilHere"/> made at a barrier; every action is a command through the hero's desk (recorded for replay).
/// </summary>
public partial class StreetView
{
    SoilHere? _soilView;
    Task<SoilHere?>? _soilTask;
    double _soilAcc;
    long _soilSeen;
    bool _soilOn; int _soilSerial; long _soilFrom, _soilAt;

    void RefreshSoil()
    {
        if (Runtime is not { } rt || _heroLink == null) return;
        if (_soilTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _soilView = t.Result; _soilTask = null; }
        if (_soilTask != null) return;
        var (x, y) = HeroRegionMm(); int day = (int)(HeroNow() / 86400); var hero = HeroId;
        _soilTask = Bar(rt, () =>
        {
            var sw = SoilWorld.For(rt.State); var lw = LandOf(rt);
            if (sw == null || lw == null) return null;
            var (jobs, fields) = HoldingsDesk.SoilView(sw, hero, sw.HeroHousehold(), day);
            int xm = (int)(x / 1000), ym = (int)(y / 1000);
            var reg = lw.Registry(); var (s, l) = reg.LotAt(xm, ym);
            EntityId deed = EntityId.None; long floor = 0; string why = "hw_q_no_house";
            if (s >= 0 && reg.LotDeed(s, l) is { } d && d.Holder != hero && !d.IsLease)
            {
                var (lord, _) = LandRegistry.LordOf(lw.Pop, s);
                if (d.Holder == lord || d.HolderPurse != Ravensguard.Sim.Economy.PurseKind.Household) why = "ld_offer_use_listing";
                else if (!lw.LotEmpty(s, l)) why = "ld_offer_lived_in";
                else { deed = d.Id; floor = lw.OfferFloor(d, hero); why = ""; }
            }
            return new SoilHere(jobs, fields, deed, floor, why);
        });
    }

    /// <summary>Per frame: the view is kept fresh, and the hands-on soil work is credited while it is on.</summary>
    void SoilTick(double delta)
    {
        _soilAcc += delta;
        if (_soilAcc >= 2.0 || _soilTask is { IsCompleted: true }) { _soilAcc = 0; RefreshSoil(); }
        if (_heroLink?.View is { } v)
            foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Holdings && r.Seq > _soilSeen)) { _soilSeen = r.Seq; if (!r.Ok) _soilOn = false; }
        if (!_soilOn || _heroLink == null) return;
        long now = HeroNow();
        if (now - _soilAt < 30) return;
        var row = _soilView?.Jobs.FirstOrDefault(j => j.Serial == _soilSerial);
        var (x, y) = HeroRegionMm();
        if (row == null) { _soilOn = false; return; }
        long dx = x / 1000 - row.XM, dy = y / 1000 - row.YM;
        if (dx * dx + dy * dy > 200L * 200) { _soilOn = false; _router?.ShowToast(L.T("hw_q_no_field"), 3f); return; }
        _heroLink.Issue(HoldingsDesk.SoilWork(_soilSerial, Math.Max(_soilFrom, now - 600), now, now));
        _soilFrom = now; _soilAt = now; _hwAcc = 2.0;
    }

    /// <summary>The Q menu's soil verbs and the offer for a house (P6-D4).</summary>
    void SoilVerbs(List<QMenu.Verb> v)
    {
        if (Runtime == null || _heroLink == null) return;
        var sv = _soilView; if (sv == null) return;
        var (x, y) = HeroRegionMm(); int xm = (int)(x / 1000), ym = (int)(y / 1000);
        var near = sv.Fields.Where(f => (long)(f.XM - xm) * (f.XM - xm) + (long)(f.YM - ym) * (f.YM - ym) <= 200L * 200)
            .OrderBy(f => (long)(f.XM - xm) * (f.XM - xm) + (long)(f.YM - ym) * (f.YM - ym)).ThenBy(f => f.Field).FirstOrDefault();
        if (near != null || sv.Jobs.Count > 0 || sv.Fields.Count > 0) v.Add(new(L.T("hw_q_soil"), () => { }, Heading: true));
        if (near != null)
        {
            int field = near.Field;
            v.Add(new(L.T("hw_q_marl"), () => IssueHoldings(HoldingsDesk.SoilOwn(field, SoilKind.Marl, HeroNow())), near.Busy ? L.T("hw_soil_busy") : !near.CanMarl ? L.T("hw_soil_no_marl") : null));
            v.Add(new(L.T("hw_q_manure"), () => IssueHoldings(HoldingsDesk.SoilOwn(field, SoilKind.Manure, HeroNow())), near.Busy ? L.T("hw_soil_busy") : !near.CanManure ? L.T("hw_soil_no_dung") : null));
        }
        else if (sv.Fields.Count > 0) v.Add(new(L.T("hw_q_marl"), () => { }, L.T("hw_q_no_field")));
        foreach (var j in sv.Jobs)
        {
            int serial = j.Serial;
            v.Add(new(L.F("hw_q_soil_status", j.Fertility, j.Percent), () => { }, Heading: true));
            v.Add(new(_soilOn && _soilSerial == serial ? L.T("hw_q_soil_stop") : L.T("hw_q_soil_work"), () => { _soilOn = !(_soilOn && _soilSerial == serial); _soilSerial = serial; _soilFrom = HeroNow(); _soilAt = _soilFrom - 30; },
                (long)(j.XM - xm) * (j.XM - xm) + (long)(j.YM - ym) * (j.YM - ym) > 200L * 200 ? L.T("hw_q_no_field") : null));
        }
        // Offer for this house (holdings 3.3): the holder takes the price times 1,100 less a tenth of his trust, over 1,000
        var deed = sv.HouseDeed; long floor = sv.OfferFloor;
        v.Add(new(sv.HouseDeed != EntityId.None ? L.F("hw_q_offer_at", UiKit.Money(floor)) : L.T("hw_q_offer"), () => IssueLand(LandDesk.Bid(deed, floor, HeroNow())),
            sv.HouseDeed != EntityId.None ? null : sv.HouseWhy == "hw_q_no_house" ? L.T("hw_q_no_house") : L.T(sv.HouseWhy)));
    }
}
