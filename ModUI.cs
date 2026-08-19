using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  ModUI - IMGUI 控制面板（标签页版，需求 5/6）
//  ---------------------------------------------------------------------------
//  标签页:
//   [初始物资] [Spawn] [地图编辑] [战斗辅助] [存档]
//  现代简约风格：深色半透明面板 + 强调色高亮 + 统一圆角观感
//  存档页为占位（下一版完整实现存档/加载/导出导入）
// ============================================================================

public static class ModUI
{
    private static Rect windowRect = new Rect(0, 0, 500, 640);
    private static bool windowVisible = true;
    private static bool rectInitialized;
    private const int WindowId = 57322;

    private static int activeTab;
    private static readonly string[] TabNames = { "初始物资", "Spawn", "地图编辑", "战斗辅助", "状态", "存档" };

    // 整体滚动
    private static Vector2 windowScrollPos;

    // Tab1 初始物资
    private static bool editGiftMode;
    private static readonly Dictionary<string, (string count, string param)> editGifts = new Dictionary<string, (string, string)>();
    private static string giftFilter = "";
    private static Vector2 giftScrollPos;
    private static string giftTarget = TargetPicker.AllTarget;

    // Tab2 Spawn
    private static string spawnFilter = "";
    private static string spawnCount = "1";
    private static string spawnParam = "1";
    private static string spawnTarget = TargetPicker.CursorTarget;
    private static Vector2 targetScrollPos;
    private static string liquidFilter = "";
    private static string liquidMl = "100";
    private static Vector2 spawnScrollPos;
    private static Vector2 liquidScrollPos;
    private static string decorFilter = "";
    private static Vector2 decorScrollPos;
    private static bool decorCursorActive;
    private static bool decorEraser;
    private static float lastDecorTime = -999f;
    private static DecorEntry selectedDecor;
    // 生物倍率编辑框（独立字符串，点"应用"才写 cfg）
    private static string biomeMultInput;
    // 光标放置模式（左键选中 / 右键放置 / PgDn 取消）
    private static bool spawnCursorActive;
    private static string selectedSpawnItem;
    private static float lastSpawnTime = -999f;

    // Tab3 地图编辑
    private static string blockFilter = "";
    private static string brushSize = "1";
    private static Vector2 blockScrollPos;
    private static bool mapEditActive;
    private static bool eraserMode;
    private static int selectedBlock = -1;
    private static Vector2Int lastPlacedGrid = new Vector2Int(int.MinValue, int.MinValue);

    // Tab4 战斗辅助
    // （直接绑 cfg）

    private static List<string> allItems;
    private static List<string> allLiquids;
    private static List<MapEditor.BlockEntry> allBlocks;

    // 样式
    private static GUIStyle windowStyle;
    private static GUIStyle labelStyle;
    private static GUIStyle smallLabelStyle;
    private static GUIStyle buttonStyle;
    private static GUIStyle accentButtonStyle;
    private static GUIStyle itemButtonStyle;
    private static GUIStyle toggleStyle;
    private static GUIStyle toolbarStyle;
    private static GUIStyle titleStyle;
    private static GUIStyle accentLabelStyle;
    private static Texture2D winBgTex;
    private static Color accent = new Color(0.16f, 0.62f, 0.87f);

    internal static void ToggleVisible()
    {
        windowVisible = !windowVisible;
        Plugin.Log.LogInfo($"[CU_ServerPilot] 面板: {(windowVisible ? "显示" : "隐藏")}");
    }

