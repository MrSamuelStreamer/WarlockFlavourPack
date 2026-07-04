using Verse;

namespace WarlockFlavourPack.Preservation;

/// <summary>
/// Companion CompProperties for <see cref="CompPreserved"/>. Attached to every
/// corpse ThingDef at runtime by <see cref="Harmony_CorpseDefGenerator_AddComp"/>
/// (no XML because vanilla generates corpse defs procedurally from race defs).
///
/// No XML fields — all state lives on the instance comp.
/// </summary>
public class CompProperties_Preserved : CompProperties
{
    public CompProperties_Preserved()
    {
        compClass = typeof(CompPreserved);
    }
}
