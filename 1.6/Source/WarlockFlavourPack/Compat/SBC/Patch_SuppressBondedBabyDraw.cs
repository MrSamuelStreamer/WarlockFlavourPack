using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WarlockFlavourPack.Compat.SBC;

/// <summary>
/// Prefixes SimpleBabyCarry's five DrawBaby* rendering methods and short-circuits
/// them when the carrier has an active <c>MSS_WFP_Hediff_PsychicBond</c> hediff
/// AND is custom-carrying the linked baby.
///
/// Why five targets: SBC decomposes baby rendering into body / head / apparel /
/// swaddle / render-tree-overlays. Suppressing only one leaves the others still
/// drawing a partial baby ghost anywhere the wearer stands.
///
/// TargetMethods() returns an empty enumerable when SimpleBabyCarry is absent —
/// Harmony 2.x accepts this and skips the patch class entirely. No NRE, no crash.
///
/// On first successful patch, we log the SBC build folder so future breakage
/// reports name a concrete workshop revision.
/// </summary>
[HarmonyPatch]
public static class Patch_SuppressBondedBabyDraw
{
    private const string SbcTypeName = "b4ttl3m3ds.simplebabycarry.Patches.Patch_PawnRenderer_BabyAndSling";

    private static readonly string[] TargetMethodNames = new[]
    {
        "DrawBabyBody",
        "DrawBabyHead",
        "DrawBabyApparel",
        "DrawBabySwaddle",
        "DrawBabyRenderTreeOverlays",
    };

    private static bool _buildIdLogged;

    public static IEnumerable<MethodBase> TargetMethods()
    {
        Type sbc = GenTypes.GetTypeInAnyAssembly(SbcTypeName);
        if (sbc == null)
        {
            // SBC absent — don't emit any targets. Harmony treats an empty enumerable as
            // "nothing to patch" and skips this class silently. Compat folder is also
            // IfModActive-gated so this branch is only hit if the user unloads SBC mid-session.
            yield break;
        }

        LogSbcBuildOnce();

        foreach (string name in TargetMethodNames)
        {
            MethodInfo mi = sbc.GetMethod(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (mi != null)
            {
                yield return mi;
            }
            else
            {
                ModLog.Warn($"Mystic bond: SBC method '{SbcTypeName}.{name}' not found. " +
                            "SBC may have renamed it — floating-baby suppression will be partial.");
            }
        }
    }

    // Harmony name-matches parameters. All five SBC DrawBaby* methods take a `Pawn carrier`.
    // Returning false skips the original method entirely.
    public static bool Prefix(Pawn carrier)
    {
        if (carrier == null) return true;
        try
        {
            return !MysticBond.WearerBondedToCurrentCarry(carrier, out _);
        }
        catch (Exception e)
        {
            // Never let our patch break SBC's render — on any exception, fall through to vanilla SBC draw.
            ModLog.Error("Mystic bond: exception in draw-suppression prefix; falling through to SBC.", e);
            return true;
        }
    }

    private static void LogSbcBuildOnce()
    {
        if (_buildIdLogged) return;
        _buildIdLogged = true;

        // Best-effort: identify the SBC ModContentPack so a breakage bug ticket
        // can quote a concrete workshop revision.
        try
        {
            foreach (var m in LoadedModManager.RunningModsListForReading)
            {
                if (m.PackageId != null && m.PackageId.Equals("b4ttl3m3ds.simplebabycarry", StringComparison.OrdinalIgnoreCase))
                {
                    ModLog.Log($"Mystic bond: patching SimpleBabyCarry at '{m.RootDir}'.");
                    return;
                }
            }
        }
        catch { /* swallow — logging path must never throw */ }
    }
}
