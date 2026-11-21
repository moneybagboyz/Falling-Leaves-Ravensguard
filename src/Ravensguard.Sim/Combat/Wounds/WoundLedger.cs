using Ravensguard.Sim.Combat.Resolver2;
using static Ravensguard.Sim.Combat.Wounds.MedicineTables;

namespace Ravensguard.Sim.Combat.Wounds;

/// <summary>
/// Every wounded person's <see cref="WoundBook"/>, by person id value (a sorted map, so every walk and every save is in id order). The one object both tiers share:
/// the live tier calls <see cref="AdvanceTo"/> on each 20 Hz step with the world clock, the abstract tier once an hour; a call to a time already reached does nothing,
/// and a book advanced in many small steps equals one advanced in a single big one, so promote and demote have nothing to copy (wounds 5, test WB-14).
/// </summary>
public sealed class WoundLedger
{
    public readonly SortedDictionary<ulong, WoundBook> Books = [];
    public ulong Seed;
    /// <summary>Difficulty (wound lethality), percent of the calibrated bleed rate at a wound's creation and of the infection chance per day; handed to every book the ledger holds.</summary>
    public int LethalityPct = 100;
    /// <summary>The last game day whose daily step has run.</summary>
    public long LastDay;
    /// <summary>Tick and day steps may come from the live thread and the tick worker; both take this lock.</summary>
    public readonly object Sync = new();
    private readonly List<ulong> _drop = [];

    public WoundLedger(ulong seed = 0, long startSec = 0) { Seed = seed; LastDay = startSec / GameSecPerDay; }

    public int Count => Books.Count;
    public bool IsEmpty => Books.Count == 0;

    public WoundBook? Find(ulong key) => Books.GetValueOrDefault(key);

    /// <summary>The person's book, made on first use (blood scaled by body size, wounds 3.3). Advances the whole ledger to <paramref name="nowSec"/> first.</summary>
    public WoundBook For(ulong key, BodyPlanV2 plan, long nowSec, int sizeCm3 = 0, int toughness = 10)
    {
        lock (Sync)
        {
            AdvanceTo(nowSec);
            if (Books.TryGetValue(key, out var b)) return b;
            long size = sizeCm3 > 0 ? sizeCm3 : plan.SizeCm3;
            int max = (int)((long)plan.BloodMl * size / plan.SizeCm3);
            b = new WoundBook(Seed, key, max, toughness, plan.Id) { LastSec = nowSec, DayDone = LastDay, LethalityPct = LethalityPct };
            Books[key] = b;
            return b;
        }
    }

    /// <summary>
    /// Puts a book made elsewhere (a fight's, an abstract battle's) into the ledger at <paramref name="nowSec"/>. Its day cursor starts no earlier than that day, so the next
    /// <see cref="AdvanceTo"/> gives it the daily steps of the days after it came, never those before (whatever day the ledger's own cursor was left at).
    /// </summary>
    public void Adopt(ulong key, WoundBook book, long nowSec)
    {
        lock (Sync)
        {
            book.DayDone = Math.Max(book.DayDone, nowSec / GameSecPerDay);
            book.LethalityPct = LethalityPct;
            // the book's rolls (infection, healing, surgery) are keyed by its seed and key; a book made elsewhere carries its own, but the save keeps only the ledger's seed and the dictionary key,
            // so the book takes both on joining: a saved and loaded world rolls the same as the one that was not saved
            book.Seed = Seed; book.Key = key;
            Books[key] = book;
        }
    }

    /// <summary>Runs every day boundary up to <paramref name="sec"/> (the daily step, in id order), then every book to <paramref name="sec"/>. Books that are healthy again leave the ledger.</summary>
    public void AdvanceTo(long sec)
    {
        lock (Sync)
        {
            if (Books.Count == 0) { LastDay = Math.Max(LastDay, sec / GameSecPerDay); return; }
            long dayNow = sec / GameSecPerDay;
            // each book from its own day cursor (a book only touches itself and its rolls are keyed by its seed, day and key, so book by book equals day by day)
            foreach (var b in Books.Values)
            {
                b.LethalityPct = LethalityPct;
                long from = b.DayDone >= 0 ? b.DayDone : LastDay;
                for (long d = from + 1; d <= dayNow; d++) b.DailyStep(d);
                b.DayDone = Math.Max(from, dayNow);
            }
            LastDay = Math.Max(LastDay, dayNow);
            _drop.Clear();
            foreach (var (k, b) in Books)
            {
                b.AdvanceTo(sec);
                if (b.Empty) _drop.Add(k);
            }
            foreach (var k in _drop) Books.Remove(k);
        }
    }

    /// <summary>The people who bleed or hurt now: the live tier's <c>BleedingSet</c>, rebuilt from the rows on promotion (wounds 5).</summary>
    public List<ulong> BleedingSet()
    {
        var l = new List<ulong>();
        foreach (var (k, b) in Books) if (b.Active) l.Add(k);
        return l;
    }

    /// <summary>FNV-1a over every book in id order: the WoundHash of the daily trail (wounds 6).</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        foreach (var (k, b) in Books) h = b.Hash(h ^ k);
        return h ^ (ulong)LastDay;
    }
}
