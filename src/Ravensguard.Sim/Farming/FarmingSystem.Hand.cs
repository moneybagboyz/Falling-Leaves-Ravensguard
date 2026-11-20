using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Farming;

// P6-B6 (holdings 3.1): the hero's household as a field holder. A cleared acre of a claim is ploughed and sown by hand from the hero's own seed, in the crop's sowing window; from then on it is
// one of the holder household's parcels like any other (the labour planner weeds and reaps it, the harvest goes to the holder's barn).
public sealed partial class FarmingSystem
{
    /// <summary>The kind of sowing the day of the year allows: spring grain and legumes (true), winter grain (false), or none (null).</summary>
    public bool? SowingSeasonAt(int doy)
    {
        var sp = Settings.Info(Crop.Barley); var sw = Settings.Info(Crop.Wheat);
        if (doy >= sp.SowFirstDoy && doy <= _d.SpringWorkLastDoy) return true;
        if (doy >= sw.SowFirstDoy && doy <= sw.SowLastDoy) return false;
        return null;
    }

    /// <summary>The household holding a parcel (-1 none).</summary>
    public int HolderOf(int parcel) => (uint)parcel < (uint)P.Count ? P.Holder[parcel] : -1;

    /// <summary>
    /// Ploughs and sows every unsown field of <paramref name="parcel"/> by hand: the crop of the day's window that the soil bears (spring: barley, oats, peas, beans; winter: wheat, rye), its seed
    /// (<see cref="CropInfo.SeedG"/> an acre) taken from <paramref name="seed"/>. The parcel takes the slot of the sowing (1 spring, 2 autumn), so the rotation carries it on, and the ploughing and
    /// sowing tasks of its year read as done. Returns null when sown, else the refusal key.
    /// </summary>
    public string? SowByHand(int parcel, Store seed, int doy, out Crop sown)
    {
        sown = Crop.Fallow;
        if ((uint)parcel >= (uint)P.Count) return "hw_sow_no_field";
        if (SowingSeasonAt(doy) is not bool spring) return "hw_sow_not_now";
        var pc = Region.Parcels[parcel]; int gm = _d.GramsPerMeasure;
        var pool = spring ? FarmingSettings.SpringCrops : FarmingSettings.WinterCrops;
        string? why = "hw_sow_poor_soil";
        for (int k = 0; k < pc.FieldCount; k++)
        {
            int f = pc.FieldStart + k;
            if (F.Crop[f] != Crop.Fallow || F.Stage[f] is FieldStage.Sown or FieldStage.Growing) continue;
            why = "hw_sow_poor_soil";
            foreach (var c in pool)
            {
                var info = Settings.Info(c); int fert = F.Fertility[f];
                if (fert < info.MinFertility) continue;
                if (!spring && c == Crop.Wheat && fert < _d.WheatMinFertilityForWinterPick) continue;
                long g = info.SeedG(gm);
                if (seed.Qty(info.Good) < g) { why = "hw_sow_no_seed"; continue; }
                seed.Destroy(info.Good, g, DestroyReason.Sown);
                if (!spring) CreditFallow(f);
                P.Slot[parcel] = (byte)(spring ? 1 : 2); F.Slot[f] = P.Slot[parcel];
                F.Crop[f] = c; F.Stage[f] = FieldStage.Sown; F.SownDay[f] = (short)doy; F.SownFertility[f] = (byte)fert; F.SeedG[f] = (int)g; F.DamagePermille[f] = 0; F.YieldG[f] = 0;
                F.Clear(f, FieldFlags.Resown); F.Clear(f, FieldFlags.Lost);
                State.SeedSownG += g; State.SownAcres++;
                sown = c; why = null;
                break;
            }
            if (why != null) return why;
        }
        if (why == null)
        {
            int plough = spring ? (int)FarmTask.PloughSpring : (int)FarmTask.PloughFallow, sow = spring ? (int)FarmTask.SowSpring : (int)FarmTask.SowWinter;
            P.ProgressPpm[parcel * ParcelWork.ProgressTasks + plough] = 1_000_000; P.ProgressPpm[parcel * ParcelWork.ProgressTasks + sow] = 1_000_000;
        }
        return why;
    }
}