    public static void Draw()
    {
        if (!windowVisible) return;
        EnsureStyles();

        // 进游戏世界后游戏可能改动 GUI 状态，这里强制复位，避免 UI 样式异常
        GUI.color = Color.white;
        GUI.enabled = true;

        if (allItems == null || allItems.Count == 0) allItems = ItemCatalog.GetAllItems();
        if (allLiquids == null) allLiquids = LiquidManager.GetLiquidList();

        if (!rectInitialized)
        {
            rectInitialized = true;
            windowRect.x = Screen.width - windowRect.width - 16;
            windowRect.y = Screen.height - windowRect.height - 40;
        }
        windowRect.x = Mathf.Clamp(windowRect.x, -windowRect.width + 80, Screen.width - 60);
        windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - 40);
        windowRect = GUI.Window(WindowId, windowRect, WindowFunc, "", windowStyle);
    }

    private static void WindowFunc(int id)
    {
        // 手动绘制窗口背景（覆盖标题栏区域），保证任何场景都显示半透明黑底
        if (winBgTex != null)
            GUI.DrawTexture(new Rect(-8, -28, windowRect.width + 16, windowRect.height + 34), winBgTex);

        // 自绘标题（GUI.Window 传空 title，避免自定义样式下标题被遮挡）
        GUILayout.Label($"CU ServerPilot v{VersionCheck.CurrentVersion}", titleStyle);
        if (VersionCheck.HasNew)
            GUILayout.Label($"⚠ 发现新版本 v{VersionCheck.LatestVersion}（GitHub 可下载）", accentLabelStyle);
        GUI.DragWindow(new Rect(0, 0, 10000, 22));

        // 标签页（手动 hover 检测 + 当前页高亮）
        GUILayout.BeginHorizontal();
        for (int i = 0; i < TabNames.Length; i++)
        {
            if (HoverButton(TabNames[i], i == activeTab ? accentButtonStyle : toolbarStyle))
                activeTab = i;
        }
        GUILayout.EndHorizontal();

        windowScrollPos = GUILayout.BeginScrollView(windowScrollPos,
            GUILayout.Width(windowRect.width - 24), GUILayout.Height(windowRect.height - 80));

        switch (activeTab)
        {
            case 0: DrawTabGifts(); break;
            case 1: DrawTabSpawn(); break;
            case 2: DrawTabMap(); break;
            case 3: DrawTabCombat(); break;
            case 4: DrawTabMoodles(); break;
            case 5: DrawTabSave(); break;
        }

        GUILayout.EndScrollView();
    }

    // ==========================================================================
    //  目标选择器（单选列表）：固定项 + 服务器玩家
    // ==========================================================================
    private static void DrawTargetPicker(ref string selected, bool allowCursor)
    {
        var targets = new List<string>();
        if (allowCursor) targets.Add(TargetPicker.CursorTarget);
        targets.AddRange(TargetPicker.GetTargets());
        if (!targets.Contains(selected)) selected = targets.Count > 0 ? targets[0] : TargetPicker.AllTarget;

        GUILayout.Label("目标:", labelStyle);
        targetScrollPos = GUILayout.BeginScrollView(targetScrollPos, GUILayout.Height(64));
        foreach (var t in targets)
        {
            bool sel = selected == t;
            bool newSel = GUILayout.Toggle(sel, " " + t, toggleStyle);
            if (newSel && !sel) selected = t;
        }
        GUILayout.EndScrollView();
    }

    // ==========================================================================
    //  Tab1 初始物资（multi-select + 数量 + 参数 + 目标）
    // ==========================================================================
    private static void DrawTabGifts()
    {
        bool ge = StartupGifts.GetGiftEnabled();
        ge = GUILayout.Toggle(ge, " 开局道具（启用/关闭）", toggleStyle);
        StartupGifts.SetGiftEnabled(ge);

        if (editGiftMode)
        {
            GUILayout.Label($"编辑初始物资（已选 {editGifts.Count}）: 名称 | 数量 | 参数(耐久)", labelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("搜索:", labelStyle, GUILayout.Width(44));
            giftFilter = GUILayout.TextField(giftFilter, 24);
            GUILayout.EndHorizontal();

            var shown = Filter(allItems, giftFilter);
            giftScrollPos = GUILayout.BeginScrollView(giftScrollPos, GUILayout.Height(250));
            foreach (var name in shown)
            {
                GUILayout.BeginHorizontal();
                bool sel = editGifts.ContainsKey(name);
                bool newSel = GUILayout.Toggle(sel, "", toggleStyle, GUILayout.Width(20));
                if (newSel != sel)
                {
                    if (newSel) editGifts[name] = ("1", "0");
                    else editGifts.Remove(name);
                }
                string label = Translations.ItemName(name);
                GUILayout.Label(label == name ? name : $"{label} ({name})", smallLabelStyle, GUILayout.Width(150));
                if (sel)
                {
                    var e = editGifts[name];
                    GUILayout.Label("x", smallLabelStyle, GUILayout.Width(12));
                    e.count = GUILayout.TextField(e.count, 3, GUILayout.Width(32));
                    GUILayout.Label("参", smallLabelStyle, GUILayout.Width(20));
                    e.param = GUILayout.TextField(e.param, 5, GUILayout.Width(48));
                    editGifts[name] = e;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (HoverButton("全选", buttonStyle)) foreach (var n in allItems) if (!editGifts.ContainsKey(n)) editGifts[n] = ("1", "0");
            if (HoverButton("清空", buttonStyle)) editGifts.Clear();
            if (HoverButton("保存配置", accentButtonStyle))
            {
                var parts = editGifts
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}:{kv.Value.count}:{kv.Value.param}");
                StartupGifts.SetGiftItems(string.Join(",", parts));
                editGiftMode = false;
                ConsoleManager.SendFeedback($"初始物资已保存（{editGifts.Count} 项）");
            }
            if (HoverButton("取消", buttonStyle)) editGiftMode = false;
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label("当前配置:", labelStyle);
            GUILayout.Label(StartupGifts.GetConfigText(), smallLabelStyle);
            GUILayout.BeginHorizontal();
            if (HoverButton("编辑初始物资", buttonStyle))
                EnterGiftEdit();
            if (HoverButton("立即发放", accentButtonStyle))
                SpawnTool.GiveGiftsToTarget(giftTarget);
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
            DrawTargetPicker(ref giftTarget, allowCursor: false);
        }
    }

    private static void EnterGiftEdit()
    {
        editGiftMode = true;
        editGifts.Clear();
        foreach (var g in StartupGifts.ParseGifts())
            editGifts[g.Name] = (g.Count.ToString(), g.Param.ToString("0.##"));
        giftFilter = "";
    }

    // ==========================================================================
    //  Tab2 Spawn 工具（物品/液体，数量/参数/对象）
    // ==========================================================================
    private static void DrawTabSpawn()
    {
        DrawTargetPicker(ref spawnTarget, allowCursor: true);

        GUILayout.BeginHorizontal();
        GUILayout.Label("数量:", labelStyle, GUILayout.Width(44));
        spawnCount = GUILayout.TextField(spawnCount, 4, GUILayout.Width(50));
        GUILayout.Label("参数:", labelStyle, GUILayout.Width(44));
        spawnParam = GUILayout.TextField(spawnParam, 6, GUILayout.Width(60));
        GUILayout.Label("(0~1 比例,1=满)", smallLabelStyle);
        GUILayout.EndHorizontal();

        // 超级物品开关
        bool si = SuperItem.GetEnabled();
        si = GUILayout.Toggle(si, " 超级物品", toggleStyle);
        SuperItem.SetEnabled(si);
        if (si)
        {
            bool ld = SuperItem.GetLockDurability();
            ld = GUILayout.Toggle(ld, " 耐久100%", toggleStyle);
            SuperItem.SetLockDurability(ld);
            bool ng = SuperItem.GetNoGravity();
            ng = GUILayout.Toggle(ng, " 无重力", toggleStyle);
            SuperItem.SetNoGravity(ng);
        }

        // 光标放置模式状态提示
        if (spawnCursorActive && spawnTarget == TargetPicker.CursorTarget)
        {
            string label = Translations.ItemName(selectedSpawnItem);
            GUILayout.Label($"光标放置: {label} — 右键放置，PgDn 取消", accentLabelStyle);
        }

        GUILayout.Label(spawnTarget == TargetPicker.CursorTarget ? "物品列表（左键选中）:" : "物品列表（点击发放）:", labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索:", labelStyle, GUILayout.Width(44));
        spawnFilter = GUILayout.TextField(spawnFilter, 24);
        GUILayout.EndHorizontal();

        var shown = Filter(allItems, spawnFilter);
        spawnScrollPos = GUILayout.BeginScrollView(spawnScrollPos, GUILayout.Height(150));
        foreach (var name in shown)
        {
            string label = Translations.ItemName(name);
            bool sel = spawnCursorActive && spawnTarget == TargetPicker.CursorTarget && selectedSpawnItem == name;
            if (HoverButton(label == name ? name : $"{label} ({name})", sel ? accentButtonStyle : itemButtonStyle))
                DoSpawn(name);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);

        // 生物生成倍率（平衡器）
        bool bb = BiomeBalance.GetEnabled();
        bb = GUILayout.Toggle(bb, " 生物生成倍率（每5秒平衡）", toggleStyle);
        BiomeBalance.SetEnabled(bb);
        if (bb)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("倍率:", labelStyle, GUILayout.Width(44));
            // 独立编辑框：不随 cfg 实时重置输入（避免输入过程被截断），点"应用"才生效
            if (biomeMultInput == null) biomeMultInput = BiomeBalance.GetMultiplier().ToString("0.##");
            biomeMultInput = GUILayout.TextField(biomeMultInput, 4, GUILayout.Width(50));
            if (HoverButton("应用", buttonStyle))
            {
                if (float.TryParse(biomeMultInput, out float bv))
                {
                    BiomeBalance.SetMultiplier(bv);
                    ConsoleManager.SendFeedback($"生物倍率已设为 {BiomeBalance.GetMultiplier():0.##}");
                }
                else ConsoleManager.SendFeedback("倍率无效，请输入数字");
            }
            GUILayout.Label("(0.1~3, 1=原样)", smallLabelStyle);
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6);

        GUILayout.Label("液体（点击=加入主手容器）:", labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索:", labelStyle, GUILayout.Width(44));
        liquidFilter = GUILayout.TextField(liquidFilter, 16);
        GUILayout.Label("毫升:", labelStyle, GUILayout.Width(44));
        liquidMl = GUILayout.TextField(liquidMl, 6, GUILayout.Width(60));
        GUILayout.EndHorizontal();
        var liqShown = Filter(allLiquids, liquidFilter);
        liquidScrollPos = GUILayout.BeginScrollView(liquidScrollPos, GUILayout.Height(80));
        foreach (var liq in liqShown)
        {
            string label = Translations.LiquidName(liq);
            if (HoverButton(label == liq ? liq : $"{label} ({liq})", itemButtonStyle))
            {
                if (!float.TryParse(liquidMl, out float ml)) ml = 100f;
                LiquidManager.AddToHandContainer(liq, ml);
            }
        }
        GUILayout.EndScrollView();
    }

    private static void DoSpawn(string name)
    {
        if (spawnTarget == TargetPicker.CursorTarget)
        {
            // 光标目标：左键选中，进入放置模式（右键放置，PgDn 取消）
            selectedSpawnItem = name;
            spawnCursorActive = true;
            ConsoleManager.SendFeedback($"已选中 {name} — 右键放置到鼠标位置，PgDn 取消");
            return;
        }
        int c = ParseInt(spawnCount, 1);
        float p = ParseFloat(spawnParam, 1f);
        SpawnTool.SpawnToTarget(spawnTarget, name, c, p);
    }

    // ==========================================================================
    //  Tab3 地图编辑（方块放置）
    // ==========================================================================
    private static void DrawTabMap()
    {
        // 每次都从 MapEditor 拿（内部有缓存 + 2 秒重试），避免缓存空结果
        allBlocks = MapEditor.GetBlocks();

        GUILayout.BeginHorizontal();
        GUILayout.Label("刷子半径:", labelStyle, GUILayout.Width(70));
        brushSize = GUILayout.TextField(brushSize, 2, GUILayout.Width(40));
        GUILayout.EndHorizontal();

        // 橡皮擦
        bool er = GUILayout.Toggle(eraserMode && mapEditActive, " 橡皮擦", toggleStyle);
        if (er && !(eraserMode && mapEditActive)) { eraserMode = true; mapEditActive = true; selectedBlock = -1; }
        else if (!er && eraserMode && mapEditActive) { eraserMode = false; }

        // 放置模式状态提示
        if (mapEditActive)
        {
            string tip = eraserMode
                ? "橡皮擦模式 — 右键擦除，PgDn 取消"
                : $"放置: {GetBlockDisplay(selectedBlock)} — 右键放置，PgDn 取消";
            GUILayout.Label(tip, accentLabelStyle);
        }

        GUILayout.Label("方块列表（左键选中）:", labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索:", labelStyle, GUILayout.Width(44));
        blockFilter = GUILayout.TextField(blockFilter, 24);
        GUILayout.EndHorizontal();

        // 方块搜索：支持中文名（翻译）或原名
        var shown = allBlocks.Where(b =>
            string.IsNullOrWhiteSpace(blockFilter) ||
            b.Name.Contains(blockFilter.Trim().ToLowerInvariant()) ||
            Translations.BlockName(b.Name).Contains(blockFilter.Trim().ToLowerInvariant())
        ).ToList();
        blockScrollPos = GUILayout.BeginScrollView(blockScrollPos, GUILayout.Height(200));
        foreach (var b in shown)
        {
            string label = Translations.BlockName(b.Name);
            string display = label == b.Name ? $"{b.Name} [{b.Id}]" : $"{label} ({b.Name}) [{b.Id}]";
            bool sel = mapEditActive && !eraserMode && selectedBlock == b.Id;
            if (HoverButton(display, sel ? accentButtonStyle : itemButtonStyle))
            {
                selectedBlock = b.Id;
                eraserMode = false;
                mapEditActive = true;
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(8);

        // ---- 背景装饰物（装饰/物品；左键选中，右键放置，PgDn 取消） ----
        bool de = GUILayout.Toggle(decorEraser, " 装饰橡皮擦（右键擦除）", toggleStyle);
        if (de != decorEraser) { decorEraser = de; if (de) decorCursorActive = true; }
        GUILayout.Label("背景装饰（装饰/物品 — 左键选中，右键放置，PgDn 取消）:", labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索:", labelStyle, GUILayout.Width(44));
        decorFilter = GUILayout.TextField(decorFilter, 16);
        GUILayout.EndHorizontal();
        if (decorCursorActive)
        {
            string tip = decorEraser
                ? "装饰橡皮擦 — 右键擦除装饰/物品，PgDn 取消"
                : $"装饰放置: [{TypeTag(selectedDecor?.Type)}] {selectedDecor?.Display} — 右键放置，PgDn 取消";
            GUILayout.Label(tip, accentLabelStyle);
        }
        var decorAll = DecorSpawner.GetAll();
        var decorShown = string.IsNullOrWhiteSpace(decorFilter)
            ? decorAll
            : decorAll.Where(x =>
                x.Name.Contains(decorFilter.Trim().ToLowerInvariant()) ||
                x.Display.Contains(decorFilter.Trim().ToLowerInvariant())).ToList();
        decorScrollPos = GUILayout.BeginScrollView(decorScrollPos, GUILayout.Height(120));
        foreach (var dn in decorShown)
        {
            bool sel = decorCursorActive && selectedDecor == dn;
            string display = $"[{TypeTag(dn.Type)}] {dn.Display}";
            if (HoverButton(display, sel ? accentButtonStyle : itemButtonStyle))
            {
                selectedDecor = dn;
                decorCursorActive = true;
            }
        }
        GUILayout.EndScrollView();
    }

    private static string TypeTag(string t) => t == "item" ? "物品" : "装饰";

    private static string GetBlockDisplay(int id)
    {
        foreach (var b in allBlocks)
            if (b.Id == id) return Translations.BlockName(b.Name);
        return "?" + id;
    }

    // ==========================================================================
    //  放置模式主循环（插件 Update 每帧调用）
    //  PgDn 取消；按住右键拖动连续放置/擦除（移到新格子才放置，同格去重）
    // ==========================================================================
    public static void Update()
    {
        // ---- 背景装饰放置模式（左键选中 / 右键放置或擦除 / PgDn 取消） ----
        if (decorCursorActive)
        {
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                decorCursorActive = false;
                decorEraser = false;
                selectedDecor = null;
                ConsoleManager.SendFeedback("已退出装饰放置模式");
                return;
            }
            if (Input.GetMouseButton(1) && Time.time - lastDecorTime > 0.15f)
            {
                lastDecorTime = Time.time;
                if (decorEraser)
                {
                    int n = DecorSpawner.EraseAtCursor();
                    if (n > 0) ConsoleManager.SendFeedback($"已擦除 {n} 个装饰");
                }
                else if (selectedDecor != null)
                {
                    DecorSpawner.SpawnAtCursor(selectedDecor);
                }
            }
            return;
        }

        // ---- Spawn 光标放置模式（左键选中 / 右键放置 / PgDn 取消） ----
        if (spawnCursorActive && spawnTarget == TargetPicker.CursorTarget)
        {
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                spawnCursorActive = false;
                selectedSpawnItem = null;
                ConsoleManager.SendFeedback("已退出光标放置模式");
                return;
            }
            if (Input.GetMouseButton(1) && Time.time - lastSpawnTime > 0.15f)   // 按住拖动，节流
            {
                lastSpawnTime = Time.time;
                int c = ParseInt(spawnCount, 1);
                float p = ParseFloat(spawnParam, 1f);
                ConsoleManager.SpawnItemAt(selectedSpawnItem, SpawnTool.GetMouseWorldPos(), c, p);
            }
            return;
        }

        // ---- 地图编辑放置模式 ----
        if (!mapEditActive) return;

        if (Input.GetKeyDown(KeyCode.PageDown))
        {
            mapEditActive = false;
            eraserMode = false;
            selectedBlock = -1;
            ConsoleManager.SendFeedback("已退出放置模式");
            return;
        }

        if (Input.GetMouseButton(1))   // 按住右键（含拖动）
        {
            int brush = ParseInt(brushSize, 1);
            Vector2Int grid = CurrentMouseGrid();
            if (grid != lastPlacedGrid)   // 移到新格子才放置（画线平滑 + 防同格重复）
            {
                lastPlacedGrid = grid;
                if (eraserMode)
                    MapEditor.EraseAtCursor(brush, quiet: true);
                else if (selectedBlock >= 0)
                    MapEditor.PlaceBlockAtCursor((ushort)selectedBlock, brush, quiet: true);
            }
        }
        else
        {
            lastPlacedGrid = new Vector2Int(int.MinValue, int.MinValue);   // 松开右键，重置去重标记
        }
    }

    private static Vector2Int CurrentMouseGrid()
    {
        var world = WorldGeneration.world;
        if (world == null) return new Vector2Int(int.MinValue, int.MinValue);
        return world.WorldToBlockPos(SpawnTool.GetMouseWorldPos());
    }

    // ==========================================================================
    //  Tab4 战斗辅助
    // ==========================================================================
    private static void DrawTabCombat()
    {
        bool e = CombatAssist.GetEnabled();
        e = GUILayout.Toggle(e, " 战斗辅助：按 R 给主手枪械拉栓", toggleStyle);
        CombatAssist.SetEnabled(e);
        GUILayout.Space(6);

        bool ia = InfiniteAmmo.GetEnabled();
        ia = GUILayout.Toggle(ia, " 无限子弹", toggleStyle);
        InfiniteAmmo.SetEnabled(ia);
        if (ia)
        {
            bool ch = InfiniteAmmo.GetChamber();
            ch = GUILayout.Toggle(ch, " 连枪膛一起补（霰弹枪第二发）", toggleStyle);
            InfiniteAmmo.SetChamber(ch);
        }
        GUILayout.Space(6);

        // ---- 玩家操作（拉取/昏迷/治疗） ----
        GUILayout.Label("玩家操作:", labelStyle);
        DrawTargetPicker(ref playerOpsTarget, allowCursor: false);
        GUILayout.BeginHorizontal();
        if (HoverButton("一键拉取玩家", accentButtonStyle))
            PlayerOps.PullToLocal(playerOpsTarget);
        if (HoverButton("一键昏迷", buttonStyle))
            PlayerOps.Knockout(playerOpsTarget);
        if (HoverButton("一键治疗", buttonStyle))
            PlayerOps.Heal(playerOpsTarget);
        GUILayout.EndHorizontal();
        GUILayout.Space(6);

        GUILayout.Label("说明: 启用后，游戏内按 R 键即可给主手枪械拉栓（替代拖拽鼠标拉栓）", smallLabelStyle);
        GUILayout.Space(8);
        if (HoverButton("一键清除掉落物", buttonStyle))
            ConsoleManager.SendFeedback($"已清除 {ConsoleManager.ClearDrops()} 个掉落物");
    }

    private static string playerOpsTarget = TargetPicker.AllTarget;

    // ==========================================================================
    //  Tab5 自定义状态（Moodle）
    // ==========================================================================
    private static string moodleTarget = TargetPicker.AllTarget;
    private static Vector2 moodleScrollPos;

    private static void DrawTabMoodles()
    {
        bool me = CustomMoodles.GetEnabled();
        me = GUILayout.Toggle(me, " 自定义状态模块（启用/关闭）", toggleStyle);
        CustomMoodles.SetEnabled(me);

        GUILayout.Space(2);
        DrawTargetPicker(ref moodleTarget, allowCursor: false);

        var moods = CustomMoodles.GetMoodles();
        GUILayout.Label($"状态列表（{moods.Count} 个，配置在 plugins/CU_ServerPilot/moodles.json）:", labelStyle);
        moodleScrollPos = GUILayout.BeginScrollView(moodleScrollPos, GUILayout.Height(180));
        foreach (var m in moods)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"[{m.title}]", labelStyle, GUILayout.Width(120));
            GUILayout.Label(m.desc, smallLabelStyle);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (HoverButton("应用给目标", buttonStyle))
                CustomMoodles.ApplyToTarget(moodleTarget, m);
            if (m.applyOnJoin)
                GUILayout.Label("(进服自动)", smallLabelStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }
        GUILayout.EndScrollView();

        if (HoverButton("应用全部状态给目标", accentButtonStyle))
            CustomMoodles.ApplyAllToTarget(moodleTarget);
        if (HoverButton("列出全部可选图标", buttonStyle))
            CustomMoodles.ListIcons();
        GUILayout.Label("提示: 状态图标/文本在 moodles.json 中自定义，支持多个状态、指定玩家、进服自动；「列出图标」可查看可用的内图标 key", smallLabelStyle);
    }

    // ==========================================================================
    //  Tab6 存档（保存/加载/导出/导入）
    // ==========================================================================
    private static string saveName = "map1";
    private static string[] saveList = Array.Empty<string>();
    private static Vector2 saveScrollPos;

    private static void DrawTabSave()
    {
        GUILayout.Label("存档名:", labelStyle);
        saveName = GUILayout.TextField(saveName, 24);

        GUILayout.BeginHorizontal();
        if (HoverButton("保存地图", accentButtonStyle)) MapSave.SaveMap(saveName);
        if (HoverButton("加载地图", accentButtonStyle)) MapSave.LoadMap(saveName);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (HoverButton("导出存档", buttonStyle)) MapSave.ExportMap(saveName);
        if (HoverButton("导入存档", buttonStyle)) MapSave.ImportMap(saveName);
        GUILayout.EndHorizontal();

        GUILayout.Label($"存档目录: {MapSave.SaveFolder}", smallLabelStyle);
        GUILayout.Label($"导出目录: {MapSave.ExportFolder}", smallLabelStyle);

        GUILayout.Space(6);
        GUILayout.Label("已有存档（点击=填入名称）:", labelStyle);
        saveList = MapSave.ListSaves();
        saveScrollPos = GUILayout.BeginScrollView(saveScrollPos, GUILayout.Height(160));
        foreach (var s in saveList)
        {
            if (HoverButton(s, itemButtonStyle))
                saveName = s;
        }
        if (saveList.Length == 0)
            GUILayout.Label("（暂无存档）", smallLabelStyle);
        GUILayout.EndScrollView();
    }

    // ==========================================================================
    //  工具
    // ==========================================================================
    // 中文/英文混合搜索：匹配翻译名 或 英文 ID
    private static List<string> Filter(List<string> list, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return list;
        string k = key.Trim().ToLowerInvariant();
        return list.Where(x =>
            x.Contains(k) ||
            Translations.ItemName(x).ToLowerInvariant().Contains(k)
        ).ToList();
    }

    private static int ParseInt(string s, int def) => int.TryParse(s, out int v) ? v : def;
    private static float ParseFloat(string s, float def) => float.TryParse(s, out float v) ? v : def;

    // ==========================================================================
    //  手动 hover 检测按钮 + 强制背景绘制：
    //  进游戏世界后 Unity 的 hover 状态机和按钮背景绘制可能失效，
    //  这里背景用 GUI.DrawTexture 强制画、文字用 GUI.Label、点击用无样式按钮，
    //  完全不依赖 GUI.Button 的内部绘制路径
    // ==========================================================================
    private static GUIStyle hoverStyle;
    private static GUIStyle btnLabelStyle;
    private static Texture2D hoverBgTex;

    private static bool HoverButton(string text, GUIStyle style)
    {
        var content = new GUIContent(text);
        var rect = GUILayoutUtility.GetRect(content, style);
        bool hover = rect.Contains(Event.current.mousePosition);

        // 背景：hover 提亮，否则用按钮自身背景（选中=accent 蓝）
        Texture2D bg = hover ? hoverBgTex : style.normal.background;
        if (Event.current.type == EventType.Repaint && bg != null)
            GUI.DrawTexture(rect, bg, ScaleMode.StretchToFill);

        // 文字
        if (Event.current.type == EventType.Repaint)
            GUI.Label(rect, content, btnLabelStyle);

        // 点击检测（透明按钮，只占位接收事件）
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    private static void EnsureStyles()
    {
        if (windowStyle != null) return;

        // 半透明黑纯色贴图（方形，无圆角无边框）
        winBgTex = MakeTex(0.05f, 0.06f, 0.08f, 0.92f);
        var winBg = winBgTex;
        var btnBg = MakeTex(0.16f, 0.20f, 0.28f, 0.95f);
        var btnHover = MakeTex(0.24f, 0.31f, 0.42f, 1f);
        var btnActive = MakeTex(0.10f, 0.13f, 0.18f, 1f);
        var itemBg = MakeTex(0.12f, 0.15f, 0.21f, 0.9f);
        var itemHover = MakeTex(0.20f, 0.26f, 0.36f, 1f);
        var accentBg = MakeTex(0.10f, 0.45f, 0.70f, 1f);
        var accentHover = MakeTex(0.15f, 0.55f, 0.85f, 1f);

        windowStyle = new GUIStyle();
        windowStyle.normal.background = winBg;
        windowStyle.normal.textColor = Color.white;
        windowStyle.fontSize = 13;
        windowStyle.border = new RectOffset(0, 0, 0, 0);
        windowStyle.padding = new RectOffset(8, 8, 6, 6);

        labelStyle = new GUIStyle();
        labelStyle.fontSize = 13;
        labelStyle.normal.textColor = Color.white;
        labelStyle.wordWrap = true;
        labelStyle.padding = new RectOffset(2, 2, 1, 1);

        smallLabelStyle = new GUIStyle(labelStyle);
        smallLabelStyle.fontSize = 11;
        smallLabelStyle.normal.textColor = new Color(0.75f, 0.80f, 0.87f);

        buttonStyle = MakeButton(btnBg, btnHover, btnActive);
        itemButtonStyle = MakeButton(itemBg, itemHover, btnActive);
        itemButtonStyle.alignment = TextAnchor.MiddleLeft;
        itemButtonStyle.fontSize = 11;

        accentButtonStyle = MakeButton(accentBg, accentHover, accentBg);

        toggleStyle = new GUIStyle(GUI.skin.toggle);
        toggleStyle.fontSize = 12;
        toggleStyle.normal.textColor = Color.white;
        toggleStyle.hover.textColor = Color.white;

        toolbarStyle = MakeButton(btnBg, btnHover, btnActive);
        toolbarStyle.fontSize = 12;
        toolbarStyle.alignment = TextAnchor.MiddleCenter;

        titleStyle = new GUIStyle(labelStyle);
        titleStyle.fontSize = 14;
        titleStyle.fontStyle = FontStyle.Bold;

        accentLabelStyle = new GUIStyle(labelStyle);
        accentLabelStyle.normal.textColor = accent;
        accentLabelStyle.fontStyle = FontStyle.Bold;

        // 按钮文字样式（HoverButton 用）：居中
        btnLabelStyle = new GUIStyle(labelStyle);
        btnLabelStyle.fontSize = 12;
        btnLabelStyle.alignment = TextAnchor.MiddleCenter;

        // hover 提亮背景（蓝色方框）
        hoverBgTex = MakeTex(0.28f, 0.45f, 0.68f, 1f);
    }

    private static GUIStyle MakeButton(Texture2D bg, Texture2D hover, Texture2D active)
    {
        var s = new GUIStyle();
        s.normal.background = bg;
        s.normal.textColor = Color.white;
        s.hover.background = hover;
        s.hover.textColor = Color.white;
        s.active.background = active;
        s.active.textColor = Color.white;
        s.border = new RectOffset(0, 0, 0, 0);
        s.padding = new RectOffset(8, 8, 4, 4);
        s.margin = new RectOffset(2, 2, 2, 2);
        s.fontSize = 12;
        return s;
    }

    private static Texture2D MakeTex(float r, float g, float b, float a)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, new Color(r, g, b, a));
        t.Apply();
        return t;
    }
}
