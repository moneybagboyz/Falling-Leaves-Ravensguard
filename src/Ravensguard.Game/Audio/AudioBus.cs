using System;
using System.Collections.Generic;

namespace Ravensguard.Game.Audio;

/// <summary>
/// The placeholder audio hook (feedback-flavour-log 4.6): <see cref="Emit"/> is where a sound is asked for, by name, at a place in the fight (mm) and an intensity 0 to 1000. No sound
/// files ship in Phase 4 (real audio is Phase 8), so nothing plays; the call is recorded for the captions and the tests, and a <see cref="Sink"/> can be set to play it when files exist.
/// At most 16 voices are kept in the recent list, the closest and most severe first (<c>VoicePool</c>). Audio never touches the sim.
/// </summary>
public static class AudioBus
{
    public readonly record struct Played(string Name, int XMm, int YMm, int Intensity, long Order);

    static readonly List<Played> _recent = [];
    static long _order;

    /// <summary>Set by the audio layer when there is one; receives every emitted sound.</summary>
    public static Action<Played>? Sink { get; set; }
    public static IReadOnlyList<Played> Recent => _recent;
    public static long Emitted => _order;

    public static void Emit(string name, int xMm, int yMm, int intensity)
    {
        if (name.Length == 0) return;
        var p = new Played(name, xMm, yMm, Math.Clamp(intensity, 0, 1000), _order++);
        _recent.Add(p);
        if (_recent.Count > 16) _recent.RemoveAt(0);
        Sink?.Invoke(p);
    }

    public static void Clear() { _recent.Clear(); }
}
