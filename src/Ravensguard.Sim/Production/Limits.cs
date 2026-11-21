using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Production;

/// <summary>What a guild's caps say of one member's shop (economies-of-scale 5.3): the journeymen and apprentices over the caps and the stations of one kind over the cap.</summary>
public readonly record struct GuildOver(int Journeymen, int Apprentices, int Stations)
{
    public int Total => Journeymen + Apprentices + Stations;
}

/// <summary>One shop of a guild's trade in a guild town (derived, never saved).</summary>
public sealed record GuildShop(GuildDef Guild, int Ordinal, int Settlement, Business Business, GuildOver Over);

/// <summary>
/// The limits of size that are rules rather than hourly arithmetic (P7-ES1, economies-of-scale 5.3): which towns hold a guild, who its master is, how far a shop is over the guild's caps, and the terms of a
/// manufactory licence petition. Pure over the production system and the people; the seasonal inspection and the petition itself are <see cref="ScaleWorld"/>'s.
/// </summary>
public static class Limits
{
    /// <summary>The guilds of the content in id order (a guild's ordinal keys the inspection's draw).</summary>
    public static List<GuildDef> Guilds(ContentDatabase db) => [.. db.All<GuildDef>().OrderBy(g => g.Id, StringComparer.Ordinal)];

    private static bool IsOfTrade(ProductionSystem prod, GuildDef g, Business b) => !prod.KindOf(b).Trader && g.Trades.Contains(prod.KindOf(b).OwnerOccupation.ToString());

    /// <summary>The open shops of a guild's trades in a settlement, ascending by index.</summary>
    public static List<Business> TradeShops(ProductionSystem prod, GuildDef g, int settlement)
        => [.. prod.Businesses.Where(b => b.Settlement == settlement && b.State == BusinessState.Open && IsOfTrade(prod, g, b))];

    /// <summary>Whether the settlement holds the guild: a town with at least three open shops of its trades (as Phase 5's <c>Guilds.Of</c>).</summary>
    public static bool HasGuild(ProductionSystem prod, GuildDef g, int settlement)
        => (uint)settlement < (uint)prod.Pop.Region.Settlements.Count && prod.Pop.Region.Settlements[settlement].Class == SettlementClass.Town && TradeShops(prod, g, settlement).Count >= 3;

    /// <summary>The guild's master in a town: the most skilled person of its trades working in its shops (owners and staff), ties by the lower person index; -1 for none.</summary>
    public static int MasterOf(ProductionSystem prod, GuildDef g, int settlement)
    {
        Enum.TryParse(g.Skill, out Skill sk); var pt = prod.Pop.People; int best = -1, who = -1;
        foreach (var b in TradeShops(prod, g, settlement))
        {
            if (b.Owner >= 0 && b.OwnerWorks) Consider(b.Owner);
            foreach (int p in b.Staff) Consider(p);
        }
        return who;
        void Consider(int p) { int lv = pt.SkillLevelOf(p, sk); if (lv > best || lv == best && p < who) { best = lv; who = p; } }
    }

    /// <summary>
    /// How far a shop stands over its guild's caps (5.3, ES-12): journeymen (staff of the trade's skill from the journeyman's up to the master's, adults) over <c>max_journeymen</c>, apprentices (under 16)
    /// over <c>max_apprentices</c>, and the most stations of one kind over <c>max_stations</c>.
    /// </summary>
    public static GuildOver OverOf(ProductionSystem prod, GuildDef g, Business b, int day)
    {
        var pt = prod.Pop.People; var ss = prod.Content.Scale; Enum.TryParse(g.Skill, out Skill sk);
        int jm = 0, ap = 0;
        foreach (int p in b.Staff)
        {
            if (pt.AgeYears(p, day) < 16) { ap++; continue; }
            int lv = pt.SkillLevelOf(p, sk);
            if (lv >= ss.JourneymanSkill && lv < ss.MasterSkill) jm++;
        }
        int maxKind = 0; foreach (var grp in b.Stations.GroupBy(s => s.Kind)) maxKind = Math.Max(maxKind, grp.Count());
        return new GuildOver(Math.Max(0, jm - g.MaxJourneymen), Math.Max(0, ap - g.MaxApprentices), Math.Max(0, maxKind - g.MaxStations));
    }

    /// <summary>Every shop of every guild town, in guild id, settlement, business order, with how far it is over the caps.</summary>
    public static List<GuildShop> Shops(ProductionSystem prod, ContentDatabase db, int day)
    {
        var res = new List<GuildShop>(); var guilds = Guilds(db);
        for (int gi = 0; gi < guilds.Count; gi++)
            for (int s = 0; s < prod.Pop.Region.Settlements.Count; s++)
            {
                if (!HasGuild(prod, guilds[gi], s)) continue;
                foreach (var b in TradeShops(prod, guilds[gi], s)) res.Add(new GuildShop(guilds[gi], gi, s, b, OverOf(prod, guilds[gi], b, day)));
            }
        return res;
    }

    /// <summary>
    /// The terms of a manufactory licence petition (5.3, ES-13): the charter rule's sum (the lord's opinion of the petitioner, his standing over ten, five a ten crowns of the fee; <see cref="Ravensguard.Sim.Rule.RuleMath.CharterScore"/>)
    /// plus the guild master's opinion of the petitioner and -20 for every licence already granted in the trade in the town. Granted at the charter threshold; a refusal names the largest negative term
    /// ("ld_licence_refused_opinion", "_standing", "_guild_master", "_licences", else "_fee").
    /// </summary>
    public static (int Score, bool Granted, string RefusalKey) LicenceTerms(RuleSettingsDef rs, ScaleSettingsDef ss, int opinion, int standing, long feeMites, int guildMasterOpinion, int licencesGranted)
    {
        var (baseScore, _) = Ravensguard.Sim.Rule.RuleMath.CharterScore(rs, opinion, standing, feeMites, false, false);
        int perGranted = -ss.LicencePerGrantedPenalty * licencesGranted;
        int score = baseScore + guildMasterOpinion + perGranted;
        if (score >= rs.CharterGrantThreshold) return (score, true, "");
        string key = "ld_licence_refused_fee"; int worst = 0;
        void T(int v, string k) { if (v < worst) { worst = v; key = k; } }
        T(opinion, "ld_licence_refused_opinion"); T(standing / 10, "ld_licence_refused_standing"); T(guildMasterOpinion, "ld_licence_refused_guild_master"); T(perGranted, "ld_licence_refused_licences");
        return (score, false, key);
    }
}
