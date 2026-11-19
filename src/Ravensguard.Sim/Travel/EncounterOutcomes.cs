using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Travel;

/// <summary>What the player may do about an encounter (travel spec 4.1 outcomes). <c>GoOn</c> is always there for the harmless ones; a demand has no such way out.</summary>
public enum EncounterChoice : byte { GoOn, Pay, Trade, Talk, Flee, Detour, Fight, Wait }

/// <summary>One button of the encounter panel: the choice, its text key, and what it costs in mites (0: free).</summary>
public sealed record EncounterOption(EncounterChoice Choice, string TextKey, long Mites = 0)
{
    /// <summary>P4-F (watch-mode 3.3, overview 3): the estimated odds of winning a Fight (permille; -1 not estimated), shown on the button ("probably lose"); a fight is offered, never forced.</summary>
    public int OddsPm { get; init; } = -1;
}

/// <summary>
/// How an encounter ended, for the game to act on: coin to pay through the hero desk (to <see cref="Payee"/>, a real person, or the toll purse when none), the person to trade or talk with, and the delay it cost.
/// Pure: no record is touched here, the game sends the pay as a <c>HeroCommandKind.EncounterPay</c> and opens the screens.
/// </summary>
public sealed record EncounterOutcome(EncounterChoice Choice, string Result, long PayMites, EntityId Payee, EntityId Counterpart, long DelaySec, string Text);

/// <summary>
/// The outcomes of road encounters (Phase 3 cleanup B, item 1): the options each script offers, and the resolution of a choice. A fight is a stub until Phase 4: the panel says combat is not yet,
/// and the party is robbed or runs, by a draw on stream 0x348 keyed on game time and hero id only (never frame rate), weighted by the party's strength.
/// </summary>
public static class EncounterOutcomes
{
    public const long FleeDelaySec = 30 * 60, DetourDelaySec = 20 * 60, WaitDelaySec = 60;
    public const long FerryMites = 30, AlmsMites = 10;

    /// <summary>The toll a demand asks of a hero holding <paramref name="coinMites"/>: a toll keeper a fiftieth (10 to 400), highwaymen a tenth, an ambush a sixth (10 at least), never more than the hero has.</summary>
    public static long TollFor(EncounterDef d, long coinMites)
    {
        long toll = d.Script == "demand_toll" && d.Kind == "Check" ? Math.Clamp(coinMites / 50, 10, 400)
                  : d.Script == "demand_toll" ? Math.Max(10, coinMites / 10)
                  : d.Script == "ambush_attack" ? Math.Max(10, coinMites / 6)
                  : d.Outcomes.ContainsKey("ferry") ? FerryMites
                  : AlmsMites;
        return Math.Min(toll, Math.Max(0, coinMites));
    }

    /// <summary>The choices with the Fight button carrying the odds (P4-F: <c>RiskEstimate</c> of the fight the setup would start). The fight stays a choice: Flee or Pay is always beside it.</summary>
    public static IReadOnlyList<EncounterOption> Options(EncounterDef d, long coinMites, int fightOddsPm)
        => [.. Options(d, coinMites).Select(o => o.Choice == EncounterChoice.Fight ? o with { OddsPm = fightOddsPm } : o)];

    /// <summary>The choices of an encounter, in the order the panel lists them (the first is the default key).</summary>
    public static IReadOnlyList<EncounterOption> Options(EncounterDef d, long coinMites)
    {
        var o = new List<EncounterOption>();
        bool Has(string k) => d.Outcomes.ContainsKey(k);
        switch (d.Script)
        {
            case "demand_toll":
                if (Has("pay")) o.Add(new(EncounterChoice.Pay, "ENC_PAY", TollFor(d, coinMites)));
                if (Has("fight")) o.Add(new(EncounterChoice.Fight, "ENC_FIGHT"));
                o.Add(new(EncounterChoice.Flee, "ENC_FLEE"));
                break;
            case "ambush_attack":
                if (Has("pay")) o.Add(new(EncounterChoice.Pay, "ENC_PAY", TollFor(d, coinMites)));
                if (Has("fight")) o.Add(new(EncounterChoice.Fight, "ENC_FIGHT"));
                o.Add(new(EncounterChoice.Flee, "ENC_FLEE"));
                break;
            case "trade":
                o.Add(new(EncounterChoice.Trade, "ENC_TRADE")); o.Add(new(EncounterChoice.Talk, "ENC_TALK")); o.Add(new(EncounterChoice.GoOn, "ENC_GOON"));
                break;
            case "block_road":
                if (Has("ferry")) o.Add(new(EncounterChoice.Pay, "ENC_FERRY", TollFor(d, coinMites)));
                o.Add(new(EncounterChoice.Wait, "ENC_WAIT"));
                o.Add(new(EncounterChoice.Detour, "ENC_DETOUR"));
                break;
            case "beg": case "ask_help":
                if (coinMites >= AlmsMites && !Has("push") && !Has("escort")) o.Add(new(EncounterChoice.Pay, "ENC_ALMS", AlmsMites));
                o.Add(new(EncounterChoice.Talk, "ENC_TALK")); o.Add(new(EncounterChoice.GoOn, "ENC_GOON"));
                break;
            default:    // approach_talk, pass_by
                if (d.Groups.Count > 0) o.Add(new(EncounterChoice.Talk, "ENC_TALK"));
                o.Add(new(EncounterChoice.GoOn, "ENC_GOON"));
                break;
        }
        return o;
    }

