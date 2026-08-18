using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  ConsoleManager - 管理员聊天命令框架
//  ---------------------------------------------------------------------------
//  原理: 游戏控制台/聊天命令统一走 ConsoleScript.TryExecuteCommand(string[], bool)。
//        我们 hook 它的 postfix，识别自己的命令前缀（默认 "sp"）并分发执行。
//        游戏原本不认识 /spxxx 命令，会照常走它的流程——所以命令名要避开游戏内置。
//
//  命令:  /sphelp            - 列出可用命令
//         /spgive <物品名> [数量] - 给本机玩家物品（物品名见 Item 表）
// ============================================================================

public static class ConsoleManager
{
    private static readonly Dictionary<string, Action<string[]>> Commands = new Dictionary<string, Action<string[]>>();
    private static ConfigEntry<string> cfgPrefix;

    public static void Init(Plugin plugin)
    {
        cfgPrefix = plugin.Config.Bind("Commands", "Prefix", "sp", "命令前缀（聊天输入 /spxxx 触发）");

        // ---- 内置命令 ----
        Register("help", args => SendFeedback("命令: help, give <物品> [数量], addliquid <液体> <毫升>, cleardrops"));
        Register("give", GiveCommand);
        Register("addliquid", args =>
        {
            if (args.Length < 3)
            {
                SendFeedback("用法: addliquid <液体id> <毫升>");
                return;
            }
            if (!float.TryParse(args[2], out float ml))
            {
                SendFeedback("毫升必须是数字");
                return;
            }
            LiquidManager.AddToHandContainer(args[1], ml);
        });
        Register("cleardrops", args => SendFeedback($"已清除 {ClearDrops()} 个掉落物"));

        // ---- hook 游戏命令入口（手动 patch + 验证，本环境 PatchAll 不可靠） ----
        try
        {
            var harmony = new Harmony("com.cuserverpilot.commands");
            var target = AccessTools.Method(typeof(ConsoleScript), "TryExecuteCommand");
            if (target == null)
            {
                Plugin.Log.LogError("[CU_ServerPilot] 找不到 ConsoleScript.TryExecuteCommand");
                return;
            }
            var postfix = typeof(ConsoleManager).GetMethod("TryExecuteCommand_Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            var info = Harmony.GetPatchInfo(target);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 命令 hook 完成 | postfix 数量={info?.Postfixes?.Count ?? -1}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 命令 hook 失败: {e}");
        }
    }

    public static void Register(string name, Action<string[]> handler)
    {
        Commands[name] = handler;
    }

    // ==========================================================================
    //  游戏命令入口 postfix：识别我们的命令前缀并分发
    // ==========================================================================
    private static void TryExecuteCommand_Postfix(ConsoleScript __instance, string[] args, bool addToLog)
    {
        if (args == null || args.Length == 0) return;

        string cmd = args[0];
        if (string.IsNullOrEmpty(cmd)) return;
        cmd = cmd.TrimStart('/');   // 兼容 "/spgive" 和 "spgive"

        string prefix = cfgPrefix.Value.ToLowerInvariant();
        if (cmd.Length <= prefix.Length || !cmd.ToLowerInvariant().StartsWith(prefix)) return;

        string name = cmd.Substring(prefix.Length).ToLowerInvariant();
        if (!Commands.TryGetValue(name, out var handler)) return;

        try
        {
            handler(args);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 命令 {name} 执行异常: {e}");
        }
    }

    // ==========================================================================
    //  命令实现
    // ==========================================================================

    // /spgive <物品名> [数量] —— 给本机玩家物品
    private static void GiveCommand(string[] args)
    {
        if (args.Length < 2)
        {
            SendFeedback("用法: give <物品名> [数量]");
            return;
        }
        string itemName = args[1];
        int count = 1;
        if (args.Length >= 3 && !int.TryParse(args[2], out count))
        {
            SendFeedback("数量必须是数字");
            return;
        }
        count = Mathf.Clamp(count, 1, 100);

        var body = PlayerCamera.main?.body;
        if (body == null)
        {
            SendFeedback("找不到本机玩家（需要在游戏世界中）");
            return;
        }

        int ok = 0;
        for (int i = 0; i < count; i++)
        {
            if (GiveItemToBody(body, itemName)) ok++;
        }
        SendFeedback(ok > 0
            ? $"已给 {ok} 个 {itemName}"
            : $"物品 {itemName} 无效或无法生成");
    }

