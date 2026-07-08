using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace WarlockFlavourPack.NecroticSyphon;

/// <summary>
/// Core comp for the Necrotic Syphon building.
///
/// Behaviour summary:
///   • Every <c>TickRare</c> the comp scans for corpses within <see cref="CompProperties_NecroticSyphon.radius"/>
///     and consumes them (destroys the corpse Thing), adding <see cref="CompProperties_NecroticSyphon.ticksPerCorpse"/>
///     ticks of charge per corpse.
///   • Charge drains at 1 tick per game tick while the building is powered/active.
///   • While charged, all crop plants within range have their growth rate boosted
///     via <see cref="NecroticSyphonManager"/> (queried by the Harmony patch on
///     <c>Plant.GrowthRate</c>).
///   • Charge and active state are saved/loaded via <see cref="PostExposeData"/>.
/// </summary>
public class Comp_NecroticSyphon : ThingComp
{
    // ── Charge ──────────────────────────────────────────────────────────────
    private int chargeTicks;

    public int ChargeTicks => chargeTicks;
    public bool IsCharged => chargeTicks > 0;

    // ── Cached props shortcut ────────────────────────────────────────────────
    public CompProperties_NecroticSyphon Props => (CompProperties_NecroticSyphon)props;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        NecroticSyphonManager.Register(this);
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        NecroticSyphonManager.Deregister(this);
    }

    public override void CompTickRare()
    {
        base.CompTickRare();
        DrainCharge(GenTicks.TickRareInterval);
        TryConsumeCorpses();
    }

    // ── Charge management ────────────────────────────────────────────────────

    private void DrainCharge(int ticks)
    {
        chargeTicks = Mathf.Max(0, chargeTicks - ticks);
    }

    private void TryConsumeCorpses()
    {
        if (parent.Map == null) return;

        var consumed = new List<Corpse>();
        foreach (Corpse corpse in GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.radius, useCenter: true).OfType<Corpse>().Where(t=>!t.Destroyed ))
        {
            if(Rand.Chance(1-Props.chanceToConsumeCorpsePerRareTick)) break;
            consumed.Add(corpse);
            // 66% chance to stop consuming corpses this cycle
        }

        foreach (Corpse corpse in consumed)
        {
            chargeTicks += Props.ticksPerCorpse;
            ModLog.Verbose($"NecroticSyphon consumed {corpse.LabelShortCap} at {corpse.Position}, added {Props.ticksPerCorpse} charge ticks.");
            corpse.Destroy(DestroyMode.Vanish);
        }
    }

    // ── Inspection / UI ──────────────────────────────────────────────────────

    public override string CompInspectStringExtra()
    {
        StringBuilder sb = new StringBuilder();
        if (IsCharged)
        {
            float daysRemaining = (float)chargeTicks / GenDate.TicksPerDay;
            sb.Append("MSS_NecroticSyphon_ChargeRemaining".Translate(daysRemaining.ToString("F1")));
        }
        else
        {
            sb.Append("MSS_NecroticSyphon_Uncharged".Translate());
        }
        return sb.ToString();
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (Gizmo g in base.CompGetGizmosExtra())
            yield return g;

        yield return new Command_Action
        {
            defaultLabel = "MSS_NecroticSyphon_CreateStockpile_Label".Translate(),
            defaultDesc = "MSS_NecroticSyphon_CreateStockpile_Desc".Translate(),
            icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile"),
            action = CreateCorpseStockpile
        };

        yield return new Command_Action
        {
            defaultLabel = "MSS_NecroticSyphon_CreateGrowZone_Label".Translate(),
            defaultDesc = "MSS_NecroticSyphon_CreateGrowZone_Desc".Translate(),
            icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Designators/ZoneCreate_Growing"),
            action = CreateGrowZone
        };

        if (DebugSettings.godMode)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEBUG: Add 1 day charge",
                action = () => chargeTicks += GenDate.TicksPerDay
            };
        }
    }

    private static bool CellAllowsZone(IntVec3 cell, Map map)
    {
        if (map.zoneManager.ZoneAt(cell) != null) return false;
        foreach (Thing t in cell.GetThingList(map))
            if (!t.def.CanOverlapZones) return false;
        return true;
    }

    private void CreateGrowZone()
    {
        Map map = parent.Map;
        if (map == null) return;

        IEnumerable<IntVec3> cells = GenRadial.RadialCellsAround(parent.Position, Props.radius, useCenter: true)
            .Where(c => c.InBounds(map));

        Zone_Growing zone = new Zone_Growing(map.zoneManager);
        map.zoneManager.RegisterZone(zone);

        foreach (IntVec3 cell in cells)
        {
            if (CellAllowsZone(cell, map))
                zone.AddCell(cell);
        }
    }

    private void CreateCorpseStockpile()
    {
        Map map = parent.Map;
        if (map == null) return;

        IEnumerable<IntVec3> cells = GenRadial.RadialCellsAround(parent.Position, Props.radius, useCenter: true)
            .Where(c => c.InBounds(map));

        Zone_Stockpile zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
        map.zoneManager.RegisterZone(zone);

        foreach (IntVec3 cell in cells)
        {
            if (CellAllowsZone(cell, map))
                zone.AddCell(cell);
        }

        // Restrict to corpses only
        zone.settings.filter.SetDisallowAll();
        ThingCategoryDef corpsesCategory = ThingCategoryDefOf.Corpses;
        if (corpsesCategory != null)
            zone.settings.filter.SetAllow(corpsesCategory, allow: true);
    }

    // ── Save / Load ──────────────────────────────────────────────────────────

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref chargeTicks, "MSS_NecroticSyphon_chargeTicks", defaultValue: 0);
    }
}
