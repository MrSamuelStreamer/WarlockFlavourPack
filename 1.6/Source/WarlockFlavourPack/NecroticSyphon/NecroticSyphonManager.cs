using System.Collections.Generic;
using Verse;

namespace WarlockFlavourPack.NecroticSyphon;

/// <summary>
/// Lightweight static registry of all spawned <see cref="Comp_NecroticSyphon"/> instances.
///
/// The Harmony patch on <c>Plant.GrowthRate</c> calls
/// <see cref="TryGetInfluence"/> every time a plant's growth rate is evaluated.
/// Keeping a flat list (typically very small — players rarely build more than a
/// handful of these) is cheaper than a per-map spatial structure.
/// </summary>
public static class NecroticSyphonManager
{
    private static readonly List<Comp_NecroticSyphon> _all = new();

    public static void Register(Comp_NecroticSyphon comp)
    {
        if (!_all.Contains(comp))
            _all.Add(comp);
    }

    public static void Deregister(Comp_NecroticSyphon comp)
    {
        _all.Remove(comp);
    }

    /// <summary>
    /// Returns the first charged syphon on the same map whose radius covers
    /// <paramref name="cell"/>, or <c>null</c> if none.
    /// </summary>
    public static Comp_NecroticSyphon TryGetInfluence(IntVec3 cell, Map map)
    {
        foreach (Comp_NecroticSyphon syphon in _all)
        {
            if (!syphon.IsCharged) continue;
            if (syphon.parent.Map != map) continue;
            if (syphon.parent.Position.DistanceTo(cell) <= syphon.Props.radius)
                return syphon;
        }
        return null;
    }

    /// <summary>
    /// Returns true if any charged syphon exists on the given map.
    /// Used by the map-wide GrowthSeasonNow patch to allow sow jobs.
    /// </summary>
    public static bool AnyChargedOnMap(Map map)
    {
        foreach (Comp_NecroticSyphon syphon in _all)
        {
            if (syphon.IsCharged && syphon.parent.Map == map)
                return true;
        }
        return false;
    }
}
