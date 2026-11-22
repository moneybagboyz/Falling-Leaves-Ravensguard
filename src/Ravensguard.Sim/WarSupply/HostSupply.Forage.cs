using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WorldGen.Region;
using SeasonKind = Ravensguard.Sim.Core.Season;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// Foraging (army-economy 4.2, P7-AE2), ladder step 7: the host's foragers (a tenth of its men) bring in up to 30 kg of staple a day each, times 0.8 to 1.2 (stream 0x750, key the host's place and the day), from within 4 km of the camp:
/// the barns and household stores of the settlements there (on enemy land plunder, every household robbed thinks 40 the worse of the host's realm; on own land, with the policy <see cref="ForagePolicy.OwnLandPaid"/>, purveyance paid by
/// tallies), then the standing crops: in autumn the ripe fields are cut roughly (half of an acre's yield taken, the other half trampled and lost to farming), in summer green crops are cut for the horses' hay (a tonne an acre, the crop
/// lost), in winter only stores. The grain moves from real stores or is cut from real standing crops; the land loses the acres; <c>army_foraged</c> is written for each settlement and day.
/// </summary>
public sealed partial class HostSupply
{
    /// <summary>Test hook, not saved: a fixed draw in per mille (800 to 1,200) for the foragers' yield; null draws the stream.</summary>
    public int? ForageDrawOverride { get; set; }

    private sealed class ForageDay { public long CapG; }
    private readonly Dictionary<int, ForageDay> _forageDay = [];

    /// <summary>The foragers' grams still to bring in today (the draw is made once a host-day).</summary>
    private long ForageCap(HostAccount a, int day)
    {
        if (!_forageDay.TryGetValue(a.Id, out var fd))
        {
            int draw = ForageDrawOverride ?? DetRng.Range(Economy.World.Seed, day, (ulong)Math.Max(0, a.PlaceSettlement) + 1, RngStreams.ForageYield, 0, 800, 1201);
            long foragers = (long)a.Total * SupplyConfig.ForageSharePm / 1000;
            _forageDay[a.Id] = fd = new ForageDay { CapG = foragers * SupplyConfig.ForageGramsEach * draw / 1000 };
        }
        return fd.CapG;
    }

    private void SpendForage(HostAccount a, long g) { if (_forageDay.TryGetValue(a.Id, out var fd)) fd.CapG = Math.Max(0, fd.CapG - g); }

