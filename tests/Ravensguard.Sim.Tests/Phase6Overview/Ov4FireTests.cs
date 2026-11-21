using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Building;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.Tests.Fire;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// OV-4 (overview 4, "a thatched street burns realistically"): in the live bubble (the fire world on the room world, the same pieces the street view builds) a hearth lights the thatch of the first of ten houses
/// of a village street, the wind blowing 5 m/s along it; the fire runs downwind house by house, twelve villagers form a bucket chain from a well 30 m off and the ones with hooks pull down thatch, the stone church at the
/// street's end does not burn, the man asleep in the burning house dies of the smoke, and the Chronicle's fire entries name the hearth, count the losses and the dead and chain back to the first flame, in en.csv's words.
/// </summary>
public class Ov4FireTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    private static readonly FireWeather Wind5FromWest = new(5000, 270, 0, false, false);

    [Fact]
    public void OV4_AHearthFireInAThatchedStreetWith5MetresASecondOfWind_RunsDownwind_AChainFromTheWellAndHooksHoldIt_TheStoneChurchStands_TheChronicleNamesTheHearthAndCountsTheLossesAndTheDead()
    {
        // the control: the same street and wind with nobody to fight it burns end to end, house after house along the wind, and the stone church stands
        var control = FireRig.Make(fx, 1234); control.Wx = Wind5FromWest;
        var cstreet = control.Street(10, stoneAtEnd: true); control.Light(0, 0, FireCause.Hearth);
        var cburnt = new Dictionary<int, int>(); int csim = 0;
        while (control.Fw.Book.Cells.Count > 0 && csim < 3000)
        {
            control.Run(1); csim++;
            for (int h = 0; h < 10; h++) if (!cburnt.ContainsKey(h) && control.Burnt(cstreet[h].X, cstreet[h].Y)) cburnt[h] = csim;
        }
        output.WriteLine($"unfought: {csim} sim-s end to end, houses (index: sim-s) {string.Join(", ", cburnt.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"))}");
        Assert.Equal(10, cburnt.Count); Assert.InRange(csim, 600, 1800);
        Assert.True(cburnt.OrderBy(k => k.Key).Select(k => k.Value).SequenceEqual(cburnt.Values.Order()), "the unfought street did not catch in order along the wind");
        Assert.False(control.Burnt(cstreet[10].X, cstreet[10].Y));

        var v2 = new V2Fixture();
        var ledger = new WoundLedger(1234, 0);
        var r = FireRig.Make(fx, 1234); r.Wx = Wind5FromWest;
        r.Fw.Wounds = ledger; r.Fw.PlanOf = _ => v2.Human;
        var street = r.Street(10, stoneAtEnd: true);                 // ten thatched houses, 14 m apart, and a stone building (the church) at the east end
        var church = street[^1];
        Assert.False(r.Fw.Fuels.ClassOf("roof_slate").Burns);       // the church is slate on stone: nothing there to catch

        // a sleeper in the first house, by the bed; the hearth lights the thatch above it
        const ulong sleeper = 77;
        r.People.Add(new FirePerson(sleeper, 5, 1, 0, Asleep: true));
        // a well 30 m west of the fire, twelve villagers with buckets in a ring by the first houses, one in six with a hook
        r.Fw.WaterSources = () => [(-30, 3)];
        var villagers = Enumerable.Range(0, 12).Select(i => new FireFighter(100UL + (ulong)i, 4 + i, 8, 0, i % 6 == 0)).ToList();
        Assert.True(r.Light(0, 0, FireCause.Hearth));
        var started = Assert.Single(r.Chron.ByType("fire_started"));

        var burntAt = new Dictionary<int, int>(); bool noted = false;
        int sim = 0;
        while (r.Fw.Book.Cells.Count > 0 && sim < 3000)
        {
            r.Run(1); sim++;
            if (sim == 45) foreach (var f in villagers) r.Fighters.Add(f);                 // the village wakes to the smoke and the cry: forty-five sim-seconds in, the chain forms
            lock (ledger.Sync) ledger.AdvanceTo(r.Sec);
            for (int h = 0; h < street.Count; h++) if (!burntAt.ContainsKey(h) && r.Burnt(street[h].X, street[h].Y)) burntAt[h] = sim;
            if (!noted && ledger.Find(sleeper) is { Dead: true })
            {
                Assert.Equal(started.Id, r.Fw.FireOfDeath(sleeper));                 // the wound step names the fire as his cause at the death, as it does for a man who dies in it
                r.Fw.NoteDeath(sleeper); noted = true;
            }
        }
        output.WriteLine($"the fire lasted {sim} sim-s; houses burnt (index: sim-s) {string.Join(", ", burntAt.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"))}; hooks pulled down {r.Fw.Book.Scars.Values.Count(s => s == ScarKind.Rubble)} cells");

        // it ran downwind: the houses that burnt are the first ones, one after the other, and the first house burnt first
        Assert.Contains(0, burntAt.Keys);
        var order = burntAt.OrderBy(k => k.Key).Select(k => k.Value).ToList();
        Assert.True(order.SequenceEqual(order.Order()), "the houses did not catch in order along the wind: " + string.Join(",", order));
        int last = burntAt.Keys.Max();
        Assert.Equal(Enumerable.Range(0, last + 1), burntAt.Keys.Order());               // no house skipped upwind
        // the bucket chain and the hooks held it to three houses or fewer (FB-5's bar), the stone church not touched
        Assert.True(last <= 2, $"the villagers did not stop it: {last + 1} of 10 houses burnt");
        Assert.False(r.Burnt(church.X, church.Y));
        // the hooks pulled thatch down, and the fire's own record names those who fought it
        Assert.Contains(r.Fw.Book.Scars.Values, s => s == ScarKind.Rubble);
        // the man asleep in the burning house is burnt where he lies and unconscious (burn wounds to the part of him in the flames); the fire counts him among its dead only if he dies in it
        var body = ledger.Find(sleeper)!;
        Assert.Contains(body.Records, w => w.Kind == Ravensguard.Sim.Combat.WoundKind.Burn); Assert.False(body.Conscious);
        int deathsNoted = body.Dead ? 1 : 0;

        // the Chronicle: the first flame names the hearth; the end counts what was lost and who died and chains back to the start; the fighters have their own entries
        Assert.Equal("hearth", started.Data["cause"]);
        Assert.Equal("ch_note_fire_hearth", started.Causes[0].NoteKey);
        var ended = Assert.Single(r.Chron.ByType("fire_ended"));
        Assert.Equal(started.Id, ended.Causes[0].EventId);
        Assert.Equal(deathsNoted.ToString(), ended.Data["deaths"]); Assert.Equal(deathsNoted, ended.Subjects.Count);
        Assert.True(int.Parse(ended.Data["buildings"]) >= 1);
        Assert.NotEmpty(r.Chron.ByType("fire_fought"));
        Assert.All(r.Chron.ByType("fire_fought"), e => Assert.Equal(started.Id, e.Causes[0].EventId));
        var types = ChronicleTypes.From(fx.Load.Database);
        string Said(ChronicleEntry e) => ChronicleTypes.Format(e, P6Game.Tr, (k, v) => "Ashby");
        output.WriteLine(Said(started)); output.WriteLine(Said(ended));
        Assert.StartsWith("The fire burnt out: ", Said(ended));
        Assert.EndsWith($"{deathsNoted} dead.", Said(ended));
        Assert.Contains("a hearth set the thatch alight", ChronicleTypes.FormatNote(started.Causes[0], P6Game.Tr, (_, _) => "?"));
        Assert.True(types.Has("fire_started") && types.Has("fire_ended"));
    }
}
