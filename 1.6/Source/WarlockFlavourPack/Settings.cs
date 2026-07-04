using UnityEngine;
using Verse;

namespace WarlockFlavourPack;

public class Settings : ModSettings
{
    public bool VerboseLogging = false;
    /// <summary>
    /// Master switch for the mystic-bond floating-baby renderer + SBC baby-draw suppression.
    /// When false, both the ghost draw AND the Harmony suppression turn off together, so a
    /// pawn who would otherwise be bonded simply carries their baby the normal SBC way.
    /// </summary>
    public bool MysticBondEnabled = true;

    public void DoWindowContents(Rect wrect)
    {
        Listing_Standard options = new();
        options.Begin(wrect);

        options.CheckboxLabeled(
            "WarlockFlavourPack_Settings_MysticBondEnabled".Translate(),
            ref MysticBondEnabled,
            "WarlockFlavourPack_Settings_MysticBondEnabled_Tooltip".Translate());
        options.Gap();

        options.CheckboxLabeled(
            "WarlockFlavourPack_Settings_VerboseLogging".Translate(),
            ref VerboseLogging,
            "WarlockFlavourPack_Settings_VerboseLogging_Tooltip".Translate());
        options.Gap();

        options.End();
    }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref VerboseLogging, "verboseLogging", false);
        Scribe_Values.Look(ref MysticBondEnabled, "mysticBondEnabled", true);
    }
}
