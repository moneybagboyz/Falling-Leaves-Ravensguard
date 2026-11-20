using MessagePack;
using Ravensguard.Sim.Construction;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "designs" section (P5-C, overview 7; no format bump, absent while the book is empty): the text and hash of every player building plan the save knows, in id order.
/// A hash that does not match its text on load is refused (a hand-edited save), as the other sections refuse what they cannot read.
/// </summary>
[MessagePackObject]
public sealed class DesignsSave
{
    public const string SectionName = "designs";

    [Key(0)] public string[] Ids { get; set; } = [];
    [Key(1)] public byte[] Kinds { get; set; } = [];
    [Key(2)] public string[] Texts { get; set; } = [];
    [Key(3)] public string[] Hashes { get; set; } = [];

    public static DesignsSave Capture(DesignBook b) => new()
    {
        Ids = [.. b.Designs.Keys],
        Kinds = [.. b.Designs.Values.Select(d => (byte)d.Kind)],
        Texts = [.. b.Designs.Values.Select(d => d.Text)],
        Hashes = [.. b.Designs.Values.Select(d => DesignBook.HashOf(d.Text))],
    };

    public DesignBook Restore()
    {
        var b = new DesignBook();
        for (int i = 0; i < Ids.Length; i++)
        {
            if (i < Hashes.Length && Hashes[i] != DesignBook.HashOf(Texts[i])) throw new InvalidDataException($"design {Ids[i]}: its text does not match its hash");
            b.Put(Ids[i], Texts[i], (DesignKind)(i < Kinds.Length ? Kinds[i] : 0));
        }
        return b;
    }
}
