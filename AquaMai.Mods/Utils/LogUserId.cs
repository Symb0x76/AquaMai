using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AquaMai.Config.Attributes;
using HarmonyLib;
using MelonLoader;
using Net.Packet.Mai2;
using Net.VO.Mai2;

namespace AquaMai.Mods.Utils;

[ConfigSection(
    name: "用户 ID 日志",
    en: "Log user ID on login.",
    zh: "登录时将 UserID 输出到日志")]
public class LogUserId
{
}

[HarmonyPatch]
public static class LogUserIdPatch
{
    // Patch any PacketGetUserPreview ctor whose first parameter is a userId (ulong).
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var ctors = typeof(PacketGetUserPreview).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var targets = ctors.Where(ctor =>
        {
            var parameters = ctor.GetParameters();
            return parameters.Length >= 1 && parameters[0].ParameterType == typeof(ulong);
        }).ToArray();

        if (targets.Length == 0)
        {
            MelonLogger.Warning("[LogUserId] No PacketGetUserPreview ctor with userId found; skipping patch.");
        }

        return targets;
    }

    [HarmonyPostfix]
    private static void Postfix(ulong __0)
    {
        MelonLogger.Msg($"[LogUserId] UserLogin: {__0}");
    }
}
