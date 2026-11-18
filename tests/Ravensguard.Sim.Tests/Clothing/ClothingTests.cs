using Ravensguard.Content;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Live;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Clothing;

/// <summary>The golden numbers of docs/specs/phase3/clothing-warmth-exposure.md section 7 (C-1 to C-10, C-14 to C-16; butchering C-11 to C-13 belongs to the butchery step), and the scenarios the lead asked for.</summary>
public class ClothingTests(ITestOutputHelper output)
{
    private static ClothingBook NewBook() => new(EconFixture.Content);
    private static ExposureTuning Tu => ExposureTuning.From(EconFixture.Content);
    private static EntityId P(int n) => EntityId.Make(IdKind.Person, (ulong)n);

    /// <summary>A person wearing exactly these shapes (in default materials), undyed, perfectly fitting.</summary>
    private static Tracked Wearing(ClothingBook b, int n, params string[] shapes)
    {
        var t = b.Track(P(n), Sex.Male, SocialClass.Peasant, Occupation.Farmer, 50, 1, 0, dress: false);
        foreach (var s in shapes) { var g = b.Garments[s]; b.Wear(t, b.Create(g, g.DefaultMaterial!, t.Person, fitCm3: t.SizeCm3)); }
        return t;
    }

    private static readonly string[] WinterPeasant = ["shirt", "tunic", "hose", "cloak", "hood", "boots", "gloves"];

    [Fact] // the winter peasant outfit is about one clo (spec 4.2)
    public void WinterPeasantOutfit_IsAboutOneClo_AndAGambesonAddsTorsoInsulation()
    {
        var b = NewBook(); var t = Wearing(b, 1, WinterPeasant);
        int i = ClothingMath.IEff(b.Tuning, b.WornOf(t)); output.WriteLine($"winter peasant I_eff {i}");
        Assert.InRange(i, 85, 105);
        var t2 = Wearing(b, 2, [.. WinterPeasant, "gambeson"]);
        int i2 = ClothingMath.IEff(b.Tuning, b.WornOf(t2)); output.WriteLine($"with gambeson {i2}");
        Assert.InRange(i2 - i, 30, 60);                                       // spec: about +0.34 clo of torso-weighted insulation; all regions here
    }

    [Fact] // C-1
    public void C1_WinterPeasantWalking_ZeroDegrees()
    {
        var tu = Tu; int req = ClothingMath.IReq(tu, 0, Activity.Walk);
        Assert.Equal(171, req);
        int stress = ClothingMath.ColdStressPm(tu, req, 96); Assert.Equal(250, stress);
        long rate = ClothingMath.CoreRatePerHour(tu, stress, 0, 3700, false, 1000, true);
        Assert.Equal(-20_000, rate);                                          // 0.20 C an hour
        Assert.Equal(5.0, 100.0 / (-rate / 1000.0), 0.01);                     // chilled (36.0) after 5.0 h
    }

    [Fact] // C-2
    public void C2_ColdWindyNight_CloakBlocksTheWind()
    {
        var tu = Tu;
        int chill = ClothingMath.WindChillCenti(tu, 800, 600); Assert.Equal(144, chill);
        int tEff = -800 - chill; Assert.Equal(-944, tEff);
        int req = ClothingMath.IReq(tu, tEff, Activity.Walk); Assert.Equal(244, req);
        int stress = ClothingMath.ColdStressPm(tu, req, 96); Assert.Equal(493, stress);
        long rate = ClothingMath.CoreRatePerHour(tu, stress, 0, 3700, false, 1000, true);
        Assert.Equal(39.44, -rate / 1000.0, 0.01);
        Assert.InRange(100.0 / (-rate / 1000.0), 2.4, 2.6);                   // chilled 2.5 h
        Assert.InRange(200.0 / (-rate / 1000.0), 5.0, 5.2);                   // moderate 5.1 h
        Assert.InRange(500.0 / (-rate / 1000.0), 12.6, 12.8);                 // severe 12.7 h
    }

