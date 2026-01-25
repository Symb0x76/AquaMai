using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AquaMai.Config.Attributes;
using AquaMai.Core.Attributes;
using HarmonyLib;
using MAI2.Util;
using Manager;
using MelonLoader;
using Process;

namespace AquaMai.Mods.Fix;

// ReSharper disable UnusedMember.Local
[EnableGameVersion(25500)]
[ConfigSection(name: "Long Music 重开修复",
    zh: "修复 Long Music 重开时重复扣除 Track 数",
    exampleHidden: true, defaultOn: true)]
[HarmonyPatch]
public class FixLongMusicRestart
{
    private static bool _isThisGameRestart;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameProcess), "OnStart")]
    public static void PreGameProcessStart()
    {
        _isThisGameRestart = Singleton<GamePlayManager>.Instance.IsQuickRetry();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameProcess), "OnStart")]
    public static void PostGameProcessStart()
    {
        _isThisGameRestart = false;
    }

    [HarmonyPrepare]
    private static bool Prepare() => AccessTools.Method(typeof(DataManager), "IsLong") != null;

    [HarmonyPrefix]
    private static bool PatchIsLong(ref bool result)
    {
        if (!_isThisGameRestart)
        {
            return true;
        }

        var stackTrace = new StackTrace();
        var stackFrames = stackTrace.GetFrames();
        if (stackFrames == null || stackFrames.All(it => it.GetMethod().DeclaringType?.Name != "GameProcess"))
        {
#if DEBUG
            MelonLogger.Msg("[FixLongMusicRestart] IsLong called outside GameProcess, returning true.");
#endif
            return true;
        }

        result = false;
        return false;
    }

    private static MethodBase TargetMethod() => AccessTools.Method(typeof(DataManager), "IsLong");
}