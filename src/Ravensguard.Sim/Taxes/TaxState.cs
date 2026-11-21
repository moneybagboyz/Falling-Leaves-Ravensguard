using MessagePack;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Taxes;

/// <summary>
/// The saved state of taxes and treasuries (taxes spec 2): the tax level of each fief, the households' arrears, the season's receipts and payments of every treasury
/// by transfer reason, wages owed, the unrest of each settlement with its ladder, and the counters. Lives in <see cref="Ravensguard.Sim.Abstract.AbstractState"/> and
/// is saved with the "abstract" section. The coin itself is in the ledger; nothing here holds coin.
/// </summary>
public sealed class TaxState
{
    public byte[] FiefLevel = [];
    public readonly Dictionary<int, ArrearsRow> Arrears = [];
    // per settlement
    public short[] Unrest = [], QuietDays = [], TopDays = [];
    public byte[] Rung = [];
    public int[] LastRiotDay = [], LastEvasionDay = [];
    // per fief
    public short[] FiefUnrest = [];
    public int[] LevyDue = [];
    // per treasury slot x transfer reason, and per slot
    public long[] Income = [], Expense = [], LastIncome = [], LastExpense = [];
    public long[] WageOwed = [], LordPaid = [], LastLordPaid = [];
    public long[] Tally = new long[Enum.GetValues<TaxTally>().Length];
    /// <summary>Day of the last season roll of the treasuries' ledgers (a season is rolled once).</summary>
    public int LastRollDay = -1;
    public int Reasons = Ledger.TransferReasons;

    public void Shape(int settlements, int fiefs, int slots)
    {
        PadReasons();
        if (FiefLevel.Length != fiefs) { var n = new byte[fiefs]; Array.Fill(n, (byte)TaxLevel.Normal); Array.Copy(FiefLevel, n, Math.Min(FiefLevel.Length, fiefs)); FiefLevel = n; }
        Unrest = Fit(Unrest, settlements); QuietDays = Fit(QuietDays, settlements); TopDays = Fit(TopDays, settlements); Rung = Fit(Rung, settlements);
        if (LastRiotDay.Length != settlements) { var n = new int[settlements]; Array.Fill(n, int.MinValue / 2); Array.Copy(LastRiotDay, n, Math.Min(LastRiotDay.Length, settlements)); LastRiotDay = n; }
        if (LastEvasionDay.Length != settlements) { var n = new int[settlements]; Array.Fill(n, int.MinValue / 2); Array.Copy(LastEvasionDay, n, Math.Min(LastEvasionDay.Length, settlements)); LastEvasionDay = n; }
        FiefUnrest = Fit(FiefUnrest, fiefs); LevyDue = Fit(LevyDue, fiefs);
        int w = slots * Reasons;
        Income = Fit(Income, w); Expense = Fit(Expense, w); LastIncome = Fit(LastIncome, w); LastExpense = Fit(LastExpense, w);
        WageOwed = Fit(WageOwed, slots); LordPaid = Fit(LordPaid, slots); LastLordPaid = Fit(LastLordPaid, slots);
    }

    /// <summary>A save from before a transfer reason was appended (or one saved compact, see <see cref="TaxSave.Capture"/>): re-pads every row of the by-reason tables to this build's reasons.</summary>
    public void PadReasons()
    {
        if (Reasons == Ledger.TransferReasons) return;
        Income = Repad(Income); Expense = Repad(Expense); LastIncome = Repad(LastIncome); LastExpense = Repad(LastExpense);
        Reasons = Ledger.TransferReasons;
    }

    private long[] Repad(long[] a)
    {
        int old = Math.Max(1, Reasons), rows = a.Length / old, now = Ledger.TransferReasons; var r = new long[rows * now];
        for (int i = 0; i < rows; i++) for (int k = 0; k < Math.Min(old, now); k++) r[i * now + k] = a[i * old + k];
        return r;
    }

    private static T[] Fit<T>(T[] a, int n) { if (a.Length == n) return a; var r = new T[n]; Array.Copy(a, r, Math.Min(a.Length, n)); return r; }

    public ArrearsRow? Row(int household) => Arrears.TryGetValue(household, out var r) ? r : null;
    public ArrearsRow RowFor(int household) { if (!Arrears.TryGetValue(household, out var r)) Arrears[household] = r = new ArrearsRow(); return r; }
    public TaxLevel Level(int fief) => fief >= 0 && fief < FiefLevel.Length ? (TaxLevel)FiefLevel[fief] : TaxLevel.Normal;
    public long TallyOf(TaxTally t) => Tally[(int)t];
    public void Add(TaxTally t, long v) => Tally[(int)t] += v;
}

