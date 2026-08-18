using System;
using System.Collections.Generic;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  PlayerOps - 玩家操作（拉取 / 昏迷 / 治疗）
//  ---------------------------------------------------------------------------
//  拉取: NetPlayer.Server_TeleportCharacter(Vector2) —— 目标玩家传送到指定位置
//  昏迷: Body.consciousness 设为极低（深度昏迷，恢复慢 = 时长足够长）
//        + 本机玩家触发 PlayerCamera.OnBecameUnconscious()
//  治疗: 仿 SuperGodFistTool ResetHealth（脑/血/氧/压/心率/呼吸等字段全恢复）
//  对象: 复用 TargetPicker（@本机/@a/玩家）
// ============================================================================

public static class PlayerOps
{
    // ---- 拉取：目标玩家传送到本机位置 ----
    public static int PullToLocal(string target)
    {
        var local = PlayerCamera.main?.body;
        if (local == null)
        {
            ConsoleManager.SendFeedback("找不到本机玩家（需在世界中）");
            return 0;
        }
        var bodies = TargetPicker.ResolveBodies(target);
        if (bodies.Count == 0)
        {
            ConsoleManager.SendFeedback($"找不到目标: {target}");
            return 0;
        }
        Vector2 pos = (Vector2)local.transform.position;
        int ok = 0;
        foreach (var b in bodies)
        {
            if (b == local) { ok++; continue; }
            if (TryTeleportBody(b, pos)) ok++;
        }
        ConsoleManager.SendFeedback($"已拉取 {target} 到本机位置（{ok}/{bodies.Count}）");
        return ok;
    }

    private static bool TryTeleportBody(Body body, Vector2 pos)
    {
        try
        {
            var players = ServerMain.AllPlayersExceptHost;
            if (players != null)
            {
                foreach (var p in players)
                {
                    if (p?.body == body)
                    {
                        p.Server_TeleportCharacter(pos);
                        return true;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 传送失败: {e.Message}");
        }
        return false;
    }

    // ---- 昏迷：目标深度昏迷（时长足够长） ----
    public static int Knockout(string target)
    {
        var bodies = TargetPicker.ResolveBodies(target);
        if (bodies.Count == 0)
        {
            ConsoleManager.SendFeedback($"找不到目标: {target}");
            return 0;
        }
        int ok = 0;
        foreach (var b in bodies)
        {
            try
            {
                b.consciousness = -500f;   // 深度昏迷，恢复缓慢 → 昏迷足够长
                // 本机玩家：触发昏迷 UI/流程
                if (b == PlayerCamera.main?.body)
                {
                    var cam = PlayerCamera.main;
                    var m = AccessToolsHarmony.Method(typeof(PlayerCamera), "OnBecameUnconscious");
                    m?.Invoke(cam, null);
                }
                ok++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[CU_ServerPilot] 昏迷失败: {e.Message}");
            }
        }
        ConsoleManager.SendFeedback($"已昏迷 {target}（{ok} 人，深度昏迷）");
        return ok;
    }

    // ---- 治疗：仿游戏 heal（MedStationScript.HealBody）+ MP 注入增强 ----
    public static int Heal(string target)
    {
        var bodies = TargetPicker.ResolveBodies(target);
        if (bodies.Count == 0)
        {
            ConsoleManager.SendFeedback($"找不到目标: {target}");
            return 0;
        }
        int ok = 0;
        foreach (var b in bodies)
        {
            try
            {
                // == 游戏 heal 逻辑（MedStationScript.HealBody 反编译） ==
                b.hunger = Mathf.Max(b.hunger, 100f);
                b.thirst = Mathf.Max(b.thirst, 100f);
                b.happiness += 4f;
                b.sicknessAmount *= 0.5f;
                b.stamina += 50f;
                b.energy += 30f;

                // == 增强（MP 模组 heal 注入的内容） ==
                b.overdoseIndex = 0;               // 药物过量清零
                b.consciousness = 100f;            // 恢复意识（昏迷中直接唤醒）
                b.radiationSickness = 0f;          // 辐射病清零
                b.brainGrowSickness = 0f;          // 脑生长病清零

                // 肢体治疗
                if (b.limbs != null)
                {
                    foreach (var limb in b.limbs)
                    {
                        if (limb == null) continue;
                        limb.bleedAmount = 0f;                                   // 完全止血
                        limb.muscleHealth = Mathf.Lerp(limb.muscleHealth, 100f, 0.3f);
                        limb.skinHealth = Mathf.Lerp(limb.skinHealth, 100f, 0.3f);
                        limb.boneHealTimer *= 0.5f;                              // 骨折恢复加速
                        limb.dislocationTimer *= 0.5f;                           // 脱臼恢复加速
                        if (limb.dismembered) limb.dismembered = false;          // 截肢恢复（重新接回）
                    }
                }
                ok++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[CU_ServerPilot] 治疗失败: {e.Message}");
            }
        }
        ConsoleManager.SendFeedback($"已治疗 {target}（{ok} 人：生理/截肢/过量/昏迷/辐射全恢复）");
        return ok;
    }
}

// 小的反射助手（避免每次写 AccessTools 全名）
internal static class AccessToolsHarmony
{
    internal static System.Reflection.MethodInfo Method(System.Type type, string name)
    {
        return HarmonyLib.AccessTools.Method(type, name);
    }
}
