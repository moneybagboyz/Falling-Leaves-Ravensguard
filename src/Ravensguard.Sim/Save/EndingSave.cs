using MessagePack;
using Ravensguard.Sim.Endings;

namespace Ravensguard.Sim.Save;

/// <summary>One saved paragraph of the epilogue (endings spec 4): only ids, keys and integers.</summary>
[MessagePackObject]
public sealed class EpilogueBeatSave
{
    [Key(0)] public string Kind { get; set; } = "";
    [Key(1)] public int Variant { get; set; }
    [Key(2)] public ulong[] Entries { get; set; } = [];
    [Key(3)] public ulong[] Persons { get; set; } = [];
    [Key(4)] public string[] Ids { get; set; } = [];
    [Key(5)] public long[] Nums { get; set; } = [];
}

/// <summary>The optional <c>ending</c> section (P8-0A names it, P8-EN-1 fills it; no version bump): written only in an ended save. The text is rebuilt from the beats in the language set.</summary>
[MessagePackObject]
public sealed class EndingSave
{
    public const string SectionName = Phase8Saves.EndingSection;
    [Key(0)] public string EndingId { get; set; } = "";
    [Key(1)] public int Day { get; set; }
    [Key(2)] public ulong HeroPerson { get; set; }
    [Key(3)] public EpilogueBeatSave[] Beats { get; set; } = [];

    public static EndingSave Capture(EndingBook b) => new()
    {
        EndingId = b.EndingId, Day = b.Day, HeroPerson = b.HeroPerson,
        Beats = [.. b.Beats.Select(x => new EpilogueBeatSave { Kind = x.Kind, Variant = x.Variant, Entries = x.Entries, Persons = x.Persons, Ids = x.Ids, Nums = x.Nums })],
    };

    public EndingBook Restore()
    {
        var b = new EndingBook { EndingId = EndingId, Day = Day, HeroPerson = HeroPerson };
        foreach (var x in Beats) b.Beats.Add(new EpilogueBeat { Kind = x.Kind, Variant = x.Variant, Entries = x.Entries, Persons = x.Persons, Ids = x.Ids, Nums = x.Nums });
        return b;
    }
}
