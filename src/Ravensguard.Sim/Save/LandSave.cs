using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "land" section (P5-A2, land spec 3.2; no format bump: absent while the book is empty, so a world that never touched land keeps its bytes): the deeds play changed, the change log,
/// claims, founding charters and the standing with grantors, as parallel arrays in book order. The deeds generation implies are not stored: <see cref="LandRegistry.Build"/> derives them at load.
/// </summary>
[MessagePackObject]
public sealed class LandSave
{
    public const string SectionName = "land";

    [Key(0)] public int NextSerial { get; set; } = 1;
    [Key(1)] public long DistrainedMites { get; set; }
    // deeds (ascending id)
    [Key(2)] public ulong[] DId { get; set; } = [];
    [Key(3)] public byte[] DKind { get; set; } = [];
    [Key(4)] public long[] DSubject { get; set; } = [];
    [Key(5)] public int[] DSettlement { get; set; } = [];
    [Key(6)] public ulong[] DHolder { get; set; } = [];
    [Key(7)] public byte[] DHolderPurse { get; set; } = [];
    [Key(8)] public byte[] DTenure { get; set; } = [];
    [Key(9)] public int[] DLord { get; set; } = [];
    [Key(10)] public long[] DDues { get; set; } = [];
    [Key(11)] public int[] DLeaseEnd { get; set; } = [];
    [Key(12)] public ulong[] DLessor { get; set; } = [];
    [Key(13)] public byte[] DLessorPurse { get; set; } = [];
    [Key(14)] public byte[] DArrears { get; set; } = [];
    [Key(15)] public long[] DOwed { get; set; } = [];
    /// <summary>Bit 0 in debt, bit 1 renews.</summary>
    [Key(16)] public byte[] DFlags { get; set; } = [];
    [Key(17)] public int[] DNotice { get; set; } = [];
    [Key(18)] public int[] DAcquired { get; set; } = [];
    [Key(19)] public byte[] DCause { get; set; } = [];
    [Key(20)] public long[] DAsk { get; set; } = [];
    /// <summary>x, y, w, h per deed.</summary>
    [Key(21)] public int[] DRect { get; set; } = [];
    // change log
    [Key(22)] public int[] CDay { get; set; } = [];
    [Key(23)] public ulong[] CDeed { get; set; } = [];
    [Key(24)] public ulong[] CHolder { get; set; } = [];
    [Key(25)] public byte[] CTenure { get; set; } = [];
    [Key(26)] public long[] CDues { get; set; } = [];
    [Key(27)] public int[] CLeaseEnd { get; set; } = [];
    [Key(28)] public byte[] CCause { get; set; } = [];
    // claims and charters
    [Key(29)] public ClaimRow[] Claims { get; set; } = [];
    [Key(30)] public CharterRow[] Charters { get; set; } = [];
    [Key(31)] public long[] StandingKey { get; set; } = [];
    [Key(32)] public int[] StandingValue { get; set; } = [];

    [MessagePackObject]
    public sealed class ClaimRow
    {
        [Key(0)] public int Serial { get; set; }
        [Key(1)] public ulong Claimant { get; set; }
        [Key(2)] public byte ClaimantPurse { get; set; }
        [Key(3)] public int[] Tiles { get; set; } = [];
        [Key(4)] public int BannerX { get; set; }
        [Key(5)] public int BannerY { get; set; }
        [Key(6)] public int StakeDay { get; set; }
        [Key(7)] public int Fief { get; set; }
        /// <summary>Bit 0 licensed, bit 1 squatting.</summary>
        [Key(8)] public byte Flags { get; set; }
        [Key(9)] public long LicenceMites { get; set; }
        [Key(10)] public byte State { get; set; }
        [Key(11)] public int Improvements { get; set; }
        [Key(12)] public int DwellingDay { get; set; }
        [Key(13)] public int SownAcres { get; set; }
        [Key(14)] public ulong DeedId { get; set; }
        [Key(15)] public int EndDay { get; set; }
    }

    [MessagePackObject]
    public sealed class CharterRow
    {
        [Key(0)] public int Serial { get; set; }
        [Key(1)] public byte GrantorKind { get; set; }
        [Key(2)] public int Grantor { get; set; }
        [Key(3)] public ulong Grantee { get; set; }
        [Key(4)] public byte GranteePurse { get; set; }
        [Key(5)] public int[] Tiles { get; set; } = [];
        [Key(6)] public int CenterX { get; set; }
        [Key(7)] public int CenterY { get; set; }
        [Key(8)] public int Anchor { get; set; }
        /// <summary>Grant day, settle-by day, households needed, service men, service days, fulfil day, lapse day, village day.</summary>
        [Key(9)] public int[] Days { get; set; } = [];
        [Key(10)] public long DuesMitesPerYear { get; set; }
        [Key(11)] public long FeeMites { get; set; }
        [Key(12)] public byte State { get; set; }
        [Key(13)] public ulong[] Households { get; set; } = [];
        [Key(14)] public ulong GrantEvent { get; set; }
        [Key(15)] public ulong DeedId { get; set; }
    }