    [Fact] // C-3 and C-4
    public void C3_C4_HeatAndAKnightInHarness()
    {
        var tu = Tu;
        int s = ClothingMath.HeatStressPm(tu, 3400, Activity.Work, 120, 0); Assert.Equal(513, s);
        Assert.Equal(51_300, ClothingMath.CoreRatePerHour(tu, 0, s, 3700, false, 1000, true));
        Assert.InRange(100.0 / 51.3, 1.9, 2.0);                                // warm (38.0) after 1.9 h
        int k = ClothingMath.HeatStressPm(tu, 3400, Activity.Work, 120, 1550); Assert.Equal(1000, k);
        Assert.Equal(100_000, ClothingMath.CoreRatePerHour(tu, 0, k, 3700, false, 1000, true));       // +1.0 C an hour: heatstroke (41.0) after 4.0 h
        // the real harness: gambeson 400 + mail 300 + helm 250 + breastplate 600 = 1,550 retention from the content
        var b = NewBook(); var t = Wearing(b, 3, "shirt", "gambeson", "mail_hauberk", "helm", "breastplate");
        Assert.Equal(1550, b.WornOf(t).Sum(w => w.G.HeatRetentionPm) - b.Garments["shirt"].HeatRetentionPm);
    }

    [Fact] // C-5
    public void C5_Wetting_TunicSoakedInAnHour_OiledCloakTakesFourHours()
    {
        var tu = Tu;
        Assert.Equal(1800, ClothingMath.WetGainPerMinute(tu, 40, false, 100));        // +18 permille a minute (in 1/100 permille)
        Assert.Equal(400, ClothingMath.WetGainPerMinute(tu, 40, false, 800));         // +4 a minute
        Assert.Equal(900, ClothingMath.WetGainPerMinute(tu, 40, true, 100));          // snow counts half
        var b = NewBook(); var t = Wearing(b, 1, "tunic"); var env = new ExposureEnv(1000, 0, 40);
        var tunic = t.Worn[0];
        for (int m = 0; m < 55; m++) b.StepMinute(t, m, env, Activity.Walk);
        Assert.Equal(990, b.ExtraOf(tunic).WetPm);
        for (int m = 55; m < 57; m++) b.StepMinute(t, m, env, Activity.Walk);
        Assert.Equal(1000, b.ExtraOf(tunic).WetPm);                                  // soaked in 56 minutes
        var t2 = Wearing(b, 2, "shirt", "oiled_cloak"); var cloak = t2.Worn[1];
        int mins = 0; while (b.ExtraOf(cloak).WetPm < 1000 && mins < 400) b.StepMinute(t2, mins++, env, Activity.Walk);
        Assert.InRange(mins, 245, 255);
        Assert.True(b.ExtraOf(t2.Worn[0]).WetPm < 1000, "the shirt under a cloak that is dry keeps dry for a long while");
    }

    [Fact] // C-6
    public void C6_WetWoolCloak_LosesFortyPercent_AndGainsFiftyPercentMass()
    {
        var b = NewBook(); var g = b.Garments["cloak"];
        long dry = ClothingMath.ItemRegionMilli(g, Region.Torso, 1000, 0), wet = ClothingMath.ItemRegionMilli(g, Region.Torso, 1000, 1000);
        Assert.Equal(0.60, wet / (double)dry, 0.01);
        var t = Wearing(b, 1, "cloak"); Assert.Equal(1800, b.WornMassG(t, wet: false));
        b.Immerse(t); Assert.Equal(2700, b.WornMassG(t));
        Assert.Equal(2700, ClothingMath.WetMassG(1800, 50, 1000));
    }

    [Fact] // C-7
    public void C7_DryingASoakedCloak()
    {
        var tu = Tu;
        double Hours(ExposureEnv e, bool hung = false) => 100_000.0 / ClothingMath.DryPerMinute(tu, e, hung) / 60;
        Assert.InRange(Hours(new ExposureEnv(1000, Fire: true)), 3.9, 4.1);
        Assert.InRange(Hours(new ExposureEnv(1000, Fire: true), true), 2.4, 2.6);
        Assert.InRange(Hours(new ExposureEnv(1000)), 14.0, 14.7);
        Assert.InRange(Hours(new ExposureEnv(1000, Roofed: true)), 16.3, 17.1);
        Assert.True(Hours(new ExposureEnv(1000, 0, 20)) > 50, "in rain a garment barely dries");
        Assert.True(Hours(new ExposureEnv(1000, WindCmps: 500, Sunny: true)) < Hours(new ExposureEnv(1000)));
    }

