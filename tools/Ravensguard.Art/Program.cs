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
          bake     [--out art/out] [--palette path]   write PNG atlases + index.json
          validate [--palette path]                    check palette use, sizes, outlines (exit 1 on failure)
          preview  [--out art/out/preview.png]         2x contact sheet of all sprites
        """);
    return args.Length == 0 ? 2 : 0;
}

var root = FindRoot() ?? Directory.GetCurrentDirectory();
var palPath = Opt(args, "--palette") ?? Path.Combine(root, "content", "base", "art", "palette.jsonc");
if (!File.Exists(palPath)) { Console.Error.WriteLine($"palette not found: {palPath}"); return 2; }
var pal = Palette.Load(palPath);
var defs = Catalog.Build(pal);
var cache = defs.ToDictionary(d => d.Id, d => d.Build());

string Rel(string? p, string def) => Path.GetFullPath(p ?? def, Directory.GetCurrentDirectory());

switch (args[0])
{
    case "validate":
    {
        var errs = Validator.Validate(defs, pal, cache);
        foreach (var e in errs) Console.Error.WriteLine("FAIL " + e);
        Console.WriteLine(errs.Count == 0 ? $"OK: {defs.Count} sprites valid ({pal.Colors.Count} palette colours)" : $"{errs.Count} problem(s) in {defs.Count} sprites");
        return errs.Count == 0 ? 0 : 1;
    }
    case "bake":
    {
        var errs = Validator.Validate(defs, pal, cache);
        if (errs.Count > 0) { foreach (var e in errs) Console.Error.WriteLine("FAIL " + e); return 1; }
        var outDir = Rel(Opt(args, "--out"), Path.Combine(root, "art", "out"));
        Atlas.Bake(defs, pal, outDir, cache);
        Console.WriteLine($"baked {defs.Count} sprites to {outDir}");
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
