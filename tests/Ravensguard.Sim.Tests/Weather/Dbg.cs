using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Xunit.Abstractions;
namespace Ravensguard.Sim.Tests.Weather;
public class Dbg(ITestOutputHelper o)
{
    [Fact] public void Sin()
    {
        foreach (var d in new[]{-66,0,30,90,150,180,200,270,300,359}) o.WriteLine($"{d}: {WeatherMath.SinDeg(Fixed.FromInt(d))}");
        for (int p=0;p<60;p+=5) o.WriteLine($"pos {p}: phase {Daylight.Phase(Fixed.FromInt(p)-Fixed.FromInt(3))} sin {WeatherMath.SinDeg(Daylight.Phase(Fixed.FromInt(p)-Fixed.FromInt(3)))}");
    }
}
