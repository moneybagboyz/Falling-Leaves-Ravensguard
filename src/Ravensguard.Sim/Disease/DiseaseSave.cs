using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Disease;

/// <summary>
/// The optional "disease" section (P6-C4, disease-and-plague 3, overview 7; no format bump, absent while nothing has happened, so a world that never had a sickness keeps its bytes): the tracked persons'
/// everyday illnesses, the region's great plague (epidemics, cases, the immune, quarantines), the scarcity terms and the continent's plague. Plain integer rows, all in ascending key order.
/// </summary>
[MessagePackObject]
public sealed class DiseaseSave
{
    public const string SectionName = "disease";

    /// <summary>Per illness: person, kind, start day, care, flags (1 cared, 2 checked), nursed until, physician until, settlement, entry.</summary>
    [Key(0)] public long[][] Illnesses { get; set; } = [];
    [Key(1)] public string[] OnsetKeys { get; set; } = [];
    /// <summary>The region's plague: present, id, name, origin region, start day, begin entry, active, dead, recovered; empty when none.</summary>
    [Key(2)] public long[] Plague { get; set; } = [];
    /// <summary>Per epidemic: settlement, start, end, daily ppm, population at start, dead, infected, entry, active.</summary>
    [Key(3)] public long[][] Epidemics { get; set; } = [];
    /// <summary>Per case: person, settlement, infected day, death day, end day, nursed until, physician until, checked.</summary>
    [Key(4)] public long[][] Cases { get; set; } = [];
    [Key(5)] public long[] Immune { get; set; } = [];
    /// <summary>Quarantines (settlement, until) then burned bedding (household, until) as a second list.</summary>
    [Key(6)] public long[][] Quarantine { get; set; } = [];
    [Key(7)] public long[][] Burned { get; set; } = [];
    /// <summary>Wage term, land term, peak workers, peak households, last day.</summary>
    [Key(8)] public long[] Scarcity { get; set; } = [];
    /// <summary>The continent's plague: active, id, name, origin, start day, start season, dead estimate.</summary>
    [Key(9)] public long[] Continent { get; set; } = [];
    /// <summary>Infected regions: region, from season, until season.</summary>
    [Key(10)] public long[][] Regions { get; set; } = [];
    [Key(11)] public long[][] Reports { get; set; } = [];
    [Key(12)] public int LastDay { get; set; } = -1;

    public static DiseaseSave Capture(DiseaseBook b)
    {
        var s = new DiseaseSave
        {
            Illnesses = [.. b.Illnesses.Values.Select(i => new long[] { (long)i.Person, (long)i.Kind, i.StartDay, (long)i.Care, (i.Cared ? 1 : 0) | (i.Checked ? 2 : 0), i.NursedUntil, i.PhysicianUntil, i.Settlement, (long)i.Entry })],
            OnsetKeys = [.. b.Illnesses.Values.Select(i => i.OnsetKey)],
            Scarcity = b.Scarcity.IsDefault ? [] : [b.Scarcity.WageMultPm, b.Scarcity.LandMultPm, b.Scarcity.PeakWorkers, b.Scarcity.PeakHouseholds, b.Scarcity.LastDay],
            LastDay = b.LastDay,
        };
        if (b.Plague is { } p)
        {
            s.Plague = [1, p.Id, p.NameIndex, p.OriginRegion, p.StartDay, (long)p.BeginEntry, p.Active ? 1 : 0, p.Dead, p.Recovered];
            s.Epidemics = [.. p.Epidemics.Values.Select(e => new long[] { e.Settlement, e.StartDay, e.EndDay, e.DailyPpm, e.PopStart, e.Dead, e.Infected, (long)e.Entry, e.Active ? 1 : 0 })];
            s.Cases = [.. p.Cases.Values.Select(c => new long[] { (long)c.Person, c.Settlement, c.InfectedDay, c.DeathDay, c.EndDay, c.NursedUntil, c.PhysicianUntil, c.Checked ? 1 : 0 })];
            s.Immune = [.. p.Immune.Select(x => (long)x)];
            s.Quarantine = [.. p.Quarantine.Select(kv => new long[] { kv.Key, kv.Value })];
            s.Burned = [.. p.Burned.Select(kv => new long[] { kv.Key, kv.Value })];
        }
        var c2 = b.Continent;
        if (!c2.IsBlank)
        {
            s.Continent = [c2.Active ? 1 : 0, c2.Id, c2.NameIndex, c2.Origin, c2.StartDay, c2.StartSeason, c2.DeadEstimate];
            s.Regions = [.. c2.Infected.Select(kv => new long[] { kv.Key, kv.Value.From, kv.Value.Until })];
            s.Reports = [.. c2.Reports.Select(kv => new long[] { kv.Key, kv.Value })];
        }
        return s;
    }