    [Fact] // C-8
    public void C8_IndoorsUnheatedAndByAFire()
    {
        var tu = Tu;
        Assert.Equal(0, ClothingMath.TAir(tu, new ExposureEnv(-500, Roofed: true)));          // -5 + 5
        Assert.Equal(1000, ClothingMath.TAir(tu, new ExposureEnv(-500, Fire: true)));         // -5 + 15
        Assert.Equal(171, ClothingMath.IReq(tu, 0, Activity.Walk));                           // as C-1
        Assert.Equal(93, ClothingMath.IReq(tu, 1000, Activity.Walk));                         // by the fire
        Assert.Equal(0, ClothingMath.ColdStressPm(tu, 93, 96));
        Assert.Equal(250_000, ClothingMath.CoreRatePerHour(tu, 0, 0, 3500, true, 1000, true)); // rewarm +2.5 C an hour
        Assert.Equal(125_000, ClothingMath.CoreRatePerHour(tu, 0, 0, 3500, true, 1000, false)); // hungry: half
    }

    [Fact] // C-9
    public void C9_Fit_PlusOrMinusOneSeventh()
    {
        Assert.True(ClothingMath.Fits(100, 86)); Assert.True(ClothingMath.Fits(100, 114));
        Assert.False(ClothingMath.Fits(100, 85)); Assert.False(ClothingMath.Fits(100, 115));
        var b = NewBook(); var t = b.Track(P(1), Sex.Male, SocialClass.Peasant, Occupation.Farmer, 50, 1, 0, dress: false);
        var g = b.Garments["tunic"];
        var small = b.Create(g, "wool", t.Person, fitCm3: t.SizeCm3 * 6 / 10);
        Assert.Contains("refit", b.CannotWear(t, small)); Assert.Null(b.CannotWear(t, b.Create(g, "wool", t.Person, fitCm3: t.SizeCm3)));
        Assert.Equal("it is not worn", b.CannotWear(t, b.Create(b.Garments["bedroll"], "wool", t.Person, fitCm3: t.SizeCm3)));
        // two cloaks do not stack, a cloak over a gambeson over a shirt does
        b.Wear(t, b.Create(g, "wool", t.Person, fitCm3: t.SizeCm3));
        Assert.NotNull(b.CannotWear(t, b.Create(g, "wool", t.Person, fitCm3: t.SizeCm3)));
        Assert.Null(b.CannotWear(t, b.Create(b.Garments["cloak"], "wool", t.Person, fitCm3: t.SizeCm3)));
    }

    [Fact] // C-10
    public void C10_StainsLowerValue_UntilWashed()
    {
        var b = NewBook(); var t = Wearing(b, 1, "tunic"); var it = t.Worn[0];
        long clean = b.ValueMites(it);
        b.Stain(it, ClothingMath.Blood, 60);
        Assert.Equal(94, ClothingMath.StainLossPm(ClothingMath.Blood, 60));
        Assert.Equal(clean * (1000 - 94) / 1000, b.ValueMites(it));
        b.Wash(it, soap: false); Assert.True(b.Items.Get(it).Stains is > 0 and < 60); Assert.Equal(1000, b.ExtraOf(it).WetPm);
        b.Wash(it, soap: true); Assert.Equal(0, b.Items.Get(it).Stains); Assert.Equal(clean, b.ValueMites(it));
    }

