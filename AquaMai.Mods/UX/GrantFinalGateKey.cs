using HarmonyLib;
using AquaMai.Config.Attributes;
using System;
using MelonLoader;
using Manager;
using System.Reflection;
using AquaMai.Core.Attributes;
using MAI2.Util;
using UnityEngine;


namespace AquaMai.Mods.UX;

[ConfigSection(
    en: "Press Enter key to grant KLD Final Gate Key (DXPASS)",
    zh: "按 Enter 键获得 KLD 里门的钥匙 (DXPASS)",
    defaultOn: true
)]
[EnableGameVersion(25500)]
public class GrantFinalGateKey
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(KaleidxScopeProcess), "OnUpdate")]
    public static void KaleidxScope_Key_Postfix(KaleidxScopeProcess instance)
    {
        if (!UnityEngine.Input.GetKeyDown(KeyCode.Return) && !UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) return;

        try
        {
            // 判断玩家的 userData 阶段/entry 状态
            bool[] isEntryArray = new bool[2];
            for (int p = 0; p < 2; p++)
            {
                var ud = Singleton<UserDataManager>.Instance.GetUserData(p);
                if (!ud.IsEntry) continue;
                var phaseDbg = GetUserKaleidxScopePhase(ud);
                if (phaseDbg != 5) continue; // KaleidxScopeManager.KaleidxScopePhase.ClearHopeGate
                isEntryArray[p] = true;
            }
            if (!isEntryArray[0] && !isEntryArray[1]) return; // 没有符合条件的玩家，跳过

            // 反射拿到内部 stateMachine（基于运行时类型）
            var stateMachineField = typeof(KaleidxScopeProcess).GetField("stateMachine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (stateMachineField == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] stateMachine field not found.");
                return;
            }
            var stateMachineObj = stateMachineField.GetValue(instance);
            if (stateMachineObj == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] stateMachine object is null.");
                return;
            }
            var smType = stateMachineObj.GetType();

            // 拿到 monitors（基于运行时类型）
            var monitorsField = smType.GetField("monitorList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (monitorsField == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] monitorList field not found.");
                return;
            }
            var monitors = monitorsField.GetValue(stateMachineObj) as System.Collections.IList;
            if (monitors == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] monitors is null.");
                return;
            }

            // 判断触发开门
            bool triggered = false;
            for (int i = 0; i < monitors.Count && !triggered; i++) // 一次enter只给一人开门
            {
                if (!isEntryArray[i]) continue; // 该玩家不符合条件，跳过
                var userData = Singleton<UserDataManager>.Instance.GetUserData(i);
                var mon = monitors[i];

                // 使用反射获取entry字段（基于运行时类型）
                bool monitorEntry = false;
                if (mon != null)
                {
                    var entryField = mon.GetType().GetField("entry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    monitorEntry = entryField != null ? (bool)entryField.GetValue(mon) : false;
                }
                if (monitorEntry) continue; // 已经entry了，不需要刷钥匙

                var keyData = GetUserKaleidxScopeData(userData, 10);
                bool hasKey = keyData != null && GetIsKeyFound(keyData);
                if (hasKey) continue; // 已经有钥匙了，不需要刷钥匙

                // 尝试触发开门（使用运行时类型）
                if (TriggerOpenLastBoss(stateMachineObj, i)) continue;
                triggered = true;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[GrantFinalGateKey] error: {ex}");
        }
    }





    // 全局变量 防止多次开门
    private static bool[] _playerHasOpened = new bool[2];
    
    // 触发开门（基于运行时类型）
    private static bool TriggerOpenLastBoss(object stateMachineObj, int playerIndex)
    {
        try
        {
            if (_playerHasOpened[playerIndex]) return false;
            if (stateMachineObj == null) return false;
            var smType = stateMachineObj.GetType();

            // 找到内部类型 OpenLastBoss 和 枚举 StateType
            var openType = smType.GetNestedType("OpenLastBoss", BindingFlags.Public | BindingFlags.NonPublic);
            if (openType == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] OpenLastBoss type not found.");
                return false;
            }
            var stateTypeEnum = smType.GetNestedType("StateType", BindingFlags.Public | BindingFlags.NonPublic);
            if (stateTypeEnum == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] StateType not found.");
                return false;
            }

            // 构造 StateType.OpenLastBoss = (int)3
            object openLastBossEnumValue = Enum.ToObject(stateTypeEnum, 3);

            // 构造新状态实例
            var ctor = openType.GetConstructor(new System.Type[] { smType, stateTypeEnum, typeof(int) });
            if (ctor == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] OpenLastBoss ctor not found.");
                return false;
            }
            object newState = ctor.Invoke(new object[] { stateMachineObj, openLastBossEnumValue, playerIndex });

            // 调用 ChangeState（不通过泛型定义，直接在实例类型上找）
            MethodInfo changeStateMethod = null;
            foreach (var m in smType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "ChangeState")
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1)
                    {
                        changeStateMethod = m;
                        break;
                    }
                }
            }
            if (changeStateMethod == null)
            {
                MelonLogger.Msg("[GrantFinalGateKey] ChangeState method not found.");
                return false;
            }
            changeStateMethod.Invoke(stateMachineObj, new object[] { newState });

            MelonLogger.Msg($"[GrantFinalGateKey] P{playerIndex + 1} Grant Final Gate Key.");
            _playerHasOpened[playerIndex] = true;
            return true;
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[GrantFinalGateKey] P{playerIndex + 1} Grant Final Gate Key Error: {e}");
            return false;
        }
    }
    
    // 重置开门状态
    [HarmonyPrefix]
    [HarmonyPatch(typeof(KaleidxScopeProcess), "OnStart")]
    public static void KaleidxScope_OnStart_Prefix()
    {
        _playerHasOpened[0] = false;
        _playerHasOpened[1] = false;
    }

    private static int GetUserKaleidxScopePhase(UserData userData)
    {
        var method = AccessTools.Method(typeof(KaleidxScopeManager), "GetUserKaleidxScopePhase", [typeof(UserData)]);
        if (method != null)
        {
            return Convert.ToInt32(method.Invoke(Singleton<KaleidxScopeManager>.Instance, new object[] { userData }) ?? -1);
        }

        MelonLogger.Msg("[GrantFinalGateKey] GetUserKaleidxScopePhase not found, returning -1");
        return -1;
    }

    private static object GetUserKaleidxScopeData(UserData userData, int index)
    {
        var method = AccessTools.Method(typeof(UserData), "GetUserKaleidxScopeData", [typeof(int)]);
        if (method != null)
        {
            return method.Invoke(userData, new object[] { index });
        }

        MelonLogger.Msg("[GrantFinalGateKey] GetUserKaleidxScopeData not found");
        return null;
    }

    private static bool GetIsKeyFound(object keyData)
    {
        if (keyData == null) return false;
        var prop = AccessTools.Property(keyData.GetType(), "isKeyFound") ?? AccessTools.Property(keyData.GetType(), "IsKeyFound");
        if (prop?.GetMethod != null)
        {
            return (bool)(prop.GetValue(keyData) ?? false);
        }

        var field = AccessTools.Field(keyData.GetType(), "isKeyFound") ?? AccessTools.Field(keyData.GetType(), "IsKeyFound");
        if (field != null)
        {
            return (bool)(field.GetValue(keyData) ?? false);
        }

        return false;
    }
}
