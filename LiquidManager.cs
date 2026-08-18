using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  LiquidManager - 液体管理
//  ---------------------------------------------------------------------------
//  液体名单: Liquids.Registry（static Dictionary<string, LiquidType>）—— 游戏液体注册表
//  添加液体: 玩家主手物品的 WaterContainerItem.AddLiquid(液体id, 毫升) -> 返回实际加入量
//  （实现模仿游戏控制台加液体命令 ConsoleScript.<RegisterAllCommands>b__51_30：
//   主手物品 = Body.GetItem(0)，再 GetComponent<WaterContainerItem>()）
// ============================================================================

public static class LiquidManager
{
    private static List<string> _liquids;

    public static List<string> GetLiquidList()
    {
        if (_liquids != null) return _liquids;
        _liquids = new List<string>();
        try
        {
            var field = AccessTools.Field(typeof(Liquids), "Registry");
            var reg = field?.GetValue(null) as IDictionary;
            if (reg != null)
            {
                foreach (var k in reg.Keys)
                    if (k != null && !string.IsNullOrEmpty(k.ToString())) _liquids.Add(k.ToString());
            }
            _liquids.Sort(StringComparer.OrdinalIgnoreCase);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 液体表加载: {_liquids.Count} 种（Liquids.Registry）");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 读取液体表失败: {e.Message}");
        }
        return _liquids;
    }

    // 导出用：液体 id + 游戏显示名（localeName），格式 "id|localeName"
    internal static List<string> GetLiquidDetails()
    {
        var list = new List<string>();
        try
        {
            var field = AccessTools.Field(typeof(Liquids), "Registry");
            var reg = field?.GetValue(null) as IDictionary;
            if (reg != null)
            {
                foreach (var k in reg.Keys)
                {
                    string id = k?.ToString() ?? "";
                    string loc = "";
                    try
                    {
                        if (reg[k] is LiquidType lt) loc = lt.localeName ?? "";
                    }
                    catch { /* localeName 不可达则留空 */ }
                    list.Add($"{id}|{loc}");
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 读取液体详情失败: {e.Message}");
        }
        return list;
    }

    // 给玩家主手容器添加液体
    public static bool AddToHandContainer(string liquidId, float amountMl)
    {
        var body = PlayerCamera.main?.body;
        if (body == null)
        {
            ConsoleManager.SendFeedback("找不到本机玩家（需要在游戏世界中）");
            return false;
        }
        var item = body.GetItem(0);   // 主手
        if (item == null)
        {
            ConsoleManager.SendFeedback("主手没有物品");
            return false;
        }
        var wc = item.GetComponent<WaterContainerItem>();
        if (wc == null)
        {
            ConsoleManager.SendFeedback($"{item.fullName} 不是液体容器");
            return false;
        }
        float space = wc.SpaceLeft;
        if (space <= 0f)
        {
            ConsoleManager.SendFeedback($"{item.fullName} 已满");
            return false;
        }
        float added = wc.AddLiquid(liquidId, Mathf.Min(amountMl, space));
        ConsoleManager.SendFeedback($"已添加 {Translations.DisplayName(liquidId)} {added}ml → {item.fullName}（剩余 {Mathf.Max(0f, space - added)}ml）");
        return true;
    }
}
