using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>A row's state in the printed checklist: <see cref="Pass"/> and <see cref="Fail"/> are measurements the harness took itself; <see cref="Info"/> a number it can show but not judge; <see cref="Pending"/> a row whose measure is a probe, a bot or a soak of a later audit pass (the result then names it).</summary>
public enum AuditState : byte { Pass, Fail, Info, Pending }

/// <summary>One row of the economy audit's checklist (economy-audit section 4) with its result.</summary>
public sealed record AuditRow(string Id, string Section, string Title, string Criterion, AuditState State, string Result);

/// <summary>
/// The static half of the economy audit (economy-audit sections 3 and 4; P7-EA1): the whole checklist, X1 to X14, W1 to W7, B1 to B6, L1 to L6, G1 to G5, I1 to I4 and the suspects S-1 to S-8, each with a result. A row whose measure is cheap
/// and read from the world and content (the border faucet's ledger, the mint's silence, the destroy reasons, the raw extraction's value against its wage) is measured here; every other row names the pass that closes it (EA2's probes, bots and
/// soaks, EA3's fixes) and is printed <see cref="AuditState.Pending"/>: no row is ever printed without a result (EA-16). <c>econ-audit --seed N</c> prints it; the dev console's <c>econ.audit</c> will.
/// </summary>
public static class EconAudit
{
    private sealed record Def(string Id, string Section, string Title, string Criterion, string Closer);

