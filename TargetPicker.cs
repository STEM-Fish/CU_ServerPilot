using System;
using System.Collections.Generic;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  TargetPicker - 目标玩家选择器（需求：服务器玩家下拉）
//  ---------------------------------------------------------------------------
//  选项:
//   光标位置（spawn 专用）
//   @a 全体（所有玩家，含主机）
//   @主机名（本机/主机玩家）
//   具体联机玩家名（KrokMP ServerMain.AllPlayersExceptHost）
// ============================================================================

public static class TargetPicker
{
    public const string CursorTarget = "光标位置";
    public const string AllTarget = "@a 全体";

    private static List<string> _cached;
    private static float _lastRefresh = -999f;

    // 构建目标列表（固定项 + 服务器玩家），5 秒缓存刷新
    public static List<string> GetTargets()
    {
        if (_cached != null && UnityEngine.Time.time - _lastRefresh < 5f) return _cached;
        _lastRefresh = UnityEngine.Time.time;
        _cached = Build();
        return _cached;
    }

    private static List<string> Build()
    {
        var list = new List<string>();
        try
        {
            var local = NetPlayer.LOCAL_PLAYER;
            if (local != null && !string.IsNullOrEmpty(local.playername))
                list.Add("@" + local.playername);
            else
                list.Add("@本机");
        }
        catch
        {
            list.Add("@本机");
        }

        list.Add(AllTarget);

        try
        {
            var players = ServerMain.AllPlayersExceptHost;
            if (players != null)
            {
                foreach (var p in players)
                {
                    if (p == null || string.IsNullOrEmpty(p.playername)) continue;
                    if (!list.Contains(p.playername)) list.Add(p.playername);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 读取玩家列表失败: {e.Message}");
        }
        return list;
    }

    // 解析目标 → Body 列表（null 返回 = 无法解析；"光标位置"返回特殊标记）
    internal static List<Body> ResolveBodies(string target)
    {
        var bodies = new List<Body>();
        if (string.IsNullOrEmpty(target)) return bodies;

        if (target == AllTarget)
        {
            var local = PlayerCamera.main?.body;
            if (local != null) bodies.Add(local);
            try
            {
                var players = ServerMain.AllPlayersExceptHost;
                if (players != null)
                    foreach (var p in players)
                        if (p?.body != null) bodies.Add(p.body);
            }
            catch { }
            return bodies;
        }

        // 主机：@主机名 或 @本机
        if (target.StartsWith("@"))
        {
            string name = target.Substring(1);
            if (name == "本机")
            {
                var b = PlayerCamera.main?.body;
                if (b != null) bodies.Add(b);
                return bodies;
            }
            try
            {
                var local = NetPlayer.LOCAL_PLAYER;
                if (local != null && string.Equals(local.playername, name, StringComparison.OrdinalIgnoreCase))
                {
                    var b = PlayerCamera.main?.body;
                    if (b != null) bodies.Add(b);
                    return bodies;
                }
            }
            catch { }
            // @ 开头但非主机 → 当作普通名（去掉 @ 再匹配）
            target = name;
        }

        // 具体玩家
        try
        {
            var players = ServerMain.AllPlayersExceptHost;
            if (players != null)
                foreach (var p in players)
                    if (p?.body != null && string.Equals(p.playername, target, StringComparison.OrdinalIgnoreCase))
                        bodies.Add(p.body);
        }
        catch { }
        return bodies;
    }
}
