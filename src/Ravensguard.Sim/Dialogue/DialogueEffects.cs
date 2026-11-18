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
    /// <summary>The kinds <see cref="StartContract"/> posts; anything else is refused (and content naming another is an error at load, <c>BaseVocabulary.ContractKinds</c>).</summary>
    public static readonly OddJobKind[] ContractKinds = [OddJobKind.Guard, OddJobKind.Haul, OddJobKind.Deliver, OddJobKind.TendShop, OddJobKind.FarmWork];

    /// <summary>
    /// <c>start_contract</c> with <c>kind</c> (guard, haul, deliver, tendshop, farmwork; default guard), <c>hours</c> (default 6), an optional <c>wage</c> in mites and, for a carry, <c>kg</c> (default 20).
    /// The poster is the speaker's household and the place is the speaker's home settlement. A haul carries a load of the household's own goods <see cref="HaulM"/> towards the nearest other
    /// settlement and a delivery takes it all the way there; either way the goods go back into the poster's store at the far end (conserved), and the pay is for the real distance. Returns the
    /// job, or null (an unknown kind, a household that cannot pay or has nothing to carry).
    /// </summary>
    public static Job? StartContract(WorldState st, ContentDatabase db, int speakerRow, IReadOnlyDictionary<string, string> args, long nowSec)
    {
        var e = st.Economy; var pop = st.Population; var pt = pop.People;
        if (e == null || speakerRow < 0 || speakerRow >= pt.Count || !pt.IsAlive(speakerRow)) return null;
        int hh = pt.Household[speakerRow], s = pt.HomeSettlement[speakerRow];
        if (hh < 0 || hh >= e.HouseholdCount || s < 0 || s >= pop.Region.Settlements.Count) return null;
        string A(string k) => args.TryGetValue(k, out var v) ? v : "";
        var env = new JobEnv { Ledger = e.Ledger, Econ = e, Pop = pop, Tuning = BehaviourContent.From(db).OddJobs };
        OddJobKind kind;
        if (A("kind").Length == 0) kind = OddJobKind.Guard;
        else if (!Enum.TryParse(A("kind"), true, out kind) || Array.IndexOf(ContractKinds, kind) < 0) return null;   // review finding 12: no quiet guard job for a kind it does not know
        long hours = long.TryParse(A("hours"), out var hv) ? Math.Clamp(hv, 1, 12) : 6;
        var rs = pop.Region.Settlements[s]; var table = st.Jobs ??= new JobTable();
        var poster = pop.Households.IdOf(hh);
        bool carry = kind is OddJobKind.Haul or OddJobKind.Deliver;
        long fromX = rs.CenterX * 1000L, fromY = rs.CenterY * 1000L, toX = fromX, toY = fromY, grams = 0; ushort good = 0;
        if (carry)
        {
            long kg = long.TryParse(A("kg"), out var kv) ? Math.Clamp(kv, 1, 60) : 20;
            if (Ravensguard.Sim.Onboarding.OnboardingSetup.Carryable(st, hh, kg * 1000) is not { } load) return null;
            (good, grams) = load;
            int other = NearestOther(pop.Region, s);
            if (other < 0) return null;
            var os = pop.Region.Settlements[other];
            long dx = (os.CenterX - rs.CenterX) * 1000L, dy = (os.CenterY - rs.CenterY) * 1000L, d = Ravensguard.Sim.Production.ProductionSystem.ISqrt(dx * dx + dy * dy);
            if (kind == OddJobKind.Haul && d > HaulM * 1000L) { dx = dx * HaulM * 1000L / d; dy = dy * HaulM * 1000L / d; }
            toX = fromX + dx; toY = fromY + dy;
        }
        long path = Ravensguard.Sim.Production.ProductionSystem.ISqrt((toX - fromX) * (toX - fromX) + (toY - fromY) * (toY - fromY));
        long wage = long.TryParse(A("wage"), out var wv) && wv > 0 ? wv
            : carry ? JobPay.Haul(env.Tuning, env.UnskilledWage, path, grams)
            : kind == OddJobKind.Guard ? JobPay.Guard(env.Tuning, env.UnskilledWage, hours) : JobPay.Labour(env.Tuning, env.UnskilledWage, hours);
        return table.Post(env, kind, poster, e.HouseholdPurse[hh], st.HomeRegion.Id, s, nowSec, nowSec + 2 * 86400L, wage, j =>
        {
            j.P0 = fromX; j.P1 = fromY; j.SrcOwner = poster; j.MinAge = 14; j.Slots = 1;
            if (carry) { j.P2 = toX; j.P3 = toY; j.P4 = good; j.P5 = grams; j.DstOwner = poster; }
            else j.P4 = hours;
            j.Title = kind switch { OddJobKind.Guard => "Stand watch", OddJobKind.TendShop => "Mind a stall", OddJobKind.Haul => "Carry a load", OddJobKind.Deliver => "Deliver goods", _ => "Field work" };
        });
    }

    /// <summary>How far a haul goes (metres): a load carried out of the settlement towards its nearest neighbour; a delivery goes all the way.</summary>
    public const int HaulM = 800;

    private static int NearestOther(Ravensguard.Sim.WorldGen.Region.Region r, int s)
    {
        int best = -1; long bd = long.MaxValue; var a = r.Settlements[s];
        for (int o = 0; o < r.Settlements.Count; o++)
        {
            if (o == s) continue;
            long dx = r.Settlements[o].CenterX - a.CenterX, dy = r.Settlements[o].CenterY - a.CenterY, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = o; }
        }
        return best;
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
