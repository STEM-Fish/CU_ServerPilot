using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using Newtonsoft.Json;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  MoodleClient - 自定义状态（客户端侧：接收 + 本地渲染）
//  ---------------------------------------------------------------------------
//  原理：服务器通过 KrokMP 聊天通道广播 "[CUSP-MOODLE]{target,ids}" 消息，
//        客户端拦截 Chat.LogMessage（聊天消息显示入口），解析后：
//         1. target 匹配自己（@a 全体 或 自己的名字）才处理
//         2. 本地 AddMoodle（客户端自己的 MoodleManager 渲染）
//         3. 本地保持（patch UpdateMoodles postfix，游戏重建后重挂）
//  要求：客户端也需要安装 CU_ServerPilot（两端同 dll）
// ============================================================================

public static class MoodleClient
{
    private static readonly HashSet<string> ActiveIds = new HashSet<string>();
    private static bool _hooked;

    public static void Init(Plugin plugin)
    {
        try
        {
            if (_hooked) return;
            _hooked = true;
            var harmony = new Harmony("com.cuserverpilot.moodleclient");

            // 拦截聊天消息显示（服务器广播的 CUSP 消息不显示到聊天框）
            var logMsg = AccessTools.Method(typeof(Chat), "LogMessage");
            if (logMsg != null)
            {
                harmony.Patch(logMsg, prefix: new HarmonyMethod(
                    typeof(MoodleClient).GetMethod("LogMessage_Prefix", BindingFlags.NonPublic | BindingFlags.Static)));
            }

            // 客户端本地保持（游戏每 0.5s 重建状态后重挂）
            var um = AccessTools.Method(typeof(MoodleManager), "UpdateMoodles");
            if (um != null)
            {
                harmony.Patch(um, postfix: new HarmonyMethod(
                    typeof(MoodleClient).GetMethod("UpdateMoodles_Postfix", BindingFlags.NonPublic | BindingFlags.Static)));
            }

            var li = logMsg != null ? Harmony.GetPatchInfo(logMsg) : null;
            var ui = um != null ? Harmony.GetPatchInfo(um) : null;
            Plugin.Log.LogInfo($"[CU_ServerPilot] 客户端状态接收 hook 完成 | LogMessage={li?.Prefixes?.Count ?? -1}, UpdateMoodles={ui?.Postfixes?.Count ?? -1}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 客户端状态接收 hook 失败: {e}");
        }
    }

    // 拦截聊天消息：CUSP 状态消息不显示，解析后本地渲染
    private static bool LogMessage_Prefix(string plrname, string msg, ref bool richtext)
    {
        if (msg != null && msg.StartsWith(CustomMoodles.CuspPrefix))
        {
            try { HandlePayload(msg); }
            catch (Exception e) { Plugin.Log.LogWarning($"[CU_ServerPilot] 状态消息解析失败: {e.Message}"); }
            return false;   // 不显示到聊天框
        }
        return true;
    }

    private static void HandlePayload(string payload)
    {
        string json = payload.Substring(CustomMoodles.CuspPrefix.Length);
        var data = JsonConvert.DeserializeObject<MoodlePayload>(json);
        if (data?.ids == null || data.ids.Count == 0) return;

        // target 匹配：@a 全体 或 自己的名字
        string myName = GetMyName();
        if (data.target != "@a 全体" && data.target != "@a" &&
            data.target != myName && data.target != "@" + myName)
            return;

        ActiveIds.Clear();
        foreach (var id in data.ids) ActiveIds.Add(id);
        ApplyLocal();
        Plugin.Log.LogInfo($"[CU_ServerPilot] 客户端收到状态同步: [{string.Join(",", ActiveIds)}]");
    }

    // 客户端本地渲染（立即）
    private static void ApplyLocal()
    {
        if (ActiveIds.Count == 0) return;
        var body = PlayerCamera.main?.body;
        if (body == null) return;
        var mm = CustomMoodles.GetMoodleManagerFor(body);
        if (mm == null) return;
        foreach (var id in ActiveIds)
        {
            var def = CustomMoodles.GetMoodles().FirstOrDefault(m => m.id == id);
            if (def != null) CustomMoodles.ImmediateAdd(mm, def);
        }
    }

    // 游戏重建状态后，客户端重新挂载（保持显示）
    private static void UpdateMoodles_Postfix(MoodleManager __instance)
    {
        if (ActiveIds.Count == 0) return;
        ApplyLocal();
    }

    private static string GetMyName()
    {
        try
        {
            var local = NetPlayer.LOCAL_PLAYER;
            if (local != null && !string.IsNullOrEmpty(local.playername)) return local.playername;
        }
        catch { }
        return PlayerCamera.main?.body?.name ?? "";
    }
}
