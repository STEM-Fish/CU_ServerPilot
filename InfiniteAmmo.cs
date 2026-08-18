using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  InfiniteAmmo - 无限子弹
//  ---------------------------------------------------------------------------
//  逻辑（减少 BUG）：
//   - patch GunScript.Update postfix：每帧补满弹匣 roundsInMag = magCapacity
//     → 空枪/新枪也直接满弹，无需先手动装一发
//   - patch GunScript.Fire postfix：开火后补枪膛 racked = true
//     （霰弹枪第二发；不每帧写 racked，避免打断拉栓动画）
// ============================================================================

public static class InfiniteAmmo
{
    private static ConfigEntry<bool> cfgEnabled;
    private static ConfigEntry<bool> cfgChamber;   // 是否连枪膛一起补

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("InfiniteAmmo", "Enabled", false, "无限子弹：自动补满弹匣");
        cfgChamber = plugin.Config.Bind("InfiniteAmmo", "RefillChamber", true, "同时补充枪膛（霰弹枪第二发）");

        try
        {
            var harmony = new Harmony("com.cuserverpilot.ammo");

            var update = AccessTools.Method(typeof(GunScript), "Update");
            if (update != null)
            {
                harmony.Patch(update, postfix: new HarmonyMethod(
                    typeof(InfiniteAmmo).GetMethod("Update_Postfix", BindingFlags.NonPublic | BindingFlags.Static)));
            }

            var fire = AccessTools.Method(typeof(GunScript), "Fire");
            if (fire != null)
            {
                harmony.Patch(fire, postfix: new HarmonyMethod(
                    typeof(InfiniteAmmo).GetMethod("Fire_Postfix", BindingFlags.NonPublic | BindingFlags.Static)));
            }

            var ui = update != null ? Harmony.GetPatchInfo(update) : null;
            var fi = fire != null ? Harmony.GetPatchInfo(fire) : null;
            Plugin.Log.LogInfo($"[CU_ServerPilot] 无限子弹 hook 完成 | Update={ui?.Postfixes?.Count ?? -1}, Fire={fi?.Postfixes?.Count ?? -1}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 无限子弹 hook 失败: {e}");
        }
    }

    public static bool GetEnabled() => cfgEnabled?.Value ?? false;
    public static void SetEnabled(bool v) { if (cfgEnabled != null) cfgEnabled.Value = v; }
    public static bool GetChamber() => cfgChamber?.Value ?? true;
    public static void SetChamber(bool v) { if (cfgChamber != null) cfgChamber.Value = v; }

    // 每帧补弹匣：空枪也满弹，直接能开火
    private static void Update_Postfix(GunScript __instance)
    {
        if (!GetEnabled() || __instance == null) return;
        __instance.roundsInMag = __instance.magCapacity;
    }

    // 开火后补枪膛（霰弹枪第二发）
    private static void Fire_Postfix(GunScript __instance)
    {
        if (!GetEnabled() || __instance == null) return;
        if (GetChamber())
            __instance.racked = true;
    }
}
