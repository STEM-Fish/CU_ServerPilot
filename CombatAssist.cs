using BepInEx.Configuration;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  CombatAssist - 战斗辅助
//  ---------------------------------------------------------------------------
//  启用后，按 R 键给主手枪械"拉栓"（GunScript.TryRack()），无需拖拽鼠标。
//
//  v0.10.7: 按一次拉一次（GetKeyDown 只在按下瞬间触发一次，长按不会重复）。
// ============================================================================

public static class CombatAssist
{
    private static ConfigEntry<bool> cfgEnabled;

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("CombatAssist", "Enabled", false, "启用后按 R 键给主手枪械拉栓");
    }

    public static bool GetEnabled() => cfgEnabled?.Value ?? false;
    public static void SetEnabled(bool v) { if (cfgEnabled != null) cfgEnabled.Value = v; }

    public static void Update()
    {
        if (!GetEnabled()) return;
        // GetKeyDown：只在按下瞬间触发一次，长按不会重复触发
        if (!Input.GetKeyDown(KeyCode.R)) return;

        var gun = GetMainGun();
        if (gun == null) return;

        gun.TryRack();
        Plugin.Log.LogInfo("[CU_ServerPilot] R 键：已拉栓");
    }

    private static GunScript GetMainGun()
    {
        var body = PlayerCamera.main?.body;
        var item = body?.GetItem(0);
        return item?.GetComponent<GunScript>();
    }
}
