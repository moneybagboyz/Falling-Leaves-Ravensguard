using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Crafting;

/// <summary>A formal apprenticeship (crafting-full 4.6): the apprentice, the master, the trade's skill, the term's days and the recipes learnt so far.</summary>
public sealed class Apprenticeship
{
    public EntityId Id, Apprentice, Master;
    public Skill Trade;
    public int StartDay, EndDay;
    /// <summary>The hardest recipe the term teaches: <c>min(master - 10, apprentice + 30)</c> at the start.</summary>
    public int Cap;
    /// <summary>Days of the term worked so far (a day counts only when the apprentice was with the master), and the recipes learnt, in order.</summary>
    public int DaysWorked;
    public List<ushort> Learnt = [];
    public bool Ended;
}

public enum ApprenticeAnswer : byte { Ok, MasterTooUnskilled, MasterFull, CandidateTooUnskilled, AlreadyApprenticed, NotKnown }

/// <summary>
/// Apprenticeship (crafting-full 4.6, P5-E). "Take me as apprentice" needs a master of skill 40 or more who has fewer than 2 apprentices and a candidate whose skill is at least his minus 50. The term is
/// one season (15 days) living in his household, working his station 10 hours a day unpaid. Through the term the apprentice learns, at no fee, every recipe of the master's trade the master knows with
/// <c>difficulty &lt;= min(master - 10, apprentice + 30)</c>, one every 2 days in ascending difficulty (then id), with skill and proficiency gains doubled; guild secrets only between members.
/// Leaving early keeps what was learnt. NPC apprentices use the same calls, so a town's crafts improve by themselves.
/// </summary>
public static class Apprenticeships
{
    public static IEnumerable<Apprenticeship> Of(CraftState st, EntityId master) => st.Apprenticeships.Values.Where(a => !a.Ended && a.Master == master);
    public static Apprenticeship? As(CraftState st, EntityId apprentice) => st.Apprenticeships.Values.FirstOrDefault(a => !a.Ended && a.Apprentice == apprentice);

    public static ApprenticeAnswer Check(CraftContext w, EntityId apprentice, EntityId master, Skill trade)
    {
        var st = w.Runner.State; var set = w.Runner.Db.Settings;
        int ms = st.Book.Level(master, trade), cs = st.Book.Level(apprentice, trade);
        if (ms < set.ApprenticeMasterMinSkill) return ApprenticeAnswer.MasterTooUnskilled;
        if (Of(st, master).Count() >= set.ApprenticeMaxPerMaster) return ApprenticeAnswer.MasterFull;
        if (cs < ms - 50) return ApprenticeAnswer.CandidateTooUnskilled;
        if (As(st, apprentice) != null) return ApprenticeAnswer.AlreadyApprenticed;
        return ApprenticeAnswer.Ok;
    }

    public static (ApprenticeAnswer, Apprenticeship?) Start(CraftContext w, EntityId apprentice, EntityId master, Skill trade, int day, IdAllocator ids)
    {
        var a = Check(w, apprentice, master, trade);
        if (a != ApprenticeAnswer.Ok) return (a, null);
        var set = w.Runner.Db.Settings;
        var ap = new Apprenticeship { Id = ids.Allocate(IdKind.Other), Apprentice = apprentice, Master = master, Trade = trade, StartDay = day, EndDay = day + set.ApprenticeTermDays,
            Cap = Math.Min(w.Runner.State.Book.Level(master, trade) - 10, w.Runner.State.Book.Level(apprentice, trade) + 30) };
        w.Runner.State.Apprenticeships[ap.Id] = ap;
        return (ApprenticeAnswer.Ok, ap);
    }

    public static void Leave(CraftState st, Apprenticeship a) => a.Ended = true;

    /// <summary>The next recipe the master would teach: of his trade, known to him, teachable by a master, not yet known to the apprentice, under the cap, ascending difficulty then id.</summary>
    public static CraftRecipe? Next(CraftContext w, Apprenticeship a)
    {
        var st = w.Runner.State;
        int cap = a.Cap;
        CraftRecipe? best = null;
        foreach (var r in w.Runner.Db.Recipes)
        {
            if (r.Skill != a.Trade || !r.MasterTeaches || r.IsPractice || r.Difficulty > cap) continue;
            if (st.Know.Knows(r, a.Apprentice, st.Book) || !Commissions.MakerKnows(w, r, a.Master)) continue;
            if (r.Guild != null && !(Guilds.OfGuild(w, r.Guild, a.Master) && Guilds.IsMember(st, r.Guild, a.Apprentice))) continue;
            if (best == null || r.Difficulty < best.Difficulty || r.Difficulty == best.Difficulty && string.CompareOrdinal(r.Id, best.Id) < 0) best = r;
        }
        return best;
    }

    /// <summary>
    /// One day of every term (the H4 daily slice): an apprentice with his master that day works 10 hours (skill used at double rate on the next recipe's work) and every second day worked learns the
    /// next recipe. A term past its last day ends. Returns (apprenticeship, recipe learnt) for each recipe learnt, in id order.
    /// </summary>
    public static List<(Apprenticeship A, CraftRecipe R)> Day(CraftContext w, int day, Func<Apprenticeship, bool> present)
    {
        var st = w.Runner.State; var set = w.Runner.Db.Settings; var learnt = new List<(Apprenticeship, CraftRecipe)>();
        foreach (var a in st.Apprenticeships.Values)
        {
            if (a.Ended) continue;
            if (day >= a.EndDay) { a.Ended = true; continue; }
            if (day <= a.StartDay || !present(a)) continue;
            a.DaysWorked++;
            var next = Next(w, a);
            if (next != null)
                for (int h = 0; h < set.ApprenticeHoursPerDay; h++)
                    st.Book.Use(a.Apprentice, a.Trade, CraftMath.UseWeight(next.Difficulty), next.Difficulty, ((long)day * 24 + set.MakerDayStartHour + h) * 3600, 0, masterTeaching: true);
            if (a.DaysWorked % set.ApprenticeDaysPerRecipe != 0) continue;
            next = Next(w, a);
            if (next == null) continue;
            st.Know.Learn(a.Apprentice, next.Index); a.Learnt.Add(next.Index);
            foreach (var u in next.Profs) st.Know.Practise(w.Runner.Db, a.Apprentice, u.Prof, set.ApprenticeHoursPerDay * 3600L * set.ApprenticeDaysPerRecipe, w.Runner.Db.Prof(u.Prof).TeachMultPm);
            learnt.Add((a, next));
        }
        return learnt;
    }
}
