using System;
using System.Globalization;

namespace Ravensguard.Game.World;

/// <summary>Launch options for the street view. The command line (after `--`) can start it directly and take a screenshot.</summary>
public sealed class StreetOptions
{
    public int? Hour;            // --shot-street <hour>: also means "capture and quit"
    public string? ShotPath;     // --shot-out <png>
    public int Zoom = 1;         // --shot-zoom <1|2>
    public bool? Torch;          // --shot-torch <0|1>; default: on when it is night
    public int? ViewZ;           // --shot-level <z>
    public (int X, int Y, int Z)? Start; // --shot-start x,y,z
    public (int X, int Y)? Pan;  // --shot-pan x,y in metres
    public (int X, int Y)? Click; // --shot-click px,py: click that screen pixel as if with the mouse
    public int Frames = 12;      // --shot-frames n: frame to capture on
    public double BenchSeconds;  // --street-bench <s>: scripted walk, prints tick and frame timings, quits
    public ulong? RegionSeed;    // --street-region <seed>: walk the generated region of that seed instead of the test hamlet (dev)
    public string? Poi;          // --street-poi town|village|river: where in the region to start (default: the largest town's square)

    public string? Band;         // --shot-band close|street|field|command
    public string? Season;       // --shot-season spring|summer|autumn|winter (dev: start date)
    public int? Day;             // --shot-day 1..15 (day of that season)
    public string? Weather;      // --shot-weather clear|overcast|rain|heavy|storm|snow|blizzard|fog|frost[:tempC] (dev: forces the weather)
    public string? Panel;        // --shot-panel codex|character: open that panel and capture it instead of the street
    public string? PanelCategory, PanelEntry; // --shot-codex-cat legend|place|person|landmark|rule, --shot-codex-entry <id>
    public bool MapShot;         // --shot-map: open the world map and capture it instead of the street
    public int MapScale = 1;     // --shot-map-scale 1..6
    public int Settle = 900;     // frames to wait for far-band chunks to bake before a screenshot

    public bool IsShot => Hour.HasValue && ShotPath != null;

    public static StreetOptions? Parse(string[] args)
    {
        int shot = Array.IndexOf(args, "--shot-street"), bench = Array.IndexOf(args, "--street-bench"), region = Array.IndexOf(args, "--street-region");
        if (shot < 0 && bench < 0 && region < 0) return null;
        var o = new StreetOptions();
        string? Val(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (shot >= 0 && Val("--shot-street") is { } h) o.Hour = int.Parse(h, CultureInfo.InvariantCulture);
        o.ShotPath = Val("--shot-out");
        if (Val("--street-region") is { } rs) o.RegionSeed = ulong.Parse(rs, CultureInfo.InvariantCulture);
        o.Poi = Val("--street-poi");
        if (Val("--shot-zoom") is { } z) o.Zoom = Math.Clamp(int.Parse(z, CultureInfo.InvariantCulture), 1, 2);
        o.Band = Val("--shot-band");
        o.Season = Val("--shot-season");
        if (Val("--shot-day") is { } dy) o.Day = int.Parse(dy, CultureInfo.InvariantCulture);
        o.Weather = Val("--shot-weather");
        o.MapShot = Array.IndexOf(args, "--shot-map") >= 0;
        o.Panel = Val("--shot-panel"); o.PanelCategory = Val("--shot-codex-cat"); o.PanelEntry = Val("--shot-codex-entry");
        if (Val("--shot-map-scale") is { } ms) o.MapScale = Math.Clamp(int.Parse(ms, CultureInfo.InvariantCulture), 1, 6);
        if (Val("--shot-torch") is { } t) o.Torch = t == "1";
        if (Val("--shot-level") is { } l) o.ViewZ = int.Parse(l, CultureInfo.InvariantCulture);
        if (Val("--shot-start") is { } s) { var p = s.Split(','); o.Start = (int.Parse(p[0]), int.Parse(p[1]), p.Length > 2 ? int.Parse(p[2]) : 0); }
        if (Val("--shot-pan") is { } pn) { var p = pn.Split(','); o.Pan = (int.Parse(p[0]), int.Parse(p[1])); }
        if (Val("--shot-click") is { } ck) { var p = ck.Split(','); o.Click = (int.Parse(p[0]), int.Parse(p[1])); }
        if (Val("--shot-frames") is { } fr) o.Frames = int.Parse(fr, CultureInfo.InvariantCulture);
        if (Val("--street-bench") is { } b) o.BenchSeconds = double.Parse(b, CultureInfo.InvariantCulture);
        return o;
    }
}
