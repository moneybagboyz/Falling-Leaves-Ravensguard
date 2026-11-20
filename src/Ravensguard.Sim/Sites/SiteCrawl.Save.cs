using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Sites;

public sealed partial class SiteCrawl
{
    /// <summary>The fight in progress for a save: its record, the fighter map and the origin.</summary>
    internal void CaptureFight(CrawlSave s)
    {
        s.Loud = _loudWindow;
        if (Fight == null) return;
        s.Fight = FightRecord.Capture(Fight, "site_inhabitants", "site").ToBytes();
        s.FightMap = _fightMap; s.FightAt = [_fightOx, _fightOy, _fightLevel, _fights];
    }

    /// <summary>A crawl as saved, on the site's map as saved: the same people where they were, the same clock, map, collapses and fight; the next steps are the ones an uninterrupted run would take.</summary>
    public static SiteCrawl Restore(CrawlSave s, SiteRecord r, SiteContent c, FightContent fc, WoundLedger ledger, ulong seed)
    {
        var crawl = new SiteCrawl(r.Map!, c, fc, ledger, s.GameSec, seed, r.Known);
        crawl.Steps = s.Steps; crawl.FloodSince = s.FloodSince; crawl.ClearedDay = s.ClearedDay; crawl.Slain.AddRange(s.Slain);
        if (s.Flags.Length >= 4) { crawl.TabooBroken = s.Flags[0]; crawl.GraveTaken = s.Flags[1]; crawl.Resting = s.Flags[2]; crawl.HeroAi = s.Flags[3]; }
        crawl._loudWindow = s.Loud;
        for (int i = 0; i + 4 < s.CaveIns.Length; i += 5) crawl.CaveIns.Add(new PendingCaveIn((int)s.CaveIns[i], (int)s.CaveIns[i + 1], (int)s.CaveIns[i + 2], s.CaveIns[i + 3], (ulong)s.CaveIns[i + 4]));
        foreach (var ms in s.Members)
        {
            var kit = SiteSave.KitOf(ms.KitJson);
            int[] n = ms.Ints; bool[] f = ms.Flags;
            var m = new CrawlMember
            {
                Person = ms.Person, Name = ms.Name, Kit = kit, X = ms.Pos[0], Y = ms.Pos[1], Level = ms.Pos[2], FX = ms.Pos[3], FY = ms.Pos[4], Cell = ms.Pos[5],
                Light = new CarriedLight { Kind = (LightKind)ms.LightKind, LeftMilliS = ms.LightLeft, Lit = ms.Lit },
                SpareTorches = n[0], SpareCandles = n[1], OilMl = n[2], Perception = n[3], Lockpicking = n[4], Athletics = n[5], Stealth = n[6], DigQuality = n[7], GearG = n[8], CapacityG = n[9], LootG = n[10],
                MoraleX100 = n[11], FoulS = n[12], ClearS = n[13], BreathS = n[14], HeldS = n[15], BusyS = n[16], SwimChecks = n[17], BusyKind = (CrawlInputKind)n[18], BusyTarget = n[19], DragBy = n[20],
                Mode = (CrawlMode)n[21], MoveDir = n[22], PathIdx = n[23], TorchesUsed = n[24], CandlesUsed = n[25], OilUsedMl = n[26],
                Tinder = f[0], Rope = f[1], Superstitious = f[2], HasMap = f[3], Unconscious = f[4], Dead = f[5], Sunk = f[6], Swimming = f[7], WarnedAir = f[8], Lost = f[9], Sneak = f[10], Retracing = f[11],
                Loot = SiteCodec.Items(ms.Loot), Path = f[12] ? [.. ms.Path] : null, DeathCause = ms.DeathCause,
            };
            m.Plan = fc.T.Plan(kit.Plan); m.Worn = crawl.Blows.Worn(kit); m.Book = ledger.For(m.Person, m.Plan, s.GameSec, kit.SizeCm3, kit.Toughness);
            m.Crumbs = Breadcrumbs.From(n[27], ms.Crumbs);
            if (n.Length > 29) { m.Bandages = n[28]; m.FirstAid = n[29]; }
            if (f.Length > 13) m.AutoTreat = f[13];
            m.Hero = crawl.Members.Count == 0;
            crawl.Members.Add(m);
        }
        if (s.Fight.Length > 0)
        {
            var f = FightRecord.FromBytes(s.Fight).Restore(fc, ledger);
            crawl.AttachRestoredFight(f, s.FightMap, s.FightAt);
        }
        crawl._lightDirty = crawl._mapDirty = true;
        crawl.RefreshLight();
        crawl.Snapshot = crawl.Publish();
        return crawl;
    }

    private void AttachRestoredFight(FightWorld f, int[] map, int[] at)
    {
        _fightMap = map; _fightOx = at[0]; _fightOy = at[1]; _fightLevel = at[2]; _fights = at[3];
        int ox = _fightOx, oy = _fightOy, level = _fightLevel;
        f.SiteLight = (x, y) => { int cx = (ox + x) / 1000, cy = (oy + y) / 1000; return Map.In(level, cx, cy) ? _frame.Total(cx, cy, SiteMap.Z(level)) : 0; };
        f.SitePlacePoints = fi => { int idx = fi.Id < _fightMap.Length ? _fightMap[fi.Id] : -1; return idx >= 0 ? AirOf(Members[idx]).AccuracyPoints : 0; };
        Fight = f;
    }
}
