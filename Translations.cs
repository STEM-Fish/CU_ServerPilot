using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;

namespace CU_ServerPilot;

// ============================================================================
//  Translations - 物品/液体翻译（分开两个文件，可自定义）
//  ---------------------------------------------------------------------------
//  物品翻译: BepInEx/plugins/CU_ServerPilot/items.zh-CN.json
//            （兼容旧文件 translations.zh-CN.json、plugins 根目录）
//  液体翻译: BepInEx/plugins/CU_ServerPilot/liquids.zh-CN.json
//  格式: {"英文id": "中文名", ...}；值留空 "" 的条目显示英文 ID
// ============================================================================

public static class Translations
{
    private static Dictionary<string, string> _items;
    private static Dictionary<string, string> _liquids;
    private static Dictionary<string, string> _blocks;
    private static Dictionary<string, string> _decors;
    private static bool _loaded;

    public static string ItemName(string id) { Ensure(); return Lookup(_items, id); }
    public static string LiquidName(string id) { Ensure(); return Lookup(_liquids, id); }
    public static string BlockName(string name) { Ensure(); return Lookup(_blocks, name); }
    public static string DecorName(string name) { Ensure(); return Lookup(_decors, name); }

    // 兼容旧调用
    public static string DisplayName(string id) { return ItemName(id); }

    private static string Lookup(Dictionary<string, string> map, string id)
    {
        if (string.IsNullOrEmpty(id)) return id ?? "";
        if (map != null && map.TryGetValue(id, out var zh) && !string.IsNullOrEmpty(zh))
            return zh;
        return id;
    }

    private static void Ensure()
    {
        if (_loaded) return;
        _loaded = true;

        string dir = Path.Combine(Paths.PluginPath, "CU_ServerPilot");

        _items = LoadJson(Path.Combine(dir, "items.zh-CN.json"))
              ?? LoadJson(Path.Combine(dir, "translations.zh-CN.json"))
              ?? LoadJson(Path.Combine(Paths.PluginPath, "translations.zh-CN.json"));
        _liquids = LoadJson(Path.Combine(dir, "liquids.zh-CN.json"));
        _blocks = LoadJson(Path.Combine(dir, "blocks.zh-CN.json"));
        _decors = LoadJson(Path.Combine(dir, "decor.zh-CN.json"));

        Plugin.Log.LogInfo($"[CU_ServerPilot] 翻译: 物品 {_items?.Count ?? 0} / 液体 {_liquids?.Count ?? 0} / 方块 {_blocks?.Count ?? 0} / 装饰 {_decors?.Count ?? 0}");
    }

    private static Dictionary<string, string> LoadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 翻译文件解析失败 {path}: {e.Message}");
            return null;
        }
    }
}
