using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Disease;

/// <summary>One sick person as the character screen shows him (disease-and-plague 6): the illness, its day and course, the care he has and the band of his death risk.</summary>
public sealed record IllnessView(ulong Person, string PersonName, string IllnessKey, int Day, int Course, IllnessCare Care, string RiskKey, bool BedBound, bool IsHero, bool Plague);

/// <summary>The wage and land terms and what the plague has done, as the ledger shows them (6): read-only, built at a barrier.</summary>
public sealed record ScarcityView(int WageMultPm, int LandMultPm, bool PlagueActive, int PlagueDead, string PlagueNameKey);

public static class DiseaseView
{
    /// <summary>The risk band's key for a death chance (ppm): low under 1 percent, some under 5, high under 20, grave above.</summary>
    public static string RiskKeyOf(int ppm) => ppm < 10_000 ? "dz_risk_low" : ppm < 50_000 ? "dz_risk_some" : ppm < 200_000 ? "dz_risk_high" : "dz_risk_grave";

    /// <summary>The sick among the tracked persons, hero first (then by id).</summary>
    public static IReadOnlyList<IllnessView> Of(WorldState st, int day)
    {
        var b = st.Disease; if (b == null || st.Population == null || (b.Illnesses.Count == 0 && (b.Plague?.Cases.Count ?? 0) == 0)) return [];
        var pt = st.Population.People; var rules = DiseaseRules.Current; var list = new List<IllnessView>();
        ulong hero = st.Party?.Hero.Value ?? 0; bool hasHero = st.Party != null;
        foreach (var t in b.Tracked?.Invoke() ?? [])
        {
            int row = pt.IndexOf(new Ravensguard.Sim.Core.EntityId(t.Id)); if (row < 0) continue;
            if (b.Illnesses.TryGetValue(t.Id, out var i))
            {
                var r = rules.Row(i.Kind);
                list.Add(new IllnessView(t.Id, pt.FullName(row), DiseaseRules.NameKey(i.Kind), day - i.StartDay + 1, Math.Max(1, r.CourseDays - (i.Cared ? r.CareShortensDays : 0)), i.Care,
                    RiskKeyOf(rules.DeathPpm(i.Kind, i.Care, b.Severity)), r.BedBound, hasHero && t.Id == hero, false));
            }
            else if (b.Plague?.Cases.TryGetValue(t.Id, out var c) == true)
            {
                var care = c.PhysicianUntil >= day ? IllnessCare.Physician : c.NursedUntil >= day ? IllnessCare.Nursed : IllnessCare.None;
                list.Add(new IllnessView(t.Id, pt.FullName(row), DiseaseRules.NameKey(IllnessKind.Plague), day - c.InfectedDay + 1, c.EndDay - c.InfectedDay, care,
                    RiskKeyOf(rules.DeathPpm(IllnessKind.Plague, care, b.Severity)), day - c.InfectedDay >= rules.S.IncubationDays, hasHero && t.Id == hero, true));
            }
        }
        return list;
    }

    public static ScarcityView ScarcityOf(DiseaseBook? b) => b == null ? new ScarcityView(1000, 1000, false, 0, "") : new ScarcityView(b.Scarcity.WageMultPm, b.Scarcity.LandMultPm, b.Plague?.Active ?? false, b.Plague?.Dead ?? 0,
        b.Plague != null ? ContinentPlague.PlagueNames[b.Plague.NameIndex] : "");
}
