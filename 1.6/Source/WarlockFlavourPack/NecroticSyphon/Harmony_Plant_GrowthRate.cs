using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WarlockFlavourPack.NecroticSyphon;

[HarmonyPatch(typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow), new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef) })]
internal static class Harmony_PlantUtility_GrowthSeasonNow
{
    [HarmonyPostfix]
    private static void Postfix(IntVec3 c, Map map, ref bool __result)
    {
        if (__result) return; // already true, nothing to do
        if (map == null) return;
        Comp_NecroticSyphon syphon = NecroticSyphonManager.TryGetInfluence(c, map);
        if (syphon != null)
            __result = true;
    }
}

[HarmonyPatch(typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow), new[] { typeof(Map), typeof(ThingDef) })]
internal static class Harmony_PlantUtility_GrowthSeasonNow_MapWide
{
    [HarmonyPostfix]
    private static void Postfix(Map map, ref bool __result)
    {
        if (__result) return; // already true, nothing to do
        if (map == null) return;
        // If any charged syphon exists on this map, sowing is permitted map-wide.
        if (NecroticSyphonManager.AnyChargedOnMap(map))
            __result = true;
    }
}

/// <summary>
/// Postfix on <c>PlantUtility.CanEverPlantAt</c>.
/// Overrides fertility rejection for cells covered by a charged syphon.
/// </summary>
[HarmonyPatch(typeof(PlantUtility), nameof(PlantUtility.CanEverPlantAt),
    new[] { typeof(ThingDef), typeof(IntVec3), typeof(Map), typeof(Thing), typeof(bool), typeof(bool), typeof(bool) },
    new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
internal static class Harmony_PlantUtility_CanEverPlantAt
{
    [HarmonyPostfix]
    private static void Postfix(IntVec3 c, Map map, ref AcceptanceReport __result, ref Thing blockingThing)
    {
        if (__result.Accepted) return;
        if (map == null || !c.InBounds(map)) return;
        // Don't override physical blockers (buildings, walls, things).
        if (blockingThing != null) return;
        // Don't override impassable terrain (deep water, sealed rock, etc.).
        if (c.GetTerrain(map).passability == Traversability.Impassable) return;

        Comp_NecroticSyphon syphon = NecroticSyphonManager.TryGetInfluence(c, map);
        if (syphon == null) return;

        // Syphon overrides environmental rejections (fertility, temperature, growth-season).
        __result = AcceptanceReport.WasAccepted;
        blockingThing = null;
    }
}

[HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRate), MethodType.Getter)]
internal static class Harmony_Plant_GrowthRate
{
    [HarmonyPostfix]
    private static void Postfix(Plant __instance, ref float __result)
    {
        // Only act on spawned plants with a valid map.
        if (!__instance.Spawned || __instance.Map == null)
            return;

        Comp_NecroticSyphon syphon = NecroticSyphonManager.TryGetInfluence(__instance.Position, __instance.Map);
        if (syphon == null)
            return;

        var props = syphon.Props;

        // If vanilla already returned > 0, apply fertility debuff then clamp.
        // If vanilla returned 0 (blighted OR environmentally suppressed), we
        // must distinguish: blighted plants should stay at 0.
        if (__result <= 0f)
        {
            if (__instance.Blighted)
                return; // Blight overrides the syphon — no growth.

            // Environmentally suppressed (no light / wrong temp / off-season).
            // Recompute using only the fertility factor with debuff applied.
            float fertilityFactor = __instance.GrowthRateFactor_Fertility * props.fertilityDebuff;
            __result = Mathf.Max(fertilityFactor, props.minGrowthRate);
        }
        else
        {
            // Normal growth — debuff fertility contribution, then clamp.
            // We can't easily separate the fertility factor from the product,
            // so we divide out the original fertility factor and multiply in
            // the debuffed one.
            float originalFertility = __instance.GrowthRateFactor_Fertility;
            float debuffedFertility = originalFertility * props.fertilityDebuff;

            float adjusted = (originalFertility > 0f)
                ? __result / originalFertility * debuffedFertility
                : __result;

            __result = Mathf.Max(adjusted, props.minGrowthRate);
        }
    }
}

// Prevents unlitTicks from accumulating for plants in syphon range.
// unlitTicks > 450,000 with dieIfNoSunlight=true triggers rotting damage in CurrentDyingDamagePerTick.
[HarmonyPatch(typeof(Plant), "HasEnoughLightToGrow", MethodType.Getter)]
internal static class Harmony_Plant_HasEnoughLightToGrow
{
    [HarmonyPostfix]
    private static void Postfix(Plant __instance, ref bool __result)
    {
        if (__result) return;
        if (!__instance.Spawned || __instance.Map == null) return;
        if (NecroticSyphonManager.TryGetInfluence(__instance.Position, __instance.Map) != null)
            __result = true;
    }
}

// Replaces the vanilla temperature-condition label with syphon-specific text
// in the plant's inspect string, so the player sees the real reason for the growth rate.
[HarmonyPatch(typeof(Plant), nameof(Plant.GetInspectString))]
internal static class Harmony_Plant_GetInspectString
{
    [HarmonyPostfix]
    private static void Postfix(Plant __instance, ref string __result)
    {
        if (!__instance.Spawned || __instance.Map == null) return;
        if (NecroticSyphonManager.TryGetInfluence(__instance.Position, __instance.Map) == null) return;

        string tempLabel = "OutOfIdealTemperatureRangeNotGrowing".Translate();
        if (!__result.Contains(tempLabel)) return;
        __result = __result.Replace(tempLabel, "MSS_NecroticSyphon_NecroticEnergies".Translate());
    }
}

// Suppresses cold-based leaflessness for plants in syphon range, while still
// allowing pollution/no-pollution leaflessness to fire normally.
[HarmonyPatch(typeof(Plant), "CheckMakeLeafless")]
internal static class Harmony_Plant_CheckMakeLeafless
{
    [HarmonyPrefix]
    private static bool Prefix(Plant __instance)
    {
        if (!__instance.Spawned || __instance.Map == null) return true;
        if (NecroticSyphonManager.TryGetInfluence(__instance.Position, __instance.Map) == null) return true;

        // Still apply pollution-based leaflessness; skip the cold check.
        if (__instance.DyingFromPollution) { __instance.MakeLeafless(Plant.LeaflessCause.Pollution); return false; }
        if (__instance.DyingFromNoPollution) { __instance.MakeLeafless(Plant.LeaflessCause.NoPollution); return false; }
        return false; // skip cold leaflessness
    }
}
