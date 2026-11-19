using System.Diagnostics;
using System.Text.RegularExpressions;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Combat.Flavour;

/// <summary>feedback-flavour-log 8: FL-1 to FL-13.</summary>
public class FlavourGateTests(FlavourFixture fx) : IClassFixture<FlavourFixture>
{
    static readonly LayerOutcome[] SixOutcomes = [LayerOutcome.Cut, LayerOutcome.Crushed, LayerOutcome.Stopped, LayerOutcome.Deflected, LayerOutcome.NotCovered, LayerOutcome.Bypassed];
    static readonly string[] Kinds = ["human", "horse", "draugr", "beast"];

    static string Out(LayerOutcome o) => FlavourBook.Snake(o.ToString());

    // ------------------------------------------------------------------ FL-1 coverage

    [Fact]
    public void FL1_EveryAttackOutcomeFlagAndTargetKind_HasATemplateForEachSlotItNeeds()
    {
        var flags = Enum.GetValues<WoundFlags>().Where(f => f != WoundFlags.None).ToList();
        Assert.True(flags.Count >= 15);
        int combos = 0;
        foreach (var attack in AttackKinds.Names)
            foreach (var o in SixOutcomes)
                foreach (var f in flags)
                    foreach (var kind in Kinds)
                    {
                        var c = FlavourCtx.Blank();
                        c.Attack = attack; c.ArmourOutcome = Out(o); c.Target = kind; c.Flags = f; c.Severity = 1;
                        string where = $"{attack} x {o} x {f} x {kind}";
                        Assert.True(fx.Book.CountMatches("open", c) > 0, "no open for " + where);
                        Assert.True(fx.Book.CountMatches("armour", c) > 0, "no armour clause for " + where);
                        Assert.True(fx.Book.CountMatches("consequence", c) > 0, "no consequence for " + where);
                        combos++;
                    }
        Assert.Equal(AttackKinds.Names.Length * SixOutcomes.Length * flags.Count * Kinds.Length, combos);
    }

    [Fact]
    public void FL1_EveryTissueReached_HasAPieceAfterAnArmourClauseAndWithout()
    {
        var tissues = new (string T, string[] Grades)[]
        {
            ("skin", [""]), ("fat", [""]), ("muscle", ["sound", "bruised", "torn", "mangled"]), ("bone", ["bruised", "cracked", "broken", "displaced", "shattered"]), ("organ", [""]),
            ("artery", [""]), ("vein", [""]), ("tendon", [""]), ("nerve", [""]), ("ligament", [""]), ("spine", [""]),
        };
        foreach (var attack in AttackKinds.Names)
            foreach (var (t, grades) in tissues)
                foreach (var g in grades)
                    foreach (var chained in new[] { false, true })
                    {
                        var c = FlavourCtx.Blank();
                        c.Attack = attack; c.Tissue = t; c.Grade = g; c.Chained = chained; c.ArmourOutcome = chained ? "cut" : "none"; c.Severity = 1;
                        Assert.True(fx.Book.CountMatches("tissue", c) > 0, $"no tissue piece for {attack} {t} {g} chained={chained}");
                    }
        foreach (var (ev, slot) in new[] { ("blocked", "defence"), ("parried", "defence"), ("dodged", "defence"), ("whiff", "defence"), ("missed", "defence"), ("shot", "shot"), ("throw", "shot"), ("down", "after"), ("death", "after"),
            ("wake", "after"), ("rout", "morale"), ("rally", "morale"), ("surrender", "morale"), ("fled", "morale"), ("projectile_miss", "defence"), ("broke", "after"), ("disarmed", "after"), ("interrupted", "after") })
            foreach (var kind in Kinds)
            {
                var c = FlavourCtx.Blank(); c.Event = ev; c.Target = kind; c.Cause = ev == "down" ? "blood" : ev == "death" ? "blood" : ev == "broke" ? "shield" : ev == "disarmed" ? "weapon" : "";
                c.ArmourFamily = ev == "blocked" ? "guard" : ""; c.Style = "normal";
                Assert.True(fx.Book.CountMatches(slot, c) > 0, $"no {slot} template for {ev} on a {kind}");
            }
        foreach (var cause in new[] { "blood", "pain", "heart", "concussion", "breath" })
        {
            var c = FlavourCtx.Blank(); c.Event = "down"; c.Cause = cause;
            Assert.True(fx.Book.CountMatches("after", c) > 0, "no falling line for " + cause);
        }
        foreach (var cause in new[] { "blood", "organ", "brain", "breath", "infection" })
        {
            var c = FlavourCtx.Blank(); c.Event = "death"; c.Cause = cause;
            Assert.True(fx.Book.CountMatches("after", c) > 0, "no dying line for " + cause);
        }
    }

    [Fact]
    public void FL1_EveryWoundOfSixtyFightsFormatsToANonEmptyLine_AndTheBlowsOfEveryWeaponAreCovered()
    {
        int lines = 0, hits = 0;
        var weapons = new[] { ("arming_sword", "steel"), ("axe", "iron"), ("mace", "iron"), ("spear", "steel"), ("great_axe", "steel"), ("dagger", "bronze"), ("war_hammer", "iron"), ("club", "oak"), ("flail", "iron"), ("falchion", "steel") };
        for (int i = 0; i < 60; i++)
        {
            var (wa, ma) = weapons[i % weapons.Length]; var (wb, mb) = weapons[(i * 3 + 1) % weapons.Length];
            var armour = i % 3 == 0 ? new[] { "shirt:linen" } : i % 3 == 1 ? new[] { "gambeson:linen", "mail_hauberk:iron", "helm:iron" } : new[] { "gambeson:linen", "breastplate:steel", "helm:steel" };
            var (w, r) = fx.Duel(100 + (ulong)i, wa, ma, wb, mb, armour, armour, maxTicks: 20 * 60);
            for (int k = 0; k < r.Log.Lines.Count; k++)
            {
                string s = fx.Line(r.Log, k);
                Assert.False(string.IsNullOrWhiteSpace(s), $"fight {i}: empty line for {r.Log.Lines[k].Kind}");
                Assert.DoesNotMatch(@"\{[a-z_A-Z]+\}", s);
                Assert.True(s.Length < 500, s);
                lines++; if (r.Log.Lines[k].Kind == FightEventKind.Hit) hits++;
            }
        }
        Assert.True(lines > 600 && hits > 150, $"{lines} lines, {hits} hits");
    }

