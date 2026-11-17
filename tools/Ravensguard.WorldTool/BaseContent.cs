using Ravensguard.Sim.WorldGen;

namespace Ravensguard.WorldTool;

/// <summary>Finds content/base or fails with a clear message (the built-in fallbacks in LoadDefaults would silently use the wrong settings).</summary>
public static class BaseContent
{
    public static string Require()
    {
        var dir = ContinentSettings.FindBaseContent();
        if (dir == null)
            throw new InvalidOperationException("Cannot find content/base (a folder with mod.json) above the working directory or the tool's folder. "
                + "Run the tool from inside the repository, e.g. `dotnet run --project tools/Ravensguard.WorldTool -c Release -- ...`.");
        return dir;
    }
}
