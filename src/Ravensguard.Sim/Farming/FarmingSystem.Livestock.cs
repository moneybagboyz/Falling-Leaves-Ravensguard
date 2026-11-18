using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Farming;

public sealed partial class FarmingSystem
{
    /// <summary>S2 of summer: a settlement grazes its fallow when sheep reach 0.4 per fallow acre; grazed fallow later gains +15 fertility instead of +5.</summary>
    private void SetGrazing()
    {
        var lv = State.Livestock;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            int fallow = SlotAcres(s, 2);
            lv.Grazed[s] = (long)lv.Sheep[s] * 100 >= (long)_d.GrazingSheepPerFallowAcreX100 * fallow;
            foreach (int p in Region.Settlements[s].Parcels)
            {
                if (P.Slot[p] != 2) continue;
                var parcel = Region.Parcels[p];
                for (int k = 0; k < parcel.FieldCount; k++) { int f = parcel.FieldStart + k; if (lv.Grazed[s]) F.Set(f, FieldFlags.Grazed); else F.Clear(f, FieldFlags.Grazed); }
            }
        }
    }

    /// <summary>Kilograms times ten of hay the herds eat in one winter day.</summary>
    private long DailyNeedKgX10(int s) => (long)State.Livestock.Sheep[s] * _d.SheepHayKgPerHeadDayX10 + (long)State.Livestock.Oxen[s] * _d.OxHayKgPerHeadDayX10;

    /// <summary>S3 at autumn end (resources spec 3.3): sheep are culled when hay (plus half the straw) cannot carry the winter; oxen are kept.</summary>
    private void CullHerds(GameTime now)
    {
        var lv = State.Livestock;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            long needed = DailyNeedKgX10(s) * 100 * _d.WinterFodderDays;                    // grams: kg x10 -> g is x100
            var fod = Fodder(s);
            long have = fod.Qty(_hay) + fod.Qty(_straw) / 2;
            int permille = YieldMath.CullPermille(have, needed, _d.CullMaxPermille);
            if (permille <= 0) continue;
            int cull = lv.Sheep[s] * permille / 1000;
            if (cull <= 0) continue;
            lv.Sheep[s] -= cull;
            Chronicle.Add(new ChronicleEntry(NewEvent(), now, "farm.cull", [Region.Settlements[s].Id], [new Cause(CauseKind.Weather, null, "short of fodder: sheep slaughtered before winter")],
                new Dictionary<string, string> { ["settlement"] = Region.Settlements[s].Name, ["culled"] = cull.ToString(), ["permille"] = permille.ToString() }));
        }
    }

    /// <summary>Winter feeding (autumn end to year end): hay first, then straw at two grams per gram of hay.</summary>
    private void FeedHerds()
    {
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            long need = DailyNeedKgX10(s) * 100; if (need <= 0) continue;
            var fod = Fodder(s);
            long ate = fod.DestroyUpTo(_hay, need, DestroyReason.Consumed);
            long rest = need - ate;
            if (rest > 0) fod.DestroyUpTo(_straw, rest * 2, DestroyReason.Consumed);
        }
    }
}
