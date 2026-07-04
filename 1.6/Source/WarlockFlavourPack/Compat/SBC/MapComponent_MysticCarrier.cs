using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WarlockFlavourPack.Compat.SBC;

/// <summary>
/// Auto-instantiated on every map (RimWorld reflects all MapComponent subclasses
/// and constructs them via <c>Map.ConstructComponents</c> — no XML injection needed).
///
/// Every frame, for each spawned pawn on the currently-visible map:
///   1. Ask <see cref="MysticBond.WearerBondedToCurrentCarry"/> whether this pawn
///      is (a) wearing an SBC carrier AND (b) has the Psychic Bond hediff AND
///      (c) is currently custom-carrying the bonded baby.
///   2. If yes, advance spring+wander physics for that pair and draw the baby
///      portrait at the wandered position with a green tether back to the carrier.
///
/// The companion Harmony patch <see cref="Patch_SuppressBondedBabyDraw"/> ensures
/// SBC's own attached-baby draw is skipped for the same predicate, so the baby
/// appears in exactly one place: floating.
///
/// Runs OUTSIDE any SBC render postfix, so there is no render-tree reentrancy,
/// no matrix-inheritance issue, and no Harmony coupling to SBC internals beyond
/// the tightly-scoped suppression patch.
/// </summary>
public class MapComponent_MysticCarrier : MapComponent
{
    // ---- Physics constants (formerly per-def; now global for the mod) ----
    // Cribbed from MSSFP's HediffComp_Haunt defaults, tuned slightly heavier
    // (larger wander radius) because babies are bigger than ghost sprites.
    private const float WanderRadius = 0.5f;
    private const float WanderAcceleration = 0.35f;
    private const float CatchupStrength = 2.5f;
    private const float DampingPerSecond = 0.15f;
    // 2.0 world tiles matches roughly half an adult pawn body — the PortraitsCache
    // crop bakes UI padding into the texture, so drawSize needs to be larger than
    // the target visible size to compensate.
    private const float BabyDrawSize = 2.0f;
    private const float BabyAltitudeOffset = 0.15f;
    // Portrait resolution — higher gives crisper edges at BabyDrawSize > 1.
    private const int PortraitPixels = 256;

    // Per-facing anchor offset (wearer-relative, tiles). North/East/South/West.
    private static readonly Vector3 AnchorNorth = new Vector3(0.35f, 0f, 0.35f);
    private static readonly Vector3 AnchorEast  = new Vector3(0.45f, 0f, 0.20f);
    private static readonly Vector3 AnchorSouth = new Vector3(-0.35f, 0f, 0.35f);
    private static readonly Vector3 AnchorWest  = new Vector3(-0.45f, 0f, 0.20f);

    private static Vector3 AnchorFor(Rot4 rot)
    {
        if (rot == Rot4.North) return AnchorNorth;
        if (rot == Rot4.East)  return AnchorEast;
        if (rot == Rot4.West)  return AnchorWest;
        return AnchorSouth;
    }

    private struct GhostState
    {
        public Vector3 Pos;      // Current world position of the floating baby (XZ; Y is altitude, computed at draw).
        public Vector3 Vel;      // Current velocity (XZ; Y unused).
        public bool Initialized; // Guard against init at (0,0) before first anchor is known.
    }

    // Transient state — rebuilt after save/load, so we do NOT scribe it.
    private readonly Dictionary<int, GhostState> _ghosts = new();

    // Cached material per baby thingIDNumber. Materials are unmanaged; freed in MapRemoved.
    private readonly Dictionary<int, Material> _babyMats = new();

    // Reusable buffer to avoid GC when iterating eviction candidates.
    private readonly List<int> _evictBuffer = new();

    private static bool _portraitWarned;

    public MapComponent_MysticCarrier(Map map) : base(map) { }

    public override void MapComponentUpdate()
    {
        // Cheap guards first — this runs every frame on every map.
        if (map == null || Find.CurrentMap != map) return;
        IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
        if (pawns == null || pawns.Count == 0) return;

        bool paused = Find.TickManager?.Paused ?? false;
        float dt = paused ? 0f : Time.deltaTime;

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn wearer = pawns[i];
            if (wearer?.apparel == null) continue;

            // The single predicate. Guarantees the wearer has the bond hediff AND
            // is currently custom-carrying the linked baby via SBC.
            if (!MysticBond.WearerBondedToCurrentCarry(wearer, out Pawn baby))
            {
                // Evict any stale ghost state for this wearer.
                if (_ghosts.ContainsKey(wearer.thingIDNumber)) _ghosts.Remove(wearer.thingIDNumber);
                continue;
            }

            // NOTE: do NOT gate on baby.Spawned — SBC un-spawns the baby while
            // custom-carried (BabyDeSpawn is prevented and vanilla render is
            // blocked). The Pawn is still fully valid for PortraitsCache and
            // property access; we just draw it ourselves at the wander point.
            if (baby == null) continue;

            UpdateAndDraw(wearer, baby, dt);
        }