    [Fact] // everyone gets clothes by class and season, as real items with mass, value and wear
    public void Wardrobe_ByClassAndSeason_RealItemsWithMassValueAndWear()
    {
        var b = NewBook(); long winter = new GameTime(0).Seconds + (long)(3 * 15 + 3) * 86400, summer = (long)(15 + 4) * 86400;
        Assert.True(ClothingBook.ColdSeason(winter)); Assert.False(ClothingBook.ColdSeason(summer));
        int cloaksW = 0, cloaksS = 0, n = 300;
        for (int i = 1; i <= n; i++)
        {
            var w = b.Track(P(i), Sex.Female, SocialClass.Peasant, Occupation.Farmer, 50, 7, winter);
            var s = b.Track(P(1000 + i), Sex.Female, SocialClass.Peasant, Occupation.Farmer, 50, 7, summer);
            Assert.Contains(w.Worn, x => b.GarmentOf(x).Id == "tunic"); Assert.Contains(w.Worn, x => b.GarmentOf(x).Id == "shirt");
            if (w.Worn.Any(x => b.GarmentOf(x).Id == "cloak")) cloaksW++;
            if (s.Worn.Any(x => b.GarmentOf(x).Id == "cloak")) cloaksS++;
            foreach (var x in w.Worn) { Assert.True(b.Items.MassG(b.Items.IndexOf(x), null!) > 50); Assert.True(b.ValueMites(x) > 0); Assert.True(b.Items.Get(x).ConditionPermille <= 1000); Assert.Equal(w.Person, b.Items.Get(x).Owner); }
        }
        output.WriteLine($"cloaks: winter {cloaksW}/{n}, summer {cloaksS}");
        Assert.InRange(cloaksW, 150, 260); Assert.Equal(0, cloaksS);
        var gent = b.Track(P(5000), Sex.Male, SocialClass.Gentry, Occupation.Lord, 50, 7, winter);
        var peas = b.Track(P(5001), Sex.Male, SocialClass.Peasant, Occupation.Farmer, 50, 7, winter);
        Assert.True(gent.Worn.Count >= 7);
        Assert.True(ClothingMath.IEff(b.Tuning, b.WornOf(gent)) >= ClothingMath.IEff(b.Tuning, b.WornOf(peas)));
        var guard = b.Track(P(5002), Sex.Male, SocialClass.Peasant, Occupation.Guard, 50, 7, winter);
        Assert.Contains(guard.Worn, x => b.GarmentOf(x).Id == "gambeson");
        // worn clothes wear with the days (the Phase 2 lifetime of clothes): a game year (60 days) of wear takes 82 permille of a peasant's tunic (730 days of life)
        var t = Wearing(b, 9000, "tunic"); int c0 = b.Items.Get(t.Worn[0]).ConditionPermille;
        for (long m = 0; m < 60 * 1440; m += 1) b.StepMinute(t, m, new ExposureEnv(2200), Activity.Rest);
        int c1 = b.Items.Get(t.Worn[0]).ConditionPermille; output.WriteLine($"one year of wear: {c0} -> {c1}, wearday {t.WearDay}, wear {b.Items.Get(t.Worn[0]).Wear}");
        Assert.InRange(c0 - c1, 78, 86);
    }

    /// <summary>The winter night the lead asked for: thin clothes reach the warning stages; a cloak, hood, boots and a fire prevent it.</summary>
    [Fact]
    public void WinterNightOutdoors_ThinClothesReachWarnings_CloakAndFirePrevent()
    {
        var b = NewBook(); var env = ExposureEnv.Outdoors(-5, windMps: 3, precipTenthsMmH: 0);
        var thin = Wearing(b, 1, "shirt", "tunic", "hose"); var kit = Wearing(b, 2, WinterPeasant); var kitFire = Wearing(b, 3, WinterPeasant); var thinFire = Wearing(b, 4, "shirt", "tunic", "hose");
        var states = new List<BodyState>(); int firstChilled = -1;
        for (int m = 0; m < 12 * 60; m++)
        {
            b.StepMinute(thin, m, env, Activity.Walk); b.StepMinute(kit, m, env, Activity.Walk);
            b.StepMinute(kitFire, m, env with { Fire = true }, Activity.Work); b.StepMinute(thinFire, m, env with { Fire = true }, Activity.Work);
            if (firstChilled < 0 && thin.State >= BodyState.Chilled) firstChilled = m;
        }
        output.WriteLine($"thin: core {thin.CoreCenti} {thin.State} (chilled at {firstChilled} min), kit {kit.CoreCenti} {kit.State}, kit+fire {kitFire.CoreCenti} {kitFire.State}, thin+fire {thinFire.CoreCenti}");
        Assert.True(thin.State >= BodyState.Moderate, "thin clothes outdoors reach the warning stages");
        Assert.True(firstChilled is > 0 and < 240);
        Assert.Equal(BodyState.Normal, kitFire.State); Assert.True(kitFire.CoreCenti >= 3600, "cloak, hood, boots and a fire prevent it");
        Assert.True(kit.CoreCenti > thin.CoreCenti, "the winter kit alone is better than thin clothes, if not enough to stay out in the open all night");
        Assert.True(thinFire.CoreCenti > thin.CoreCenti + 400, "thin clothes at a fire (T +15 C) lose little");
    }

