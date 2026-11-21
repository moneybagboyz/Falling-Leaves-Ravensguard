using Ravensguard.Content;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.Rumours;

namespace Ravensguard.Sim.Worlds;

/// <summary>The links <see cref="Phase6Links.AttachAll"/> made (the game keeps the talk and family links).</summary>
public sealed record Phase6LinkSet(TalkLink Talk, FamilyLink Family);

/// <summary>
/// The hourly hooks of the Phase 6 systems that <see cref="WorldRuntime"/> does not attach itself, in the one order the game and every headless soak use (P6-G2, review finding G1): talk and rumours, then the law
/// (crime, justice; <c>LawLink</c> attaches the heists, bonds and faith politics), the family, and the contracts. <c>WorldRuntime.Start</c> calls it (P7-0B, G1) when the world has a hero; the street view and the soaks may still call it and get the same set back.
/// </summary>
public static class Phase6Links
{
    public static Phase6LinkSet AttachAll(WorldRuntime rt, ContentDatabase content)
    {
        if (rt.Phase6 is { } have) return have;                                      // P7-0B (G1): WorldRuntime.Start attaches these itself behind the hero; a second call hands back the same set
        var talk = TalkLink.Attach(rt, LineBook.From(content));
        Ravensguard.Sim.Law.LawLink.Attach(rt);                                    // P6-B2: reports and bounties at a day's start, NPC crime at hour 4
        var family = FamilyLink.Attach(rt);                                        // P6-B3: birthdays, weddings, births, lessons and the kidnappers
        Ravensguard.Sim.Contracts.ContractLink.Attach(rt);                         // P6-C2: the contracts hour
        return rt.Phase6 = new Phase6LinkSet(talk, family);
    }
}
