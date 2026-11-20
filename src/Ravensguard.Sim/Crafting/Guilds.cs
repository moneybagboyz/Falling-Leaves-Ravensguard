using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Crafting;

/// <summary>A guild of a town (derived, never saved): its template, the town, and its master (the most skilled of its trades there, ties by person id).</summary>
public sealed record GuildInstance(GuildDef Def, int Settlement, EntityId Master);

/// <summary>A member of a guild: since when, the last season paid and the seasons owed.</summary>
public sealed class GuildMember
{
    public string Guild = "";
    public int Settlement;
    public EntityId Person;
    public int JoinedDay, PaidSeason, Arrears;
}

/// <summary>A guild offence (crafting-full 4.8): a secret taught to an outsider, a secret taken apart, a manuscript of secrets leaving the guild; with its witnesses. Phase 6 enforces them.</summary>
public sealed record GuildOffence(EntityId Id, string Guild, int Settlement, EntityId Offender, string Kind, ushort Recipe, long AtSec, EntityId[] Witnesses);

public enum GuildAnswer : byte { Ok, NoGuild, AlreadyMember, NotResident, TooUnskilled, CannotAfford, NotMember }

/// <summary>
/// Guilds and their secrets (crafting-full 3.4, 4.8, P5-E). A guild exists in each chartered town with at least three businesses of its trades, generated without a draw from the world as it stands
/// (<see cref="Of"/>); its master is the most skilled person of its trades there. Its secrets (<c>secret: "guild:&lt;id&gt;"</c>) are taught only by members, to members. Joining needs 15 days'
/// residence, the guild's minimum skill and the entry fee (paid to the master's purse, the guild chest); dues each season (15 days), and two seasons in arrears expel. A member teaching a secret to an
/// outsider, and taking a secret item apart where someone sees, are recorded as offences with their witnesses.
/// </summary>
public static class Guilds
{
    public const int SeasonDays = 15, ExpelArrears = 2;

    /// <summary>The guilds of a settlement, by template id.</summary>
    public static List<GuildInstance> Of(CraftContext w, int settlement)
    {
        var list = new List<GuildInstance>();
        if (settlement < 0 || !w.IsTown(settlement)) return list;
        foreach (var g in w.Runner.Db.Guilds.Values)
        {
            int n = 0; foreach (var t in g.Trades) n += w.BusinessesOf(settlement, t);
            if (n < 3) continue;
            Enum.TryParse(g.Skill, out Skill sk);
            EntityId master = EntityId.None; int best = -1;
            foreach (var p in w.PeopleOf(settlement))
            {
                if (!g.Trades.Contains(w.TradeOf(p))) continue;
                int lv = w.Runner.State.Book.Level(p, sk);
                if (lv > best) { best = lv; master = p; }
            }
            if (master != EntityId.None) list.Add(new GuildInstance(g, settlement, master));
        }
        return list;
    }

    public static GuildInstance? Find(CraftContext w, string guild, int settlement) => Of(w, settlement).FirstOrDefault(g => g.Def.Id == guild);

    public static bool IsMember(CraftState st, string guild, EntityId person)
        => st.Members.Values.Any(m => m.Guild == guild && m.Person == person);

    /// <summary>Whether a person counts as of the guild: a member, or (an NPC) a person of its trades living in a town that has it.</summary>
    public static bool OfGuild(CraftContext w, string guild, EntityId person)
    {
        if (IsMember(w.Runner.State, guild, person)) return true;
        if (!w.Runner.Db.Guilds.TryGetValue(guild, out var g) || !g.Trades.Contains(w.TradeOf(person))) return false;
        return Find(w, guild, w.SettlementOf(person)) != null;
    }

    /// <summary>Days a person has lived in a settlement (residence is recorded by <see cref="SeeAt"/>, the hour pass's "where is he").</summary>
    public static int ResidentDays(CraftState st, EntityId person, int settlement, int day)
        => st.Residence.TryGetValue(person.Value, out var r) && r.Settlement == settlement ? day - r.SinceDay : 0;

    /// <summary>Records where a person is today: a different settlement restarts his residence there.</summary>
    public static void SeeAt(CraftState st, EntityId person, int settlement, int day)
    {
        if (settlement < 0) return;
        if (!st.Residence.TryGetValue(person.Value, out var r) || r.Settlement != settlement) st.Residence[person.Value] = (settlement, day);
    }