    [Fact] // C-14: severe cold kills only after collapse; the warning stages come first
    public void C14_ColdDeath_AfterTheWarningStagesAndACollapse_WithinThirteenHours()
    {
        var b = NewBook(); var t = Wearing(b, 1, "shirt", "tunic", "hose"); var env = ExposureEnv.Outdoors(-20);
        var seen = new List<BodyState>(); int collapsedAt = -1, deadAt = -1;
        for (int m = 0; m < 14 * 60 && t.Died == ExposureDeath.None; m++)
        {
            b.StepMinute(t, m, env, Activity.Rest);
            if (seen.Count == 0 || seen[^1] != t.State) seen.Add(t.State);
            if (t.Collapsed && collapsedAt < 0) collapsedAt = m;
            if (t.Died != ExposureDeath.None) deadAt = m;
        }
        output.WriteLine($"stages {string.Join(">", seen)}, collapsed at {collapsedAt / 60.0:0.0} h, died at {deadAt / 60.0:0.0} h");
        Assert.Equal(ExposureDeath.Cold, t.Died); Assert.InRange(deadAt, 1, 13 * 60);
        Assert.Equal([BodyState.Normal, BodyState.Chilled, BodyState.Moderate, BodyState.Severe, BodyState.Lethal], seen);
        Assert.True(collapsedAt > 0 && collapsedAt < deadAt, "unconscious before death");
        // a person warmed in time recovers and never dies
        var b2 = NewBook(); var u = Wearing(b2, 2, "shirt", "tunic", "hose");
        for (int m = 0; m < 8 * 60; m++) b2.StepMinute(u, m, env, Activity.Rest);
        Assert.True(u.Collapsed && u.Died == ExposureDeath.None);
        foreach (var sh in new[] { "gambeson", "cloak", "hood", "boots", "gloves" }) { var g = b2.Garments[sh]; b2.Wear(u, b2.Create(g, g.DefaultMaterial!, u.Person, fitCm3: u.SizeCm3)); }     // wrapped up and brought in to the hearth
        for (int m = 8 * 60; m < 24 * 60; m++) b2.StepMinute(u, m, new ExposureEnv(0, Fire: true, Roofed: true), Activity.Rest);
        Assert.Equal(BodyState.Normal, u.State); Assert.False(u.Collapsed);
    }

    [Fact] // heatstroke: a knight in harness in a heatwave
    public void Heatstroke_KnightInHarnessInAHeatwave_ThinClothesAreFine()
    {
        var b = NewBook(); var knight = Wearing(b, 1, "shirt", "gambeson", "mail_hauberk", "helm", "breastplate"); var farmer = Wearing(b, 2, "shirt", "tunic", "hose");
        var env = ExposureEnv.Outdoors(34, sunny: true);
        for (int m = 0; m < 6 * 60; m++) { b.StepMinute(knight, m, env, Activity.Work); b.StepMinute(farmer, m, env, Activity.Work); }
        output.WriteLine($"knight {knight.CoreCenti} {knight.State}, farmer {farmer.CoreCenti} {farmer.State}");
        Assert.Equal(BodyState.Heatstroke, knight.State); Assert.True(knight.Collapsed);
        Assert.True(farmer.State is BodyState.Warm or BodyState.Exhausted);
        Assert.True(farmer.CoreCenti < knight.CoreCenti);
    }

