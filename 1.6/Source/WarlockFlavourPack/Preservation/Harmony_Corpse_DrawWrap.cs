using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace WarlockFlavourPack.Preservation;

/// <summary>
/// Draws a head-attached wrap sprite on preserved corpses.
///
/// Hook chain:
///   Corpse.DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip)
///   → InnerPawn.DynamicDrawPhaseAt(phase, drawLoc.WithYOffset(SeededYOffset))
///
/// We postfix the corpse-side entry, gate on the actual Draw phase (the
/// method is also called for EnsureInitialized and ParallelPreDraw), and
/// paint the wrap at the pawn's head anchor using
/// <c>PawnRenderer.BaseHeadOffsetAt(rotation)</c> — identical to how vanilla
/// apparel head layers locate themselves.
///
/// Rotation coverage: RimWorld exposes east/south/north textures; west is
/// drawn by mirroring the east sprite via a negative-x scale in the transform
/// matrix. This is the same trick vanilla apparel uses.
///
/// The whole postfix runs in a try/catch — a bad matrix or a null
/// <c>InnerPawn.Drawer</c> must NEVER kill corpse rendering. One error is
/// logged per session then swallowed.
/// </summary>
[HarmonyPatch(typeof(Corpse), nameof(Corpse.DynamicDrawPhaseAt))]
public static class Harmony_Corpse_DrawWrap
{
    // Wrap sits BETWEEN vanilla Head (baseLayer 50) and ApparelHead
    // (baseLayer 70) so it draws over hair/skin but UNDER any hat the pawn
    // was wearing when they died (which reads naturally — the mortician
    // wrapped the head; the hat sits on top).
    //
    // PawnRenderUtility.AltitudeForLayer(layer) = layer * 0.0003658537f, so
    // the vanilla Y offsets are:
    //   Head        (50) → 0.01829
    //   ApparelHead (70) → 0.02561
    // Pick layer ~60 → 0.02195. Add a nudge on top of PawnRenderer's own
    // sub-slotting for hair/beard which layer-worker resolvers may add.
    private const float WrapAltitudeOffset = 60f * 0.0003658537f;

    // Per-rotation material cache. Materials are unmanaged; RimWorld destroys
    // them at app shutdown, so we don't need to free them ourselves.
    private static readonly Dictionary<byte, Material> MatCache = new();

    private static bool _errorLogged;

    public static void Postfix(Corpse __instance, DrawPhase phase, Vector3 drawLoc, bool flip)
    {
        // Only paint during the final draw phase — earlier phases are for
        // caching / pre-render bookkeeping and calling Graphics.DrawMesh from
        // them can cause double-draws.
        if (phase != DrawPhase.Draw) return;

        CompPreserved comp = __instance?.TryGetComp<CompPreserved>();
        if (comp == null || !comp.isPreserved) return;

        Pawn inner = __instance.InnerPawn;
        if (inner?.Drawer?.renderer == null) return;

        try
        {
            PawnRenderer renderer = inner.Drawer.renderer;

            // For corpses (and downed/sleeping pawns), the visual body is
            // laid on its side. Two pieces of state drive the visual pose:
            //   • LayingFacing() — the Rot4 the sprite is drawn with
            //     (deterministic per pawn; NOT the same as pawn.Rotation).
            //   • BodyAngle(PawnRenderFlags.None) — the world-space rotation
            //     applied to the whole body (0° standing, non-zero lying).
            // We anchor the wrap using LayingFacing()'s head offset, then
            // rotate BOTH the offset and the sprite by BodyAngle so the wrap
            // stays glued to the tilted head instead of floating a tile away.
            Rot4 rot = renderer.LayingFacing();
            float bodyAngle = renderer.BodyAngle(PawnRenderFlags.None);
            Quaternion bodyRot = Quaternion.AngleAxis(bodyAngle, Vector3.up);

            // Corpse.DynamicDrawPhaseAt already applies SeededYOffset before
            // handing to InnerPawn; mirror that here.
            Vector3 rootLoc = drawLoc.WithYOffset(inner.Drawer.SeededYOffset);
            Vector3 headOffsetLocal = renderer.BaseHeadOffsetAt(rot);
            Vector3 headOffsetWorld = bodyRot * headOffsetLocal;

            Vector3 wrapPos = new Vector3(
                rootLoc.x + headOffsetWorld.x,
                rootLoc.y + WrapAltitudeOffset,
                rootLoc.z + headOffsetWorld.z);

            Material mat = MaterialFor(rot);
            if (mat == null) return;

            // West mirrors east (vanilla apparel trick).
            bool mirror = rot == Rot4.West;
            Vector3 scale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            Matrix4x4 matrix = Matrix4x4.TRS(wrapPos, bodyRot, scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }
        catch (Exception e)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                ModLog.Error(
                    $"Preserve corpse: exception drawing wrap on {__instance?.LabelShortCap ?? "<null>"}. " +
                    "Wrap overlay will be silent from now on; corpse rendering is unaffected.",
                    e);
            }
        }
    }

    private static Material MaterialFor(Rot4 rot)
    {
        byte key = rot.AsByte;
        if (MatCache.TryGetValue(key, out Material cached)) return cached;

        string texPath = rot == Rot4.North
            ? "MSS_Wraps_north"
            : rot == Rot4.South
                ? "MSS_Wraps_south"
                : "MSS_Wraps_east"; // east AND west use the east texture (west mirrors)

        Texture2D tex = ContentFinder<Texture2D>.Get(texPath, reportFailure: false);
        if (tex == null)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                ModLog.Error($"Preserve corpse: wrap texture '{texPath}' missing. Wrap overlay disabled.");
            }
            return null;
        }

        Material mat = MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout);
        MatCache[key] = mat;
        return mat;
    }
}
