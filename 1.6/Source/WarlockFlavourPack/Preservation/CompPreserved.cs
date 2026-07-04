using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WarlockFlavourPack.Preservation;

/// <summary>
/// State comp attached to every corpse (via Harmony postfix on the def generator).
///
/// Two bools drive the whole flow:
///   • <see cref="markedForPreservation"/> — set by the player clicking the gizmo.
///     <see cref="WorkGiver_PreserveCorpse"/> scans for corpses with this flag
///     set and dispatches a hauler.
///   • <see cref="isPreserved"/> — set by <see cref="Preserve"/> when the
///     JobDriver completes the work. Once true, the corpse's sibling
///     <see cref="CompRottable"/> is <c>disabled</c>d (a vanilla-scribed public
///     flag), which halts rot progression and, transitively, rot-stink emission
///     (<c>GasUtility.RotStinkToSpawnForCorpse</c> returns 0 for RotStage.Fresh).
///
/// Butcher output is suppressed by <see cref="Harmony_Corpse_ButcherProducts"/>.
/// A head-attached wrap sprite is drawn by <see cref="Harmony_Corpse_DrawWrap"/>.
///
/// This comp itself is inert — no CompTick, no per-frame cost. All state lives
/// as two bools and rides through save/load via <see cref="PostExposeData"/>.
/// </summary>
public class CompPreserved : ThingComp
{
    public bool markedForPreservation;
    public bool isPreserved;

    // Cached gizmo icon — resolved once at startup on the main thread by
    // PreservedIconCache (see below). Comp instances share the reference.
    private static Texture2D Icon => PreservedIconCache.GizmoIcon;

    /// <summary>
    /// Called from <see cref="JobDriver_PreserveCorpse"/> once the hauler has
    /// consumed 3 herbal medicine and completed the 10-minute wait. Flips the
    /// preserved flag, clears the marked-for flag, and disables rot on the
    /// sibling CompRottable. Idempotent — safe to call on an already-preserved
    /// corpse (shouldn't happen but jobs can double-fire under save-load edge
    /// cases).
    /// </summary>
    public void Preserve()
    {
        if (isPreserved) return;

        isPreserved = true;
        markedForPreservation = false;

        // Freeze rot in place. `disabled` is a public vanilla field on
        // CompRottable, scribed by vanilla PostExposeData — no separate save
        // handling needed on our side for the rot half.
        CompRottable rot = parent.GetComp<CompRottable>();
        if (rot != null)
        {
            rot.disabled = true;
        }
        else
        {
            // Modded "corpse" ThingDefs sometimes ship without CompRottable
            // (e.g. mummified or synthetic remains). No rot to freeze — the
            // butcher + draw hooks still apply, so preservation is still
            // meaningful for those.
            ModLog.Verbose($"Preserved {parent?.LabelShortCap ?? "<null>"} has no CompRottable — rot freeze skipped.");
        }
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (Gizmo g in base.CompGetGizmosExtra())
            yield return g;

        if (parent == null || !parent.Spawned) yield break;

        if (isPreserved)
        {
            // No gizmo once preserved — user answered "one-way" during planning.
            yield break;
        }

        if (markedForPreservation)
        {
            yield return new Command_Action
            {
                defaultLabel = "MSS_WFP_CancelPreserve_Label".TranslateSimple(),
                defaultDesc = "MSS_WFP_CancelPreserve_Desc".TranslateSimple(),
                icon = Icon,
                action = () =>
                {
                    markedForPreservation = false;
                    // Cancel any in-flight job targeting this corpse.
                    Map map = parent.MapHeld;
                    if (map != null)
                    {
                        foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                        {
                            if (p.CurJobDef == MSSDefOf.MSS_PreserveCorpse &&
                                p.CurJob?.targetA.Thing == parent)
                            {
                                p.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                            }
                        }
                    }
                }
            };
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "MSS_WFP_Preserve_Label".TranslateSimple(),
            defaultDesc = "MSS_WFP_Preserve_Desc".TranslateSimple(),
            icon = Icon,
            action = () => { markedForPreservation = true; }
        };
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref markedForPreservation, "MSS_WFP_markedForPreservation", defaultValue: false);
        Scribe_Values.Look(ref isPreserved, "MSS_WFP_isPreserved", defaultValue: false);
    }
}

/// <summary>
/// Main-thread-safe holder for the gizmo icon texture. RimWorld requires all
/// <c>Texture2D</c> loads to happen on the main thread; <c>[StaticConstructorOnStartup]</c>
/// guarantees this static ctor runs during the main-thread startup sweep.
///
/// Kept as a separate class (rather than a static field on <see cref="CompPreserved"/>)
/// so the comp itself is not decorated with StaticConstructorOnStartup — comps
/// can be instantiated during def load, and mixing the two attributes causes
/// engine warnings.
/// </summary>
[StaticConstructorOnStartup]
internal static class PreservedIconCache
{
    public static readonly Texture2D GizmoIcon =
        ContentFinder<Texture2D>.Get("MSS_Wraps_south", reportFailure: true);
}
