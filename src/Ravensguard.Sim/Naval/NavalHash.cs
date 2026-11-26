namespace Ravensguard.Sim.Naval;

/// <summary>
/// P7-SH-A: the <c>NavalHash</c> (overview 11): FNV-1a over every ship frame (placement, heading, motion, riders) in id order and the water's swimmers. The ships at sea and their raids join it in the parts that save them (SH-B to SH-D, the
/// <c>naval</c> section); a fight's own ships and swimmers are already in its <c>FightHash</c> through <see cref="CarrierLayer.Hash"/>. Nothing joins <c>Phase7Trail</c> until a section has something to hash.
/// </summary>
public static class NavalHash
{
    /// <summary>A saved frame's state mixed into a hash (the book's share).</summary>
    internal static ulong Mix(ulong h, ShipFrameState f)
    {
        h = ShipFrame.Mix(h, (long)f.Id); h = ShipFrame.Mix(h, f.X); h = ShipFrame.Mix(h, f.Y); h = ShipFrame.Mix(h, f.Heading); h = ShipFrame.Mix(h, f.Speed); h = ShipFrame.Mix(h, f.TurnPerTick); h = ShipFrame.Mix(h, f.AccX); h = ShipFrame.Mix(h, f.AccY);
        for (int i = 0; i < f.RiderIds.Length; i++) { h = ShipFrame.Mix(h, (long)f.RiderKinds[i] | (long)f.RiderDecks[i] << 8); h = ShipFrame.Mix(h, (long)f.RiderIds[i]); h = ShipFrame.Mix(h, f.RiderLx[i]); h = ShipFrame.Mix(h, f.RiderLy[i]); }
        return h;
    }

    public static ulong Of(IEnumerable<ShipFrame> frames, Swimmers? water = null)
    {
        ulong h = 14695981039346656037UL;
        foreach (var f in frames.OrderBy(f => f.Id.Value)) h = f.Hash(h);
        return water == null ? h : water.Hash(h);
    }
}
