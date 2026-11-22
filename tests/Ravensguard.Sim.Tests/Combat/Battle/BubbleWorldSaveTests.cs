using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Phase 7 BB-C, BB-14 in the world save (battle-bubble-and-formations 7): the optional <c>battle</c> section is written only while a bubble is open, no version bump; a world saved inside a battle loads and
/// goes on to the same end, and save, load, save gives the same bytes. The people of the battle keep their wounds in the world's ledger, which the same save holds.
/// </summary>
[Collection("fight")]
public class BubbleWorldSaveTests(FightFixture fx)
{
    private static FightContent Fc => _fc ??= new FightContent(new CombatTables(EconFixture.Content), EconFixture.Content);
    private static FightContent? _fc;

    /// <summary>The live tier's step: the ledger's books advance to the fight's second under its lock, then the battle steps (LiveWorld.Step).</summary>
    private static void LiveStep(WoundLedger l, BattleBubble b) { lock (l.Sync) { l.AdvanceTo(b.W.GameSec); b.StepShared(); } }

    [Fact]
    public void ABattleSection_IsWrittenOnlyWhileABubbleIsOpen_AndALoadedWorldContinuesTheBattle()
    {
        var w = new TradeWorld();
        var st = w.State;
        st.Markets = null;
        var ledger = new WoundLedger(st.Seed, w.Sec);
        st.Wounds = ledger;
        // no bubble: no section, the bytes of a world that never fought
        var plain = SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0")));
        Assert.False(plain.Sections.ContainsKey(BattleRecord.SectionName));

        var sc = new BubbleScenario(fx) { Persons = true };
        var b = BattleBubble.Open(Fc, sc.Opening(400, 400, 500, unitSize: 50, gap: 50_000, hero: true, ledger: ledger));
        for (int t = 0; t < 600; t++) LiveStep(ledger, b);
        lock (ledger.Sync) ledger.AdvanceTo(b.W.GameSec);                      // the next live tick's first act: the books are at the second the fight stands at when the save is made
        Assert.False(b.Closed);
        st.FightSource = () => FightRecord.Capture(b.W, "raid", "battle");
        st.BattleSource = () => BattleRecord.Capture(b);
        var bytes = SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0"));
        var file = SaveCodec.Decode(bytes);
        Assert.True(file.Sections.ContainsKey(BattleRecord.SectionName));
        Assert.True(file.Sections.ContainsKey(FightRecord.SectionName));
        var back = WorldSave.Restore(file, EconFixture.Content, EconFixture.ContentHash);
        Assert.NotNull(back.Battle); Assert.NotNull(back.Fight);
        // the game's ResumeBattle: the fight from its record on the loaded ledger, the bubble over it
        var fight = back.Fight!.Restore(Fc, back.Wounds);
        var c = BattleBubble.Restore(fight, back.Battle!);
        back.Fight = null; back.Battle = null;
        back.FightSource = () => FightRecord.Capture(c.W, "raid", "battle");
        back.BattleSource = () => BattleRecord.Capture(c);
        lock (back.Wounds!.Sync) back.Wounds.AdvanceTo(c.W.GameSec);           // and the loaded world's first tick does the same (the books of the unhurt, made as the fight was rebuilt, go)
        var bytes2 = SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0"));
        var file2 = SaveCodec.Decode(bytes2);
        var differing = file.Sections.Keys.Where(k => !file2.Sections.TryGetValue(k, out var o) || !file.Sections[k].Span.SequenceEqual(o.Span)).ToList();
        Assert.True(bytes.AsSpan().SequenceEqual(bytes2), "save -> load -> save changed the bytes: " + string.Join(", ", differing));

        for (int i = 0; i < b.W.Fighters.Count; i++) Assert.True(b.W.Fighters[i].Book.Hash(1) == c.W.Fighters[i].Book.Hash(1), $"the book of fighter {i} differs after the load");
        Assert.Equal(b.W.Hash(), c.W.Hash());
        for (int t = 0; t < 40_000 && !(b.Closed && c.Closed); t++) { LiveStep(ledger, b); LiveStep(back.Wounds!, c); }
        Assert.True(b.Closed && c.Closed);
        Assert.Equal(b.W.Hash(), c.W.Hash());
        Assert.Equal(b.Result!.Sides, c.Result!.Sides);
        // closed: the section is gone again
        st.BattleSource = () => BattleRecord.Capture(b); st.FightSource = null;
        var after = SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0")));
        Assert.False(after.Sections.ContainsKey(BattleRecord.SectionName));
    }
}
