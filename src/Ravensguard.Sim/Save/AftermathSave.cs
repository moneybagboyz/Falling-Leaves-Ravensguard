using MessagePack;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Save;

/// <summary>One captive as saved (P4-D, save v7): every field of <see cref="Captive"/> in a fixed order.</summary>
[MessagePackObject]
public sealed class CaptiveRow
{
    [Key(0)] public ulong Person { get; set; }
    [Key(1)] public bool IsHero { get; set; }
    [Key(2)] public long[] Ints { get; set; } = [];
    [Key(3)] public byte[] PayerKinds { get; set; } = [];
    [Key(4)] public ulong[] PayerOwners { get; set; } = [];
    [Key(5)] public ulong[] StashItems { get; set; } = [];
    [Key(6)] public string Note { get; set; } = "";
}

/// <summary>The "captivity" section (P4-D, optional; save v7): everyone held, the control switch and the last attempt's outcome.</summary>
[MessagePackObject]
public sealed class CaptivitySave
{
    public const string SectionName = "captivity";
    [Key(0)] public ulong Seed { get; set; }
    [Key(1)] public ulong Controlled { get; set; }
    [Key(2)] public CaptiveRow[] Captives { get; set; } = [];
    [Key(3)] public long[] Last { get; set; } = [];
    [Key(4)] public string LastText { get; set; } = "";

    public static CaptivitySave Capture(CaptiveBook b)
    {
        lock (b.Sync)
        {
            var s = new CaptivitySave { Seed = b.Seed, Controlled = b.Controlled.Value };
            s.Captives = [.. b.Captives.Values.Select(c => new CaptiveRow
            {
                Person = c.Person.Value, IsHero = c.IsHero, Note = c.Note,
                Ints =
                [
                    (long)c.Captor, (long)c.CaptorId.Value, (long)c.CaptorPurse.Kind, (long)c.CaptorPurse.Owner.Value, c.PlaceX, c.PlaceY, (long)c.Place.Value, (long)c.Restraint, c.SinceDay, c.SinceSec,
                    (long)c.Rank, c.BaseRansom, c.RansomMites, (long)c.PayerKind, c.DueDay, c.Negotiated ? 1 : 0, c.Paid ? 1 : 0, c.Guards, c.ShiftHours, (long)c.Stash.Value,
                    (long)c.Care, c.Cared ? 1 : 0, c.NextTransferDay, c.EscapeAttempts, (long)c.State, c.HoldUntilDay, c.EndedSec, c.HasPick ? 1 : 0, c.ImprovisedPick ? 1 : 0,
                    c.Stealth, c.Athletics, c.Lockpicking, c.Perception, c.Speech, c.RescueMembers, c.LastDay,
                ],
                PayerKinds = [.. c.Payers.Select(p => (byte)p.Kind)], PayerOwners = [.. c.Payers.Select(p => p.Owner.Value)], StashItems = [.. c.StashItems.Select(i => i.Value)],
            })];
            if (b.Last is { } l) { s.Last = [(long)l.Verb, l.Ok ? 1 : 0, l.Caught ? 1 : 0, l.EndSec, l.Mites]; s.LastText = l.Text; }
            return s;
        }
    }

