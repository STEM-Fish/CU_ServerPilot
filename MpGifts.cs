using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  MpGifts - 联机进服发道具 + 初始技能 + 广播（KrokMP）
//  ---------------------------------------------------------------------------
//  原理: KrokMP 服务器在玩家角色同步完成（成功进服）时调用
//        ServerMain.ServerReceiver__ClientCharacterSyncUpdate(knetid clientId, ...)。
//        我们 postfix 里通过 NetPlayer.TryGetNetPlayerAndNetBodyFromClientId
//        拿到该玩家的 NetBody -> Body，依次:
//         1) 设置初始技能等级（Skills 组件 STR/RES/INT + UpdateExpBoundaries）
//         2) 发放开局道具（与 StartupGifts.GiftItems 共用列表）
//         3) 聊天广播提示（Chat.Server_ChatAnnouncement）
//
//  配置:  MpGifts.Enabled - 发道具总开关
//         Skills.Enabled / INT / RES / STR - 初始技能
//         MpGifts.AnnounceGift - 广播开关
// ============================================================================

public static class MpGifts
{
    private static ConfigEntry<bool> cfgEnabled;
    private static ConfigEntry<bool> cfgSkillEnabled;
    private static ConfigEntry<int> cfgSkillInt;
    private static ConfigEntry<int> cfgSkillRes;
    private static ConfigEntry<int> cfgSkillStr;
    private static ConfigEntry<bool> cfgAnnounce;
    private static readonly HashSet<knetid> GaveThisSession = new HashSet<knetid>();

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("MpGifts", "Enabled", true, "联机玩家进服时发放开局道具（与 StartupGifts.GiftItems 共用列表）");
        cfgSkillEnabled = plugin.Config.Bind("Skills", "Enabled", false, "进服时设置玩家初始技能等级");
        cfgSkillInt = plugin.Config.Bind("Skills", "INT", 1, "初始智力等级");
        cfgSkillRes = plugin.Config.Bind("Skills", "RES", 1, "初始抗性等级");
        cfgSkillStr = plugin.Config.Bind("Skills", "STR", 1, "初始力量等级");
        cfgAnnounce = plugin.Config.Bind("MpGifts", "AnnounceGift", true, "发放初始物资时在聊天框广播提示");

        try
        {
            var harmony = new Harmony("com.cuserverpilot.mpgifts");
            var target = AccessTools.Method(typeof(ServerMain), "ServerReceiver__ClientCharacterSyncUpdate");
            if (target == null)
            {
                Plugin.Log.LogError("[CU_ServerPilot] 找不到 KrokMP 进服同步点 ServerReceiver__ClientCharacterSyncUpdate");
                return;
            }
            var postfix = typeof(MpGifts).GetMethod("OnClientSync_Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            var info = Harmony.GetPatchInfo(target);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 联机进服发道具 hook 完成 | postfix 数量={info?.Postfixes?.Count ?? -1}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 联机进服 hook 失败: {e}");
        }
    }

    // ---- UI 绑定接口 ----
    public static bool GetSkillEnabled() => cfgSkillEnabled?.Value ?? false;
    public static void SetSkillEnabled(bool v) { if (cfgSkillEnabled != null) cfgSkillEnabled.Value = v; }
    public static int GetSkill(string name) => name switch
    {
        "INT" => cfgSkillInt?.Value ?? 1,
        "RES" => cfgSkillRes?.Value ?? 1,
        "STR" => cfgSkillStr?.Value ?? 1,
        _ => 1
    };
    public static void SetSkillValues(int intV, int resV, int strV)
    {
        if (cfgSkillInt != null) cfgSkillInt.Value = intV;
        if (cfgSkillRes != null) cfgSkillRes.Value = resV;
        if (cfgSkillStr != null) cfgSkillStr.Value = strV;
    }
    public static bool GetAnnounce() => cfgAnnounce?.Value ?? true;
    public static void SetAnnounce(bool v) { if (cfgAnnounce != null) cfgAnnounce.Value = v; }

    // 玩家角色同步完成（成功进服）postfix
    private static void OnClientSync_Postfix(knetid clientId)
    {
        if (!cfgEnabled.Value) return;
        if (!GaveThisSession.Add(clientId)) return;   // 每个玩家本会话只发一次

        if (!NetPlayer.TryGetNetPlayerAndNetBodyFromClientId(clientId, out var plr, out var pb))
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 进服玩家 {clientId} 找不到 NetBody");
            return;
        }
        if (pb?.body == null)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 进服玩家 {clientId} 还没有 Body");
            return;
        }
        if (plr != null && plr.is_local)
        {
            // 本机（主机）玩家不走网络同步，由 StartupGifts 处理
            return;
        }

        string playerName = plr?.playername ?? clientId.ToString();

        // 1) 初始技能等级
        if (cfgSkillEnabled.Value)
            ApplySkills(pb.body);

        // 1.5) 进服自动状态（Moodle）
        CustomMoodles.ApplyJoinMoodles(pb.body);

        // 2) 初始物资
        var items = StartupGifts.GetGiftItems();
        int ok = 0;
        if (items.Length > 0)
        {
            Plugin.Log.LogInfo($"[CU_ServerPilot] 联机玩家进服 ({playerName})，发放开局道具: {string.Join(", ", items)}");
            ok = StartupGifts.GiveGiftsToBody(pb.body);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 进服发放完成: 成功 {ok} / {items.Length}");
        }

        // 3) 聊天广播
        if (cfgAnnounce.Value && items.Length > 0)
        {
            try
            {
                string msg = $"[服务器] 玩家 {playerName} 初始物资已给出";
                Chat.Server_ChatAnnouncement(ref msg);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[CU_ServerPilot] 广播失败: {e.Message}");
            }
        }
    }

    // 设置玩家技能等级（Skills 组件字段 + UpdateExpBoundaries 同步经验边界）
    private static void ApplySkills(Body body)
    {
        try
        {
            var skills = body.GetComponent<Skills>();
            if (skills == null)
            {
                Plugin.Log.LogWarning("[CU_ServerPilot] 玩家 Body 没有 Skills 组件");
                return;
            }
            skills.INT = cfgSkillInt.Value;
            skills.RES = cfgSkillRes.Value;
            skills.STR = cfgSkillStr.Value;
            skills.UpdateExpBoundaries();
            Plugin.Log.LogInfo($"[CU_ServerPilot] 已设置技能 INT={skills.INT} RES={skills.RES} STR={skills.STR}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 设置技能失败: {e.Message}");
        }
    }
}