    public DiseaseBook Restore()
    {
        if (Illnesses.Length != OnsetKeys.Length) throw new SaveException("Corrupt 'disease' section in save.");
        var b = new DiseaseBook { LastDay = LastDay };
        for (int k = 0; k < Illnesses.Length; k++)
        {
            var v = Illnesses[k]; if (v.Length < 9) throw new SaveException("Corrupt illness in save.");
            b.Illnesses[(ulong)v[0]] = new Illness { Person = (ulong)v[0], Kind = (IllnessKind)v[1], StartDay = (int)v[2], Care = (IllnessCare)v[3], Cared = (v[4] & 1) != 0, Checked = (v[4] & 2) != 0,
                NursedUntil = (int)v[5], PhysicianUntil = (int)v[6], Settlement = (int)v[7], Entry = (ulong)v[8], OnsetKey = OnsetKeys[k] };
        }
        if (Plague.Length >= 9)
        {
            var p = new GreatPlague { Id = (int)Plague[1], NameIndex = (int)Plague[2], OriginRegion = (int)Plague[3], StartDay = (int)Plague[4], BeginEntry = (ulong)Plague[5], Active = Plague[6] != 0, Dead = (int)Plague[7], Recovered = (int)Plague[8] };
            foreach (var v in Epidemics) { if (v.Length < 9) throw new SaveException("Corrupt epidemic in save."); p.Epidemics[(int)v[0]] = new Epidemic { Settlement = (int)v[0], StartDay = (int)v[1], EndDay = (int)v[2], DailyPpm = (int)v[3], PopStart = (int)v[4], Dead = (int)v[5], Infected = (int)v[6], Entry = (ulong)v[7], Active = v[8] != 0 }; }
            foreach (var v in Cases) { if (v.Length < 8) throw new SaveException("Corrupt plague case in save."); p.Cases[(ulong)v[0]] = new PlagueCase { Person = (ulong)v[0], Settlement = (int)v[1], InfectedDay = (int)v[2], DeathDay = (int)v[3], EndDay = (int)v[4], NursedUntil = (int)v[5], PhysicianUntil = (int)v[6], Checked = v[7] != 0 }; }
            foreach (var x in Immune) p.Immune.Add((ulong)x);
            foreach (var v in Quarantine) p.Quarantine[(int)v[0]] = (int)v[1];
            foreach (var v in Burned) p.Burned[(int)v[0]] = (int)v[1];
            b.Plague = p;
        }
        if (Scarcity.Length >= 5) b.Scarcity = new ScarcityTerms { WageMultPm = (int)Scarcity[0], LandMultPm = (int)Scarcity[1], PeakWorkers = (int)Scarcity[2], PeakHouseholds = (int)Scarcity[3], LastDay = (int)Scarcity[4] };
        if (Continent.Length >= 7)
        {
            var c = b.Continent; c.Active = Continent[0] != 0; c.Id = (int)Continent[1]; c.NameIndex = (int)Continent[2]; c.Origin = (int)Continent[3]; c.StartDay = (int)Continent[4]; c.StartSeason = Continent[5]; c.DeadEstimate = (int)Continent[6];
            foreach (var v in Regions) c.Infected[(int)v[0]] = (v[1], v[2]);
            foreach (var v in Reports) c.Reports[(int)v[0]] = (int)v[1];
        }
        return b;
    }
}
