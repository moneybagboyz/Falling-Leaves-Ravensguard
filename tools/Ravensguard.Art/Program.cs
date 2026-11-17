using Ravensguard.Art;

static string? FindRoot()
{
    var d = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (d != null && !File.Exists(Path.Combine(d.FullName, "Ravensguard.slnx"))) d = d.Parent;
    return d?.FullName;
}

static string? Opt(string[] a, string name)
{
    for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
    return null;
}

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Ravensguard.Art - sprite baker
          bake     [--out art/out] [--out-game dir] [--palette path]   write PNG atlases + index.json (--out-game also copies them to the game's assets/art)
          validate [--palette path]                    check palette use, sizes, outlines (exit 1 on failure)
          preview  [--out art/out/preview.png]         2x contact sheet of all sprites
          rig      [--out art/out] [--out-game dir]    bake the posed paper-doll rig: rig_32x64.png + rig_index.json
          rig-preview [--out path] [--scale 3] [--outfits soldier,peasant] [--cols idle:0,walk:1,..]   contact sheet of rig outfits, 4 directions
        """);
    return args.Length == 0 ? 2 : 0;
}

var root = FindRoot() ?? Directory.GetCurrentDirectory();
var palPath = Opt(args, "--palette") ?? Path.Combine(root, "art", "palette.jsonc");
if (!File.Exists(palPath)) { Console.Error.WriteLine($"palette not found: {palPath}"); return 2; }
var pal = Palette.Load(palPath);
if (args[0] is "rig" or "rig-preview")
{
    var rb = new Ravensguard.Art.Rig.RigBake(pal);
    var rerrs = rb.Validate();
    foreach (var e in rerrs) Console.Error.WriteLine("FAIL " + e);
    Console.WriteLine($"rig: {rb.Warnings.Count} frame(s) touch the 32x64 frame edge (combat/fall reach), see README");
    if (rerrs.Count > 0 && args[0] == "rig") return 1;
    if (args[0] == "rig")
    {
        var od = Path.GetFullPath(Opt(args, "--out") ?? Path.Combine(root, "art", "out"), Directory.GetCurrentDirectory());
        rb.Write(od);
        Console.WriteLine($"baked rig ({rb.Sprites.Count} layer frames) to {od}");
        if (Opt(args, "--out-game") is { } og)
        {
            var gd = Path.GetFullPath(og, Directory.GetCurrentDirectory());
            Directory.CreateDirectory(gd);
            foreach (var f in new[] { "rig_32x64.png", "rig_index.json" }) File.Copy(Path.Combine(od, f), Path.Combine(gd, f), true);
            Console.WriteLine($"copied rig atlas to {gd}");
        }
        return 0;
    }
    var scale = int.Parse(Opt(args, "--scale") ?? "3");
    var ids = (Opt(args, "--outfits") ?? "soldier,peasant").Split(',');
    var rec = new Dictionary<string, (string, string)[]> { ["peasant"] = new[] { ("green", "red") }, ["traveller"] = new[] { ("green", "brown") } };
    var op = Path.GetFullPath(Opt(args, "--out") ?? Path.Combine(root, "art", "out", "rig.png"), Directory.GetCurrentDirectory());
    Directory.CreateDirectory(Path.GetDirectoryName(op)!);
    Png.Write(op, rb.Sheet(ids, scale, rec, Opt(args, "--cols")));
    Console.WriteLine($"wrote {op}");
    return 0;
}
var defs = Catalog.Build(pal);
// Duplicate ids must surface as a validator error, not an ArgumentException from ToDictionary: keep the first build per id.
var cache = new Dictionary<string, PixelBuffer>();
foreach (var d in defs) if (!cache.ContainsKey(d.Id)) cache[d.Id] = d.Build();

string Rel(string? p, string def) => Path.GetFullPath(p ?? def, Directory.GetCurrentDirectory());

switch (args[0])
{
    case "validate":
    {
        var errs = Validator.Validate(defs, pal, cache);
        foreach (var e in errs) Console.Error.WriteLine("FAIL " + e);
        var rerr = new Ravensguard.Art.Rig.RigBake(pal).Validate();
        foreach (var e in rerr) Console.Error.WriteLine("FAIL " + e);
        errs.AddRange(rerr);
        Console.WriteLine(errs.Count == 0 ? $"OK: {defs.Count} sprites + rig valid ({pal.Colors.Count} palette colours)" : $"{errs.Count} problem(s) in {defs.Count} sprites and rig");
        return errs.Count == 0 ? 0 : 1;
    }
    case "bake":
    {
        var errs = Validator.Validate(defs, pal, cache);
        if (errs.Count > 0) { foreach (var e in errs) Console.Error.WriteLine("FAIL " + e); return 1; }
        var outDir = Rel(Opt(args, "--out"), Path.Combine(root, "art", "out"));
        Atlas.Bake(defs, pal, outDir, cache);
        Console.WriteLine($"baked {defs.Count} sprites to {outDir}");
        if (Opt(args, "--out-game") is { } og)
        {
            var gd = Rel(og, ".");
            Directory.CreateDirectory(gd);
            foreach (var atlas in defs.Select(d => d.Atlas).Distinct()) File.Copy(Path.Combine(outDir, atlas + ".png"), Path.Combine(gd, atlas + ".png"), true);
            File.Copy(Path.Combine(outDir, "index.json"), Path.Combine(gd, "index.json"), true);
            Console.WriteLine($"copied sprite atlases + index.json to {gd}");
        }
        return 0;
    }
    case "preview":
    {
        var outPath = Rel(Opt(args, "--out"), Path.Combine(root, "art", "out", "preview.png"));
        Png.Write(outPath, Preview.Compose(defs, cache));
        Console.WriteLine($"wrote {outPath}");
        return 0;
    }
    default:
        Console.Error.WriteLine($"unknown command '{args[0]}'");
        return 2;
}