    // UI 调用：手动给本机玩家发 count 个物品
    internal static void GiveManualItem(string itemName, int count)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            SendFeedback("物品名不能为空");
            return;
        }
        var body = PlayerCamera.main?.body;
        if (body == null)
        {
            SendFeedback("找不到本机玩家（需要在游戏世界中）");
            return;
        }
        int ok = 0;
        for (int i = 0; i < count; i++)
        {
            if (GiveItemToBody(body, itemName.Trim())) ok++;
        }
        SendFeedback(ok > 0
            ? $"已发放 {ok} 个 {itemName.Trim()}"
            : $"物品 {itemName.Trim()} 无效（请用全小写无空格的内部 ID）");
    }

    // 一键清除掉落物：地上无人持有的 Item（不在容器、不被任何 Body 手持）销毁
    internal static int ClearDrops()
    {
        var allItems = Item.allItems;
        if (allItems == null || allItems.Count == 0) return 0;

        var bodies = UnityEngine.Object.FindObjectsOfType<Body>();
        int removed = 0;
        foreach (var it in allItems.ToArray())
        {
            if (it == null) continue;
            if (it.container != null) continue;                 // 在容器里（背包/箱子等）
            bool held = false;
            foreach (var b in bodies)
            {
                if (b != null && b.HoldingItem(it)) { held = true; break; }
            }
            if (held) continue;                            // 被手持/装备
            UnityEngine.Object.Destroy(it.gameObject);
            removed++;
        }
        Plugin.Log.LogInfo($"[CU_ServerPilot] 清除掉落物: {removed} 个");
        return removed;
    }

    // ==========================================================================
    //  工具：给一个 Body 发物品（模仿游戏 ConsoleScript.SpawnBodyItem 的调用链）
    //  ItemLootPool.pool[分类] -> 物品名 -> Utils.Create(名字, 位置, 0)
    //  -> GetComponent<Item>() -> Body.AutoPickUpItem(item)
    // ==========================================================================
    internal static bool GiveItemToBody(Body body, string itemName)
        => GiveItemToBody(body, itemName, 1, 0f);

    internal static bool GiveItemToBody(Body body, string itemName, int count, float param)
    {
        int ok = 0;
        for (int i = 0; i < count; i++)
            if (GiveItemToBodySingle(body, itemName, param)) ok++;
        return ok == count;
    }

    internal static bool GiveItemToBodySingle(Body body, string itemName, float param)
    {
        try
        {
            var go = Utils.Create(itemName, (Vector2)body.transform.position, 0f);
            if (go == null) return false;
            var item = go.GetComponent<Item>();
            if (item == null) return false;
            ApplyParam(item, param);
            SuperItem.Apply(item);
            body.AutoPickUpItem(item);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 生成物品 {itemName} 失败: {e.Message}");
            return false;
        }
    }

    // 在世界指定位置生成物品（返回是否全部成功）
    internal static bool SpawnItemAt(string itemName, Vector2 worldPos, int count, float param)
    {
        int ok = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var go = Utils.Create(itemName, worldPos, 0f);
                if (go == null) continue;
                var item = go.GetComponent<Item>();
                if (item == null) continue;
                ApplyParam(item, param);
                SuperItem.Apply(item);
                ok++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[CU_ServerPilot] 生成物品 {itemName} 失败: {e.Message}");
            }
        }
        return ok == count;
    }

    // 参数应用（仿游戏 spawn 命令语义 + 增强枪械弹匣）：
    // param 是"填充比例"（0~1，1=满）。枪械→弹匣弹量；弹药→弹数；电池→电量；否则→耐久
    internal static void ApplyParam(Item item, float param)
    {
        if (item == null || param <= 0f) return;

        var gun = item.GetComponent<GunScript>();
        if (gun != null)
        {
            gun.roundsInMag = Mathf.RoundToInt(gun.magCapacity * param);
            return;
        }
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.rounds = Mathf.RoundToInt(ammo.maxRounds * param);
            return;
        }
        var bat = item.GetComponent<BatteryItem>();
        if (bat != null)
        {
            bat.maxCharge = bat.maxCharge * param;
            return;
        }
        item.SetCondition(param);   // 游戏 spawn 原逻辑
    }

    // 反馈到游戏控制台日志（ConsoleScript.instance 是游戏的单例；LogToConsole 是私有方法，用反射调）
    internal static void SendFeedback(string msg)
    {
        Plugin.Log.LogInfo($"[CU_ServerPilot] {msg}");
        try
        {
            var inst = ConsoleScript.instance;
            if (inst == null) return;
            var m = AccessTools.Method(typeof(ConsoleScript), "LogToConsole");
            m?.Invoke(inst, new object[] { $"[CU_ServerPilot] {msg}" });
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 反馈写入失败: {e.Message}");
        }
    }
}
