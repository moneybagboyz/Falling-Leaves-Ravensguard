namespace Ravensguard.Sim.Naval;

/// <summary>
/// P7-SH-A: the <c>NavalHash</c> (overview 11): FNV-1a over every ship frame (placement, heading, motion, riders) in id order and the water's swimmers. The ships at sea and their raids join it in the parts that save them (SH-B to SH-D, the
/// <c>naval</c> section); a fight's own ships and swimmers are already in its <c>FightHash</c> through <see cref="CarrierLayer.Hash"/>. Nothing joins <c>Phase7Trail</c> until a section has something to hash.
/// </summary>
public static class NavalHash
{
    public static ulong Of(IEnumerable<ShipFrame> frames, Swimmers? water = null)
    {
        ulong h = 14695981039346656037UL;
        foreach (var f in frames.OrderBy(f => f.Id.Value)) h = f.Hash(h);
        return water == null ? h : water.Hash(h);
    }
}