/// <summary>The "taxes" part of the abstract section (explicit keys; arrears in ascending household order, so the bytes are deterministic).</summary>
[MessagePackObject]
public sealed class TaxSave
{
    [Key(0)] public byte[] FiefLevel { get; set; } = [];
    [Key(1)] public int[] ArrHh { get; set; } = [];
    [Key(2)] public long[] ArrValues { get; set; } = [];     // 4 per row: hearth, dwelling, debt hearth, debt dwelling
    [Key(3)] public int[] ArrSeasons { get; set; } = [];
    [Key(4)] public short[] Unrest { get; set; } = [];
    [Key(5)] public short[] QuietDays { get; set; } = [];
    [Key(6)] public short[] TopDays { get; set; } = [];
    [Key(7)] public byte[] Rung { get; set; } = [];
    [Key(8)] public int[] LastRiotDay { get; set; } = [];
    [Key(9)] public int[] LastEvasionDay { get; set; } = [];
    [Key(10)] public short[] FiefUnrest { get; set; } = [];
    [Key(11)] public int[] LevyDue { get; set; } = [];
    [Key(12)] public long[] Income { get; set; } = [];
    [Key(13)] public long[] Expense { get; set; } = [];
    [Key(14)] public long[] LastIncome { get; set; } = [];
    [Key(15)] public long[] LastExpense { get; set; } = [];
    [Key(16)] public long[] WageOwed { get; set; } = [];
    [Key(17)] public long[] LordPaid { get; set; } = [];
    [Key(18)] public long[] LastLordPaid { get; set; } = [];
    [Key(19)] public long[] Tally { get; set; } = [];
    [Key(20)] public int LastRollDay { get; set; } = -1;
    [Key(21)] public int Reasons { get; set; }

    public static TaxSave Capture(TaxState s)
    {
        var rows = s.Arrears.Where(kv => !kv.Value.IsClear).OrderBy(kv => kv.Key).ToList();
        var vals = new long[rows.Count * 4]; var seasons = new int[rows.Count]; var hh = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i].Value; hh[i] = rows[i].Key; seasons[i] = r.Seasons;
            vals[i * 4] = r.ArrHearth; vals[i * 4 + 1] = r.ArrDwelling; vals[i * 4 + 2] = r.DebtHearth; vals[i * 4 + 3] = r.DebtDwelling;
        }
        // the by-reason tables are saved as wide as the highest reason any row has used (never narrower than the pre-Phase 7 count): a world that never used the Phase 7 reasons keeps its bytes, and Shape re-pads a narrower save
        int R = s.Reasons, w = Math.Min(R, (int)TransferReason.RelicSale + 1);
        foreach (var a in new[] { s.Income, s.Expense, s.LastIncome, s.LastExpense })
            for (int i = 0; i < a.Length; i++) if (a[i] != 0 && i % R >= w) w = i % R + 1;
        long[] Narrow(long[] a) { if (w == R) return (long[])a.Clone(); int n = a.Length / R; var o = new long[n * w]; for (int i = 0; i < n; i++) Array.Copy(a, i * R, o, i * w, w); return o; }
        return new TaxSave
        {
            FiefLevel = (byte[])s.FiefLevel.Clone(), ArrHh = hh, ArrValues = vals, ArrSeasons = seasons,
            Unrest = (short[])s.Unrest.Clone(), QuietDays = (short[])s.QuietDays.Clone(), TopDays = (short[])s.TopDays.Clone(), Rung = (byte[])s.Rung.Clone(),
            LastRiotDay = (int[])s.LastRiotDay.Clone(), LastEvasionDay = (int[])s.LastEvasionDay.Clone(), FiefUnrest = (short[])s.FiefUnrest.Clone(), LevyDue = (int[])s.LevyDue.Clone(),
            Income = Narrow(s.Income), Expense = Narrow(s.Expense), LastIncome = Narrow(s.LastIncome), LastExpense = Narrow(s.LastExpense),
            WageOwed = (long[])s.WageOwed.Clone(), LordPaid = (long[])s.LordPaid.Clone(), LastLordPaid = (long[])s.LastLordPaid.Clone(), Tally = (long[])s.Tally.Clone(),
            LastRollDay = s.LastRollDay, Reasons = w,
        };
    }

    public TaxState Restore()
    {
        var s = new TaxState
        {
            FiefLevel = (byte[])FiefLevel.Clone(), Unrest = (short[])Unrest.Clone(), QuietDays = (short[])QuietDays.Clone(), TopDays = (short[])TopDays.Clone(), Rung = (byte[])Rung.Clone(),
            LastRiotDay = (int[])LastRiotDay.Clone(), LastEvasionDay = (int[])LastEvasionDay.Clone(), FiefUnrest = (short[])FiefUnrest.Clone(), LevyDue = (int[])LevyDue.Clone(),
            Income = (long[])Income.Clone(), Expense = (long[])Expense.Clone(), LastIncome = (long[])LastIncome.Clone(), LastExpense = (long[])LastExpense.Clone(),
            WageOwed = (long[])WageOwed.Clone(), LordPaid = (long[])LordPaid.Clone(), LastLordPaid = (long[])LastLordPaid.Clone(), LastRollDay = LastRollDay, Reasons = Reasons == 0 ? Ledger.TransferReasons : Reasons,
        };
        s.PadReasons();
        Array.Copy(Tally, s.Tally, Math.Min(Tally.Length, s.Tally.Length));
        for (int i = 0; i < ArrHh.Length; i++)
            s.Arrears[ArrHh[i]] = new ArrearsRow { ArrHearth = ArrValues[i * 4], ArrDwelling = ArrValues[i * 4 + 1], DebtHearth = ArrValues[i * 4 + 2], DebtDwelling = ArrValues[i * 4 + 3], Seasons = ArrSeasons[i] };
        return s;
    }
}
