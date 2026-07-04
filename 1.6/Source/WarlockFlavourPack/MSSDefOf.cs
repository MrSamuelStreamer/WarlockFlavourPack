using RimWorld;
using Verse;

namespace WarlockFlavourPack;

/// <summary>
/// Static def references for this mod. Populated by <see cref="DefOfHelper"/>
/// during def-load; NRE'ing here means a def failed to load — check the log for
/// XML parse errors before looking at C#.
/// </summary>
[DefOf]
public static class MSSDefOf
{
    public static JobDef MSS_PreserveCorpse;

    static MSSDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(MSSDefOf));
    }
}