        EvictStale();
    }

    // ---- Physics + draw for one (wearer, baby) pair ----

    private void UpdateAndDraw(Pawn wearer, Pawn baby, float dt)
    {
        Vector3 wearerDrawPos = wearer.DrawPos;
        Vector3 offset = AnchorFor(wearer.Rotation);
        Vector3 anchor = new Vector3(wearerDrawPos.x + offset.x, 0f, wearerDrawPos.z + offset.z);

        int key = wearer.thingIDNumber;
        _ghosts.TryGetValue(key, out GhostState state);

        if (!state.Initialized)
        {
            state.Pos = anchor;
            state.Vel = Vector3.zero;
            state.Initialized = true;
        }
        else if (dt > 0f)
        {
            // Random-walk push (independent per axis).
            float aX = (Rand.Value - 0.5f) * 2f * WanderAcceleration;
            float aZ = (Rand.Value - 0.5f) * 2f * WanderAcceleration;

            // Spring pull-back if we've drifted outside the wander radius.
            float dx = state.Pos.x - anchor.x;
            float dz = state.Pos.z - anchor.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            float sX = 0f, sZ = 0f;
            if (dist > WanderRadius && dist > 0f)
            {
                float overshoot = dist - WanderRadius;
                sX = -(dx / dist) * overshoot * CatchupStrength;
                sZ = -(dz / dist) * overshoot * CatchupStrength;
            }

            // Integrate + exponential damping (frame-rate independent).
            state.Vel.x += (aX + sX) * dt;
            state.Vel.z += (aZ + sZ) * dt;
            float decay = Mathf.Pow(Mathf.Clamp01(DampingPerSecond), dt);
            state.Vel.x *= decay;
            state.Vel.z *= decay;
            state.Pos.x += state.Vel.x * dt;
            state.Pos.z += state.Vel.z * dt;
        }

        _ghosts[key] = state;

        // ---- Baby draw ----
        float altitude = AltitudeLayer.Pawn.AltitudeFor() + BabyAltitudeOffset;
        Vector3 babyDrawPos = new Vector3(state.Pos.x, altitude, state.Pos.z);

        Material babyMat = GetOrRefreshBabyMaterial(baby);
        if (babyMat != null)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(
                babyDrawPos,
                Quaternion.identity,
                new Vector3(BabyDrawSize, 1f, BabyDrawSize));
            Graphics.DrawMesh(MeshPool.plane10, matrix, babyMat, 0);
        }

        // ---- Tether ----
        // From the wearer's centre (slightly above pawn base altitude) to the ghost.
        Vector3 tetherFrom = new Vector3(
            wearerDrawPos.x,
            AltitudeLayer.Pawn.AltitudeFor() + 0.05f,
            wearerDrawPos.z);
        Vector3 tetherTo = new Vector3(babyDrawPos.x, altitude - 0.02f, babyDrawPos.z);
        GenDraw.DrawLineBetween(tetherFrom, tetherTo, SimpleColor.Green);
    }

    // ---- Baby portrait material caching ----

    private Material GetOrRefreshBabyMaterial(Pawn baby)
    {
        RenderTexture rt;
        try
        {
            rt = PortraitsCache.Get(
                baby,
                new Vector2(PortraitPixels, PortraitPixels),
                Rot4.South,             // Fixed south-facing portrait for simplicity — matches "hovering ghost" reading.
                default(Vector3),
                1f);
        }
        catch (Exception e)
        {
            if (!_portraitWarned)
            {
                _portraitWarned = true;
                ModLog.Error("Mystic bond: PortraitsCache.Get failed for baby " + baby.LabelShortCap + ".", e);
            }
            return null;
        }

        if (rt == null) return null;

        int key = baby.thingIDNumber;
        if (!_babyMats.TryGetValue(key, out Material mat) || mat == null || mat.mainTexture != rt)
        {
            // Cutout gives us proper alpha silhouette against the world; MoteGlow would over-brighten.
            mat = new Material(ShaderDatabase.Cutout) { mainTexture = rt };
            _babyMats[key] = mat;
        }
        return mat;
    }

    // ---- Eviction ----

    private void EvictStale()
    {
        _evictBuffer.Clear();

        // Drop ghost entries whose wearer is no longer spawned on this map.
        IReadOnlyList<Pawn> spawned = map.mapPawns?.AllPawnsSpawned;
        foreach (var kv in _ghosts)
        {
            Pawn found = null;
            if (spawned != null)
            {
                for (int i = 0; i < spawned.Count; i++)
                {
                    if (spawned[i].thingIDNumber == kv.Key) { found = spawned[i]; break; }
                }
            }
            if (found == null || !found.Spawned || found.Map != map) _evictBuffer.Add(kv.Key);
        }
        for (int i = 0; i < _evictBuffer.Count; i++) _ghosts.Remove(_evictBuffer[i]);
    }

    // Called when the map is being removed — free cached Materials so we don't leak GPU handles.
    public override void MapRemoved()
    {
        foreach (var mat in _babyMats.Values)
        {
            if (mat != null) UnityEngine.Object.Destroy(mat);
        }
        _babyMats.Clear();
        _ghosts.Clear();
    }
}
