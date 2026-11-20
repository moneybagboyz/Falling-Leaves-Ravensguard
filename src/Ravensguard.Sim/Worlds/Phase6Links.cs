using Ravensguard.Content;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.Rumours;

namespace Ravensguard.Sim.Worlds;

/// <summary>The links <see cref="Phase6Links.AttachAll"/> made (the game keeps the talk and family links).</summary>
public sealed record Phase6LinkSet(TalkLink Talk, FamilyLink Family);

/// <summary>
/// The hourly hooks of the Phase 6 systems that <see cref="WorldRuntime"/> does not attach itself, in the one order the game and every headless soak use (P6-G2, review finding G1): talk and rumours, then the law
/// (crime, justice; <c>LawLink</c> attaches the heists, bonds and faith politics), the family, and the contracts. The street view and the soaks both call this, so the two cannot drift.
/// </summary>
public static class Phase6Links
{
    public static Phase6LinkSet AttachAll(WorldRuntime rt, ContentDatabase content)
    {
        var talk = TalkLink.Attach(rt, LineBook.From(content));
        Ravensguard.Sim.Law.LawLink.Attach(rt);                                    // P6-B2: reports and bounties at a day's start, NPC crime at hour 4
        var family = FamilyLink.Attach(rt);                                        // P6-B3: birthdays, weddings, births, lessons and the kidnappers
        Ravensguard.Sim.Contracts.ContractLink.Attach(rt);                         // P6-C2: the contracts hour
        return new Phase6LinkSet(talk, family);
    }
}
