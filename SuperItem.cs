using BepInEx.Configuration;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  SuperItem - 超级物品
//  ---------------------------------------------------------------------------
//  spawn 出的物品可选：
//   - 耐久锁定：condition 永远 = 1（100%）
//   - 无重力：Rigidbody2D.gravityScale = 0（精确到单物品，不动全局 itemFloating）
//  实现：生成时给物品挂 SuperItemTag 组件（Update 每帧锁定），只影响带标记的物品。
// ============================================================================

public class SuperItemTag : MonoBehaviour
{
    public Item item;

    private void Update()
    {
        if (item == null)
        {
            Destroy(this);
            return;
        }
        if (SuperItem.GetLockDurability())
            item.condition = 1f;
        if (SuperItem.GetNoGravity() && item.rb != null)
            item.rb.gravityScale = 0f;
    }
}

public static class SuperItem
{
    private static ConfigEntry<bool> cfgEnabled;
    private static ConfigEntry<bool> cfgLockDurability;
    private static ConfigEntry<bool> cfgNoGravity;

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("SuperItem", "Enabled", false, "超级物品模式：spawn 的物品带强化效果");
        cfgLockDurability = plugin.Config.Bind("SuperItem", "LockDurability", true, "耐久永远 100%");
        cfgNoGravity = plugin.Config.Bind("SuperItem", "NoGravity", true, "物品无重力（飘浮）");
    }

    public static bool GetEnabled() => cfgEnabled?.Value ?? false;
    public static void SetEnabled(bool v) { if (cfgEnabled != null) cfgEnabled.Value = v; }
    public static bool GetLockDurability() => cfgLockDurability?.Value ?? true;
    public static void SetLockDurability(bool v) { if (cfgLockDurability != null) cfgLockDurability.Value = v; }
    public static bool GetNoGravity() => cfgNoGravity?.Value ?? true;
    public static void SetNoGravity(bool v) { if (cfgNoGravity != null) cfgNoGravity.Value = v; }

    // 生成时调用：给物品挂标记
    public static void Apply(Item item)
    {
        if (item == null || !GetEnabled()) return;
        if (item.GetComponent<SuperItemTag>() != null) return;
        var tag = item.gameObject.AddComponent<SuperItemTag>();
        tag.item = item;
        if (GetLockDurability()) item.condition = 1f;
        if (GetNoGravity() && item.rb != null) item.rb.gravityScale = 0f;
    }
}
