using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  SpawnTool - spawn 图形化（需求 3）
//  ---------------------------------------------------------------------------
//  给指定位置/玩家生成物品，数量/参数（耐久）/对象可自定义。
//  相当于游戏控制台 spawn 指令的图形界面。
// ============================================================================

public static class SpawnTool
{
    // 鼠标屏幕坐标 → 世界坐标（2D）
    internal static Vector2 GetMouseWorldPos()
    {
        var cam = Camera.main;
        if (cam == null) return PlayerCamera.main?.body?.transform.position ?? Vector2.zero;
        Vector3 m = Input.mousePosition;
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(m.x, m.y, -cam.transform.position.z));
        return (Vector2)w;
    }

    // 生成到鼠标位置（地上）
    public static bool SpawnAtCursor(string itemName, int count, float param)
    {
        var pos = GetMouseWorldPos();
        bool ok = ConsoleManager.SpawnItemAt(itemName, pos, count, param);
        ConsoleManager.SendFeedback(ok
            ? $"已在光标位置生成 {count} 个 {itemName} (参{param})"
            : $"生成失败: {itemName}");
        return ok;
    }

    // 生成给本机玩家
    public static bool SpawnToPlayer(string itemName, int count, float param)
    {
        var body = PlayerCamera.main?.body;
        if (body == null)
        {
            ConsoleManager.SendFeedback("找不到本机玩家（需要在游戏世界中）");
            return false;
        }
        bool ok = ConsoleManager.GiveItemToBody(body, itemName, count, param);
        ConsoleManager.SendFeedback(ok
            ? $"已给玩家 {count} 个 {itemName} (参{param})"
            : $"生成失败: {itemName}");
        return ok;
    }

    // 生成到指定目标（光标 / @a 全体 / 具体玩家）
    public static int SpawnToTarget(string target, string itemName, int count, float param)
    {
        if (target == TargetPicker.CursorTarget)
        {
            bool ok = ConsoleManager.SpawnItemAt(itemName, GetMouseWorldPos(), count, param);
            ConsoleManager.SendFeedback(ok
                ? $"已在光标位置生成 {count} 个 {itemName} (参{param})"
                : $"生成失败: {itemName}");
            return ok ? count : 0;
        }

        var bodies = TargetPicker.ResolveBodies(target);
        if (bodies.Count == 0)
        {
            ConsoleManager.SendFeedback($"找不到目标: {target}");
            return 0;
        }
        int ok2 = 0;
        foreach (var b in bodies)
            if (ConsoleManager.GiveItemToBody(b, itemName, count, param)) ok2++;
        ConsoleManager.SendFeedback($"已给 {target} 发放 {count} 个 {itemName} (参{param})（{ok2}/{bodies.Count} 成功）");
        return ok2;
    }

    // 给目标发放开局物资（初始物资 tab 用）
    public static int GiveGiftsToTarget(string target)
    {
        if (target == TargetPicker.CursorTarget)
        {
            ConsoleManager.SendFeedback("初始物资需要指定玩家，不能发给光标位置");
            return 0;
        }
        var bodies = TargetPicker.ResolveBodies(target);
        if (bodies.Count == 0)
        {
            ConsoleManager.SendFeedback($"找不到目标: {target}");
            return 0;
        }
        int ok = 0, total = 0;
        foreach (var b in bodies)
        {
            var gifts = StartupGifts.ParseGifts();
            foreach (var g in gifts)
            {
                total++;
                if (ConsoleManager.GiveItemToBody(b, g.Name, g.Count, g.Param)) ok++;
            }
        }
        ConsoleManager.SendFeedback($"已给 {target} 发放开局物资: 成功 {ok} / {total}");
        return ok;
    }
}
