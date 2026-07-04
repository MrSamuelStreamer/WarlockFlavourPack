using System;
using System.Diagnostics;

namespace WarlockFlavourPack;

static class ModLog
{
    private const string Prefix = "<color=#8a2be2>[WarlockFlavourPack]</color>";

    [Conditional("DEBUG")]
    public static void Debug(string msg)
    {
        Verse.Log.Message($"{Prefix} {msg ?? "<null>"}");
    }

    public static void Verbose(string msg)
    {
        if (WarlockFlavourPackMod.Settings != null && WarlockFlavourPackMod.Settings.VerboseLogging)
            Verse.Log.Message($"{Prefix} {msg ?? "<null>"}");
    }

    public static void Log(string msg)
    {
        Verse.Log.Message($"{Prefix} {msg ?? "<null>"}");
    }

    public static void Warn(string msg)
    {
        Verse.Log.Warning($"{Prefix} {msg ?? "<null>"}");
    }

    public static void Error(string msg, Exception e = null)
    {
        Verse.Log.Error($"{Prefix} {msg ?? "<null>"}");
        if (e != null)
            Verse.Log.Error(e.ToString());
    }
}
