using Verse;

namespace WarlockFlavourPack.NecroticSyphon;

public class CompProperties_NecroticSyphon : CompProperties
{
    /// <summary>Radius in cells within which the syphon affects crops and consumes corpses.</summary>
    public float radius = 8f;

    /// <summary>
    /// How many ticks of charge one corpse provides.
    /// Defaults to one in-game day.
    /// </summary>
    public int ticksPerCorpse = 60000; // 1 in-game day

    /// <summary>Minimum growth rate guaranteed to plants in range (0–1).</summary>
    public float minGrowthRate = 0.75f;

    /// <summary>
    /// Multiplicative debuff applied to the fertility factor for plants in range.
    /// 0.9 = 10% reduction.
    /// </summary>
    public float fertilityDebuff = 0.9f;
    
    /// <summary>
    /// Chance to consume a corpse per rare tick.
    /// </summary>
    public float chanceToConsumeCorpsePerRareTick = 0.1f;

    public CompProperties_NecroticSyphon()
    {
        compClass = typeof(Comp_NecroticSyphon);
    }
}