    public CaptiveBook Restore()
    {
        var b = new CaptiveBook(Seed) { Controlled = new EntityId(Controlled) };
        foreach (var r in Captives)
        {
            var v = r.Ints;
            if (v.Length < 36 || r.PayerKinds.Length != r.PayerOwners.Length) throw new SaveException("Corrupt 'captivity' section in save.");
            int i = 0;
            var c = new Captive
            {
                Person = new EntityId(r.Person), IsHero = r.IsHero, Note = r.Note,
                Captor = (CaptorKind)v[i++], CaptorId = new EntityId((ulong)v[i++]), CaptorPurse = new PurseRef((PurseKind)v[i++], new EntityId((ulong)v[i++])), PlaceX = v[i++], PlaceY = v[i++],
                Place = new EntityId((ulong)v[i++]), Restraint = (Restraint)v[i++], SinceDay = (int)v[i++], SinceSec = v[i++], Rank = (CaptiveRank)v[i++], BaseRansom = v[i++], RansomMites = v[i++],
                PayerKind = (PayerKind)v[i++], DueDay = (int)v[i++], Negotiated = v[i++] != 0, Paid = v[i++] != 0, Guards = (int)v[i++], ShiftHours = (int)v[i++], Stash = new EntityId((ulong)v[i++]),
                Care = (CareLevel)v[i++], Cared = v[i++] != 0, NextTransferDay = (int)v[i++], EscapeAttempts = (int)v[i++], State = (CaptiveState)v[i++], HoldUntilDay = (int)v[i++], EndedSec = v[i++],
                HasPick = v[i++] != 0, ImprovisedPick = v[i++] != 0, Stealth = (int)v[i++], Athletics = (int)v[i++], Lockpicking = (int)v[i++], Perception = (int)v[i++], Speech = (int)v[i++],
                RescueMembers = (int)v[i++], LastDay = (int)v[i++],
            };
            for (int k = 0; k < r.PayerKinds.Length; k++) c.Payers.Add(new PurseRef((PurseKind)r.PayerKinds[k], new EntityId(r.PayerOwners[k])));
            foreach (var it in r.StashItems) c.StashItems.Add(new EntityId(it));
            b.Captives[r.Person] = c;
        }
        if (Last.Length >= 5) b.Last = new CaptiveResult((CaptiveVerb)Last[0], Last[1] != 0, Last[2] != 0, LastText, Last[3], Last[4]);
        return b;
    }
}

/// <summary>The "succession" section (P4-D, optional; save v7): the estates, the inheritance records and whether the run has ended.</summary>
[MessagePackObject]
public sealed class SuccessionSave
{
    public const string SectionName = "succession";
    [Key(0)] public long[][] Estates { get; set; } = [];
    [Key(1)] public ulong[][] EstateItems { get; set; } = [];
    [Key(2)] public long[][] Records { get; set; } = [];
    [Key(3)] public string[][] RecordText { get; set; } = [];
    [Key(4)] public bool RunEnded { get; set; }
    [Key(5)] public ulong CurrentHero { get; set; }
    /// <summary>The hero's kin (P4 close-out; absent in older saves, which read as none).</summary>
    [Key(6)] public ulong[] KinPeople { get; set; } = [];
    [Key(7)] public byte[] KinRelations { get; set; } = [];
    /// <summary>P5-H (additive): each estate's holdings, eight longs each (kind, id, value, to, to kind, to row, escheat, claimed by); null when no estate has any (older bytes unchanged).</summary>
    [Key(8)] public long[][]? EstateHoldings { get; set; }
    /// <summary>P5-H (additive): each estate's debts, six longs each (kind, creditor, creditor kind, owed, paid, subject).</summary>
    [Key(9)] public long[][]? EstateDebts { get; set; }

    public static SuccessionSave Capture(SuccessionBook b) => new()
    {
        Estates = [.. b.Estates.Select(e => new long[] { e.Id, (long)e.Dead.Value, e.PlaceX, e.PlaceY, e.CoinMites, e.FuneralMites, (long)e.Heir.Value, e.Claimed ? 1 : 0, e.Looted ? 1 : 0, e.Day })],
        EstateItems = [.. b.Estates.Select(e => e.Items.Select(i => i.Value).ToArray())],
        Records = [.. b.Records.Select(r => new long[] { (long)r.From.Value, (long)r.To.Value, (long)r.Rule, r.Coin, r.EstateId, r.GameDay, (long)r.Guardian.Value })],
        RecordText = [.. b.Records.Select(r => new[] { r.ReputationPending, r.RecoverShape }.Concat(r.Deeds).ToArray())],
        RunEnded = b.RunEnded, CurrentHero = b.CurrentHero.Value,
        KinPeople = [.. b.Kin.Select(k => k.Person.Value)], KinRelations = [.. b.Kin.Select(k => (byte)k.Relation)],
        EstateHoldings = b.Estates.Any(e => e.Holdings.Count > 0 || e.Debts.Count > 0)
            ? [.. b.Estates.Select(e => e.Holdings.SelectMany(x => new long[] { (long)x.Kind, (long)x.Id.Value, x.ValueMites, (long)x.To.Value, (long)x.ToKind, x.ToRow, x.Escheat ? 1 : 0, (long)x.ClaimedBy.Value }).ToArray())] : null,
        EstateDebts = b.Estates.Any(e => e.Holdings.Count > 0 || e.Debts.Count > 0)
            ? [.. b.Estates.Select(e => e.Debts.SelectMany(d => new long[] { (long)d.Kind, (long)d.Creditor.Value, (long)d.CreditorKind, d.OwedMites, d.PaidMites, (long)d.Subject.Value }).ToArray())] : null,
    };