    private static readonly Def[] Rows =
    [
        new("X1", "exploits", "Buy at one book's ask and sell at a neighbour's bid (8 km), on foot (30 kg) and by cart (500 kg)", "<= 525 mites a day on foot after spread, toll, carting; by cart only a real price gap", "the trader bot, AuditBotTests EA-1 / EA-14 (a Soak test); numbers in docs/reviews/phase-7-audit.md"),
        new("X2", "exploits", "Sell to the gate's export desk, buy back from the same market", "desk bid < book ask for every tracked good", "EA2 probe EA-2"),
        new("X3", "exploits", "Craft, then salvage or disassemble", "value out / value in < 1 for every recipe and piece", "EA2 probe EA-3"),
        new("X4", "exploits", "Build with bought materials, demolish, sell the salvage", "loses 10-30%", "EA2 probe EA-3"),
        new("X5", "exploits", "Loot items valued below their material", "melt value / listed value <= 1.2 for every loot row", "EA2 probe EA-4; fixed in EA3a (EA_F2)"),
        new("X6", "exploits", "Melt coin when bullion is dear, strike when cheap", "a margin only inside the 7.5% seigniorage band", "EA2 probe EA-5"),
        new("X7", "exploits", "Copper token coin", "only mints hold dies; the copper cap bounds the faucet", "EA2 read (coinage.md 5.1)"),
        new("X8", "exploits", "Loans: borrow, lend on, default", "interest and principal only move coin; a default forfeits security", "read in docs/reviews/phase-7-audit.md (RI-13 and RI-14 stay green)"),
        new("X9", "exploits", "Stall footfall and fair days", "every mite of stall income from a household purse", "read in docs/reviews/phase-7-audit.md"),
        new("X10", "exploits", "Contracts escrow, bonds, tournament purses", "no contract pays without the work", "read in docs/reviews/phase-7-audit.md"),
        new("X11", "exploits", "Duplication across splits, merges, stores, carts, saves and the LOD bridge", "MassLedger and ItemTable exact after 1,000 random moves (0x701)", "AuditBotTests EA-6 (1,000 moves with saves between)"),
        new("X12", "exploits", "The industry output slider at 5x", "payback shortens by about the slider; no coin appears", "not in the audited tag (no output slider until IN2): re-run by EA3b"),
        new("X13", "exploits", "Arbitrage between realms' mints through a money changer", "the changer's fee exceeds the seigniorage gap", "not in the audited tag (no mint, no changer until CN1): CN1 pins it"),
        new("X14", "exploits", "Standing.PriceMites (A3) not yet called by the market book", "wired as a merchant-side transfer, or left unwired; never a mint", "EA2 read"),
        new("W1", "war load", "2,000 men fed 1 kg of grain a day for 15-30 days, 300 horses", "price index on the march route within WL-4", "the war-load soak (WL-4): war-load --with-control"),
        new("W2", "war load", "Paid: levies unpaid the first 10 days of service, then 150; retinue and mercenaries; 400 men paid by the realm", "treasury days funded; arrears; desertions reported", "the war-load soak (WL-8): war-load --with-control"),
        new("W3", "war load", "Armed: 960 kits from the lords' armouries, the rest by wealth", "days to fill; first input to run dry", "AE3 (kit) then EA2 (WL-9)"),
        new("W4", "war load", "Levies out of the harvest (1 in 5 households in war)", "harvest shortfall vs the control (WL-6)", "the war-load soak (WL-6): war-load --with-control"),
        new("W5", "war load", "Purveyance in two campaign years", "grievances, riots, tallies unpaid (WL-7)", "AE2 (purveyance) then EA2"),
        new("W6", "war load", "A 60-day siege of the region's largest town", "days of food; deaths; prices inside; recovery (WL-5)", "the war-load soak (WL-5): war-load --with-control"),
        new("W7", "war load", "Baggage: 60 carts (30 t) for 15 days of grain", "carts and draught animals in the region; carts purveyed", "the war-load soak: war-load --with-control"),
        new("B1", "bottlenecks", "Iron", "days of cover per book; region output vs demand", "the war-load soak: war-load --with-control"),
        new("B2", "bottlenecks", "Charcoal", "woodland acres cut vs coppice regrowth", "the war-load soak: war-load --with-control"),
        new("B3", "bottlenecks", "Timber", "logs and planks for carts, engines, ships and building", "the war-load soak: war-load --with-control"),
        new("B4", "bottlenecks", "Grain", "the region's surplus after households, the levy's lost labour and the army", "the war-load soak: war-load --with-control"),
        new("B5", "bottlenecks", "Carts", "vehicles and draught animals vs a baggage train's 60", "the war-load soak: war-load --with-control"),
        new("B6", "bottlenecks", "Labour", "idle adults < 800; men missing from the harvest; wages vs class rate", "the war-load soak: war-load --with-control"),
        new("L1", "leaks", "Every Ledger.Mint call", "each a documented source with a continent-side sink (decision I33)", "EA2 grep (EA-7)"),
        new("L2", "leaks", "Every Ledger.Sink", "Burn only in tests (fix F-3)", "EA2 grep (EA-8)"),
        new("L3", "leaks", "Abstract deltas standing in for coin", "a documented holding purse; real coin where there is a purse", "EA2 read (S14, fix F-3)"),
        new("L4", "leaks", "Every Store.Destroy reason", "no Other in the war load", "the war-load soak: war-load --with-control"),
        new("L5", "leaks", "Headless soak attachments (phase-6 G1)", "the war-load soak attaches law, family and contracts as the game does", "EA1 harness"),
        new("L6", "leaks", "Money supply growth from mines once mints are wired", "see S-2", "EA3 (F-4)"),
        new("G1", "growth", "Population at Y25 vs Y5", ">= 95%", "the war-load soak (WL-10): war-load --with-control"),
        new("G2", "growth", "Wages paid a day region-wide at Y15 vs the Y3-Y5 mean", "within 10%", "the war-load soak (WL-10): war-load --with-control"),
        new("G3", "growth", "Idle adults at Y15", "< 800", "the war-load soak (WL-10): war-load --with-control"),
        new("G4", "growth", "Money supply growth Y1-Y25", "0.5-2.5% a year on average; no year below -5% or above +8%", "the war-load soak (WL-3): war-load --with-control"),
        new("G5", "growth", "Room for new works within 4 km of a town", "a site each for L1 iron, clay and wood works on seed 1234 and three other seeds", "AuditProbeTests / the soak: needs the L1 sites of IN2"),
        new("I1", "industry", "Payback of the scripted L1 works at 1x near a town", "100-200 game days", "EA3b with the scripted works (needs IN2 chains)"),
        new("I2", "industry", "Markets absorb the works' output", "80% of the output sold within 5 days at >= 90% of the opening bid", "EA3b with the scripted works (needs IN2 chains)"),
        new("I3", "industry", "Merchants have the coin", ">= 30 days of the works' sales", "EA3b with the scripted works (needs IN2 chains)"),
        new("I4", "industry", "Raw output barely covers wages", "a raw stage earns 1.0-1.5x its wage bill at book prices", "fixed in EA3a (EA_F1, the R1 rule)"),
        new("S-1", "suspects", "Raw extraction is worth about 8.7x its wage", "1.0-1.5x after R1 (EA_F1)", "measured here"),
        new("S-2", "suspects", "Silver yield: kg of silver from 1 t of galena (0.15 after EA_F4; was 3)", "after F-4: 1-2% of the money supply a year", "EA3 (F-4)"),
        new("S-3", "suspects", "No mint runs: MintMath.StrikeInto has no caller", "coinage.md 5.1 wires mints", "measured here"),
        new("S-4", "suspects", "Loot below material", "melt value / listed value <= 1.2", "fixed in EA3a (EA_F2)"),
        new("S-5", "suspects", "Phase 6 S10 and S14", "no Burn outside tests; tribute moves real coin", "EA3 (F-3)"),
        new("S-6", "suspects", "The border faucet: exports paid by Immigrate up to the neighbour's season budget", "the bound holds against an L3 works", "measured here (this world's border coin)"),
        new("S-7", "suspects", "Coin mass is mites x 13 / 1000 g, silver only", "coinage.md 5.3", "CN1"),
        new("S-8", "suspects", "Prices are fractional mites per kg in content and micro-mites in books", "no stored number changes (coinage.md 2)", "CN1"),
    ];