    [Fact] // C-15: the abstract hour matches the live minutes
    public void C15_AbstractMatchesLive_OverADayAtMinusTen()
    {
        var b = NewBook(); var t = Wearing(b, 1, WinterPeasant); var env = ExposureEnv.Outdoors(-10);
        for (int m = 0; m < 14 * 60; m++) b.StepMinute(t, m, env, Activity.Walk);
        int outfit = t.Last.IEffCloX100;
        long core = ClothingMath.CoreUnitsOf(3700);
        for (int h = 0; h < 14; h++) core = AbstractExposure.StepHour(b.Tuning, core, outfit, env, Activity.Walk, 1000, out _);
        output.WriteLine($"live {t.CoreCenti} abstract {ClothingMath.CentiOf(core)} (outfit {outfit})");
        Assert.InRange(Math.Abs(t.CoreCenti - ClothingMath.CentiOf(core)), 0, 30);
        Assert.Equal(40, AbstractExposure.OutfitI(50, false, false)); Assert.Equal(90, AbstractExposure.OutfitI(100, false, false)); Assert.Equal(220, AbstractExposure.OutfitI(250, true, true));
        Assert.False(AbstractExposure.IsExtreme(b.Tuning, ExposureEnv.Outdoors(12), 90)); Assert.True(AbstractExposure.IsExtreme(b.Tuning, ExposureEnv.Outdoors(-3), 90));
        Assert.False(AbstractExposure.IsExtreme(b.Tuning, new ExposureEnv(-1000, Roofed: true), 90));
        Assert.True(AbstractExposure.ColdStressPm(b.Tuning, 40, ExposureEnv.Outdoors(-10), Activity.Walk) > AbstractExposure.ColdStressPm(b.Tuning, 180, ExposureEnv.Outdoors(-10), Activity.Walk));
        // a poor household's person outdoors for a whole night at -20 dies in the abstract tier too
        long c = ClothingMath.CoreUnitsOf(3700); var d = ExposureDeath.None;
        for (int h = 0; h < 16 && d == ExposureDeath.None; h++) c = AbstractExposure.StepHour(b.Tuning, c, 40, ExposureEnv.Outdoors(-20), Activity.Rest, 1000, out d);
        Assert.Equal(ExposureDeath.Cold, d);
    }

    [Fact] // C-16 and conservation: determinism at 1x and 8x, save -> load -> save, items and mass
    public void C16_Determinism_AtOneAndEightTimes_SaveRoundTripByteIdentical_ItemsAndMassConserved()
    {
        string Run(int stepSec, out byte[] bytes, out long mass, out int items)
        {
            var b = NewBook(); long t0 = 3 * 15 * 86400L + 100 * 3600;
            for (int i = 1; i <= 40; i++) b.Track(P(i), i % 2 == 0 ? Sex.Female : Sex.Male, (SocialClass)(i % 3), (Occupation)(i % 20), 40 + i % 30, 99, t0);
            long mass0 = b.TotalMassG(); int items0 = b.Items.Count;
            for (long sec = t0; sec < t0 + 6 * 3600; sec += stepSec)
            {
                long minute = sec / 60;
                foreach (var t in b.People.ToArray())
                {
                    long mm = minute; int h = (int)(sec / 3600 % 24);
                    var env = new ExposureEnv(-300 - (int)(t.Person.Value % 5) * 100, 500, h % 5 == 0 ? 10 : 0, true, false, t.Person.Value % 4 == 0, t.Person.Value % 8 == 0);
                    b.StepTo(t, mm, env, (Activity)(t.Person.Value % 4));
                }
            }
            Assert.Equal(mass0, b.TotalMassG()); Assert.Equal(items0, b.Items.Count);      // stepping neither makes nor loses an item or a gram
            bytes = b.ToBytes(); mass = b.TotalMassG(); items = b.Items.Count;
            var again = ClothingBook.FromBytes(EconFixture.Content, bytes);
            Assert.Equal(bytes, again.ToBytes());                                           // save -> load -> save byte-identical
            Assert.Equal(b.StateHash(), again.StateHash());
            Assert.Equal(mass, again.TotalMassG());
            return b.StateHash();
        }
        string h1 = Run(1, out var b1, out var m1, out var n1), h8 = Run(8, out var b8, out var m8, out var n8);
        Assert.Equal(h1, h8); Assert.Equal(b1, b8); Assert.Equal((m1, n1), (m8, n8));
        output.WriteLine($"{n1} items, {m1} g, state hash {h1[..12]}");
    }

