using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WarlockFlavourPack.Preservation;

/// <summary>
/// Corpse ThingDefs are generated procedurally at def-load from each Pawn race
/// def (see <c>RimWorld.ThingDefGenerator_Corpses.GenerateCorpseDef</c>). There
/// is no XML file to PatchOperation-add our comp to, so we postfix the
/// generator and append <see cref="CompProperties_Preserved"/> to the emitted
/// ThingDef's <c>comps</c> list.
///
/// This runs once per race at startup — cheap. The comp itself is inert
/// (no tick) so the memory cost per corpse is a single object + two bools.
/// </summary>
[HarmonyPatch(typeof(ThingDefGenerator_Corpses), "GenerateCorpseDef")]
public static class Harmony_CorpseDefGenerator_AddComp
{
    public static void Postfix(ThingDef __result)
    {
        if (__result == null) return;
        if (__result.comps == null)
        {
            // Vanilla always populates comps for corpses, but a wrapping mod could
            // conceivably NRE the list. Never let our injection break def-load.
            return;
        }
        if (__result.comps.Any(c => c is CompProperties_Preserved))
        {
            // Belt-and-braces: some load-order permutations re-hit the generator
            // during hot-reload. Idempotent.
            return;
        }
        __result.comps.Add(new CompProperties_Preserved());
    }
}
