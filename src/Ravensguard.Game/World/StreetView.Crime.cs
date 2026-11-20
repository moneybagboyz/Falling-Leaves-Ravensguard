using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the law (P6-B2, crime-and-justice 4.1, 6): the HUD's eye. While the hero stands within reach of something a crime could be done to (a market stall), the HUD says
/// <b>Seen (n)</b> or <b>Unseen</b> before he acts, five times a second, from the same geometry the act itself will use (<see cref="Witnessing"/>): everyone awake within sight range with a clear line, by the light,
/// the roof, the rain and the hero's sneaking. Nothing is rolled here and nothing is written.
/// </summary>
public partial class StreetView
{
    Label? _eyeLabel;
    long _eyeTick = -1;
    Ravensguard.Sim.Law.LawContent? _lawContent;
    /// <summary>How near a stall (metres) makes it a target.</summary>
    const double CrimeReachM = 2.5;

    void BuildEye(VBoxContainer v)
    {
        _eyeLabel = new Label { Name = "Eye", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        v.AddChild(_eyeLabel);
    }

    /// <summary>The eye, at 5 Hz (every fourth step of the 20 Hz live tick).</summary>
    void UpdateEye(LiveSnapshot s)
    {
        if (_eyeLabel == null || s.Tick / 4 == _eyeTick) return;
        _eyeTick = s.Tick / 4;
        var map = _world.Map; int hx = s.Hero.X.Floor(), hy = s.Hero.Y.Floor(), hz = s.Hero.Z;
        bool target = map != null && map.Props.Any(p => p.Z == hz && p.Sprite.Contains("stall", StringComparison.Ordinal) && Math.Abs(p.X - hx) + Math.Abs(p.Y - hy) <= (int)Math.Ceiling(CrimeReachM));
        if (!target) { _eyeLabel.Visible = false; return; }
        var law = _lawContent ??= Ravensguard.Sim.Law.LawContent.From(_source?.Content);
        var scene = new GridScene(_world.Lights, s.Light, s.Ambient, _wx.State is WeatherState.HeavyRain or WeatherState.Thunderstorm, law.S.LitLevel);
        bool sneaking = false;                                         // the live tier has no sneak mode yet (open item): the eye reads the full range
        int stealth = _hero?.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(Ravensguard.Sim.People.Skill.Stealth)) ?? 0;
        var me = new WitnessActor(Ravensguard.Sim.Core.EntityId.None, new Cell3(hx, hy, hz), sneaking, stealth, false, false, 0, ActorLook.None);
        var c = LodCentres()[0];
        var seen = PeopleNear(60).Select(p => new Observer
        {
            Person = p.Id, At = new Cell3(hx + (int)Math.Floor((p.XMm - c.X) / 1000.0), hy + (int)Math.Floor((p.YMm - c.Y) / 1000.0), p.Z), Awake = p.Intent != Ravensguard.Sim.Behaviour.ActionKind.Sleep,
        }).ToList();
        int n = Witnessing.SeenCount(law.S, scene, me, seen);
        _eyeLabel.Text = n > 0 ? L.F("CJ_HUD_SEEN", n) : L.T("CJ_HUD_UNSEEN");
        _eyeLabel.Modulate = n > 0 ? new Color(1f, 0.55f, 0.4f) : new Color(0.6f, 0.9f, 0.6f);
        _eyeLabel.Visible = true;
    }
}
