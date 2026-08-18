using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  ItemCatalog - 游戏真实物品名目录
//  ---------------------------------------------------------------------------
//  完整物品来源（按优先级合并）:
//   1. Item.GlobalItems（Dictionary<string, ItemInfo>）—— 全物品表（含控制台可
//      spawn 的一切物品，如 12 号霰弹弹药 boxof12gauge 等战利品池没有的）
//   2. ItemLootPool.pool（战利品池）—— 补充
//  注意: 两张表都由 WorldGeneration.Awake -> Item.SetupItems() 填充，
//        所以首次读取可能为空，这里带 2 秒自动重试。
// ============================================================================

public static class ItemCatalog
{
    private static List<string> _items;
    private static float _lastTry = -999f;

    public static List<string> GetAllItems()
    {
        if (_items != null && _items.Count > 0) return _items;
        if (Time.time - _lastTry < 2f) return _items ?? new List<string>();
        _lastTry = Time.time;

        var loaded = LoadFromGame();
        if (loaded.Count > 0)
        {
            _items = loaded;
            Plugin.Log.LogInfo($"[CU_ServerPilot] 物品表加载: {_items.Count} 个（Item.GlobalItems + ItemLootPool.pool）");
        }
        return _items ?? new List<string>();
    }

    private static List<string> LoadFromGame()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // 1) 全物品表
            var globalField = AccessTools.Field(typeof(Item), "GlobalItems");
            var global = globalField?.GetValue(null) as IDictionary;
            if (global != null)
            {
                foreach (var k in global.Keys)
                    if (k != null && !string.IsNullOrEmpty(k.ToString())) set.Add(k.ToString());
            }

            // 2) 战利品池
            var poolField = AccessTools.Field(typeof(ItemLootPool), "pool");
            var pool = poolField?.GetValue(null) as IDictionary;
            if (pool != null)
            {
                foreach (var key in pool.Keys)
                {
                    if (pool[key] is IEnumerable list)
                        foreach (var v in list)
                            if (v != null && !string.IsNullOrEmpty(v.ToString())) set.Add(v.ToString());
                }
            }

            // 3) 两张表都空 → 手动触发游戏初始化再试一次
            if (set.Count == 0)
            {
                AccessTools.Method(typeof(Item), "SetupItems")?.Invoke(null, null);
                global = globalField?.GetValue(null) as IDictionary;
                if (global != null)
                    foreach (var k in global.Keys)
                        if (k != null) set.Add(k.ToString());
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 读取物品表失败: {e.Message}");
        }

        var result = set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }

    // 导出完整物品表 + 液体表（供翻译文件生成），写到 plugins/CU_ServerPilot/
    internal static void ExportFullTables()
    {
        try
        {
            string dir = Path.Combine(Paths.PluginPath, "CU_ServerPilot");
            Directory.CreateDirectory(dir);

            var items = GetAllItems();
            string itemPath = Path.Combine(dir, "items_full.txt");
            File.WriteAllLines(itemPath, items);

            var liq = LiquidManager.GetLiquidDetails();
            string liqPath = Path.Combine(dir, "liquids_full.txt");
            File.WriteAllLines(liqPath, liq);

            ConsoleManager.SendFeedback($"已导出: {items.Count} 物品 → items_full.txt；{liq.Count} 液体 → liquids_full.txt（在 plugins/CU_ServerPilot/）");
        }
        catch (Exception e)
        {
            ConsoleManager.SendFeedback($"导出失败: {e.Message}");
        }
    }
}