    // ------------------------------------------------------------------ FL-2 template validity

    [Fact]
    public void FL2_EveryKeyExists_EveryParamIsSupplied_NoTemplateIsTooLong_NoDuplicateIds()
    {
        var defs = fx.F.Db.All<FlavourDef>().ToList();
        Assert.InRange(defs.Count, 400, 520);
        Assert.Equal(defs.Count, defs.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
        var keys = defs.Select(d => d.TextKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        var known = new HashSet<string>(FlavourBook.KnownParams, StringComparer.Ordinal);
        foreach (var d in defs)
        {
            Assert.True(FlavourBook.Slots.Contains(d.Slot), $"{d.Id}: slot {d.Slot}");
            Assert.True(fx.En.TryGetValue(d.TextKey, out var text), $"{d.Id}: key {d.TextKey} is not in en.csv");
            Assert.True(text!.Length <= 220, $"{d.Id}: {text.Length} characters");
            Assert.False(string.IsNullOrWhiteSpace(text), d.Id);
            foreach (var p in FlavourFormatter.ParamsOf(text)) Assert.True(known.Contains(p), $"{d.Id}: param {{{p}}} is not supplied by the formatter");
            foreach (var a in d.When.Attack) Assert.Contains(a, AttackKinds.Names);
            foreach (var f in d.When.Flags) Assert.Contains(f, FlavourBook.FlagNames);
            foreach (var t in d.When.Target) Assert.Contains(t, Kinds);
            foreach (var o in d.When.ArmourOutcome) Assert.Contains(o, new[] { "cut", "crushed", "stopped", "deflected", "not_covered", "bypassed", "absorbed", "none" });
            Assert.Contains(d.Gore, new[] { "any", "high" });
        }
    }

    [Fact]
    public void FL2_TheFlBlockOfEnCsvIsOneContiguousBlockAtTheEnd_AndEveryKeyTheCodeNamesExists()
    {
        var lines = File.ReadAllLines(Path.Combine(Dialogue.TalkKit.Root(), "src", "Ravensguard.Game", "localization", "en.csv"));
        int first = Array.FindIndex(lines, l => l.StartsWith("fl_", StringComparison.Ordinal));
        Assert.True(first > 100);
        for (int i = first; i < lines.Length; i++) if (lines[i].Length > 0) Assert.StartsWith("fl_", lines[i]);
        for (int i = 0; i < first; i++) Assert.False(lines[i].StartsWith("fl_", StringComparison.Ordinal));
        // keys of the callouts and the captions the planner and the audio rows name
        foreach (var d in fx.Audio) if (d.Caption.Length > 0) Assert.True(fx.En.ContainsKey(d.Caption), d.Caption);
        foreach (var k in new[] { "fl_call_severed", "fl_call_killed", "fl_call_artery", "fl_call_down", "fl_call_parry", "fl_call_block", "fl_call_dodge", "fl_call_miss", "fl_call_armour", "fl_call_hit", "fl_cap_dir",
            "fl_ui_log_title", "fl_ui_report_title", "fl_ui_trace_row", "fl_fallback" }) Assert.True(fx.En.ContainsKey(k), k);
        foreach (var b in new[] { "head", "neck", "upper_body", "lower_body", "upper_arm", "lower_arm", "hand", "upper_leg", "lower_leg", "foot", "eye", "ear", "nose", "jaw", "teeth", "skull", "ribs" })
            Assert.True(fx.En.ContainsKey("fl_call_part_" + b), b);
        Assert.True(fx.En.Keys.Count(k => k.StartsWith("fl_call_", StringComparison.Ordinal)) >= 40);
    }

    // ------------------------------------------------------------------ FL-3 golden lines

    [Fact]
    public void FL3_TheGoldenLines_FormatExactlyFromRecordedBlows()
    {
        var got = new List<string>();
        string Fmt(Synth s, int i) => FlavourFormatter.Format(s.Log, i, fx.Book, fx.Text);

        // the plan's sentence: Aldric's axe, Bram's bronze mail, an opened artery
        var s1 = new Synth().Person(0, "Aldric", 0).Person(1, "Bram", 1);
        int i1 = s1.Blow(0, 1, "axe", "bronze", AttackKinds.Edge, "hacks", "axe", "upper_arm", ["mail_hauberk:bronze"],
            [s1.Armour(0, LayerOutcome.Cut), s1.Layer("skin", LayerOutcome.Cut), s1.Layer("muscle", LayerOutcome.Cut)],
            [s1.W("upper_arm", "muscle", WoundFlags.ArteryHit, "brachial_artery", grade: 2, bleed: 900)],
            picks: ["open.edge.bites", "armour.cut.through", "tissue.cut.tears", "cons.artery.gen"]);
        Assert.Equal("Aldric's axe bites through the bronze mail and tears into the muscle of Bram's upper arm. An artery is opened; blood sprays.", Fmt(s1, i1));
        got.Add(Fmt(s1, i1));

        var s2 = new Synth().Person(0, "Edda", 0).Person(1, "Hugo", 1);
        int i2 = s2.Blow(0, 1, "spear", "steel", AttackKinds.Point, "thrusts", "polearm", "upper_body", ["breastplate:steel"], [s2.Armour(0, LayerOutcome.Stopped)], [], FightEventKind.Stopped, picks: ["blow.plate.scrape"]);
        Assert.Equal("Edda's spear scrapes along Hugo's steel breastplate and goes no further.", Fmt(s2, i2)); got.Add(Fmt(s2, i2));

        var s3 = new Synth().Person(0, "Aldric", 0).Person(1, "Bram", 1);
        int i3 = s3.Blow(0, 1, "arming_sword", "steel", AttackKinds.Edge, "slashes", "blade", "ribs", ["mail_hauberk:iron"], [s3.Armour(0, LayerOutcome.Crushed)],
            [s3.W("ribs", "bone_tissue", kind: WoundKind.Crush, grade: 0)], picks: ["blow.mail.turn"]);
        Assert.Equal("Aldric's sword cuts the iron links of Bram's mail, but the blow lands as a bruise on his ribs.", Fmt(s3, i3)); got.Add(Fmt(s3, i3));

        var s4 = new Synth().Person(0, "Gunnar", 0).Person(1, "Ketil", 1);
        int i4 = s4.Blow(0, 1, "great_axe", "steel", AttackKinds.Edge, "hacks", "axe", "neck", null, [s4.Layer("skin", LayerOutcome.Cut)], [s4.W("neck", "muscle", WoundFlags.Severed)], picks: ["blow.head.off"]);
        Assert.Equal("Gunnar's great axe takes Ketil's head from his shoulders in a single swing.", Fmt(s4, i4)); got.Add(Fmt(s4, i4));

        var s5 = new Synth().Person(0, "Ivo", 0).Person(1, "Karl", 1);
        int i5 = s5.Blow(0, 1, "dagger", "steel", AttackKinds.Point, "stabs", "dagger", "eye_l", ["great_helm:iron"], [s5.Armour(0, LayerOutcome.Bypassed)], [s5.W("eye_l", "muscle", WoundFlags.InsetHit)], picks: ["blow.eye.visor"]);
        Assert.Equal("The point of Ivo's dagger slides through the open visor and into Karl's left eye.", Fmt(s5, i5)); got.Add(Fmt(s5, i5));

        var s6 = new Synth().Person(0, "Hildr", 0, female: true).Person(1, "Osric", 1);
        int i6 = s6.Blow(0, 1, "mace", "iron", AttackKinds.Blunt, "bashes", "hafted", "lower_arm_l", null, [s6.Layer("skin", LayerOutcome.Crushed), s6.Layer("bone_tissue", LayerOutcome.Crushed)],
            [s6.W("lower_arm_l", "bone_tissue", WoundFlags.BoneBroken, kind: WoundKind.Crush, grade: (byte)BoneGrade.Broken)], type: AttackType.Blunt, picks: ["open.blunt.slams", "", "tissue.bone.breaks", "cons.bone.hand"]);
        Assert.Equal("Hildr's mace slams and breaks the bone of Osric's left forearm. The hand hangs limp.", Fmt(s6, i6)); got.Add(Fmt(s6, i6));

        var s7 = new Synth().Person(0, "Hild", 0, female: true).Person(1, "Ragnar", 1);
        int i7 = s7.Blow(0, 1, "spear", "steel", AttackKinds.Point, "thrusts", "polearm", "lower_body", null, [s7.Layer("skin", LayerOutcome.Cut)],
            [s7.W("lower_body", "muscle", WoundFlags.SpineHit, "cord_back_l")], picks: ["open.point.stabs", "", "tissue.spine.reaches", "cons.spine.low"]);
        Assert.Equal("Hild's spear stabs and reaches the spine beneath. The blow has taken Ragnar low in the back. His legs fold; he does not feel them.", Fmt(s7, i7)); got.Add(Fmt(s7, i7));

        var s8 = new Synth().Person(0, "Aldric", 0).Person(1, "Bram", 1);
        int i8 = s8.Event(FightEventKind.Blocked, 0, 1, flags: LineFlags.Shield, picks: ["def.block.shield1"]);
        Assert.Equal("Aldric turns the blow aside with his shield.", Fmt(s8, i8)); got.Add(Fmt(s8, i8));
        int i9 = s8.Event(FightEventKind.Parried, 0, 1, picks: ["def.parry.1"]);
        Assert.Equal("Aldric parries and answers.", Fmt(s8, i9)); got.Add(Fmt(s8, i9));
        int i10 = s8.Event(FightEventKind.Dodged, 1, 0, picks: ["def.dodge.1"]);
        Assert.Equal("Bram steps out of the way.", Fmt(s8, i10)); got.Add(Fmt(s8, i10));
        int i11 = s8.Event(FightEventKind.ProjectileMiss, 1, -1, picks: ["def.arrow.miss1"]);
        Assert.Equal("An arrow whistles past and thuds into the dirt.", Fmt(s8, i11)); got.Add(Fmt(s8, i11));
        int i12 = s8.Event(FightEventKind.Surrender, 1, 0, picks: ["morale.yield1"]);
        Assert.Equal("Bram lays his weapon on the ground.", Fmt(s8, i12)); got.Add(Fmt(s8, i12));
        int i13 = s8.Event(FightEventKind.Rout, 1, -1, picks: ["morale.rout1"]);
        Assert.Equal("Bram breaks and runs.", Fmt(s8, i13)); got.Add(Fmt(s8, i13));

        // beasts and monsters: the draugr bleeds dust, the horse screams
        var s9 = new Synth().Person(0, "Aldric", 0).Person(1, "Draugr", 1, "draugr").Person(2, "Horse", 1, "horse");
        int i14 = s9.Blow(0, 1, "arming_sword", "steel", AttackKinds.Edge, "slashes", "blade", "upper_arm_r", null, [s9.Layer("muscle", LayerOutcome.Cut)],
            [s9.W("upper_arm_r", "muscle", WoundFlags.ArteryHit, "brachial_artery", grade: 1)], picks: ["open.edge.cuts", "", "tissue.cut.muscle.a", "cons.artery.draugr"]);
        Assert.Equal("Aldric's sword cuts into the muscle of the draugr's right upper arm. No blood comes; the dry sinew parts like a snapped rope.", Fmt(s9, i14)); got.Add(Fmt(s9, i14));

        var golden = Path.Combine(Dialogue.TalkKit.Root(), "tests", "Ravensguard.Sim.Tests", "Combat", "Flavour", "golden_lines.txt");
        string text = string.Join('\n', got) + "\n";
        if (Environment.GetEnvironmentVariable("FL_WRITE_GOLDEN") == "1") File.WriteAllText(golden, text);
        Assert.Equal(File.ReadAllText(golden).Replace("\r\n", "\n"), text);
    }

    // ------------------------------------------------------------------ FL-4 variety

    [Fact]
    public void FL4_HundredIdenticalBlows_ReadManyWays_AndNoLineRepeatsWithinTwelve()
    {
        var s = new Synth().Person(0, "Aldric", 0).Person(1, "Bram", 1);
        for (int i = 0; i < 100; i++)
            s.Blow(0, 1, "arming_sword", "steel", AttackKinds.Edge, "slashes", "blade", "upper_arm_l", ["gambeson:linen"],
                [s.Armour(0, LayerOutcome.Cut), s.Layer("skin", LayerOutcome.Cut), s.Layer("muscle", LayerOutcome.Cut)], [s.W("upper_arm_l", "muscle", WoundFlags.ArteryHit, "brachial_artery", grade: 2, bleed: 800)]);
        var lines = Enumerable.Range(0, 100).Select(i => FlavourFormatter.Format(s.Log, i, fx.Book, fx.Text)).ToList();
        Assert.True(lines.Distinct().Count() >= 8, $"{lines.Distinct().Count()} distinct lines");
        for (int i = 0; i < lines.Count; i++)
            for (int j = Math.Max(0, i - 11); j < i; j++) Assert.True(lines[i] != lines[j], $"line {i} repeats line {j}: {lines[i]}");
        // the same log read again, in another order, gives the same text (the picks are kept)
        for (int i = 99; i >= 0; i -= 7) Assert.Equal(lines[i], FlavourFormatter.Format(s.Log, i, fx.Book, fx.Text));
    }

    [Fact]
    public void FL4_TheSameFightReadsTheSameWhoeverLookedFirst()
    {
        var (_, a) = fx.Duel(31);
        var (_, b) = fx.Duel(31);
        var ta = Enumerable.Range(0, a.Log.Lines.Count).Select(i => fx.Line(a.Log, i)).ToList();
        var tb = Enumerable.Range(0, b.Log.Lines.Count).Reverse().Select(i => fx.Line(b.Log, i)).Reverse().ToList();   // asked back to front
        Assert.Equal(ta, tb);
        Assert.NotEmpty(ta);
    }

    // ------------------------------------------------------------------ FL-5 filters

    static (FightWorld W, FlavourRecorder R) Skirmish(FlavourFixture fx, ulong seed)
    {
        var w = FightCalibration.Skirmish(fx.C, seed, 6);
        w.RecordEvents = true;
        w.Fighters[1].Player = true;
        var r = FlavourRecorder.Attach(w, null, fx.Book);
        while (!w.IsOver && w.Tick < 20 * 60) w.StepWithAi();
        r.Finish();
        return (w, r);
    }

    [Fact]
    public void FL5_Filters_ReturnExactlyWhatTheyPromise()
    {
        var (w, r) = Skirmish(fx, 5);
        var log = r.Log;
        Assert.Equal(1, log.Hero);
        Assert.True(log.Lines.Count > 50);
        var hero = new LogFilter { Flags = LogFilterFlags.HeroOnly | LogFilterFlags.Misses | LogFilterFlags.Treatment }.Apply(log);
        var expectHero = Enumerable.Range(0, log.Lines.Count).Where(i => log.Lines[i].Actor == 1 || log.Lines[i].Target == 1).ToList();
        Assert.Equal(expectHero, hero);
        Assert.NotEmpty(hero);
        var serious = new LogFilter { Flags = LogFilterFlags.Serious }.Apply(log);
        var expectSerious = Enumerable.Range(0, log.Lines.Count).Where(i => log.Lines[i].Severity >= 2 || log.Lines[i].Kind is FightEventKind.Down or FightEventKind.Death or FightEventKind.Rout or FightEventKind.Surrender or FightEventKind.Fled).ToList();
        Assert.Equal(expectSerious, serious);
        Assert.NotEmpty(serious);
        var side = new LogFilter { Flags = LogFilterFlags.MySide | LogFilterFlags.Misses | LogFilterFlags.Treatment }.Apply(log);
        Assert.True(side.Count > hero.Count, "my side has the hero's allies too");
        foreach (var i in side) { var l = log.Lines[i]; Assert.True(log.Fighters[l.Actor].Team == 0 || l.Target >= 0 && log.Fighters[l.Target].Team == 0); }
        // the default of a fight: the hero only and the serious
        var def = LogFilter.ForFight().Apply(log);
        Assert.All(def, i => Assert.True(log.Lines[i].IsSerious && (log.Lines[i].Actor == 1 || log.Lines[i].Target == 1)));
        // misses and defences are hidden unless asked for
        var plain = new LogFilter { Flags = LogFilterFlags.None }.Apply(log);
        Assert.All(plain, i => Assert.False(LogFilter.IsDefence(log.Lines[i].Kind)));
        Assert.True(new LogFilter { Flags = LogFilterFlags.Misses }.Apply(log).Count > plain.Count);
        // the text search formats only what passed
        var found = new LogFilter { Flags = LogFilterFlags.Misses, Search = "guard" }.Apply(log, i => fx.Line(log, i));
        Assert.All(found, i => Assert.Contains("guard", fx.Line(log, i), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("heroonly,serious", LogFilter.ForFight().Serialise());
        Assert.Equal(LogFilterFlags.MySide | LogFilterFlags.Serious, LogFilter.Parse("myside,serious").Flags);
        _ = w;
    }

    // ------------------------------------------------------------------ FL-6, FL-7 report and chronicle

    [Fact]
    public void FL6_TheReport_CountsMatchTheFight_AndEveryDeathHasACompleteChain()
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var (w, r) = Skirmish(fx, seed);
            r.Log.TriggerKey = "fl_trigger_ambush"; r.Log.TriggerArg = "the bandits"; r.Log.Place = " at the ford";
            var rep = AfterFightReport.Build(r.Log, fx.Book, fx.Text);
            Assert.Equal(w.Fighters.Count(f => f.Act == FAct.Dead), rep.Dead);
            Assert.Equal(w.Fighters.Count(f => f.Act == FAct.Down), rep.Down);
            Assert.Equal(w.Fighters.Count(f => f.Act == FAct.Surrendered), rep.Yielded);
            Assert.Equal(w.Fighters.Count(f => f.Act == FAct.Fled), rep.Routed);
            Assert.Equal(rep.Dead + rep.Down, rep.Fallen.Count);
            foreach (var f in rep.Fallen)
            {
                Assert.True(f.Chain.Complete, $"seed {seed}: {f.Name} has no complete chain: {f.Chain.Text}");
                Assert.Contains("the bandits", f.Chain.Text);
                Assert.Contains("ford", f.Chain.Text);
                Assert.False(string.IsNullOrWhiteSpace(f.FinishingLine));
            }
            Assert.Equal(w.Fighters[1].Landed, rep.Landed);
            Assert.Contains(rep.Result, new[] { "won", "lost", "drawn", "fled" });
            Assert.False(string.IsNullOrWhiteSpace(rep.ResultLine));
        }
    }

    [Fact]
    public void FL7_ANamedDeathMakesOneChronicleEntryWithACause_AndNamelessSoldiersMakeNone()
    {
        // two named fighters on one side so somebody dies: rerun seeds until a death happens among the named
        FightLog? log = null; int died = 0;
        for (ulong seed = 1; seed < 40 && died == 0; seed++)
        {
            var w = new FightWorld(fx.C, seed, seed, 8 * 3600) { RecordEvents = true };
            for (int i = 0; i < 3; i++)
                w.Add(new FighterKit { Name = $"Named {i}", Team = 0, Person = (ulong)(1000 + i), Weapon = new WeaponInstance("mace", "iron"), Skill = 50, X = -1500, Y = i * 900, FaceX = 1024, Worn = [new("shirt", "linen")] });
            for (int i = 0; i < 3; i++)
                w.Add(new FighterKit { Name = $"Soldier {i}", Team = 1, Weapon = new WeaponInstance("axe", "iron"), Skill = 50, X = 1500, Y = i * 900, FaceX = -1024, Stance = "aggressive", Worn = [new("shirt", "linen")] });
            var r = FlavourRecorder.Attach(w, null, fx.Book);
            while (!w.IsOver && w.Tick < 20 * 120) w.StepWithAi();
            for (int t = 0; t < 20 * 90; t++) w.StepWithAi();                    // the hurt who are left to bleed
            r.Finish();
            died = w.Fighters.Count(f => f.Act == FAct.Dead && f.Person != 0);
            log = r.Log;
        }
        Assert.NotNull(log);
        Assert.True(died > 0, "no named death in 40 fights");
        var chron = new Chronicle(); var ids = new IdAllocator();
        var made = FightChronicle.Write(log!, chron, ids, fx.Book, fx.Text);
        var deaths = made.Where(e => e.TypeId == "fight_death").ToList();
        Assert.Equal(log!.Fighters.Values.Count(f => f.Named && (FAct)f.End == FAct.Dead), deaths.Count);
        Assert.All(deaths, e => { Assert.Single(e.Subjects); Assert.NotEmpty(e.Causes); Assert.Equal(CauseKind.Wound, e.Causes[0].Kind); Assert.False(string.IsNullOrWhiteSpace(e.Causes[0].Note)); });
        Assert.DoesNotContain(made, e => e.Subjects.Any(s => s.Value < 1000 || s.Value >= 1003));   // no soldier of the other side is a subject
        Assert.Equal(1.0, chron.CauseCoverage());
    }

    // ------------------------------------------------------------------ FL-8 cost

    [Fact, Trait("Category", "Timing")]
    public void FL8_Cost_FormattingAThousandEventsIsCheap_RecordingAHundredThousandIsCheaper_AFarFightFormatsNothing()
    {
        var (_, r) = fx.Duel(9, maxTicks: 20 * 90);
        var log = r.Log;
        // a thousand hits
        var s = new Synth().Person(0, "Aldric", 0).Person(1, "Bram", 1);
        for (int i = 0; i < 1000; i++)
            s.Blow(0, 1, i % 2 == 0 ? "arming_sword" : "axe", "steel", AttackKinds.Edge, "slashes", "blade", i % 3 == 0 ? "upper_arm_l" : "upper_leg_r", ["gambeson:linen", "mail_hauberk:iron"],
                [s.Armour(0, LayerOutcome.Cut), s.Armour(1, LayerOutcome.Cut), s.Layer("skin", LayerOutcome.Cut), s.Layer("muscle", LayerOutcome.Cut)],
                [s.W(i % 3 == 0 ? "upper_arm_l" : "upper_leg_r", "muscle", i % 5 == 0 ? WoundFlags.ArteryHit : WoundFlags.None, i % 5 == 0 ? "brachial_artery" : "", grade: 2)]);
        FlavourFormatter.Format(s.Log, 0, fx.Book, fx.Text);                    // warm-up
        double ms = TestBudget.BestPass(25, 5, () =>
        {
            var sw = Stopwatch.StartNew();
            for (int l = 0; l < 1000; l++) FlavourFormatter.Format(s.Log, l, fx.Book, fx.Text);
            return sw.Elapsed.TotalMilliseconds;
        });
        Assert.True(ms <= TestBudget.Ms(25), $"1,000 lines took {ms:0.0} ms");
        // a hundred thousand events written
        var big = new FightLog { Seed = 1 };
        double rec = TestBudget.BestPass(5, 5, () =>
        {
            var lg = new FightLog { Seed = 1 };
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100_000; i++) lg.Add(new FightLine { Tick = i, Kind = FightEventKind.Whiff, Actor = 1, Target = 2, Blow = -1 });
            return sw.Elapsed.TotalMilliseconds;
        });
        Assert.True(rec <= TestBudget.Ms(5), $"100,000 events took {rec:0.0} ms");
        // a fight nobody reads formats nothing: the recorder alone makes no picks and no strings
        Assert.Equal(0, log.PickCursor);
        Assert.NotEmpty(log.Lines);
        _ = big;
    }

    // ------------------------------------------------------------------ FL-9 slow motion changes nothing

    [Fact]
    public void FL9_ASlowMotionSchedule_ChangesStepsPerRealSecondOnly_NeverTheFight()
    {
        ulong Run(bool slow, bool record)
        {
            var w = FightCalibration.Skirmish(fx.C, 3, 5);
            w.RecordEvents = record;
            if (record) FlavourRecorder.Attach(w, null, fx.Book);
            var pacer = new StepPacer(); var gov = new SlowMotionGovernor();
            long nowUs = 0; int guard = 0;
            while (w.Tick < 20 * 40 && guard++ < 200_000)
            {
                long nowMs = nowUs / 1000;
                if (slow && w.Tick % 97 == 0) gov.Request(nowMs, true);
                int scale = slow ? gov.Scale(nowMs) : 1000;
                int steps = pacer.Advance(16_667, scale);                // one 60 fps frame
                for (int i = 0; i < steps; i++) w.StepWithAi();
                nowUs += 16_667;
            }
            return w.Hash();
        }
        // the same number of steps are taken in both runs (800), so the hashes must be equal
        ulong plain = Run(false, false), slowed = Run(true, false), recorded = Run(false, true), slowRecorded = Run(true, true);
        Assert.Equal(plain, slowed);
        Assert.Equal(plain, recorded);
        Assert.Equal(plain, slowRecorded);
        // the governor: eases, holds 0.4 s, 3 s cooldown, at most 6 a minute, nothing when off
        var g = new SlowMotionGovernor();
        Assert.False(g.Request(0, false));
        Assert.True(g.Request(1000, true));
        Assert.Equal(1000, g.Scale(999));
        Assert.InRange(g.Scale(1040), 350, 999);
        Assert.Equal(350, g.Scale(1200));
        Assert.InRange(g.Scale(1350), 350, 999);
        Assert.Equal(1000, g.Scale(1400));
        Assert.False(g.Request(2000, true), "cooldown");
        var at = new List<long>(); var g2 = new SlowMotionGovernor();
        for (long t = 0; t < 180_000; t += 250) if (g2.Request(t, true)) at.Add(t);
        Assert.True(at.Count >= 6, at.Count.ToString());
        foreach (var t0 in at) Assert.True(at.Count(t => t >= t0 && t < t0 + 60_000) <= 6, "at most 6 a minute");
        for (int i = 1; i < at.Count; i++) Assert.True(at[i] - at[i - 1] >= 3000, "cooldown 3 s");
        var p = new StepPacer(); long total = 0;
        for (int i = 0; i < 600; i++) total += p.Advance(16_667, i < 300 ? 1000 : 350);
        Assert.InRange(total, 133, 137);      // 5 s at 20 Hz (100 steps) plus 5 s at 7 Hz (35)
    }

    [Fact]
    public void FL9_TheRecorderIsOnlyAListener_TheFightHashIsTheSameAtEveryTick()
    {
        var a = FightCalibration.Skirmish(fx.C, 8, 4); var b = FightCalibration.Skirmish(fx.C, 8, 4);
        b.RecordEvents = true;
        FlavourRecorder.Attach(b, null, fx.Book);
        for (int t = 0; t < 20 * 30; t++)
        {
            a.StepWithAi(); b.StepWithAi();
            if (t % 50 == 0) Assert.Equal(a.Hash(), b.Hash());
        }
        Assert.Equal(a.Hash(), b.Hash());
    }

    // ------------------------------------------------------------------ FL-10 accessibility

    [Fact]
    public void FL10_TogglesOff_GiveNothing()
    {
        var s = new Synth().Person(0, "Aldric", 0).Person(1, "Bram", 1);
        int hit = s.Blow(1, 0, "great_axe", "steel", AttackKinds.Edge, "hacks", "axe", "neck", null, [s.Layer("skin", LayerOutcome.Cut)], [s.W("neck", "muscle", WoundFlags.Severed | WoundFlags.ArteryHit, "carotid_artery")]);
        var line = s.Log.Lines[hit]; Assert.Equal(4, line.Severity);
        var on = new FeedbackPlanner(s.Log, fx.Audio, new FeedbackSettings(true, true, true, true)).Plan(line, near: true);
        Assert.True(on.FlashMs > 0 && on.ShakePx > 0 && on.HitStopMs > 0 && on.SlowMotion && on.Callout.Key == "fl_call_severed" && on.Sound.Length > 0 && on.Caption.Length > 0 && on.Decal > 0);
        var off = new FeedbackPlanner(s.Log, fx.Audio, new FeedbackSettings(false, false, false, false, true)).Plan(line, near: true);
        Assert.Equal(0, off.ShakePx); Assert.Equal(0, off.FlashMs); Assert.Equal(0, off.TintMs); Assert.Equal(0, off.HitStopMs); Assert.False(off.SlowMotion); Assert.Equal("", off.Caption); Assert.Equal(0, off.Decal);
        Assert.Equal("fl_call_severed", off.Callout.Key);       // the words stay: they are not an effect
        var far = new FeedbackPlanner(s.Log, fx.Audio, new FeedbackSettings()).Plan(line, near: false);
        Assert.Equal(0, far.ShakePx); Assert.False(far.SlowMotion);
        // a light blow on the hero is a flash and nothing more
        int light = s.Blow(1, 0, "dagger", "iron", AttackKinds.Point, "stabs", "dagger", "upper_arm_l", null, [s.Layer("skin", LayerOutcome.Cut)], [s.W("upper_arm_l", "skin")]);
        s.Log.Blows[s.Log.Lines[light].Blow].FinalMilli = 20_000;
        var lp = new FeedbackPlanner(s.Log, fx.Audio, new FeedbackSettings()).Plan(s.Log.Lines[light], true);
        Assert.Equal(0, lp.ShakePx); Assert.False(lp.SlowMotion); Assert.True(lp.FlashMs > 0);
        // the Game facade scales by the same toggles (Accessibility.ShakeScale and friends): the planner's numbers go through it unchanged
        var src = File.ReadAllText(Path.Combine(Dialogue.TalkKit.Root(), "src", "Ravensguard.Game", "scripts", "Settings.cs"));
        Assert.Contains("ScreenShakeEnabled ? amplitude : 0f", src);
        Assert.Contains("FlashesEnabled ? intensity : 0f", src);
        Assert.Contains("SlowMotionEnabled ? slowMotionScale : 1f", src);
    }

    // ------------------------------------------------------------------ FL-11 audio

    [Fact]
    public void FL11_EveryEventKindMapsToASoundOrAnExplicitSilence_FilesExist_VoicesAreCapped()
    {
        foreach (var k in Enum.GetValues<FightEventKind>())
        {
            string name = FlavourDescribe.EventName(k);
            Assert.True(fx.Audio.Any(a => a.Events.Contains(name)), $"{k} has no audio row");
        }
        string root = Path.Combine(Dialogue.TalkKit.Root(), "src", "Ravensguard.Game", "assets", "audio", "placeholder");
        foreach (var a in fx.Audio) if (a.File.Length > 0) Assert.True(File.Exists(Path.Combine(root, a.File)), a.File);
        Assert.All(fx.Audio.Where(a => a.Silent), a => Assert.Equal("", a.Caption));
        Assert.Equal(fx.Audio.Select(a => a.Id).Distinct().Count(), fx.Audio.Count);
        // a real fight: every line has a row; hits on mail ring, hits on flesh thud
        var (_, r) = fx.Duel(4);
        foreach (var l in r.Log.Lines) Assert.NotNull(AudioMap.Resolve(r.Log, l, fx.Audio));
        var s = new Synth().Person(0, "A", 0).Person(1, "B", 1);
        int mail = s.Blow(0, 1, "axe", "iron", AttackKinds.Edge, "hacks", "axe", "upper_arm_l", ["mail_hauberk:iron"], [s.Armour(0, LayerOutcome.Cut), s.Layer("muscle", LayerOutcome.Cut)], [s.W("upper_arm_l", "muscle")]);
        int flesh = s.Blow(0, 1, "axe", "iron", AttackKinds.Edge, "hacks", "axe", "upper_arm_l", null, [s.Layer("muscle", LayerOutcome.Cut)], [s.W("upper_arm_l", "muscle")]);
        int bone = s.Blow(0, 1, "mace", "iron", AttackKinds.Blunt, "bashes", "hafted", "lower_arm_l", null, [s.Layer("bone_tissue", LayerOutcome.Crushed)], [s.W("lower_arm_l", "bone_tissue", WoundFlags.BoneBroken, grade: 2)], type: AttackType.Blunt);
        Assert.Equal("combat.hit.metal", AudioMap.Resolve(s.Log, s.Log.Lines[mail], fx.Audio)!.Id);
        Assert.Equal("combat.hit.flesh", AudioMap.Resolve(s.Log, s.Log.Lines[flesh], fx.Audio)!.Id);
        Assert.Equal("combat.bone.break", AudioMap.Resolve(s.Log, s.Log.Lines[bone], fx.Audio)!.Id);
        var voices = Enumerable.Range(0, 40).Select(i => new Voice("combat.hit.flesh", i % 5, 1000 + i * 1500, i));
        var sel = VoicePool.Select(voices);
        Assert.Equal(16, sel.Count);
        Assert.All(sel, v => Assert.True(v.DistanceMm <= 60_000));
        Assert.True(sel[0].Severity >= sel[^1].Severity);
        Assert.Equal(1000, VoicePool.Volume(8000, false)); Assert.Equal(0, VoicePool.Volume(60_000, false)); Assert.Equal(600, VoicePool.Volume(0, true));
        Assert.Equal("left", VoicePool.Bearing(0, -1000, 1024, 0)); Assert.Equal("behind", VoicePool.Bearing(-1000, 0, 1024, 0));
        var m = new MusicState();
        m.Update(0, 100, m.Wanted(false, false, false, 0)); Assert.Equal(MusicLayer.Calm, m.Layer);
        m.Update(100, 100, m.Wanted(true, false, false, 0)); Assert.Equal(MusicLayer.Tension, m.Layer);
        m.Update(200, 100, m.Wanted(true, false, true, 0)); Assert.Equal(MusicLayer.Battle, m.Layer);
        m.Update(2200, 100, MusicLayer.Calm); Assert.Equal(MusicLayer.Battle, m.Layer);
        m.Update(11_000, 100, MusicLayer.Calm); Assert.Equal(MusicLayer.Calm, m.Layer);
        for (int t = 0; t < 40; t++) m.Update(11_100 + t * 100, 100, MusicLayer.Calm);
        Assert.Equal(1000, m.Volume(MusicLayer.Calm)); Assert.Equal(0, m.Volume(MusicLayer.Battle));
    }

    // ------------------------------------------------------------------ FL-12 lint

    [Fact]
    public void FL12_EffectCode_GoesThroughTheAccessibilityFacade()
    {
        string game = Path.Combine(Dialogue.TalkKit.Root(), "src", "Ravensguard.Game");
        // effect code: the combat view and its feedback, the log and the report (the diagnostics file writes the toggles into a bug report; the settings screen sets them)
        var files = Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                && (f.Contains(Path.Combine("World", "Combat")) || Path.GetFileName(f).StartsWith("FightLog", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("AfterFight", StringComparison.Ordinal)
                    || Path.GetFileName(f).Contains("Feedback", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("StreetView.Fight", StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(files);
        var bad = new Regex(@"\bSettings\.(ScreenShake|Flashes|SlowMotion)\b");
        foreach (var f in files) Assert.False(bad.IsMatch(File.ReadAllText(f)), $"{Path.GetRelativePath(game, f)} reads a Settings toggle directly; use Accessibility");
        // and the effects code exists and uses the facade
        var fx1 = Directory.EnumerateFiles(Path.Combine(game, "World", "Combat"), "FightFeedback*.cs").ToList();
        Assert.NotEmpty(fx1);
        Assert.Contains(fx1, f => File.ReadAllText(f).Contains("Accessibility."));
    }

    // ------------------------------------------------------------------ FL-13 replay, save and load

    [Fact]
    public void FL13_TheFlavourHash_IsTheSameAcrossASaveAndALoadMidFight_AndTheBytesRepeat()
    {
        var w = FightCalibration.Skirmish(fx.C, 12, 5);
        w.RecordEvents = true;
        w.Fighters[1].Player = true;
        var rec = FlavourRecorder.Attach(w, null, fx.Book);
        rec.Log.TriggerKey = "fl_trigger_ambush"; rec.Log.TriggerArg = "the bandits"; rec.Log.Place = " at the ford";
        for (int t = 0; t < 20 * 15; t++) w.StepWithAi();
        var save = FightLogSave.Capture(rec.Log, fx.Book);
        byte[] bytes = SaveCodec.SerializeCompressed(save);
        var loaded = SaveCodec.DeserializeCompressed<FightLogSave>(bytes).Restore();
        Assert.Equal(FlavourFormatter.Hash(rec.Log, fx.Book, fx.Text), FlavourFormatter.Hash(loaded, fx.Book, fx.Text));
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(FightLogSave.Capture(loaded, fx.Book)));           // save, load, save: the same bytes
        // the fight goes on after the load: the new lines read the same on the live log and on the loaded one
        loaded.Picker = null;
        for (int t = 0; t < 20 * 10; t++) w.StepWithAi();
        Assert.True(rec.Log.Lines.Count > loaded.Lines.Count);
        for (int i = 0; i < loaded.Lines.Count; i++) Assert.Equal(FlavourFormatter.Format(rec.Log, rec.Log.IndexOfSerial(loaded.Lines[i].Serial), fx.Book, fx.Text), FlavourFormatter.Format(loaded, i, fx.Book, fx.Text));
        // a world save carries the section only when there is a log
        var report = AfterFightReport.Build(loaded, fx.Book, fx.Text);
        Assert.Contains(report.Result, new[] { "won", "lost", "drawn", "fled" });
        Assert.True(bytes.Length < 128 * 1024 * 2, $"{bytes.Length} bytes");
    }

    [Fact]
    public void FL13_ALogLongerThanTheRing_KeepsItsSeriousLines_AndTheSaveTrimsToTheNewest()
    {
        var log = new FightLog { Seed = 5 };
        for (int i = 0; i < 20_000; i++) log.Add(new FightLine { Tick = i, Kind = i % 500 == 0 ? FightEventKind.Death : FightEventKind.Whiff, Actor = 1, Target = 2, Blow = -1, Severity = (sbyte)(i % 500 == 0 ? 4 : 0) });
        Assert.InRange(log.Lines.Count, FightLog.RingCap, FightLog.RingCap * 2);
        Assert.True(log.Lines.Count(l => l.Kind == FightEventKind.Death) >= 35);
        Assert.True(log.Dropped > 10_000);
        Assert.Equal(19_999, log.Lines[^1].Tick);
        for (int i = 1; i < log.Lines.Count; i++) Assert.True(log.Lines[i].Serial > log.Lines[i - 1].Serial);
    }
}
