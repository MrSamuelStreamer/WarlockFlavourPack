using RimWorld;
using Verse;
using Verse.AI;

namespace WarlockFlavourPack.Preservation;

/// <summary>
/// Scans corpses for ones the player has marked for preservation and issues
/// the <see cref="JobDriver_PreserveCorpse"/> job (Hauling work type).
///
/// Requires 3 herbal medicine reachable from the pawn; if none available,
/// no job is issued and the corpse stays marked until medicine appears.
/// </summary>
public class WorkGiver_PreserveCorpse : WorkGiver_Scanner
{
    private const int MedicineNeeded = 3;

    public override ThingRequest PotentialWorkThingRequest =>
        ThingRequest.ForGroup(ThingRequestGroup.Corpse);

    public override PathEndMode PathEndMode => PathEndMode.Touch;

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        // Cheap map-wide bail: no herbal medicine anywhere → nothing to do.
        return pawn.Map.listerThings.ThingsOfDef(ThingDefOf.MedicineHerbal).Count == 0;
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Corpse corpse) return false;
        CompPreserved comp = corpse.TryGetComp<CompPreserved>();
        if (comp == null) return false;
        if (!comp.markedForPreservation || comp.isPreserved) return false;
        if (corpse.IsForbidden(pawn)) return false;
        if (corpse.IsBurning()) return false;
        if (!pawn.CanReserve(corpse, 1, -1, null, forced)) return false;
        if (!pawn.CanReach(corpse, PathEndMode.Touch, Danger.Deadly)) return false;

        return FindMedicine(pawn, corpse, forced) != null;
    }

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Corpse corpse) return null;
        Thing medicine = FindMedicine(pawn, corpse, forced);
        if (medicine == null) return null;

        Job job = JobMaker.MakeJob(MSSDefOf.MSS_PreserveCorpse, corpse, medicine);
        job.count = MedicineNeeded;
        return job;
    }

    private static Thing FindMedicine(Pawn pawn, Corpse corpse, bool forced)
    {
        // Closest herbal medicine stack with count >= 3 that the pawn can
        // reserve `MedicineNeeded` units from.
        return GenClosest.ClosestThingReachable(
            pawn.Position,
            pawn.Map,
            ThingRequest.ForDef(ThingDefOf.MedicineHerbal),
            PathEndMode.ClosestTouch,
            TraverseParms.For(pawn),
            9999f,
            (Thing m) =>
                m.stackCount >= MedicineNeeded &&
                !m.IsForbidden(pawn) &&
                pawn.CanReserve(m, 1, MedicineNeeded, null, forced));
    }
}
