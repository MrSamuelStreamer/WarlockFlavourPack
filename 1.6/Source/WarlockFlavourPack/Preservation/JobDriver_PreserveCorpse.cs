using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WarlockFlavourPack.Preservation;

/// <summary>
/// Preservation job. Reserves a corpse (A) and a herbal-medicine stack (B),
/// hauls 3 herbal medicine to the corpse, waits ~10 in-game minutes, then
/// flips <see cref="CompPreserved.isPreserved"/> and consumes the carried
/// medicine.
///
/// TargetA — the corpse being preserved.
/// TargetB — the herbal-medicine stack.
/// job.count — number of medicine units required (3).
/// </summary>
public class JobDriver_PreserveCorpse : JobDriver
{
    // 10 in-game minutes. GenDate.TicksPerHour == 2500 → 10min ≈ 417 ticks.
    private const int WorkTicks = GenDate.TicksPerHour / 6;

    private Thing Corpse => job.GetTarget(TargetIndex.A).Thing;
    private Thing Medicine => job.GetTarget(TargetIndex.B).Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        // Reserve corpse (single reserver) and medicine stack (3 count).
        if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
        if (!pawn.Reserve(job.targetB, job, 1, job.count, null, errorOnFailed)) return false;
        return true;
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        this.FailOnBurningImmobile(TargetIndex.A);
        this.FailOn(() =>
        {
            // Bail if someone else preserved it or the mark was cleared while
            // we were en route.
            CompPreserved comp = Corpse?.TryGetComp<CompPreserved>();
            return comp == null || comp.isPreserved || !comp.markedForPreservation;
        });

        // 1. Reserve corpse.
        yield return Toils_Reserve.Reserve(TargetIndex.A);

        // 2. Reserve `job.count` from the medicine stack.
        yield return Toils_Reserve.Reserve(TargetIndex.B, 1, job.count);

        // 3. Walk to the medicine, fail if it despawns / is forbidden en route.
        Toil goToMedicine = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.B);
        yield return goToMedicine;

        // 4. Pick up `job.count` medicine into carry tracker. If the stack is
        //    smaller than expected (someone else took some), fail cleanly.
        yield return Toils_Haul.StartCarryThing(
            TargetIndex.B,
            putRemainderInQueue: false,
            subtractNumTakenFromJobCount: false,
            failIfStackCountLessThanJobCount: true,
            reserve: false,
            canTakeFromInventory: false);

        // 5. Walk to the corpse.
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

        // 6. Do the work. Progress bar + face-corpse.
        Toil work = Toils_General.WaitWith(
            TargetIndex.A,
            WorkTicks,
            useProgressBar: true,
            maintainPosture: false,
            maintainSleep: false,
            face: TargetIndex.A);
        work.WithEffect(EffecterDefOf.ConstructMetal, TargetIndex.A);
        yield return work;

        // 7. Finish — flip the flag on the corpse, destroy the carried medicine.
        yield return Toils_General.Do(FinishPreserve);
    }

    private void FinishPreserve()
    {
        Thing corpse = Corpse;
        if (corpse == null) return;

        CompPreserved comp = corpse.TryGetComp<CompPreserved>();
        if (comp == null)
        {
            ModLog.Warn($"MSS_PreserveCorpse: corpse {corpse.LabelShortCap} lost its CompPreserved before finish; aborting.");
            return;
        }

        comp.Preserve();

        // Consume the carried medicine. The pawn should be carrying exactly
        // job.count units at this point.
        if (pawn.carryTracker?.CarriedThing != null)
        {
            pawn.carryTracker.innerContainer.ClearAndDestroyContents();
        }

        ModLog.Verbose($"MSS_PreserveCorpse: preserved {corpse.LabelShortCap} by {pawn.LabelShortCap}.");
    }
}
