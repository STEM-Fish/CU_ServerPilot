using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  CU ServerPilot - Casualties Unknown 服务器管理工具
//  ---------------------------------------------------------------------------
//  游戏: Unity 2022.3 Mono + BepInEx 5.4.23.4
//
//  模块:
//   1. ConsoleManager - 管理员聊天命令框架（hook 游戏 ConsoleScript.TryExecuteCommand）
//   2. StartupGifts   - 开局给玩家发自定义道具（hook PlayerCamera.OnBecameConscious）
//
//  经验复用（来自 STEM_Fish_MOD）:
//   - Harmony 手动 patch + GetPatchInfo 验证（本环境 PatchAll 不可靠）
//   - 游戏私有方法用字符串名
// ============================================================================

[BepInPlugin("com.cuserverpilot", "CU ServerPilot", "0.1.0")]
[BepInProcess("CasualtiesUnknown.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static Plugin Instance;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        // ---- 模块初始化 ----
        ConsoleManager.Init(this);
        StartupGifts.Init(this);
        MpGifts.Init(this);
        CustomMoodles.Init(this);
        InfiniteAmmo.Init(this);
        SuperItem.Init(this);
        BiomeBalance.Init(this);
        MoodleClient.Init(this);
        CombatAssist.Init(this);

        Log.LogInfo("[CU_ServerPilot] 加载完成。聊天输入 /sphelp 查看命令");
    }

    private void Update()
    {
        CombatAssist.Update();
        ModUI.Update();
        BiomeBalance.Update();
    }

    private void OnDestroy()
    {
        // 预留：模块清理
    }

    // IMGUI 控制面板（F5 切换显隐）
    private void OnGUI()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F5)
            ModUI.ToggleVisible();

        ModUI.Draw();
    }
}
