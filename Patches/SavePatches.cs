using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Saves;

namespace STS2Trade.Patches;

/// <summary>
/// When the "nosave" command-line argument is present, skip all save file writes.
/// This prevents file lock conflicts when running multiple game instances
/// under the same Steam account for testing.
///
/// SaveRun returns Task, so we must set __result to a completed Task
/// to avoid NullReferenceException when the caller awaits it.
/// </summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
public static class DisableSaveRunPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref Task __result)
    {
        if (CommandLineHelper.HasArg("nosave"))
        {
            __result = Task.CompletedTask;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
public static class DisableSaveProgressPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        if (CommandLineHelper.HasArg("nosave"))
            return false;
        return true;
    }
}
