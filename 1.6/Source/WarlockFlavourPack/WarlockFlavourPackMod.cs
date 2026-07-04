using HarmonyLib;
using UnityEngine;
using Verse;

namespace WarlockFlavourPack;

public class WarlockFlavourPackMod : Mod
{
    public static Settings Settings { get; private set; }

    public WarlockFlavourPackMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<Settings>();
#if DEBUG
        Harmony.DEBUG = true;
#endif
        Harmony harmony = new Harmony("MSS.WFP.main");
        harmony.PatchAll();
        ModLog.Log("Initialised.");
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        base.DoSettingsWindowContents(inRect);
        Settings.DoWindowContents(inRect);
    }

    public override string SettingsCategory()
    {
        return "WarlockFlavourPack_SettingsCategory".Translate();
    }
}
