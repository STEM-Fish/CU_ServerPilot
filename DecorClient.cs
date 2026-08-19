using System;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using Newtonsoft.Json;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  DecorClient - 装饰物客户端同步接收（v0.10.7）
//  ---------------------------------------------------------------------------
//  服务器（主机）生成/擦除装饰时通过 KrokMP 聊天通道广播 [CUSP-DECOR]，
//  客户端（同样安装本插件）拦截该消息 → 本地生成同款装饰（KrokMP 本身
//  不同步服务器实例化的装饰实体）。要求客户端也装 CU_ServerPilot。
// ============================================================================

public static class DecorClient
{
    public static void Init(Plugin plugin)
    {
        try
        {
            var harmony = new Harmony("com.cuserverpilot.decorclient");
            var target = AccessTools.Method(typeof(Chat), "LogMessage");
            if (target == null)
            {
                Plugin.Log.LogError("[CU_ServerPilot] 找不到 Chat.LogMessage（装饰同步不可用）");
                return;
            }
            var prefix = typeof(DecorClient).GetMethod("LogMessage_Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            var info = Harmony.GetPatchInfo(target);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 装饰同步接收 hook 完成 | prefix 数量={info?.Prefixes?.Count ?? -1}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 装饰同步接收 hook 失败: {e}");
        }
    }

    // 拦截聊天消息：以 [CUSP-DECOR] 开头 → 解析并本地处理 → 不显示聊天框
    private static bool LogMessage_Prefix(string plrname, string msg, ref bool richtext)
    {
        if (string.IsNullOrEmpty(msg) || !msg.StartsWith(DecorSpawner.DecorPrefix))
            return true;   // 普通消息照常显示

        try
        {
            string json = msg.Substring(DecorSpawner.DecorPrefix.Length);
            var p = JsonConvert.DeserializeObject<DecorPayload>(json);
            if (p == null) return false;

            switch (p.op)
            {
                case "add":
                    DecorSpawner.ClientSpawn(p.id, p.x, p.y, p.rot);
                    break;
                case "remove":
                    DecorSpawner.EraseAt(new Vector2(p.x, p.y));
                    break;
                case "sync":
                    DecorSpawner.ClientSyncAll(p.list);
                    break;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 客户端装饰消息解析失败: {e.Message}");
        }
        return false;   // 系统消息不显示在聊天框
    }
}
