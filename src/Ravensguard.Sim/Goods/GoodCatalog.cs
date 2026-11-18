using System.Security.Cryptography;
using System.Text;
using Ravensguard.Content;

namespace Ravensguard.Sim.Goods;

/// <summary>Kind of store stock sits in; it picks the column of the spoilage table (items spec 3.6).</summary>
public enum StoreKind : byte { Open, House, Barn, Sealed, Granary, Cellar, Chest }

/// <summary>
/// Runtime view of the good definitions: dense <c>ushort</c> ids by ordinal string order (overview 2.2), integer per-good constants and the spoilage table.
/// Built once from the content database; shared by every store. Quantities are grams for kg and litre goods and a count for piece goods.
/// </summary>
public sealed class GoodCatalog
{
    public const int StoreKindCount = 7;
    private readonly GoodDef[] _defs;
    private readonly Dictionary<string, ushort> _byId = new(StringComparer.Ordinal);
    private readonly int[] _bulkPermille;       // litres per kg x 1000
    private readonly int[] _foodPpm;            // measures per kg x 1,000,000
    private readonly int[] _spoilRow;
    private readonly int[][] _spoilPpm;         // [row][StoreKind]
    private readonly bool[] _tempSensitive;     // [row]

    public int Count => _defs.Length;
    public GoodDef Def(ushort good) => _defs[good];
    public string StringId(ushort good) => _defs[good].Id;
    public ushort IdOf(string id) => _byId.TryGetValue(id, out var g) ? g : throw new KeyNotFoundException($"unknown good '{id}'");
    public bool TryIdOf(string id, out ushort good) => _byId.TryGetValue(id, out good);
    public bool IsPiece(ushort good) => _defs[good].Unit == GoodUnit.Piece;

    /// <summary>Base price in mites per kg (per piece for piece goods) as a rounded integer of milli-mites.</summary>
    public long BasePriceMilliMites(ushort good) => (long)Math.Round(_defs[good].BasePriceMitesPerKg * 1000.0);

    /// <summary>Measures of food per kg, x 1,000,000 (grain 909,091: 1 measure = 1,100 g).</summary>
    public int FoodMeasuresPpm(ushort good) => _foodPpm[good];

    public GoodCatalog(ContentDatabase db)
    {
        _defs = db.All<GoodDef>().OrderBy(g => g.Id, StringComparer.Ordinal).ToArray();
        if (_defs.Length > ushort.MaxValue) throw new InvalidOperationException("too many goods");
        _bulkPermille = new int[_defs.Length]; _foodPpm = new int[_defs.Length]; _spoilRow = new int[_defs.Length];
        var rows = db.All<SpoilClassDef>().OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var rowIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        _spoilPpm = new int[rows.Length][]; _tempSensitive = new bool[rows.Length];
        for (int r = 0; r < rows.Length; r++)
        {
            var s = rows[r]; rowIndex[s.Id] = r; _tempSensitive[r] = s.TempSensitive;
            var p = new int[StoreKindCount];
            p[(int)StoreKind.House] = s.HousePpm;
            p[(int)StoreKind.Chest] = s.HousePpm;
            p[(int)StoreKind.Open] = s.OpenPpm ?? s.HousePpm;
            p[(int)StoreKind.Barn] = s.BarnPpm ?? s.HousePpm;
            p[(int)StoreKind.Sealed] = s.SealedPpm ?? s.HousePpm;
            p[(int)StoreKind.Granary] = s.GranaryPpm ?? s.HousePpm;
            p[(int)StoreKind.Cellar] = s.CellarPpm ?? s.HousePpm;
            _spoilPpm[r] = p;
        }
        for (int i = 0; i < _defs.Length; i++)
        {
            var d = _defs[i]; _byId[d.Id] = (ushort)i;
            _bulkPermille[i] = (int)Math.Round(d.BulkLitresPerKg * 1000.0);
            _foodPpm[i] = (int)Math.Round(d.FoodMeasuresPerKg * 1_000_000.0);
            _spoilRow[i] = rowIndex.TryGetValue(d.SpoilClass, out var sr) ? sr : throw new InvalidOperationException($"good '{d.Id}' names unknown spoil class '{d.SpoilClass}'");
            if (d.Unit == GoodUnit.Piece && d.PieceMassG <= 0) throw new InvalidOperationException($"piece good '{d.Id}' needs piece_mass_g");
            if (d.Unit == GoodUnit.Litre && d.LitreMassG <= 0) throw new InvalidOperationException($"litre good '{d.Id}' needs litre_mass_g");
        }
    }

    /// <summary>Mass in grams of <paramref name="qty"/> units (pieces times piece mass; grams for kg and litre goods).</summary>
    public long MassG(ushort good, long qty) => IsPiece(good) ? checked(qty * _defs[good].PieceMassG) : qty;

    /// <summary>Stowage volume in millilitres (rounded up): grams x litres-per-kg.</summary>
    public long VolumeMl(ushort good, long qty)
    {
        long num = checked(MassG(good, qty) * _bulkPermille[good]);
        return (num + 999) / 1000;
    }

    /// <summary>Largest quantity of the good whose volume does not exceed <paramref name="ml"/> (floor, consistent with <see cref="VolumeMl"/>).</summary>
    public long QtyForVolumeMl(ushort good, long ml)
    {
        if (ml <= 0) return 0;
        int bp = _bulkPermille[good];
        long maxMass = bp <= 0 ? long.MaxValue / 4 : ml * 1000 / bp;           // mass with ceil(mass*bp/1000) <= ml
        while (maxMass > 0 && (maxMass * bp + 999) / 1000 > ml) maxMass--;
        return IsPiece(good) ? maxMass / _defs[good].PieceMassG : maxMass;
    }

    /// <summary>Spoilage in ppm of the quantity per day for this good in this kind of store.</summary>
    public int SpoilPpm(ushort good, StoreKind kind) => _spoilPpm[_spoilRow[good]][(int)kind];
    public bool TempSensitive(ushort good) => _tempSensitive[_spoilRow[good]];

    /// <summary>Temperature multiplier in permille for perishables (items spec 3.5): below 0 C 500, 0-9 C 800, 10-20 C 1000, above 20 C 1500.</summary>
    public static int TempMultPermille(int tempC) => tempC < 0 ? 500 : tempC < 10 ? 800 : tempC <= 20 ? 1000 : 1500;

    public bool HasTag(ushort good, string tag) => _defs[good].Tags.Contains(tag);

    /// <summary>Hash of the ids, units and spoilage numbers; stored in saves so stock rows are never read against a different good table.</summary>
    public string Hash()
    {
        var sb = new StringBuilder();
        foreach (var d in _defs) sb.Append(d.Id).Append('|').Append((int)d.Unit).Append('|').Append(d.PieceMassG).Append('|').Append(d.SpoilClass).Append('\n');
        foreach (var r in _spoilPpm) sb.Append(string.Join(',', r)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