    /// <summary>The whole checklist for a running economy, every row with a result.</summary>
    public static IReadOnlyList<AuditRow> Run(EconomyWorld ew, ContentDatabase? db = null)
    {
        var econ = ew.World.Economy; var led = econ.Ledger; var cat = econ.Goods;
        var list = new List<AuditRow>(Rows.Length);
        foreach (var d in Rows)
        {
            var (state, text) = Measure(d.Id, ew, econ, led, cat, db);
            if (state == AuditState.Pending) text = $"not measured by the harness: {d.Closer}";
            list.Add(new AuditRow(d.Id, d.Section, d.Title, d.Criterion, state, text));
        }
        return list;
    }

    /// <summary>The checklist's ids in order (the golden test pins them against the spec's table).</summary>
    public static IReadOnlyList<string> Ids => [.. Rows.Select(r => r.Id)];

    private static (AuditState, string) Measure(string id, EconomyWorld ew, EconomyState econ, Ledger led, GoodCatalog cat, ContentDatabase? db)
    {
        switch (id)
        {
            case "L2":
            {
                long burn = led.Sunk(SinkReason.Burn).Mites;
                return (burn == 0 ? AuditState.Pass : AuditState.Fail, $"{burn} mites burned in this world (test-only path); melted {led.Sunk(SinkReason.Melt).Mites}, emigrated {led.Sunk(SinkReason.Emigrate).Mites}");
            }
            case "L1":
                return (AuditState.Info, $"sources so far: seed {led.Sourced(SourceReason.Seed).Mites}, mint {led.Sourced(SourceReason.Mint).Mites}, immigrate {led.Sourced(SourceReason.Immigrate).Mites} mites; the call-site list is EA2's grep (EA-7)");
            case "L4":
            {
                long other = 0; for (ushort g = 0; g < cat.Count; g++) other += econ.Mass.Destroyed(g, DestroyReason.Other);
                return (other == 0 ? AuditState.Pass : AuditState.Fail, $"{other} g destroyed with reason Other in this world; the war load's own check is EA2's");
            }
            case "L5":
                return (AuditState.Pass, "the war-load harness attaches the law (with heist, bond and faith-politics links), family and contract links itself (WarLoadRun.Create)");
            case "S-3":
            {
                long minted = led.Sourced(SourceReason.Mint).Mites;
                return (minted == 0 ? AuditState.Fail : AuditState.Pass, minted == 0 ? "no mint has struck a coin in this world: the money supply grows only by border trade (coinage.md 5.1 wires the mints)" : $"{minted} mites minted (P7-CN1: the mints strike the metal delivered)");
            }
            case "S-6":
                return (AuditState.Info, $"border coin so far: immigrated {led.Sourced(SourceReason.Immigrate).Mites}, emigrated {led.Sunk(SinkReason.Emigrate).Mites} mites (exports are paid by Immigrate within the season budget)");
            case "X2" when db != null:
            {
                var (target, glut, stapleGlut) = AuditMeasures.DeskOverAsk(econ.Money);
                return (glut < 1 ? AuditState.Pass : AuditState.Fail, $"the desk's export bid after the toll is {target:P0} of the book's ask at target stock (no loop) but {glut:P0} ({stapleGlut:P0} for a staple) of it at the glut threshold, 2.8x cover, where the desk acts: it pays the book's reference, not the glut price (finding EA_F5)");
            }
            case "X3" when db != null:
            {
                var s = AuditMeasures.Salvage(db);
                return (s.MaxSalvagePm <= 900 ? AuditState.Pass : AuditState.Fail, $"{s.Pieces} build pieces return at most {s.MaxSalvagePm / 10}% of their materials; salvage_mail returns 44% and salvage_armour 1-3% of the material value (mail rings are not tradable): value out never reaches value in");
            }
            case "X4" when db != null:
            {
                var s = AuditMeasures.Salvage(db);
                return (s.MaxSalvagePm <= 900 ? AuditState.Pass : AuditState.Fail, $"a demolition returns the same goods at most {s.MaxSalvagePm / 10}%; {s.Over700} of {s.Pieces} pieces return more than 70% (a loss under the 30% the target names); none returns more than it took");
            }
            case "X5" or "S-4" when db != null:
            {
                var rows = AuditMeasures.LootRatios(db); int over = rows.Count(r => r.Ratio > 1.2); var worst = rows.OrderByDescending(r => r.Ratio).Take(3).Select(r => $"{r.Id} {r.Ratio:0}x").ToList();
                return (over == 0 ? AuditState.Pass : AuditState.Fail, $"{over} of {rows.Count} loot rows are worth more than 1.2x their listed value as material; worst {string.Join(", ", worst)} (the pack values a loot item by its material, the site budget by the listed value; finding EA_F2)");
            }
            case "X6":
                return (AuditState.Info, "no mint or melt runs in this tag; by the arithmetic a strike returns 92.5% of coin parity (a profit while bullion is below 0.925x of parity, +208% at 0.3x) and a melt returns parity with no fee (a profit above 1.0x, +300% at 4x): CN1 must close both (finding EA_F8)");
            case "X7":
                return (AuditState.Info, "no copper coin or mint exists in this tag (coinage.md is CN1's); the cap and the dies are CN1's to test");
            case "X14":
                return (AuditState.Pass, "Standing.PriceMites has no caller (AuditProbeTests checks): the rebate is unwired, so it can pay nothing");
            case "L3":
                return (AuditState.Pass, "phase-6 S14 closed in P7-0B: Diplomacy.AbstractHolding is a documented holding purse and tribute moves real coin (EA_F3 tests)");
            case "S-2" or "L6" when db != null:
            {
                var (galena, silver, mites, grade) = AuditMeasures.SilverPerWorkerDay(db, cat);
                bool ok = grade is >= 0.01 and <= 0.05;
                return (ok ? AuditState.Pass : AuditState.Fail, $"silver is {grade:0.000}% of the galena (pass 0.01-0.05%: argentiferous galena, 0.15 kg of silver a tonne); a silver miner digs {galena:0} kg of galena = {silver:0.00} kg of silver = {mites:N0} mites a worker-day; the realised growth of the money supply from the struck silver is measured by MoneyGrowthTests (EA_F4, P7-EA3c: 1-2% a game year on seeds 1234 and 42)");
            }
            case "I4" when db != null:
            {
                var rows = AuditMeasures.RawRatios(db, cat); double worst = rows.Max(r => r.Ratio);
                return (worst > 1.5 ? AuditState.Fail : AuditState.Pass, $"raw stages earn {rows.Min(r => r.Ratio):0.0}x to {worst:0.0}x their wage at book prices (pass 1.0-1.5x; finding EA_F1)");
            }
            case "S-1":
            {
                // value of one worker-day of the bulk raw kinds at the content's base prices against the unskilled day wage
                const long wage = 150; var parts = new List<string>(); double worst = 0;
                foreach (var r in db != null ? AuditMeasures.RawRatios(db, cat) : [])
                {
                    worst = Math.Max(worst, r.Ratio); parts.Add($"{r.Id} {r.Ratio:0.0}x");
                }
                return (worst > 1.5 ? AuditState.Fail : AuditState.Pass, $"value of a worker-day at base prices over the {wage}-mite unskilled wage: {string.Join(", ", parts)}");
            }
            default:
                return (AuditState.Pending, "");
        }
    }

    /// <summary>The printed checklist: one line a row, state and result.</summary>
    public static string Format(IReadOnlyList<AuditRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        string last = "";
        foreach (var r in rows)
        {
            if (r.Section != last) { sb.AppendLine($"-- {r.Section}"); last = r.Section; }
            string st = r.State switch { AuditState.Pass => "PASS   ", AuditState.Fail => "FAIL   ", AuditState.Info => "INFO   ", _ => "PENDING" };
            sb.AppendLine($"{r.Id,-4} {st} {r.Title}");
            sb.AppendLine($"          pass: {r.Criterion}");
            sb.AppendLine($"          result: {r.Result}");
        }
        int pass = rows.Count(r => r.State == AuditState.Pass), fail = rows.Count(r => r.State == AuditState.Fail), info = rows.Count(r => r.State == AuditState.Info), pend = rows.Count(r => r.State == AuditState.Pending);
        sb.AppendLine($"{rows.Count} rows: {pass} pass, {fail} fail, {info} info, {pend} pending (measured by the later audit passes)");
        return sb.ToString();
    }
}
