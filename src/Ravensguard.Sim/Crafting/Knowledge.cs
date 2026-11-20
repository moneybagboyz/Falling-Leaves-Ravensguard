using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// What each tracked person knows (spec 3.2, 4.6): the recipes learnt beyond the ones everyone knows or that their skill unlocks, the proficiencies in thousandths of a permille (so a quarter hour
/// of work still counts), and the hours of watching a crafter. Kept as sorted rows so a save is exact.
/// </summary>
public sealed class Knowledge
{
    public const int PmMilli = 1000;     // proficiency unit: Pm x 1000
    private readonly SortedDictionary<ulong, SortedSet<ushort>> _recipes = [];
    private readonly SortedDictionary<(ulong, ushort), long> _prof = [];
    private readonly SortedDictionary<(ulong, ushort), long> _watch = [];
    private readonly SortedDictionary<(ulong, ushort), int> _tries = [];

    public bool Learnt(EntityId p, ushort recipe) => _recipes.TryGetValue(p.Value, out var s) && s.Contains(recipe);
    public void Learn(EntityId p, ushort recipe) { if (!_recipes.TryGetValue(p.Value, out var s)) _recipes[p.Value] = s = []; s.Add(recipe); }
    public IReadOnlyCollection<ushort> LearntBy(EntityId p) => _recipes.TryGetValue(p.Value, out var s) ? s : [];

    /// <summary>True when the person can make it: known by all, learnt, or their skill has reached the unlock level.</summary>
    public bool Knows(CraftRecipe r, EntityId p, SkillBook skills)
        => r.KnownByAll || Learnt(p, r.Index) || (r.AutoLevel >= 0 && skills.Level(p, r.AutoSkill) >= r.AutoLevel);

    /// <summary>Proficiency in permille 0..1000.</summary>
    public int Pm(EntityId p, ushort prof) => (int)(_prof.GetValueOrDefault((p.Value, prof)) / PmMilli);
    public long PmRaw(EntityId p, ushort prof) => _prof.GetValueOrDefault((p.Value, prof));
    public void SetPm(EntityId p, ushort prof, int pm) => _prof[(p.Value, prof)] = Math.Clamp(pm, 0, 1000) * (long)PmMilli;

    /// <summary>Adds learning from <paramref name="seconds"/> of attended work: <c>learn_pm_per_hour</c> x hours x <paramref name="multPm"/>/1000, only once the prerequisites are met.</summary>
    public int Practise(RecipeDb db, EntityId p, ushort prof, long seconds, int multPm = 1000)
    {
        var def = db.Prof(prof);
        foreach (var q in def.Requires) if (Pm(p, db.ProfIndex(q.Id)) < q.MinPm) return 0;
        long cur = PmRaw(p, prof), max = 1000L * PmMilli;
        if (cur >= max) return 0;
        long add = (long)def.LearnPmPerHour * PmMilli * seconds / 3600 * multPm / 1000;
        long next = Math.Min(max, cur + add);
        _prof[(p.Value, prof)] = next;
        return (int)((next - cur) / PmMilli);
    }

    public long WatchSeconds(EntityId p, ushort recipe) => _watch.GetValueOrDefault((p.Value, recipe));
    public void AddWatch(EntityId p, ushort recipe, long seconds) => _watch[(p.Value, recipe)] = WatchSeconds(p, recipe) + seconds;
    public void SetWatch(EntityId p, ushort recipe, long seconds) { if (seconds <= 0) _watch.Remove((p.Value, recipe)); else _watch[(p.Value, recipe)] = seconds; }
    public int Tries(EntityId p, ushort recipe) => _tries.GetValueOrDefault((p.Value, recipe));
    public void AddTry(EntityId p, ushort recipe) => _tries[(p.Value, recipe)] = Tries(p, recipe) + 1;

    /// <summary>P5-I: forgets the recipes, proficiencies, watching and tries kept under one id (the heir who takes a dead hero's id does not inherit his craft).</summary>
    public void Forget(EntityId p)
    {
        _recipes.Remove(p.Value);
        foreach (var k in _prof.Keys.Where(k => k.Item1 == p.Value).ToList()) _prof.Remove(k);
        foreach (var k in _watch.Keys.Where(k => k.Item1 == p.Value).ToList()) _watch.Remove(k);
        foreach (var k in _tries.Keys.Where(k => k.Item1 == p.Value).ToList()) _tries.Remove(k);
    }

    // ---- save support ----
    public (ulong Person, ushort Recipe)[] RecipeRows() => [.. _recipes.SelectMany(kv => kv.Value.Select(r => (kv.Key, r)))];
    public (ulong Person, ushort Prof, long Raw)[] ProfRows() => [.. _prof.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value))];
    public (ulong Person, ushort Recipe, long Sec, int Tries)[] WatchRows()
        => [.. _watch.Keys.Concat(_tries.Keys).Distinct().Order().Select(k => (k.Item1, k.Item2, _watch.GetValueOrDefault(k), _tries.GetValueOrDefault(k)))];
    public void RestoreProf(ulong p, ushort prof, long raw) => _prof[(p, prof)] = raw;
    public void RestoreWatch(ulong p, ushort recipe, long sec, int tries) { if (sec > 0) _watch[(p, recipe)] = sec; if (tries > 0) _tries[(p, recipe)] = tries; }
    public void RestoreRecipe(ulong p, ushort recipe) { if (!_recipes.TryGetValue(p, out var s)) _recipes[p] = s = []; s.Add(recipe); }
}

/// <summary>A master teaching a student a recipe over game hours (spec 4.6): paid in coin up front, or paid for with the same hours of the student's labour for the master.</summary>
public sealed class TeachSession
{
    public ulong Student, Master;
    public ushort Recipe;
    public long NeededSec, DoneSec;
    public long CostMites;
    /// <summary>The student works for the master instead of paying coin.</summary>
    public bool ByLabour;
    public bool Paid;
    public bool Done => DoneSec >= NeededSec;
}
