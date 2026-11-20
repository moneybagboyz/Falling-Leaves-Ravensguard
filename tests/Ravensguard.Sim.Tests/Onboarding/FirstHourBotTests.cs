using Ravensguard.Sim.Onboarding;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Onboarding;

public class FirstHourBotTests(ITestOutputHelper output)
{
    [Fact]
    public void O4_The_bot_gets_through_the_first_hour_for_every_origin()
    {
        var reports = new List<BotReport>();
        foreach (var o in OnbKit.Origins)
        {
            var rep = FirstHourBot.Play(OnbKit.NewWorld(), OnbKit.Db, o, 1234);
            output.WriteLine(rep.Render());
            reports.Add(rep);
        }
        Assert.Equal(8, reports.Count);                                                       // P5-H: the tanner's widow is the eighth origin
        Assert.All(reports, r => Assert.True(r.Passed, r.Render()));
    }
}
