using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  BiomeBalance - 生物生成倍率（自定义世界没有的选项）
//  ---------------------------------------------------------------------------
//  原理：游戏世界生成时按固定密度生成生物（WorldPlaceEntities 状态机），
//       无法在 IL 里直接改密度。这里做"生物平衡器"：
//       每隔几秒统计世界生物数量（按预制体名匹配），按倍率调整：
//        - 倍率 < 1：随机销毁多余生物
//        - 倍率 > 1：在世界内随机找 Ground 位置补生成（Resources.Load 预制体）
//  生物名单来自游戏 WorldPlaceEntities 反编译（spikestabber/shadecrawler/...）
// ============================================================================

public static class BiomeBalance
{
    private static readonly string[] CreatureNames =
    {
        "spikestabber", "shadecrawler", "caveticks", "CaveTicks", "thornbackyoung",
        "thornbackelder", "overgrowntick", "wallbiter", "coil", "wallflower",
        "skullcrusher", "grabberplant", "geyser", "LifePodLight"
    };

    private static ConfigEntry<bool> cfgEnabled;
    private static ConfigEntry<float> cfgMultiplier;
    private static float _lastBalance = -999f;
    private static int _baselineCount = -1;   // 基准生物数（应用倍率前的世界实际数量）
    private static bool _applied;              // 已应用一次（之后不再干预，生物死亡不补）

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("BiomeBalance", "Enabled", false, "生物生成倍率（一次性调整，0.1~3）");
        cfgMultiplier = plugin.Config.Bind("BiomeBalance", "Multiplier", 1.0f, "生物数量倍率（1=原样，0.5=减半，2=加倍）");
    }

    public static bool GetEnabled() => cfgEnabled?.Value ?? false;
    public static void SetEnabled(bool v)
    {
        if (cfgEnabled != null) cfgEnabled.Value = v;
        _baselineCount = -1; _applied = false;
    }
    public static float GetMultiplier() => cfgMultiplier?.Value ?? 1f;
    public static void SetMultiplier(float v)
    {
        if (cfgMultiplier != null) cfgMultiplier.Value = Mathf.Clamp(v, 0.1f, 3f);
        _baselineCount = -1; _applied = false;
    }

    // 插件 Update 调用：5 秒节流；应用一次后不再干预
    public static void Update()
    {
        if (!GetEnabled() || WorldGeneration.world == null) return;
        if (_applied) return;                       // 已应用一次 → 停止
        if (Time.time - _lastBalance < 5f) return;
        _lastBalance = Time.time;

        try
        {
            float mult = GetMultiplier();
            if (Mathf.Abs(mult - 1f) < 0.01f) { _applied = true; return; }

            var creatures = FindCreatures();
            int n = creatures.Count;

            // 第一次平衡：记录基准（应用倍率前的数量），本次不调整
            if (_baselineCount < 0)
            {
                _baselineCount = n;
                int target = Mathf.RoundToInt(_baselineCount * mult);
                Plugin.Log.LogInfo($"[CU_ServerPilot] 生物基准记录: {_baselineCount} 个，倍率 {mult:0.##} → 目标 {target}");
                return;
            }

            // 第二次平衡：一次性调整到 基准×倍率，然后停止
            int target2 = Mathf.RoundToInt(_baselineCount * mult);
            _applied = true;

            if (target2 < n)
            {
                var toRemove = creatures.OrderBy(_ => UnityEngine.Random.value).Take(n - target2).ToList();
                foreach (var go in toRemove)
                    if (go != null) UnityEngine.Object.Destroy(go);
                Plugin.Log.LogInfo($"[CU_ServerPilot] 生物调整（一次性）: {n} → {target2}（清理 {n - target2}）");
            }
            else if (target2 > n)
            {
                int added = 0;
                for (int i = 0; i < target2 - n && i < 50; i++)
                {
                    if (TrySpawnRandomCreature()) added++;
                }
                Plugin.Log.LogInfo($"[CU_ServerPilot] 生物调整（一次性）: {n} → {target2}（补充 {added}）");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 生物调整失败: {e.Message}");
        }
    }

    private static List<GameObject> FindCreatures()
    {
        var list = new List<GameObject>();
        try
        {
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null) continue;
                string name = go.name.Replace("(Clone)", "").Trim();
                if (CreatureNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    list.Add(go);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 扫描生物失败: {e.Message}");
        }
        return list;
    }

    // 随机在世界内找 Ground 位置并生成一个随机生物
    private static bool TrySpawnRandomCreature()
    {
        try
        {
            var world = WorldGeneration.world;
            if (world == null) return false;
            // 世界内随机点（避开玩家太近？简化：随机全图）
            float hw = world.width / 2f, hh = world.height / 2f;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Vector2 pos = new Vector2(
                    UnityEngine.Random.Range(-hw, hw),
                    UnityEngine.Random.Range(-hh, hh));
                if (Physics2D.OverlapPoint(pos, LayerMask.GetMask("Ground")) != null) continue;
                var hit = Physics2D.Raycast(pos, Vector2.down, WorldGeneration.CHUNKSIZE, LayerMask.GetMask("Ground"));
                if (hit) pos = hit.point + Vector2.up * 0.5f;
                else continue;

                string prefabName = CreatureNames[UnityEngine.Random.Range(0, CreatureNames.Length)];
                var prefab = Resources.Load<GameObject>(prefabName);
                if (prefab == null) continue;
                UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                return true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 补生成失败: {e.Message}");
        }
        return false;
    }
}