    public static LandSave Capture(LandBook b)
    {
        var ds = b.Deeds.Values.ToList();
        return new LandSave
        {
            NextSerial = b.NextSerial, DistrainedMites = b.DistrainedMites,
            DId = [.. ds.Select(d => d.Id.Value)], DKind = [.. ds.Select(d => (byte)d.Kind)], DSubject = [.. ds.Select(d => d.Subject)], DSettlement = [.. ds.Select(d => d.Settlement)],
            DHolder = [.. ds.Select(d => d.Holder.Value)], DHolderPurse = [.. ds.Select(d => (byte)d.HolderPurse)], DTenure = [.. ds.Select(d => (byte)d.Tenure)], DLord = [.. ds.Select(d => d.Lord)],
            DDues = [.. ds.Select(d => d.DuesMitesPerSeason)], DLeaseEnd = [.. ds.Select(d => d.LeaseEndDay)], DLessor = [.. ds.Select(d => d.Lessor.Value)], DLessorPurse = [.. ds.Select(d => (byte)d.LessorPurse)],
            DArrears = [.. ds.Select(d => d.ArrearsSeasons)], DOwed = [.. ds.Select(d => d.OwedMites)], DFlags = [.. ds.Select(d => (byte)((d.InDebt ? 1 : 0) | (d.Renew ? 2 : 0)))],
            DNotice = [.. ds.Select(d => d.NoticeDay)], DAcquired = [.. ds.Select(d => d.AcquiredDay)], DCause = [.. ds.Select(d => (byte)d.Cause)], DAsk = [.. ds.Select(d => d.AskMites)],
            DRect = [.. ds.SelectMany(d => new[] { d.Rect.X, d.Rect.Y, d.Rect.W, d.Rect.H })],
            CDay = [.. b.Changes.Select(c => c.Day)], CDeed = [.. b.Changes.Select(c => c.Deed.Value)], CHolder = [.. b.Changes.Select(c => c.NewHolder.Value)], CTenure = [.. b.Changes.Select(c => (byte)c.NewTenure)],
            CDues = [.. b.Changes.Select(c => c.Dues)], CLeaseEnd = [.. b.Changes.Select(c => c.LeaseEnd)], CCause = [.. b.Changes.Select(c => (byte)c.Cause)],
            Claims = [.. b.Claims.Select(c => new ClaimRow
            {
                Serial = c.Serial, Claimant = c.Claimant.Value, ClaimantPurse = (byte)c.ClaimantPurse, Tiles = [.. c.Tiles], BannerX = c.BannerX, BannerY = c.BannerY, StakeDay = c.StakeDay, Fief = c.Fief,
                Flags = (byte)((c.Licensed ? 1 : 0) | (c.Squatting ? 2 : 0)), LicenceMites = c.LicenceMites, State = (byte)c.State, Improvements = c.Improvements, DwellingDay = c.DwellingDay, SownAcres = c.SownAcres,
                DeedId = c.DeedId.Value, EndDay = c.EndDay,
            })],
            Charters = [.. b.Charters.Select(c => new CharterRow
            {
                Serial = c.Serial, GrantorKind = (byte)c.GrantorKind, Grantor = c.Grantor, Grantee = c.Grantee.Value, GranteePurse = (byte)c.GranteePurse, Tiles = [.. c.Tiles], CenterX = c.CenterX, CenterY = c.CenterY,
                Anchor = c.Anchor, Days = [c.GrantDay, c.SettleByDay, c.MinHouseholds, c.ServiceMen, c.ServiceDays, c.FulfilDay, c.LapseDay, c.VillageDay], DuesMitesPerYear = c.DuesMitesPerYear, FeeMites = c.FeeMites,
                State = (byte)c.State, Households = [.. c.Households.Select(h => h.Value)], GrantEvent = c.GrantEvent.Value, DeedId = c.DeedId.Value,
            })],
            StandingKey = [.. b.Standing.Keys], StandingValue = [.. b.Standing.Values],
        };
    }

