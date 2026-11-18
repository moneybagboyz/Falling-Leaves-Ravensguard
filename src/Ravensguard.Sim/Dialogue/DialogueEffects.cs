using Ravensguard.Content;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Dialogue;

/// <summary>
/// The dialogue effects that change the world (Phase 3 cleanup B, item 9), applied at a barrier: <c>start_contract</c> posts a real odd job from the speaker's household (the wage moves into escrow on the
/// ledger, the job is on the board for the hero to claim) and <c>give_item</c> moves goods between the speaker's household and the hero's pack with <see cref="Store.MoveUpTo"/>, so nothing is made or lost.
/// Both return false and change nothing when they cannot be done (the household cannot pay, has none of the item).
/// </summary>
public static class DialogueEffects
{
    /// <summary>
    /// <c>start_contract</c> with <c>kind</c> (guard, haul, tendshop, farmwork, deliver ...; default guard), <c>hours</c> (default 6) and an optional <c>wage</c> in mites. The poster is the speaker's household and the place is
    /// the speaker's home settlement. Returns the job, or null.
    /// </summary>
    public static Job? StartContract(WorldState st, ContentDatabase db, int speakerRow, IReadOnlyDictionary<string, string> args, long nowSec)
    {
        var e = st.Economy; var pop = st.Population; var pt = pop.People;
        if (e == null || speakerRow < 0 || speakerRow >= pt.Count || !pt.IsAlive(speakerRow)) return null;
        int hh = pt.Household[speakerRow], s = pt.HomeSettlement[speakerRow];
        if (hh < 0 || hh >= e.HouseholdCount || s < 0 || s >= pop.Region.Settlements.Count) return null;
        string A(string k) => args.TryGetValue(k, out var v) ? v : "";
        var env = new JobEnv { Ledger = e.Ledger, Econ = e, Pop = pop, Tuning = BehaviourContent.From(db).OddJobs };
        var kind = Enum.TryParse<OddJobKind>(A("kind"), true, out var k) && k is OddJobKind.Guard or OddJobKind.TendShop or OddJobKind.FarmWork ? k : OddJobKind.Guard;
        long hours = long.TryParse(A("hours"), out var hv) ? Math.Clamp(hv, 1, 12) : 6;
        long wage = long.TryParse(A("wage"), out var wv) && wv > 0 ? wv : kind == OddJobKind.Guard ? JobPay.Guard(env.Tuning, env.UnskilledWage, hours) : JobPay.Labour(env.Tuning, env.UnskilledWage, hours);
        var rs = pop.Region.Settlements[s]; var table = st.Jobs ??= new JobTable();
        var poster = pop.Households.IdOf(hh);
        return table.Post(env, kind, poster, e.HouseholdPurse[hh], st.HomeRegion.Id, s, nowSec, nowSec + 2 * 86400L, wage, j =>
        {
            j.P0 = rs.CenterX * 1000L; j.P1 = rs.CenterY * 1000L; j.P4 = hours; j.SrcOwner = poster; j.MinAge = 14; j.Slots = 1;
            j.Title = kind switch { OddJobKind.Guard => "Stand watch", OddJobKind.TendShop => "Mind a stall", _ => "Field work" };
        });
    }

    /// <summary>
    /// <c>give_item</c>: <c>item</c> is a good id, <c>count</c> pieces (or kilograms of a mass good, default 1). The speaker's household gives it to the hero's pack; with <c>to=speaker</c> the hero gives it to them. At most what the
    /// giver holds moves. Returns the quantity moved (0: nothing).
    /// </summary>
    public static long GiveItem(WorldState st, ContentDatabase db, int speakerRow, IReadOnlyDictionary<string, string> args)
    {
        var e = st.Economy; var party = st.Party; var pt = st.Population.People;
        if (e == null || party == null || speakerRow < 0 || speakerRow >= pt.Count) return 0;
        int hh = pt.Household[speakerRow]; if (hh < 0 || hh >= e.HouseholdCount) return 0;
        string A(string k) => args.TryGetValue(k, out var v) ? v : "";
        if (!e.Goods.TryIdOf(A("item"), out ushort g)) return 0;
        long n = long.TryParse(A("count"), out var c) ? Math.Max(1, c) : 1; long qty = e.Goods.IsPiece(g) ? n : n * 1000;
        var house = e.HouseholdStore[hh]; var pack = party.Carried.Pack(e);
        bool toSpeaker = A("to") == "speaker";
        long moved = toSpeaker ? Store.MoveUpTo(pack, house, g, qty) : Store.MoveUpTo(house, pack, g, qty);
        if (moved > 0 && !toSpeaker) party.Carried.Stow(e, new ContainerRules(db, e.Goods), g);
        return moved;
    }
}
