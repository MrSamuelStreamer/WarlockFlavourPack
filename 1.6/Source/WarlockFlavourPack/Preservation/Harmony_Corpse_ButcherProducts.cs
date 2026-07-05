using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace WarlockFlavourPack.Preservation;

/// <summary>
/// A wrapped corpse yields nothing when butchered: no meat, no leather. As a
/// side-effect of returning early, we also skip vanilla's blood-filth spawn
/// and the ideology "ButcheredHuman" history event — narratively correct for
/// a body sealed in medicinal herbs.
///
/// Prefix returns <c>false</c> so the entire vanilla iterator body is skipped
/// and <c>__result</c> is our empty enumerable.
/// </summary>
[HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
public static class Harmony_Corpse_ButcherProducts
{
    public static bool Prefix(Corpse __instance, ref IEnumerable<Thing> __result)
    {
        CompPreserved comp = __instance?.TryGetComp<CompPreserved>();
        if (comp == null || !comp.isPreserved) return true; // vanilla behaviour

        __result = Enumerable.Empty<Thing>();
        return false;
    }
}