    public LandBook Restore()
    {
        int n = DId.Length;
        if (DKind.Length != n || DSubject.Length != n || DSettlement.Length != n || DHolder.Length != n || DHolderPurse.Length != n || DTenure.Length != n || DLord.Length != n || DDues.Length != n
            || DLeaseEnd.Length != n || DLessor.Length != n || DLessorPurse.Length != n || DArrears.Length != n || DOwed.Length != n || DFlags.Length != n || DNotice.Length != n || DAcquired.Length != n
            || DCause.Length != n || DAsk.Length != n || DRect.Length != n * 4)
            throw new SaveException("Corrupt 'land' section in save: the deed columns do not match.");
        int m = CDay.Length;
        if (CDeed.Length != m || CHolder.Length != m || CTenure.Length != m || CDues.Length != m || CLeaseEnd.Length != m || CCause.Length != m || StandingKey.Length != StandingValue.Length)
            throw new SaveException("Corrupt 'land' section in save: the change log does not match.");
        var b = new LandBook { NextSerial = NextSerial, DistrainedMites = DistrainedMites };
        for (int i = 0; i < n; i++)
        {
            var d = new Deed
            {
                Id = new EntityId(DId[i]), Kind = (HoldingKind)DKind[i], Subject = DSubject[i], Settlement = DSettlement[i], Holder = new EntityId(DHolder[i]), HolderPurse = (PurseKind)DHolderPurse[i],
                Tenure = (Tenure)DTenure[i], Lord = DLord[i], DuesMitesPerSeason = DDues[i], LeaseEndDay = DLeaseEnd[i], Lessor = new EntityId(DLessor[i]), LessorPurse = (PurseKind)DLessorPurse[i],
                ArrearsSeasons = DArrears[i], OwedMites = DOwed[i], InDebt = (DFlags[i] & 1) != 0, Renew = (DFlags[i] & 2) != 0, NoticeDay = DNotice[i], AcquiredDay = DAcquired[i],
                Cause = (DeedCause)DCause[i], AskMites = DAsk[i], Rect = new RectM(DRect[i * 4], DRect[i * 4 + 1], DRect[i * 4 + 2], DRect[i * 4 + 3]),
            };
            b.Deeds[d.Id] = d;
        }
        for (int i = 0; i < m; i++) b.Changes.Add(new DeedChange(CDay[i], new EntityId(CDeed[i]), new EntityId(CHolder[i]), (Tenure)CTenure[i], CDues[i], CLeaseEnd[i], (DeedCause)CCause[i]));
        foreach (var r in Claims)
            b.Claims.Add(new Claim
            {
                Serial = r.Serial, Claimant = new EntityId(r.Claimant), ClaimantPurse = (PurseKind)r.ClaimantPurse, Tiles = [.. r.Tiles], BannerX = r.BannerX, BannerY = r.BannerY, StakeDay = r.StakeDay, Fief = r.Fief,
                Licensed = (r.Flags & 1) != 0, Squatting = (r.Flags & 2) != 0, LicenceMites = r.LicenceMites, State = (ClaimState)r.State, Improvements = r.Improvements, DwellingDay = r.DwellingDay,
                SownAcres = r.SownAcres, DeedId = new EntityId(r.DeedId), EndDay = r.EndDay,
            });
        foreach (var r in Charters)
        {
            if (r.Days.Length != 8) throw new SaveException("Corrupt 'land' section in save: a charter's days are malformed.");
            var c = new Charter
            {
                Serial = r.Serial, GrantorKind = (GrantorKind)r.GrantorKind, Grantor = r.Grantor, Grantee = new EntityId(r.Grantee), GranteePurse = (PurseKind)r.GranteePurse, Tiles = [.. r.Tiles],
                CenterX = r.CenterX, CenterY = r.CenterY, Anchor = r.Anchor, GrantDay = r.Days[0], SettleByDay = r.Days[1], MinHouseholds = r.Days[2], ServiceMen = r.Days[3], ServiceDays = r.Days[4],
                FulfilDay = r.Days[5], LapseDay = r.Days[6], VillageDay = r.Days[7], DuesMitesPerYear = r.DuesMitesPerYear, FeeMites = r.FeeMites, State = (CharterState)r.State,
                GrantEvent = new EntityId(r.GrantEvent), DeedId = new EntityId(r.DeedId),
            };
            foreach (var h in r.Households) c.Households.Add(new EntityId(h));
            b.Charters.Add(c);
        }
        for (int i = 0; i < StandingKey.Length; i++) b.Standing[StandingKey[i]] = StandingValue[i];
        return b;
    }
}
