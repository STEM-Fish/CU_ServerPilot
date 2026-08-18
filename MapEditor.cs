using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  MapEditor - 地图编辑（需求 2，集成 SuperGodFistTool 的方块生成）
//  ---------------------------------------------------------------------------
//  方块列表: 遍历 ID 1~500，WorldGeneration.world.GetBlockInfo(id) 有名字的加入
//            （与 SuperGodFistTool BuildBlockCache 相同逻辑）
//  放置:     鼠标位置 WorldToBlockPos → SetBlock(网格, blockId)，支持刷子半径
//  翻译:     blocks.zh-CN.json（可自定义），未命中的显示 BlockInfo.name
// ============================================================================

public static class MapEditor
{
    public struct BlockEntry
    {
        public ushort Id;
        public string Name;
    }

    private static List<BlockEntry> _blocks;
    private static float _lastTry = -999f;

    public static List<BlockEntry> GetBlocks()
    {
        // 空结果不缓存，2 秒重试（世界未生成时可能为空）
        if (_blocks != null && _blocks.Count > 0) return _blocks;
        if (Time.time - _lastTry < 2f) return _blocks ?? new List<BlockEntry>();
        _lastTry = Time.time;

        _blocks = new List<BlockEntry>();
        try
        {
            var world = WorldGeneration.world;
            if (world == null)
            {
                Plugin.Log.LogWarning("[CU_ServerPilot] WorldGeneration.world 为空（需进入游戏世界后重试）");
                return _blocks;
            }
            for (ushort i = 1; i < 500; i++)
            {
                var info = world.GetBlockInfo(i);
                if (info != null && !string.IsNullOrEmpty(info.name))
                    _blocks.Add(new BlockEntry { Id = i, Name = info.name });
            }
            Plugin.Log.LogInfo($"[CU_ServerPilot] 方块表加载: {_blocks.Count} 个（1~500 GetBlockInfo）");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 读取方块表失败: {e.Message}");
        }
        return _blocks;
    }

    // 在鼠标位置放置方块（刷子半径；quiet=true 时不输出日志，供拖动连续放置）
    public static bool PlaceBlockAtCursor(ushort blockId, int brushRadius, bool quiet = false)
    {
        var world = WorldGeneration.world;
        if (world == null)
        {
            if (!quiet) ConsoleManager.SendFeedback("世界未生成");
            return false;
        }
        Vector2Int bp = world.WorldToBlockPos(SpawnTool.GetMouseWorldPos());
        int r = Mathf.Clamp(brushRadius, 1, 8);
        for (int dx = -r + 1; dx <= r - 1; dx++)
        for (int dy = -r + 1; dy <= r - 1; dy++)
        {
            world.SetBlock(bp + new Vector2Int(dx, dy), blockId);
        }
        if (!quiet) ConsoleManager.SendFeedback($"已放置方块 ID:{blockId} 于网格 {bp}（刷子 {r}）");
        return true;
    }

    // 在鼠标位置擦除方块（SetBlock 0 = 空；quiet=true 时不输出日志）
    public static void EraseAtCursor(int brushRadius, bool quiet = false)
    {
        var world = WorldGeneration.world;
        if (world == null)
        {
            if (!quiet) ConsoleManager.SendFeedback("世界未生成");
            return;
        }
        Vector2Int bp = world.WorldToBlockPos(SpawnTool.GetMouseWorldPos());
        int r = Mathf.Clamp(brushRadius, 1, 8);
        for (int dx = -r + 1; dx <= r - 1; dx++)
        for (int dy = -r + 1; dy <= r - 1; dy++)
        {
            world.SetBlock(bp + new Vector2Int(dx, dy), 0);
        }
        if (!quiet) ConsoleManager.SendFeedback($"已擦除网格 {bp}（刷子 {r}）");
    }

    // 导出方块表（供翻译文件生成）
    public static void ExportBlocks()
    {
        try
        {
            string dir = Path.Combine(Paths.PluginPath, "CU_ServerPilot");
            Directory.CreateDirectory(dir);
            var lines = new List<string>();
            foreach (var b in GetBlocks())
                lines.Add($"{b.Id}|{b.Name}");
            File.WriteAllLines(Path.Combine(dir, "blocks_full.txt"), lines);
            ConsoleManager.SendFeedback($"已导出 {lines.Count} 个方块 → blocks_full.txt");
        }
        catch (Exception e)
        {
            ConsoleManager.SendFeedback($"导出失败: {e.Message}");
        }
    }
}
