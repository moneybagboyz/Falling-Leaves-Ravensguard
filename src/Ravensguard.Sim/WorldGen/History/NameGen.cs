using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>Deterministic syllable names per culture. Culture name stubs seed the house and place names.</summary>
public sealed class NameGen
{
    private static readonly string[] Onsets = ["Al", "Ber", "Cor", "Dag", "El", "Fen", "Gar", "Hal", "Ing", "Jor", "Kel", "Lor", "Mar", "Nor", "Os", "Per", "Quen", "Ran", "Sig", "Tor", "Ul", "Var", "Wul", "Yor", "Ed", "Ro", "Thor", "Bran"];
    private static readonly string[] Mids = ["", "", "a", "e", "i", "o", "en", "ar", "id", "un"];
    private static readonly string[] MaleEnds = ["ric", "wald", "mund", "bert", "an", "or", "ek", "dan", "ulf", "red", "gar", "win"];
    private static readonly string[] FemaleEnds = ["a", "wyn", "ith", "elle", "ra", "ina", "is", "eth", "una", "hild", "ia", "lind"];
    private static readonly string[] HouseEnds = ["wick", "hurst", "ward", "ley", "mont", "ric", "stan", "wyn", "croft", "den", "holt", "thorne"];
    private static readonly string[] PlaceEnds = ["ford", "ton", "wick", "mere", "holt", "burg", "stead", "dale", "by", "haven", "ham", "field", "brook", "cliff", "moor", "ness"];
    private static readonly string[] Adj = ["Red", "Grey", "Black", "White", "Old", "High", "Low", "Broken", "Long", "Green", "Cold", "Bitter", "Silent", "Weeping", "Bloody", "Hollow", "Golden", "Crow", "Wolf", "Hart"];

    private readonly ulong _seed;
    private readonly string[][] _onset, _maleEnd, _femaleEnd, _houseEnd, _placeEnd;
    private readonly string[] _stub;

    public NameGen(ulong seed, IReadOnlyList<Culture> cultures)
    {
        _seed = seed;
        int n = Math.Max(1, cultures.Count);
        _onset = new string[n][]; _maleEnd = new string[n][]; _femaleEnd = new string[n][]; _houseEnd = new string[n][]; _placeEnd = new string[n][];
        _stub = new string[n];
        for (int k = 0; k < n; k++)
        {
            string nm = k < cultures.Count ? cultures[k].Name : "Mark";
            _stub[k] = nm.Length >= 3 ? nm[..3] : nm;
            _onset[k] = Pick(Onsets, 7, k, 1); _maleEnd[k] = Pick(MaleEnds, 4, k, 2); _femaleEnd[k] = Pick(FemaleEnds, 4, k, 3);
            _houseEnd[k] = Pick(HouseEnds, 4, k, 4); _placeEnd[k] = Pick(PlaceEnds, 6, k, 5);
        }
    }

    private string[] Pick(string[] pool, int count, int culture, uint stream)
    {
        var a = (string[])pool.Clone();
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = DetRng.Range(_seed, 0, (ulong)culture, 900 + stream, (uint)i, 0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
        return a[..Math.Min(count, a.Length)];
    }

    private static T Of<T>(T[] a, ref RngStream r) => a[r.Range(0, a.Length)];
    private int C(int culture) => Math.Clamp(culture, 0, _onset.Length - 1);

    public string Person(int culture, bool female, ref RngStream r)
    {
        int c = C(culture);
        string s = Of(_onset[c], ref r) + Of(Mids, ref r) + Of(female ? _femaleEnd[c] : _maleEnd[c], ref r);
        return s;
    }

    public string House(int culture, ref RngStream r)
    {
        int c = C(culture);
        string stub = r.Range(0, 3) == 0 ? _stub[c] : Of(_onset[c], ref r);
        return stub + Of(_houseEnd[c], ref r);
    }

    public string Place(int culture, ref RngStream r)
    {
        int c = C(culture);
        string s = (r.Range(0, 4) == 0 ? _stub[c] : Of(_onset[c], ref r)) + Of(Mids, ref r) + Of(_placeEnd[c], ref r);
        return s;
    }

    public string Feature(int adjSalt, ref RngStream r) => Of(Adj, ref r);
}
