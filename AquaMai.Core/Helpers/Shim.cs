using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DB;
using HarmonyLib;
using MAI2.Util;
using Manager;
using Manager.UserDatas;
using MelonLoader;
using Net;
using Net.Packet;
using Net.Packet.Mai2;
using Net.VO;

namespace AquaMai.Core.Helpers;

public static class Shim
{
    private static T Iife<T>(Func<T> func) => func();

    public static readonly string ApiSuffix = Iife(() =>
    {
        try
        {
            var baseNetQueryConstructor = typeof(NetQuery<VOSerializer, VOSerializer>)
                .GetConstructors()
                .First();
            return ((INetQuery)baseNetQueryConstructor.Invoke(
            [
                .. baseNetQueryConstructor
                    .GetParameters()
                    .Select((parameter, i) => i == 0 ? "" : parameter.DefaultValue)
            ])).Api;
        }
        catch (Exception e)
        {
            MelonLogger.Error($"Failed to resolve the API suffix: {e}");
            return null;
        }
    });

    public static bool NetHttpClientDecryptsResponse { get; private set; }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NetHttpClient), "ReadCallback")]
    public static IEnumerable<CodeInstruction> NetHttpClientReadCallbackTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var instList = instructions.ToList();
        NetHttpClientDecryptsResponse = instList.Any(inst =>
            inst.opcode == OpCodes.Callvirt &&
            inst.operand is MethodInfo { Name: "Decrypt" });
        return instList; // No changes
    }

    public static string RemoveApiSuffix(string api)
    {
        return !string.IsNullOrEmpty(ApiSuffix) && api.EndsWith(ApiSuffix)
            ? api.Substring(0, api.Length - ApiSuffix.Length)
            : api;
    }

    public static byte[] DecryptNetPacketBody(byte[] encrypted)
    {
        var type = AccessTools.TypeByName("Net.CipherAES");
        if (type == null)
        {
            MelonLogger.Warning("No matching Net.CipherAES class found");
            return encrypted;
        }

        var methods = type.GetMethods();
        var method = methods.FirstOrDefault(it => it.Name == "Decrypt" && it.GetParameters().Length <= 2);
        if (method == null)
        {
            MelonLogger.Warning("No matching Net.CipherAES.Decrypt() method found");
            // Assume encryption code is removed.
            return encrypted;
        }

        if (method.GetParameters().Length == 1)
        {
            return (byte[])method.Invoke(null, [encrypted]);
        }
        else
        {
            object[] args = [encrypted, null];
            var result = (bool)method.Invoke(null, args);
            if (result)
            {
                return (byte[])args[1];
            }
            else
            {
                throw new Exception("Net.CipherAES.Decrypt() failed");
            }
        }
    }

    public static byte[] EncryptNetPacketBody(byte[] data)
    {
        var methods = AccessTools.TypeByName("Net.CipherAES").GetMethods();
        var method = methods.FirstOrDefault(it => it.Name == "Encrypt" && it.GetParameters().Length <= 2);
        if (method == null)
        {
            MelonLogger.Warning("No matching Net.CipherAES.Encrypt() method found");
            // Assume encryption code is removed.
            return data;
        }

        if (method.GetParameters().Length == 1)
        {
            return (byte[])method.Invoke(null, [data]);
        }
        else
        {
            object[] args = [data, null];
            var result = (bool)method.Invoke(null, args);
            if (result)
            {
                return (byte[])args[1];
            }
            else
            {
                throw new Exception("Net.CipherAES.Encrypt() failed");
            }
        }
    }

    public delegate string GetAccessTokenMethod(int index);

    public static readonly GetAccessTokenMethod GetAccessToken = Iife<GetAccessTokenMethod>(() =>
    {
        var tOperationManager = Traverse.Create(Singleton<OperationManager>.Instance);
        var tGetAccessToken = tOperationManager.Method("GetAccessToken", [typeof(int)]);
        if (!tGetAccessToken.MethodExists())
        {
            return _ => throw new MissingMethodException("No matching OperationManager.GetAccessToken() method found");
        }

        return (index) => tGetAccessToken.GetValue<string>(index);
    });

    public delegate PacketUploadUserPlaylog PacketUploadUserPlaylogCreator(int index, UserData src, int trackNo,
        Action<int> onDone, Action<PacketStatus> onError = null);

    public static readonly PacketUploadUserPlaylogCreator CreatePacketUploadUserPlaylog =
        Iife<PacketUploadUserPlaylogCreator>(() =>
        {
            var type = typeof(PacketUploadUserPlaylog);
            if (type.GetConstructor([
                    typeof(int), typeof(UserData), typeof(int), typeof(Action<int>), typeof(Action<PacketStatus>)
                ]) is { } ctor1)
            {
                return (index, src, trackNo, onDone, onError) =>
                {
                    var args = new object[] { index, src, trackNo, onDone, onError };
                    return (PacketUploadUserPlaylog)ctor1.Invoke(args);
                };
            }
            else if (type.GetConstructor([
                         typeof(int), typeof(UserData), typeof(int), typeof(string), typeof(Action<int>),
                         typeof(Action<PacketStatus>)
                     ]) is { } ctor2)
            {
                return (index, src, trackNo, onDone, onError) =>
                {
                    var accessToken = GetAccessToken(index);
                    var args = new object[] { index, src, trackNo, accessToken, onDone, onError };
                    return (PacketUploadUserPlaylog)ctor2.Invoke(args);
                };
            }
            else
            {
                throw new MissingMethodException("No matching PacketUploadUserPlaylog constructor found");
            }
        });

    public delegate PacketUpsertUserAll PacketUpsertUserAllCreator(int index, UserData src, Action<int> onDone,
        Action<PacketStatus> onError = null);

    public static readonly PacketUpsertUserAllCreator CreatePacketUpsertUserAll = Iife<PacketUpsertUserAllCreator>(() =>
    {
        var type = typeof(PacketUpsertUserAll);
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (TryCreatePacketUpsertUserAllFactory(ctor, out var factory))
            {
                return factory;
            }
        }

        MelonLogger.Error("No usable PacketUpsertUserAll constructor found; upsert will be skipped.");
        return (index, src, onDone, onError) =>
        {
            MelonLogger.Error("PacketUpsertUserAll unavailable; skipping upsert.");
            onError?.Invoke(default);
            onDone?.Invoke(-1);
            return null;
        };
    });

    private static bool TryCreatePacketUpsertUserAllFactory(ConstructorInfo ctor,
        out PacketUpsertUserAllCreator factory)
    {
        var parameters = ctor.GetParameters();
        factory = (index, src, onDone, onError) =>
        {
            var args = new object[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                args[i] = p.ParameterType switch
                {
                    var t when t == typeof(int) => index,
                    var t when t == typeof(UserData) => src,
                    var t when t == typeof(string) => SafeGetAccessToken(index),
                    var t when t == typeof(Action<int>) => onDone,
                    var t when t == typeof(Action<PacketStatus>) => onError,
                    _ when p.HasDefaultValue => p.DefaultValue,
                    _ when p.ParameterType.IsValueType => Activator.CreateInstance(p.ParameterType),
                    _ => null
                };

                if (args[i] == null && !p.HasDefaultValue && p.ParameterType.IsValueType)
                {
                    // Value types must not be null; constructor is incompatible.
                    return null;
                }
            }

            try
            {
                return (PacketUpsertUserAll)ctor.Invoke(args);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"PacketUpsertUserAll ctor invoke failed ({ctor}): {ex.Message}");
                return null;
            }
        };

        // Prefer a factory where all parameters could be filled without using null for required reference types.
        var hasUnmappedRequiredReference = parameters.Any(p =>
            argsRequiredReferenceUnmapped(p.ParameterType, p.HasDefaultValue));

        return !hasUnmappedRequiredReference;

        bool argsRequiredReferenceUnmapped(System.Type type, bool hasDefault) =>
            type != typeof(int) &&
            type != typeof(UserData) &&
            type != typeof(string) &&
            type != typeof(Action<int>) &&
            type != typeof(Action<PacketStatus>) &&
            !hasDefault &&
            !type.IsValueType;
    }

    private static string SafeGetAccessToken(int index)
    {
        try
        {
            return GetAccessToken(index);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"GetAccessToken failed: {e.Message}");
            return null;
        }
    }

    public static IEnumerable<UserScore>[] GetUserScoreList(UserData userData)
    {
        var tUserData = Traverse.Create(userData);

        var tScoreList = tUserData.Property("ScoreList");
        if (tScoreList.PropertyExists())
        {
            var scoreList = tScoreList.GetValue<List<UserScore>[]>() ?? Array.Empty<List<UserScore>>();
            return scoreList.Select(list => (IEnumerable<UserScore>)list).ToArray();
        }

        var tScoreDic = tUserData.Property("ScoreDic");
        if (tScoreDic.PropertyExists())
        {
            var scoreDic = tScoreDic.GetValue<Dictionary<int, UserScore>[]>() ??
                           Array.Empty<Dictionary<int, UserScore>>();
            return scoreDic.Select(dic => (IEnumerable<UserScore>)dic.Values).ToArray();
        }

        throw new MissingFieldException("No matching UserData.ScoreList/ScoreDic found");
    }

    private static ConstructorInfo _userRateCtor =
        typeof(UserRate).GetConstructors().First(it => it.GetParameters().Length is 4 or 5);

    public static UserRate CreateUserRate(int musicId, int level, uint achievement, uint romVersion,
        PlayComboflagID comboflagId)
    {
        if (_userRateCtor.GetParameters().Length == 5)
        {
            return (UserRate)_userRateCtor.Invoke([musicId, level, achievement, romVersion, comboflagId]);
        }

        return (UserRate)_userRateCtor.Invoke([musicId, level, achievement, romVersion]);
    }

    private static bool _localIsNormalMode = true;
    public static readonly Action<bool> SetGameManagerIsNormalMode = CreateSetGameManagerIsNormalMode();
    private static readonly Func<bool> GameManagerIsNormalModeGetter = CreateGetGameManagerIsNormalMode();
    public static bool GameManagerIsNormalMode => GameManagerIsNormalModeGetter();

    private static Action<bool> CreateSetGameManagerIsNormalMode()
    {
        var property = AccessTools.Property(typeof(GameManager), "IsNormalMode");
        if (property?.SetMethod != null)
        {
            return value => property.SetValue(null, value);
        }

        var field = AccessTools.Field(typeof(GameManager), "IsNormalMode");
        if (field != null)
        {
            return value => field.SetValue(null, value);
        }

        MelonLogger.Warning("GameManager.IsNormalMode not found; using local fallback");
        return value => _localIsNormalMode = value;
    }

    private static Func<bool> CreateGetGameManagerIsNormalMode()
    {
        var property = AccessTools.Property(typeof(GameManager), "IsNormalMode");
        if (property?.GetMethod != null)
        {
            return () => (bool)(property.GetValue(null) ?? false);
        }

        var field = AccessTools.Field(typeof(GameManager), "IsNormalMode");
        if (field != null)
        {
            return () => (bool)(field.GetValue(null) ?? false);
        }

        return () => _localIsNormalMode;
    }

    private static readonly Func<bool> IsKaleidxScopeModeGetter =
        GameInfo.GameVersion < 25000 ? () => false : () => GameManager.IsKaleidxScopeMode;

    public static bool IsKaleidxScopeMode => IsKaleidxScopeModeGetter();
}