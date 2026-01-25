using System.Reflection;
using AquaMai.Config.Attributes;
using AquaMai.Core.Attributes;
using HarmonyLib;

namespace AquaMai.Mods.UX;

[ConfigSection(
    name: "Festa 开关",
    zh: "控制 “Festa” 模式 UI 的显示。如不开启，则不会更改设置，由 Event 或者服务器控制",
    en:
    "Control the display of “Festa” mode UI. If not enabled, the settings will not be changed, and Event or server will control it")]
[EnableGameVersion(26000)]
public static class FestaControl
{
    [ConfigEntry(
        name: "启用 Festa",
        zh: "是否显示 “Festa” 模式 UI",
        en: "Whether to display “Festa” mode UI")]
    public static readonly bool IsFesta = false;
}

[HarmonyPatch]
public static class FestaInitializePatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("FestaManager") ?? AccessTools.TypeByName("Manager.FestaManager");
        return type == null ? null : AccessTools.Method(type, "Initalize");
    }

    [HarmonyPostfix]
    private static void Postfix(object __instance)
    {
        var property = AccessTools.Property(__instance.GetType(), "isOpenFesta");
        if (property?.SetMethod != null)
        {
            property.SetValue(__instance, FestaControl.IsFesta);
            return;
        }

        Traverse.Create(__instance).Property<bool>("isOpenFesta").Value = FestaControl.IsFesta;
    }
}

[HarmonyPatch]
public static class FestaBorderRewardListPatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("Process.GetPresentProcess") ?? AccessTools.TypeByName("GetPresentProcess");
        return type == null ? null : AccessTools.Method(type, "CreateFestaBorderRewardListOpenFesta");
    }

    [HarmonyPrefix]
    private static bool Prefix() => false;
}

[HarmonyPatch]
public static class FestaCanAttendPatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("FestaManager") ?? AccessTools.TypeByName("Manager.FestaManager");
        return type == null ? null : AccessTools.Method(type, "CanAttendFesta");
    }

    [HarmonyPrefix]
    private static bool Prefix(ref bool result)
    {
        result = false;
        return false;
    }
}