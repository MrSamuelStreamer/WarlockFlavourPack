using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace WarlockFlavourPack.Compat.SBC;

/// <summary>
/// Central predicate for the "mystic carrier" floating-baby effect.
///
/// Design: we do NOT define our own hediff. RimWorld's vanilla
/// <c>Verse.Hediff_PsychicBond</c> already tracks a bidirectional psychic link
/// between two pawns via its public <c>target</c> field. Any pawn (warlock or
/// otherwise) who ends up in a vanilla PsychicBond with a baby they are
/// currently custom-carrying via SimpleBabyCarry will automatically get the
/// floating-baby render — no ritual, seeder, or per-save setup required.
/// The Bonnet ↔ The Child pair in the primary Warlock save already has this
/// vanilla bond set up, so the effect activates on load with zero XML.
///
/// The predicate <see cref="WearerBondedToCurrentCarry"/> is called from both
/// <see cref="MapComponent_MysticCarrier"/> (to decide whether to draw the
/// ghost) and <see cref="Patch_SuppressBondedBabyDraw"/> (to decide whether to
/// suppress SBC's own attached-baby draw). Keeping the predicate in one place
/// keeps the two consumers in perfect lockstep.
/// </summary>
public static class MysticBond
{
    // ---- Vanilla PsychicBond hediff/field access, resolved via reflection so a
    //       stray RW rename doesn't crash us — we log-and-disable instead. ----

    private static bool _bondReflectionAttempted;
    private static FieldInfo _psychicBondTargetField;

    // ---- SBC "who is this pawn custom-carrying?" helper, same fail-soft pattern. ----

    private static MethodInfo _sbcGetCarriedBabyForPawn;
    private static bool _sbcLookupAttempted;
    private static bool _sbcWarned;

    /// <summary>
    /// Master kill-switch. When false, both the floating-baby renderer and the
    /// Harmony suppression treat every pawn as un-bonded, so SBC's normal
    /// attached-baby draw returns and no ghost is drawn. Default true, wired to
    /// the user-facing checkbox in the WFP mod settings.
    /// </summary>
    public static bool FeatureEnabled =>
        WarlockFlavourPackMod.Settings?.MysticBondEnabled ?? true;

    /// <summary>
    /// Returns true iff <paramref name="wearer"/> has a vanilla PsychicBond
    /// targeting a baby pawn AND is currently custom-carrying that same baby
    /// via SimpleBabyCarry. When true, <paramref name="bondedBaby"/> is set.
    ///
    /// Short-circuits to false when the feature is disabled in mod settings —
    /// so BOTH the renderer and the Harmony suppression toggle in lockstep
    /// through a single boolean and cannot diverge.
    /// </summary>
    public static bool WearerBondedToCurrentCarry(Pawn wearer, out Pawn bondedBaby)
    {
        bondedBaby = null;
        if (!FeatureEnabled) return false;

        Pawn bonded = GetBondedTarget(wearer);
        if (bonded == null) return false;
        if (!bonded.DevelopmentalStage.Baby()) return false;

        Pawn carried = TryGetCarriedBaby(wearer);
        if (carried == null || carried != bonded) return false;

        bondedBaby = bonded;
        return true;
    }

    /// <summary>
    /// Returns the pawn on the other end of <paramref name="wearer"/>'s vanilla
    /// PsychicBond hediff, or null if they have no PsychicBond hediff (or if
    /// the target field couldn't be reflected — logged once, then silent).
    /// </summary>
    public static Pawn GetBondedTarget(Pawn wearer)
    {
        if (wearer?.health?.hediffSet == null) return null;

        HediffDef def = HediffDefOf.PsychicBond;
        if (def == null) return null;

        Hediff h = wearer.health.hediffSet.GetFirstHediffOfDef(def);
        if (h == null) return null;

        EnsureBondReflection(h);
        if (_psychicBondTargetField == null) return null;

        try
        {
            return _psychicBondTargetField.GetValue(h) as Pawn;
        }
        catch (Exception e)
        {
            ModLog.Error("Mystic bond: reading PsychicBond.target field threw.", e);
            _psychicBondTargetField = null;
            return null;
        }
    }

    private static void EnsureBondReflection(Hediff h)
    {
        if (_bondReflectionAttempted) return;
        _bondReflectionAttempted = true;

        Type t = h.GetType();
        // Walk up the type hierarchy — the field lives on Hediff_PsychicBond but the
        // Hediff instance may be a subclass in some mod. BindingFlags.Instance covers
        // both public and non-public in a walk-up.
        while (t != null && t != typeof(object))
        {
            _psychicBondTargetField = t.GetField(
                "target",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_psychicBondTargetField != null) break;
            t = t.BaseType;
        }

        if (_psychicBondTargetField == null)
        {
            ModLog.Warn("Mystic bond: vanilla Hediff_PsychicBond.target field not found. " +
                        "Floating-baby effect is disabled. (RimWorld version mismatch?)");
        }
    }

    // ---- SBC reflection ----

    public static Pawn TryGetCarriedBaby(Pawn wearer)
    {
        if (!_sbcLookupAttempted)
        {
            _sbcLookupAttempted = true;
            try
            {
                Type helpers = GenTypes.GetTypeInAnyAssembly("b4ttl3m3ds.simplebabycarry.Helper.BabyCarryHelpers");
                if (helpers != null)
                {
                    _sbcGetCarriedBabyForPawn = helpers.GetMethod(
                        "GetCarriedBabyForPawn",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Pawn) },
                        null);
                }
            }
            catch (Exception e)
            {
                ModLog.Error("Mystic bond: reflecting SBC BabyCarryHelpers failed.", e);
            }

            if (_sbcGetCarriedBabyForPawn == null && !_sbcWarned)
            {
                _sbcWarned = true;
                ModLog.Warn("Mystic bond: SBC helper 'GetCarriedBabyForPawn' not found — floating-baby rendering will be inert. (Is SimpleBabyCarry installed?)");
            }
        }

        if (_sbcGetCarriedBabyForPawn == null) return null;

        try
        {
            return _sbcGetCarriedBabyForPawn.Invoke(null, new object[] { wearer }) as Pawn;
        }
        catch (Exception e)
        {
            if (!_sbcWarned)
            {
                _sbcWarned = true;
                ModLog.Error("Mystic bond: SBC helper invocation threw — disabling.", e);
            }
            _sbcGetCarriedBabyForPawn = null;
            return null;
        }
    }
}
