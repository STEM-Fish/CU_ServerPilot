using System;
using System.Collections.Generic;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  DecorSpawner - 背景装饰物生成（两类：装饰预制体 / 无重力物品）
//  ---------------------------------------------------------------------------
//  生成不可交互、无碰撞、无重力的装饰物件（光照/动画/渲染保留）。
//  物品装饰: 实例化物品 → 禁碰撞 + 禁重力(simulated=false) + 禁交互组件
//  预制体装饰: 实例化 → 同样处理（跳过 Light2D/Animator 保留光照动画）
//  翻译: 装饰=decor.zh-CN.json / 物品=items.zh-CN.json
//  （方块装饰已删：游戏方块无逐格无碰撞机制，TilemapCollider 是整体生成的）
// ============================================================================

public class DecorEntry
{
    public string Type;    // "decor" / "item"
    public string Name;    // 显示名/资源名

    public string Display => Type switch
    {
        "item" => Translations.ItemName(Name),
        _ => Translations.DecorName(Name),
    };
}

// 标记组件：挂在生成的装饰物上，橡皮擦用它识别"这是我们生成的装饰"
public class DecorTag : MonoBehaviour { }

public static class DecorSpawner
{
    // 装饰物预制体名单（世界生成里的植被/结构类）
    private static readonly string[] DecorNames =
    {
        "glowplant", "stoneplant", "ceilingrye", "geotree", "hydreed",
        "leadbush", "cactus", "sandrose", "drybush", "brownshroom",
        "stalagmite", "oilpipe", "spentfuel", "coil", "grabberplant",
        "bananaplant", "browncap", "rosepod", "roselight", "stalactite"
    };

    private static List<DecorEntry> _decor;
    private static List<DecorEntry> _items;

    public static List<DecorEntry> GetDecors()
    {
        if (_decor == null)
        {
            _decor = new List<DecorEntry>();
            foreach (var n in DecorNames)
                _decor.Add(new DecorEntry { Type = "decor", Name = n });
        }
        return _decor;
    }

    // 物品装饰：全部物品（无重力悬浮）
    public static List<DecorEntry> GetItems()
    {
        if (_items == null)
        {
            _items = new List<DecorEntry>();
            foreach (var n in ItemCatalog.GetAllItems())
                _items.Add(new DecorEntry { Type = "item", Name = n });
        }
        return _items;
    }

    // 全部（用于搜索过滤）
    public static List<DecorEntry> GetAll()
    {
        var list = new List<DecorEntry>();
        list.AddRange(GetDecors());
        list.AddRange(GetItems());
        return list;
    }

    // 生成到鼠标位置
    public static void SpawnAtCursor(DecorEntry e)
    {
        if (e == null) return;
        var pos = SpawnTool.GetMouseWorldPos();
        try
        {
            var prefab = Resources.Load<GameObject>(e.Name);
            if (prefab == null)
            {
                ConsoleManager.SendFeedback($"预制体不存在: {e.Name}");
                return;
            }
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            if (go == null) return;
            DisableInteractions(go);
            ConsoleManager.SendFeedback($"已生成装饰 [{e.Display}]（无碰撞无重力无交互）");
        }
        catch (Exception ex)
        {
            ConsoleManager.SendFeedback($"生成失败 {e.Name}: {ex.Message}");
        }
    }

    // 擦除鼠标位置的装饰物（按 DecorTag 标记匹配，距离 < 0.6 格）
    public static int EraseAtCursor()
    {
        var pos = SpawnTool.GetMouseWorldPos();
        int n = 0;
        try
        {
            foreach (var tag in UnityEngine.Object.FindObjectsOfType<DecorTag>())
            {
                if (tag == null || tag.transform == null) continue;
                if (Vector2.Distance(pos, (Vector2)tag.transform.position) < 0.6f)
                {
                    UnityEngine.Object.Destroy(tag.gameObject);
                    n++;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 擦除装饰失败: {e.Message}");
        }
        return n;
    }

    // 剥离交互：禁用行为组件（跳过 Light2D/Animator 保留光照动画）+ 碰撞 + 重力
    private static void DisableInteractions(GameObject go)
    {
        try
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string tn = mb.GetType().Name;
                if (tn == "Light2D" || tn == "Animator" || tn == "SpriteRenderer")
                    continue;
                mb.enabled = false;
            }
            foreach (var c in go.GetComponentsInChildren<Collider2D>(true))
                c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody2D>(true))
                rb.simulated = false;

            // 标记为"插件生成的装饰物"（橡皮擦识别用）
            go.AddComponent<DecorTag>();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 装饰剥离交互失败: {e.Message}");
        }
    }
}