    /// <summary>
    /// Ladder step 7: foraging for the staple (stores, then ripe fields in autumn) or the hay (green crops in summer). Once the baggage cannot feed the day the foragers go out and bring in all they can carry (their day's capacity, not only
    /// today's meal): the host eats what it needs and the rest stays in the carts. Returns what is still wanted.
    /// </summary>
    private long Forage(HostAccount a, Rt rt, DayResult res, SupplyNeed n, ushort[] goods, long left, int day)
    {
        if (a.Forage == ForagePolicy.None || a.Abstract || left <= 0 || a.Total <= 0) return left;
        var season = GameTime.FromGameHours((long)day * 24).Season;
        if (n != SupplyNeed.Staple && n != SupplyNeed.Hay) return left;
        if (n == SupplyNeed.Hay && (season != SeasonKind.Summer || _hay == ushort.MaxValue)) return left;
        bool plunder = a.Forage == ForagePolicy.EnemyLand; long want = left;
        var region = _pop.Region; var farmSys = Economy.Farming; var farming = farmSys.State; var F = farming.Fields; var P = farming.Parcels;
        foreach (int s in SettlementsWithin(a, SupplyConfig.ForageRadiusM))
        {
            if (ForageCap(a, day) <= 0) break;
            bool ownLand = a.Realm >= 0 && RealmOf(s) == a.Realm;
            if (plunder && ownLand) continue;                                 // enemy land only
            if (!plunder && a.Realm >= 0 && !ownLand) continue;
            long takenEq = 0, trampled = 0; int acres = 0; var robbed = new SortedSet<int>(); Taken? st = null;
            if (n == SupplyNeed.Staple)
            {
                st = TakeGrain(a, rt, s, ForageCap(a, day), day, plunder ? TakeMode.ForagePlunder : TakeMode.ForagePaid);
                takenEq += st.Eq; SpendForage(a, st.Eq);
                foreach (int h in st.Households) robbed.Add(h);
                if (season == SeasonKind.Autumn)
                {
                    foreach (int pi in region.Settlements[s].Parcels)
                    {
                        if (ForageCap(a, day) <= 0) break;
                        var parcel = region.Parcels[pi];
                        for (int f = parcel.FieldStart; f < parcel.FieldStart + parcel.FieldCount && ForageCap(a, day) > 0; f++)
                        {
                            if (F.Stage[f] != FieldStage.Ripe || F.Crop[f] == Crop.Fallow || F.YieldG[f] <= 1) continue;
                            long take = Math.Min(F.YieldG[f] / 2, ForageCap(a, day)); if (take <= 0) continue;
                            long loss = Math.Min(F.YieldG[f], take * 2);
                            var good = farmSys.Settings.Info(F.Crop[f]).Good;
                            rt.Baggage.Create(good, take, CreateReason.Harvest);
                            F.YieldG[f] -= (int)loss; farming.AddLoss(LossCause.Trampling, loss, 1);
                            trampled += loss - take; acres++; takenEq += EqG(good, take); SpendForage(a, take);
                            int holder = P.Holder[F.Parcel[f]]; if (holder >= 0) robbed.Add(holder);
                        }
                    }
                }
                if (takenEq > 0) left -= EatForaged(rt, res, goods, Math.Min(left, takenEq));
            }
            else
            {
                // green crops cut as fodder in summer: a tonne an acre of hay, the crop lost
                foreach (int pi in region.Settlements[s].Parcels)
                {
                    if (ForageCap(a, day) <= 0) break;
                    var parcel = region.Parcels[pi];
                    for (int f = parcel.FieldStart; f < parcel.FieldStart + parcel.FieldCount && ForageCap(a, day) > 0; f++)
                    {
                        if (F.Crop[f] == Crop.Fallow || F.Stage[f] is not (FieldStage.Sown or FieldStage.Growing) || F.DamagePermille[f] >= 1000) continue;
                        long take = Math.Min(1_000_000, ForageCap(a, day));
                        rt.Baggage.Create(_hay, take, CreateReason.Cut);
                        farmSys.AddDamage(f, 1000, LossCause.Trampling);
                        takenEq += take; acres++; SpendForage(a, take);
                        int holder = P.Holder[F.Parcel[f]]; if (holder >= 0) robbed.Add(holder);
                    }
                }
                long eat = Math.Min(left, takenEq);
                if (eat > 0 && rt.Baggage.Destroy(_hay, eat, DestroyReason.Eaten)) { res.Delivered[(int)SupplyNeed.Hay, (int)SupplySource.Forage] += eat; left -= eat; }
            }
            if (takenEq <= 0) continue;
            Book.Stats[6] += takenEq; Book.Stats[7] += trampled; Book.Stats[8] += acres;
            NoteForage(a, res, s, takenEq, trampled, acres, robbed, plunder, st, day);
        }
        _ = want;
        return left;
    }

    /// <summary>Eats the grain the foragers brought: counted as forage.</summary>
    private long EatForaged(Rt rt, DayResult res, ushort[] goods, long eq)
    {
        long left = eq;
        foreach (var g in goods)
        {
            if (left <= 0) break;
            long have = rt.Baggage.Qty(g); if (have <= 0) continue;
            long take = Math.Min(have, QtyFor(SupplyNeed.Staple, g, left)); long m = Math.Min(left, EqG(g, take));
            if (m <= 0 || !rt.Baggage.Destroy(g, take, DestroyReason.Eaten)) continue;
            res.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Forage] += m; left -= m;
        }
        return eq - left;
    }

    private void NoteForage(HostAccount a, DayResult res, int s, long takenEq, long trampled, int acres, SortedSet<int> robbed, bool plunder, Taken? st, int day)
    {
        var sid = _pop.Region.Settlements[s].Id;
        var e = Chron.Chron(WriteTime(day), "army_foraged", "ch_war_forage", [ChronParam.Place("place", sid), ChronParam.Count("acres", acres), ChronParam.Count("kg", takenEq / 1000), ChronParam.Count("trampled", trampled / 1000)],
            a.Lord != 0 ? [sid, new EntityId(a.Lord)] : [sid], [HostCause(a, CauseKind.War, "ch_note_war_forage")], ("host", a.Id.ToString()), ("plunder", plunder ? "1" : "0"));
        if (e.Value != 0) res.EventIds.Add(e);
        if (st != null)
        {
            foreach (var tl in st.Tallies) tl.Entry = e.Value;
            Book.Stats[1] += st.Mites;
            if (!plunder && UnrestBk is { } unrest) foreach (int i in st.Households) unrest.Add(_pop.Households.IdOf(i).Value, "purveyance", day, e.Value);
        }
        if (plunder && Rules is { } rb && a.Realm >= 0)
        {
            ulong realmKey = LandWorld.RealmTreasuryId(a.Realm).Value;
            foreach (int h in robbed) rb.AddOpinion(_pop.Households.IdOf(h).Value, realmKey, SupplyConfig.ForageRobbedOpinion);
            Book.Stats[9] += robbed.Count;
        }
    }

    /// <summary>The start of a host's day: the foragers' draw is made afresh.</summary>
    private void NewForageDay(HostAccount a) => _forageDay.Remove(a.Id);
}