    public SuccessionBook Restore()
    {
        if (Estates.Length != EstateItems.Length || Records.Length != RecordText.Length) throw new SaveException("Corrupt 'succession' section in save.");
        var b = new SuccessionBook { RunEnded = RunEnded, CurrentHero = new EntityId(CurrentHero) };
        if (KinPeople.Length != KinRelations.Length) throw new SaveException("Corrupt kin in the 'succession' section.");
        for (int k = 0; k < KinPeople.Length; k++) b.Kin.Add(new HeroKinLink(new EntityId(KinPeople[k]), (KinRelation)KinRelations[k]));
        for (int k = 0; k < Estates.Length; k++)
        {
            var v = Estates[k];
            if (v.Length < 10) throw new SaveException("Corrupt estate in save.");
            b.Estates.Add(new Estate
            {
                Id = (int)v[0], Dead = new EntityId((ulong)v[1]), PlaceX = v[2], PlaceY = v[3], CoinMites = v[4], FuneralMites = v[5], Heir = new EntityId((ulong)v[6]), Claimed = v[7] != 0, Looted = v[8] != 0,
                Day = (int)v[9], Items = [.. EstateItems[k].Select(i => new EntityId(i))],
            });
            var est = b.Estates[^1];
            if (EstateHoldings != null && k < EstateHoldings.Length)
            {
                var h = EstateHoldings[k]; if (h.Length % 8 != 0) throw new SaveException("Corrupt estate holdings in save.");
                for (int j = 0; j < h.Length; j += 8)
                    est.Holdings.Add(new EstateEntry { Kind = (EstateThing)h[j], Id = new EntityId((ulong)h[j + 1]), ValueMites = h[j + 2], To = new EntityId((ulong)h[j + 3]), ToKind = (Ravensguard.Sim.Economy.PurseKind)h[j + 4],
                        ToRow = (int)h[j + 5], Escheat = h[j + 6] != 0, ClaimedBy = new EntityId((ulong)h[j + 7]) });
            }
            if (EstateDebts != null && k < EstateDebts.Length)
            {
                var d = EstateDebts[k]; if (d.Length % 6 != 0) throw new SaveException("Corrupt estate debts in save.");
                for (int j = 0; j < d.Length; j += 6)
                    est.Debts.Add(new EstateDebt { Kind = (DebtKind)d[j], Creditor = new EntityId((ulong)d[j + 1]), CreditorKind = (Ravensguard.Sim.Economy.PurseKind)d[j + 2], OwedMites = d[j + 3], PaidMites = d[j + 4], Subject = new EntityId((ulong)d[j + 5]) });
            }
        }
        for (int k = 0; k < Records.Length; k++)
        {
            var v = Records[k]; var t = RecordText[k];
            if (v.Length < 7 || t.Length < 2) throw new SaveException("Corrupt inheritance record in save.");
            b.Records.Add(new InheritanceRecord
            {
                From = new EntityId((ulong)v[0]), To = new EntityId((ulong)v[1]), Rule = (SuccessionRule)v[2], Coin = v[3], EstateId = (int)v[4], GameDay = (int)v[5], Guardian = new EntityId((ulong)v[6]),
                ReputationPending = t[0], RecoverShape = t[1], Deeds = [.. t.Skip(2)],
            });
        }
        return b;
    }
}
