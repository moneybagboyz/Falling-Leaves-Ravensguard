using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// Mounted fighters in the combat stance (mounted-combat 7): the 64 x 64 riding-horse sprite of the art atlas under the rider, who is the standing rig lifted onto its back (as the street
/// view's <see cref="RideSprites"/> does); a gallop bobs, a panicking horse rears, a fallen one lies on its side and a riderless one runs off. Above the pair: the horse's panic and stamina.
/// The HUD adds the charge meter, the horse's state, the rider's grip, the refusal warning before a braced line and the unhorse chance on a mounted target; the log and callouts tell
/// the mounted events. The called-shot list gains the mount's parts after the rider's. Reads the sim; holds no game state.
/// </summary>
public partial class FightView
{
    /// <summary>The called-shot choices of the mount are its part index plus this.</summary>
    const int HorsePartBase = 1000;
    /// <summary>How far the rider's feet are lifted, in sprite pixels (the street view's lift).</summary>
    const float RiderLiftPx = RideSprites.RiderLift;
    Vector2 _riderLift;

    MountedLayer? Mounts => W.Mounts;

    /// <summary>Draws a horse (and its rider on top) or nothing; true when the fighter was drawn here.</summary>
    bool MountedFigure(Fighter f)
    {
        var L = Mounts;
        if (L == null || L.PairOf(f.Id) is not { } m) return false;
        if (f.Id == m.Horse)
        {
            if (m.Mounted) return true;                                               // drawn with its rider
            var h = f;
            if (h.Act == FAct.Fled && (m.LeftAt < 0 || W.Tick - m.LeftAt > 60)) return true;
            DrawHorse(m, h, Screen(h.X, h.Y));
            return true;
        }
        if (!m.Mounted || f.Act == FAct.Fled && m.Mounted && W.Fighters[m.Horse].Act == FAct.Fled) return false;
        var horse = W.Fighters[m.Horse];
        var pos = Screen(f.X, f.Y);
        DrawHorse(m, horse, pos);
        _riderLift = new Vector2(0, -RiderLiftPx * Zoom + Bob(m));
        try { DrawFighter(f); }
        finally { _riderLift = Vector2.Zero; }
        return true;
    }

    float Bob(MountedFighter m) => m.Speed > m.Gaits.WalkMmps ? (float)Math.Round(Math.Sin(W.Tick * (m.Speed > m.Gaits.TrotMmps ? 1.3 : 0.8)) * 1.2) * Zoom : 0;

