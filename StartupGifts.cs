using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  StartupGifts - 开局发道具（支持数量与参数）
//  ---------------------------------------------------------------------------
//  原理: 玩家"苏醒"（进入世界/重生）时触发 PlayerCamera.OnBecameConscious，
//        我们 postfix 里给玩家的 Body 发放配置的开局道具（每个 Body 只发一次）。
//
//  配置格式:  StartupGifts.GiftItems = "物品:数量:参数,物品:数量:参数"
//             - 参数 > 0 时作为物品耐久（condition）写入
//             - 兼容旧格式（纯物品名，逗号分隔）
// ============================================================================

public static class StartupGifts
{
    // 一条初始物资：名称 + 数量 + 参数（参数>0 时写物品耐久）
    public struct GiftEntry
    {
        public string Name;
        public int Count;
        public float Param;
    }

    private static ConfigEntry<bool> cfgEnabled;
    private static ConfigEntry<string> cfgGiftItems;
    // 防重：用"玩家标识"而非 Body 引用（睡眠苏醒/重生 Body 可能变化导致重复发放）
    private static readonly HashSet<string> GivenPlayers = new HashSet<string>();

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("StartupGifts", "Enabled", true, "玩家苏醒时发放开局道具");
        cfgGiftItems = plugin.Config.Bind("StartupGifts", "GiftItems",
            "bread,splint,adhesivebandage", "开局道具，格式 物品:数量:参数（参数>0=耐久），逗号分隔");

        try
        {
            var harmony = new Harmony("com.cuserverpilot.gifts");
            var target = AccessTools.Method(typeof(PlayerCamera), "OnBecameConscious");
            if (target == null)
            {
                Plugin.Log.LogError("[CU_ServerPilot] 找不到 PlayerCamera.OnBecameConscious");
                return;
            }
            var postfix = typeof(StartupGifts).GetMethod("OnBecameConscious_Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            var info = Harmony.GetPatchInfo(target);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 开局发道具 hook 完成 | postfix 数量={info?.Postfixes?.Count ?? -1}");

            // 新世界生成时清空防重记录（每个新世界开局重新发一次）
            var wgTarget = AccessTools.Method(typeof(WorldGeneration), "Awake");
            if (wgTarget != null)
            {
                var wgPostfix = typeof(StartupGifts).GetMethod("OnWorldAwake_Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(wgTarget, postfix: new HarmonyMethod(wgPostfix));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 开局发道具 hook 失败: {e}");
        }
    }

    // 新世界生成：重置发放记录
    private static void OnWorldAwake_Postfix()
    {
        GivenPlayers.Clear();
        Plugin.Log.LogInfo("[CU_ServerPilot] 新世界生成，重置初始物资发放记录");
    }

    // 稳定玩家标识：KrokMP 玩家名 → Body 名 → 实例 ID（睡眠苏醒保持不变）
    private static string GetPlayerKey(PlayerCamera cam)
    {
        try
        {
            var local = KrokoshaCasualtiesMP.NetPlayer.LOCAL_PLAYER;
            if (local != null && !string.IsNullOrEmpty(local.playername))
                return "p:" + local.playername;
        }
        catch { /* 单机无 KrokMP */ }
        var body = cam?.body;
        if (body != null) return "b:" + body.name;
        return null;
    }

    // UI 显示用：当前 cfg 的开局道具文本
    public static string GetConfigText()
    {
        return cfgGiftItems?.Value ?? "(未配置)";
    }

    // 保存配置（name:count:param 逗号分隔）
    public static void SetGiftItems(string csv)
    {
        if (cfgGiftItems == null) return;
        cfgGiftItems.Value = csv;
        Plugin.Log.LogInfo($"[CU_ServerPilot] 初始物资配置已保存: {csv}");
    }

    public static bool GetGiftEnabled() => cfgEnabled?.Value ?? true;
    public static void SetGiftEnabled(bool value) { if (cfgEnabled != null) cfgEnabled.Value = value; }

    // 解析 cfg → GiftEntry 列表
    public static List<GiftEntry> ParseGifts()
    {
        var result = new List<GiftEntry>();
        string raw = cfgGiftItems?.Value ?? "";
        foreach (var seg in raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = seg.Trim().Split(':');
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;
            var e = new GiftEntry
            {
                Name = parts[0].Trim(),
                Count = parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int c) ? Mathf.Clamp(c, 1, 100) : 1,
                Param = parts.Length >= 3 && float.TryParse(parts[2].Trim(), out float p) ? p : 0f
            };
            result.Add(e);
        }
        return result;
    }

    public static string[] GetGiftItems()
    {
        var list = new List<string>();
        foreach (var e in ParseGifts()) list.Add(e.Name);
        return list.ToArray();
    }

    // 给指定 Body 发放开局道具（返回成功条目数）
    public static int GiveGiftsToBody(Body body)
    {
        int ok = 0;
        foreach (var g in ParseGifts())
        {
            if (ConsoleManager.GiveItemToBody(body, g.Name, g.Count, g.Param)) ok++;
        }
        return ok;
    }

    // 手动触发：给本机玩家立即发放（可重复点）
    public static void GiftNow()
    {
        var body = PlayerCamera.main?.body;
        if (body == null)
        {
            ConsoleManager.SendFeedback("找不到本机玩家（需要在游戏世界中）");
            return;
        }
        var gifts = ParseGifts();
        if (gifts.Count == 0)
        {
            ConsoleManager.SendFeedback("cfg 未配置开局道具");
            return;
        }
        int ok = GiveGiftsToBody(body);
        ConsoleManager.SendFeedback($"手动发放开局道具: 成功 {ok} / 失败 {gifts.Count - ok}");
    }

    // 玩家苏醒 postfix：每个玩家每个世界只发一次（睡眠/重生不再重复）
    private static void OnBecameConscious_Postfix(PlayerCamera __instance)
    {
        if (!cfgEnabled.Value) return;
        var body = __instance?.body;
        if (body == null) return;
        string key = GetPlayerKey(__instance);
        if (key == null || !GivenPlayers.Add(key)) return;

        var gifts = ParseGifts();
        if (gifts.Count == 0) return;

        Plugin.Log.LogInfo($"[CU_ServerPilot] 玩家苏醒，发放开局道具: {cfgGiftItems.Value}");
        foreach (var g in gifts)
        {
            if (ConsoleManager.GiveItemToBody(body, g.Name, g.Count, g.Param))
                Plugin.Log.LogInfo($"[CU_ServerPilot] 已发放: {g.Name} x{g.Count} (参{g.Param})");
            else
                Plugin.Log.LogWarning($"[CU_ServerPilot] 开局道具无效: {g.Name}");
        }
    }
}
