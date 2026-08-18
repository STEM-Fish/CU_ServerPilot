using BepInEx.Configuration;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  CombatAssist - 战斗辅助（需求 7）
//  ---------------------------------------------------------------------------
//  启用后，按 R 键给主手枪械"拉栓"（GunScript.TryRack()），无需拖拽鼠标。
//
//  联机适配：快速连按 R（0.5 秒内第二次）时，第二次拉栓延迟到
//  cfgRackDelay 之后执行——避免两次 TryRack 同帧触发被联机同步丢弃。
// ============================================================================

public static class CombatAssist
{
    private static ConfigEntry<bool> cfgEnabled;
    private static ConfigEntry<float> cfgRackDelay;

    private static float _lastRackTime = -999f;   // 上一次立即拉栓的时间
    private static float _pendingRackAt = -1f;     // 计划中的第二次拉栓执行时间

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("CombatAssist", "Enabled", false, "启用后按 R 键给主手枪械拉栓");
        cfgRackDelay = plugin.Config.Bind("CombatAssist", "RackDelay", 0.15f, "两次拉栓之间的延时（秒），避免联机中同帧拉栓失效");
    }

    public static bool GetEnabled() => cfgEnabled?.Value ?? false;
    public static void SetEnabled(bool v) { if (cfgEnabled != null) cfgEnabled.Value = v; }

    public static void Update()
    {
        if (!GetEnabled()) return;

        // 到点执行计划中的第二次拉栓
        if (_pendingRackAt >= 0f && Time.time >= _pendingRackAt)
        {
            _pendingRackAt = -1f;
            var gun = GetMainGun();
            if (gun != null)
            {
                gun.TryRack();
                Plugin.Log.LogInfo($"[CU_ServerPilot] 第二次拉栓（延时后）");
            }
        }

        if (!Input.GetKeyDown(KeyCode.R)) return;

        var gun2 = GetMainGun();
        if (gun2 == null) return;

        float now = Time.time;
        if (now - _lastRackTime < 0.5f)
        {
            // 连点第二次：延迟执行（避免联机同帧冲突）
            _pendingRackAt = _lastRackTime + cfgRackDelay.Value;
            if (_pendingRackAt < now) _pendingRackAt = now + 0.05f;
            Plugin.Log.LogInfo($"[CU_ServerPilot] 第二次拉栓已计划（{cfgRackDelay.Value:0.00}s 后）");
        }
        else
        {
            gun2.TryRack();
            _lastRackTime = now;
            Plugin.Log.LogInfo($"[CU_ServerPilot] R 键：已拉栓");
        }
    }

    private static GunScript GetMainGun()
    {
        var body = PlayerCamera.main?.body;
        var item = body?.GetItem(0);
        return item?.GetComponent<GunScript>();
    }
}