    [Fact] // the utility scorer: a person who is far too cold goes in; one who is warm does not
    public void Scorer_SeeksShelter_WhenColdOrWetAndColdOrHot_NotWhenWarm()
    {
        var brain = UtilityBrain.Default; var ctx = new ChoiceContext { Scheduled = ActionKind.Work, Current = ActionKind.Work, MinutesInCurrent = 999, WorkOpen = true, HasRoof = true, CanShelter = true, Seed = 1, GameSec = 100, Person = 7 };
        var warm = new NeedInputs { DutyPm = 500, ColdPm = 0 };
        Assert.Equal(ActionKind.Work, brain.Choose(warm, ctx).Kind);
        Assert.Equal(ActionKind.Shelter, brain.Choose(warm with { ColdPm = 800 }, ctx).Kind);
        Assert.Equal(ActionKind.Shelter, brain.Choose(warm with { HeatPm = 900 }, ctx).Kind);
        Assert.Equal(ActionKind.Shelter, brain.Choose(warm with { WetPm = 900, ColdPm = 400 }, ctx).Kind);
        Assert.Equal(ActionKind.Work, brain.Choose(warm with { ColdPm = 300 }, ctx).Kind);
        Assert.Equal(ActionKind.Work, brain.Choose(warm with { ColdPm = 800 }, ctx with { CanShelter = false }).Kind);
    }

    /// <summary>The live bridge with clothes attached: people are dressed on promotion, step once a game minute, and 1x and 8x leave identical bodies.</summary>
    [Fact]
    public void Bridge_DressesPromotedPeople_AndBodiesAreTheSameAtOneAndEightTimes()
    {
        string Run(int step, out int dressed, out int cold, out int shelter)
        {
            var rig = new PeopleRig(); var b = new PeopleBridge(); var book = NewBook();
                        long day = (rig.Pop.NowDay + 1) / 60 * 60 + 3 * 15 + 3; long start = day * 86400 + 17 * 3600;
            b.AttachClothing(book, (sec, x, y) => ExposureEnv.Outdoors(-6, 4, sec / 3600 % 3 == 0 ? 15 : 0, true));
            b.Enqueue(PeopleBridge.Scan([rig.Records], b.MakeScanInput(rig.Centre, start)));
            b.Promote(start, null, max: 60);
            for (long sec = start; sec <= start + 3 * 3600; sec += step) b.Step(sec, null, rig.Centre);
            dressed = book.People.Count(p => p.Worn.Count >= 3); cold = book.People.Count(p => p.State >= BodyState.Chilled);
            shelter = b.People.Count(p => p.Intent == ActionKind.Shelter);
            Assert.True(b.Count >= 30);
            Assert.All(b.People, p => Assert.NotNull(p.Body));
            // people who walk out of the bubble are folded back after 5 s at 1x and 8 s at 8x, so only those live at the end are compared
            var live = b.People.Select(p => p.Id).ToHashSet();
            return string.Join(";", book.People.Where(p => live.Contains(p.Person)).Select(p => $"{p.Person.Value}:{p.CoreU}:{p.ExposureMin}:{p.Worn.Count}:{string.Concat(p.Worn.Select(w => book.ExtraOf(w).WetU))}")) + ":" + string.Join(",", b.People.Select(p => (int)p.Intent));
        }
        string a = Run(1, out var d1, out var c1, out var s1), c = Run(8, out var d8, out var c8, out var s8);
        output.WriteLine($"1x: dressed {d1}, chilled {c1}, sheltering {s1}; 8x: dressed {d8}, chilled {c8}, sheltering {s8}");
        Assert.Equal(a, c); Assert.True(d1 >= 30);
    }
}