    /// <summary>Joins (a barrier command): residence, skill and the entry fee to the guild master's purse.</summary>
    public static GuildAnswer Join(CraftContext w, EntityId person, string guild, int settlement, int day)
    {
        var gi = Find(w, guild, settlement);
        if (gi == null) return GuildAnswer.NoGuild;
        var st = w.Runner.State;
        if (IsMember(st, guild, person)) return GuildAnswer.AlreadyMember;
        if (ResidentDays(st, person, settlement, day) < gi.Def.ResidenceDays) return GuildAnswer.NotResident;
        Enum.TryParse(gi.Def.Skill, out Skill sk);
        if (st.Book.Level(person, sk) < gi.Def.MinSkill) return GuildAnswer.TooUnskilled;
        if (w.PurseOf(person) is not { } pp || w.PurseOf(gi.Master) is not { } mp || w.Ledger.Balance(pp).Mites < gi.Def.EntryFeeMites) return GuildAnswer.CannotAfford;
        if (gi.Def.EntryFeeMites > 0 && !w.Ledger.Transfer(pp, mp, new Coin(gi.Def.EntryFeeMites), TransferReason.Trade)) return GuildAnswer.CannotAfford;
        st.Members[(guild, settlement, person.Value)] = new GuildMember { Guild = guild, Settlement = settlement, Person = person, JoinedDay = day, PaidSeason = day / SeasonDays };
        return GuildAnswer.Ok;
    }

    /// <summary>The seasonal dues (H4 slice, on a season's first day): each member pays the master; two seasons owed and he is out. Returns the expelled.</summary>
    public static List<GuildMember> Dues(CraftContext w, int day)
    {
        var expelled = new List<GuildMember>();
        if (day % SeasonDays != 0) return expelled;
        int season = day / SeasonDays; var st = w.Runner.State;
        foreach (var (key, m) in st.Members.ToList())
        {
            if (m.PaidSeason >= season) continue;
            var gi = Find(w, m.Guild, m.Settlement);
            long dues = w.Runner.Db.Guilds.TryGetValue(m.Guild, out var g) ? g.DuesMitesPerSeason : 0;
            bool paid = gi != null && w.PurseOf(m.Person) is { } pp && w.PurseOf(gi.Master) is { } mp && w.Ledger.Balance(pp).Mites >= dues
                && (dues == 0 || w.Ledger.Transfer(pp, mp, new Coin(dues), TransferReason.Trade));
            m.PaidSeason = season;
            if (paid) { m.Arrears = 0; continue; }
            if (++m.Arrears >= ExpelArrears) { st.Members.Remove(key); expelled.Add(m); }
        }
        return expelled;
    }

    /// <summary>
    /// Whether a master may teach a student a recipe (4.8): anything not secret; a secret only from one of the guild to a member. An NPC of the guild refuses an outsider; the hero, a member, may
    /// teach one, and that is an offence (<paramref name="offence"/> set: record it with <see cref="Record"/>).
    /// </summary>
    public static bool MayTeach(CraftContext w, CraftRecipe r, EntityId master, EntityId student, EntityId hero, out bool offence)
    {
        offence = false;
        if (r.Guild == null) return true;
        if (!OfGuild(w, r.Guild, master)) return false;
        if (IsMember(w.Runner.State, r.Guild, student)) return true;
        if (master != hero) return false;
        offence = true;
        return true;
    }

    /// <summary>Writes an offence with the witnesses at the place, and its chronicle line (cause: the offender).</summary>
    public static GuildOffence Record(CraftContext w, string guild, int settlement, EntityId offender, string kind, ushort recipe, long nowSec, IdAllocator ids, Chronicle? chronicle)
    {
        var witnesses = w.WitnessesOf(settlement, offender).Where(p => p != offender).OrderBy(p => p).ToArray();
        var o = new GuildOffence(ids.Allocate(IdKind.Event), guild, settlement, offender, kind, recipe, nowSec, witnesses);
        w.Runner.State.Offences.Add(o);
        chronicle?.Add(new ChronicleEntry(o.Id, new GameTime(nowSec), "guild_offence", [offender, .. witnesses], [new Cause(CauseKind.Crime, null, offender.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            new Dictionary<string, string> { ["guild"] = guild, ["kind"] = kind, ["recipe"] = w.Runner.Db[recipe].Id }));
        return o;
    }
}
