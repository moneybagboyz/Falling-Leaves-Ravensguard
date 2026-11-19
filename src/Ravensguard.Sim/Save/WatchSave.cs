using MessagePack;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Save;

/// <summary>The optional "watch" section (P4-F, watch-mode 10: 16 bytes a stance): the stances by person in id order, keep watching, take-back on input, the speed cap.</summary>
[MessagePackObject]
public sealed class WatchSave
{
    public const string SectionName = "watch";
    [Key(0)] public ulong[] Persons { get; set; } = [];
    [Key(1)] public long[] Packed { get; set; } = [];
    [Key(2)] public ulong[] Protect { get; set; } = [];
    [Key(3)] public bool KeepWatching { get; set; }
    [Key(4)] public bool TakeBackOnInput { get; set; } = true;
    [Key(5)] public int SpeedCap { get; set; } = 4;

    public static WatchSave Capture(WatchBook b) => new()
    {
        Persons = [.. b.Stances.Keys], Packed = [.. b.Stances.Values.Select(s => s.PackA())], Protect = [.. b.Stances.Values.Select(s => s.Protect)],
        KeepWatching = b.KeepWatching, TakeBackOnInput = b.TakeBackOnInput, SpeedCap = b.SpeedCap,
    };

    public WatchBook Restore()
    {
        var b = new WatchBook { KeepWatching = KeepWatching, TakeBackOnInput = TakeBackOnInput, SpeedCap = SpeedCap };
        for (int i = 0; i < Persons.Length && i < Packed.Length; i++) b.Stances[Persons[i]] = WatchStance.Unpack(Packed[i], i < Protect.Length ? Protect[i] : 0);
        return b;
    }
}