    void DrawHorse(MountedFighter m, Fighter h, Vector2 pos)
    {
        var art = ArtAtlas.Shared;
        if (!art.Has("object.horse_riding")) return;
        var tex = art.Texture("object.horse_riding");
        float z = Zoom, tw = tex.GetWidth() * z, th = tex.GetHeight() * z;
        bool west = m.HX < 0 || m.HX == 0 && h.FX < 0;
        var tint = Colors.White;
        if (_hitAt.TryGetValue(h.Id, out var hit) && W.Tick - hit < 3) tint = new Color(1f, 0.55f, 0.55f);
        foreach (var wv in h.Worn) if (wv.Stats.Shape.Id is "horse_mail" or "horse_plate") { tint *= new Color(0.82f, 0.86f, 0.95f); break; }
        bool down = h.Act is FAct.Down or FAct.Dead;
        if (h.Act == FAct.Dead) tint = tint * new Color(0.55f, 0.5f, 0.5f);
        // the shadow
        DrawSetTransform(pos, 0, new Vector2(1, 0.3f));
        DrawCircle(Vector2.Zero, 22 * z, new Color(0, 0, 0, 0.28f));
        float rot = 0;
        var at = pos + new Vector2(0, Bob(m));
        if (down) { rot = west ? -Mathf.Pi / 2.2f : Mathf.Pi / 2.2f; at = pos + new Vector2(0, -6 * z); }
        else if (m.Mounted && m.PanicPm >= (Mounts?.D.Panic.RearsAt ?? 600) && W.Tick >= m.BoltUntil) rot = west ? 0.35f : -0.35f;           // rearing
        DrawSetTransform(at, rot, new Vector2(west ? -1 : 1, 1));
        DrawTextureRect(tex, new Rect2(-tw / 2, -(th - 3 * z), tw, th), false, tint);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>The horse's bars under its hooves: blood (red), stamina (green) and panic (orange); true for a horse (nothing else to draw).</summary>
    bool MountedBars(Fighter f)
    {
        var L = Mounts;
        if (L == null || L.PairOf(f.Id) is not { } m) return false;
        if (f.Id != m.Horse) return false;
        var h = f;
        if (h.Act is FAct.Dead || !m.Mounted && h.Act == FAct.Fled) return true;
        var pos = Screen(h.X, h.Y);
        float z = Zoom, bw = 20 * z, x = pos.X - bw / 2, y = pos.Y + 6 * z;                       // under the horse's hooves
        float panic = m.PanicPm / 1000f, stam = (float)Math.Clamp(m.StaminaU / 1000.0 / Math.Max(1, m.Gaits.StaminaSp), 0, 1), blood = Math.Clamp(1 - h.Book.LossPm / 500f, 0, 1);
        DrawRect(new Rect2(x - 1, y - 1, bw + 2, 13), new Color(0, 0, 0, 0.55f));
        DrawRect(new Rect2(x, y, bw * blood, 3), new Color(0.65f, 0.1f, 0.1f));
        DrawRect(new Rect2(x, y + 4, bw * stam, 3), new Color(0.3f, 0.7f, 0.35f));
        DrawRect(new Rect2(x, y + 8, bw * panic, 3), panic >= 0.6f ? new Color(1f, 0.3f, 0.1f) : new Color(1f, 0.7f, 0.2f));
        return true;
    }

    /// <summary>The rider's own bars and name sit at his lifted height.</summary>
    Vector2 BarLift(Fighter f) => Mounts?.Riding(f.Id) != null ? new Vector2(0, -RiderLiftPx * Zoom) : Vector2.Zero;

    /// <summary>The HUD lines of a mounted hero: the horse, the charge meter, the grip, the refusal warning and the unhorse chance on his target.</summary>
    void DrawMountedHud(Vector2 sz)
    {
        var L = Mounts; var hero = HeroF;
        if (L == null || hero == null) return;
        var lines = new List<(string Text, Color Col)>();
        if (L.Riding(hero.Id) is { } m)
        {
            var horse = W.Fighters[m.Horse];
            int stam = (int)(m.StaminaU / 10 / Math.Max(1, m.Gaits.StaminaSp));
            lines.Add((L_F("mt_hud_mount", horse.Name, stam, m.PanicPm / 10, m.Speed / 1000.0, m.Winded ? L_T("mt_hud_winded") : ""), Colors.White));
            lines.Add((L_F("mt_hud_grip", m.GripPm / 10), Colors.LightGray));
            // the charge meter: empty at a trot, full at 12 m/s, with the momentum it adds
            float full = Math.Clamp((m.Speed - m.Gaits.TrotMmps) / (float)Math.Max(1, 12_000 - m.Gaits.TrotMmps), 0, 1);
            var o = new Vector2(10, sz.Y - 14 - 8 * 18 - 30);
            DrawRect(new Rect2(o, new Vector2(204, 14)), new Color(0, 0, 0, 0.7f));
            DrawRect(new Rect2(o + new Vector2(2, 2), new Vector2(200 * full, 10)), m.Speed >= L.D.GallopChargeMinMmps ? new Color(1f, 0.35f, 0.15f) : new Color(0.9f, 0.75f, 0.3f));
            DrawString(_font, o + new Vector2(210, 12), L_F("mt_hud_charge", W.ChargePreviewMilli(m) / 1000.0), HorizontalAlignment.Left, 260, 14, Colors.White);
            // before a braced line: the refusal this charge would face
            if (Target() is { } t && !W.IsHorse(t))
            {
                int p = MountedBrain.RefusalEstimatePm(W, m, hero, t);
                if (p >= 200) lines.Add((L_F("mt_hud_refusal", p / 10), new Color(1f, 0.55f, 0.3f)));
            }
        }
        if (Target() is { } tt && W.Riding(tt) != null)
        {
            int bp = W.UnhorsePreviewBp(hero, tt, _attack < 0 ? 0 : _attack);
            lines.Add((L_F("mt_hud_unhorse", tt.Name, bp / 100.0), new Color(0.85f, 0.9f, 1f)));
        }
        for (int i = 0; i < lines.Count; i++)
        {
            var p = new Vector2(10, 22 + (5 + i) * 18);
            DrawString(_font, p + new Vector2(1, 1), lines[i].Text, HorizontalAlignment.Left, sz.X - 20, 14, Colors.Black);
            DrawString(_font, p, lines[i].Text, HorizontalAlignment.Left, sz.X - 20, 14, lines[i].Col);
        }
    }

    static string L_T(string k) => Ravensguard.Game.L.T(k);
    static string L_F(string k, params object[] a) => Ravensguard.Game.L.F(k, a);

    /// <summary>The log line, colour, the fighter the callout floats over and the callout of a mounted event (null for any other).</summary>
    (string Text, Color Col, Fighter? At, string Call)? MountedEvent(FightEvent e)
    {
        Fighter? F(int i) => i >= 0 && i < W.Fighters.Count ? W.Fighters[i] : null;
        string N(int i) => F(i)?.Name ?? "";
        var a = F(e.Actor);
        switch (e.Kind)
        {
            case FightEventKind.Charge: return (L_F("mt_ev_charge", N(e.Actor), e.A), new Color(1f, 0.75f, 0.3f), a, L_T("mt_call_charge"));
            case FightEventKind.Unhorsed: return (L_F(e.B != 0 ? "mt_ev_unhorsed_pinned" : "mt_ev_unhorsed", N(e.Actor)), new Color(1f, 0.6f, 0.25f), a, L_T("mt_call_unhorsed"));
            case FightEventKind.Fell:
            {
                var part = a != null && e.Part >= 0 && e.Part < a.Plan.Parts.Length ? a.Plan.Parts[e.Part].Name.ToLowerInvariant() : "";
                return e.B > 0 ? (L_F("mt_ev_fell", N(e.Actor), part), new Color(0.95f, 0.6f, 0.45f), null, "") : null;
            }
            case FightEventKind.HorseFell: return (L_F("mt_ev_horse_fell", N(e.Actor)), new Color(1f, 0.5f, 0.3f), a, "");
            case FightEventKind.Balked: return (L_F("mt_ev_balked", N(e.Actor), e.A / 10), new Color(0.9f, 0.9f, 0.5f), a, L_T("mt_call_balk"));
            case FightEventKind.Reared: return (L_F("mt_ev_reared", N(e.Actor)), new Color(0.95f, 0.8f, 0.5f), null, "");
            case FightEventKind.Bolted: return (L_F("mt_ev_bolted", N(e.Actor)), new Color(1f, 0.85f, 0.4f), a, "");
            case FightEventKind.Trampled: return e.B != 0 ? (L_F("mt_ev_trampled", N(e.Actor), N(e.Target)), new Color(1f, 0.5f, 0.35f), F(e.Target), L_T("mt_call_trample")) : null;
            case FightEventKind.BraceStrike: return (L_F("mt_ev_brace", N(e.Actor)), new Color(0.7f, 0.9f, 1f), a, L_T("mt_call_brace"));
            case FightEventKind.Shattered: return (L_F("mt_ev_shattered", N(e.Actor)), new Color(0.9f, 0.75f, 0.55f), a, L_T("mt_call_shatter"));
            default: return null;
        }
    }

    /// <summary>The mount's parts after the rider's in the called-shot list (4.2: attackers choose rider or mount).</summary>
    void MountedChoices(List<(int Part, int Gap, string Name)> list, Fighter t)
    {
        if (Mounts?.Riding(t.Id) is not { } m) return;
        var h = W.Fighters[m.Horse];
        foreach (var p in h.Plan.Parts) if (!p.IsHosted) list.Add((HorsePartBase + p.Index, -1, L_F("mt_doll_mount", p.Name)));
    }

    /// <summary>A called shot at the mount: the target becomes the horse for this blow.</summary>
    bool MountedCalledShot(int part, int gap)
    {
        if (part < HorsePartBase || Target() is not { } t || Mounts?.Riding(t.Id) is not { } m) return false;
        Send(new FightInput(FightInputKind.Target, m.Horse));
        Send(new FightInput(FightInputKind.CalledShot, _callAtk, part - HorsePartBase, -1));
        Send(new FightInput(FightInputKind.Target, t.Id));
        return true;
    }

    /// <summary>The hit chance of a called shot at the mount (percent), or -1 for a rider's part.</summary>
    double MountedHitPct(int part, int attack)
    {
        if (part < HorsePartBase || HeroF is not { } h || Target() is not { } t || Mounts?.Riding(t.Id) is not { } m) return -1;
        var horse = W.Fighters[m.Horse];
        var s = W.StrikePreview(h, horse, attack, part - HorsePartBase);
        return W.Resolver.HitChanceBp(s) / 100.0;
    }
}