    /// <summary>The first real person of the cast (a household and a record), else none.</summary>
    public static EntityId Lead(EncounterEvent ev, Func<EntityId, bool> isRecord)
    {
        foreach (ulong v in ev.Cast) { var id = new EntityId(v); if (isRecord(id)) return id; }
        return EntityId.None;
    }

    /// <summary>
    /// Resolves <paramref name="choice"/> at game second <paramref name="sec"/>. <paramref name="isRecord"/> says whether a cast id is a real record (not a generated traveller): only those are paid or traded with.
    /// <paramref name="partyStrengthPm"/> (0..1000) weights a stubbed fight toward running.
    /// </summary>
    public static EncounterOutcome Resolve(TravelState st, EncounterDef d, EncounterEvent ev, EncounterChoice choice, long coinMites, int partyStrengthPm, Func<EntityId, bool> isRecord)
    {
        var lead = Lead(ev, isRecord);
        switch (choice)
        {
            case EncounterChoice.Pay:
            {
                long m = TollFor(d, coinMites);
                if (m <= 0 || coinMites < m) return new(choice, "cannot_pay", 0, EntityId.None, lead, 0, "ENC_CANNOT_PAY");
                return new(choice, "paid", m, lead, lead, 0, d.Script == "demand_toll" || d.Script == "ambush_attack" ? "ENC_PAID_TOLL" : d.Outcomes.ContainsKey("ferry") ? "ENC_PAID_FERRY" : "ENC_PAID_ALMS");
            }
            case EncounterChoice.Trade: return new(choice, "trade", 0, EntityId.None, lead, 0, "ENC_TRADE_OPEN");
            case EncounterChoice.Talk: return new(choice, "talk", 0, EntityId.None, lead, 0, "ENC_TALK_OPEN");
            case EncounterChoice.Flee: return new(choice, "fled", 0, EntityId.None, EntityId.None, FleeDelaySec, "ENC_FLED");
            case EncounterChoice.Detour: return new(choice, "detour", 0, EntityId.None, EntityId.None, DetourDelaySec, "ENC_DETOURED");
            case EncounterChoice.Wait: return new(choice, "waited", 0, EntityId.None, EntityId.None, WaitDelaySec, "ENC_WAITED");
            case EncounterChoice.Fight:
                // Phase 4 (P4-C): a real fight. The game builds the FightSetup from the cast (Melee2.FightSetup.Encounter), fights it live and settles it with AfterFight.
                return new(choice, "fight", 0, EntityId.None, lead, 0, "ENC_FIGHT_BEGIN");
            default: return new(EncounterChoice.GoOn, "went_on", 0, EntityId.None, EntityId.None, 0, "");
        }
    }

    /// <summary>
    /// How a road fight ended (P4-C, until the defeat table of morale-capture-succession): the hero's side held the field ("won", a quarter hour to catch breath); the hero ran ("fled");
    /// or the hero is down or yielded and is robbed of a quarter of the purse (10 mites at least, never more than there is) by the encounter's lead ("robbed").
    /// </summary>
    public static EncounterOutcome AfterFight(EncounterEvent ev, bool heroSideWon, bool heroFled, long coinMites, Func<EntityId, bool> isRecord)
    {
        var lead = Lead(ev, isRecord);
        if (heroSideWon) return new(EncounterChoice.Fight, "won", 0, EntityId.None, lead, 15 * 60, "ENC_FIGHT_WON");
        if (heroFled) return new(EncounterChoice.Fight, "fled", 0, EntityId.None, EntityId.None, FleeDelaySec, "ENC_FIGHT_FLED");
        long loot = Math.Min(Math.Max(0, coinMites), Math.Max(10, coinMites / 4));
        return new(EncounterChoice.Fight, "robbed", loot, lead, lead, FleeDelaySec, "ENC_FIGHT_LOST");
    }

    /// <summary>Applies the delay of an outcome to the journey: the hero stands (or loses the time on a detour) for that long, then walks on. Resumes the journey.</summary>
    public static void Settle(TravelState st, EncounterOutcome o, long sec)
    {
        if (st.Journey is { } j && o.DelaySec > 0) j.BreakUntilSec = Math.Max(j.BreakUntilSec, sec + o.DelaySec);
        st.Resume();
    }
}
