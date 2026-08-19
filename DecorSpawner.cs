using System;
using System.Collections.Generic;
using System.Linq;
using KrokoshaCasualtiesMP;
using Newtonsoft.Json;
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
//  v0.10.7 双端同步: 服务器生成/擦除/全量 通过聊天通道广播 [CUSP-DECOR]，
//  客户端（也装插件）本地渲染同款装饰（KrokMP 不同步服务器实例化的装饰实体）。
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

// 服务器 → 客户端 装饰同步消息
public class DecorPayload
{
    public string op = "";               // add / remove / sync
    public string id = "";
    public float x, y, rot;
    public List<DecorPayload> list;      // sync 用
}

public static class DecorSpawner
{
    public const string DecorPrefix = "[CUSP-DECOR]";
    private static float _lastSync = -999f;
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
            ProcessAsDecor(go);
            BroadcastAdd(e.Name, pos, 0f);
            ConsoleManager.SendFeedback($"已生成装饰 [{e.Display}]（无碰撞无重力无交互）");
        }
        catch (Exception ex)
        {
            ConsoleManager.SendFeedback($"生成失败 {e.Name}: {ex.Message}");
        }
    }

    // 装饰化处理：剥离交互 + 挂 DecorTag（生成和存档加载共用）
    public static void ProcessAsDecor(GameObject go)
    {
        if (go == null) return;
        DisableInteractions(go);
        if (go.GetComponent<DecorTag>() == null)
            go.AddComponent<DecorTag>();
    }

    // 擦除鼠标位置的装饰物（按 DecorTag 标记匹配，距离 < 0.6 格）
    public static int EraseAtCursor()
    {
        var pos = SpawnTool.GetMouseWorldPos();
        int n = EraseAt(pos);
        if (n > 0) BroadcastRemove(pos);
        return n;
    }

    // 擦除指定位置的装饰（客户端同步擦除也用它）
    public static int EraseAt(Vector2 pos)
    {
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

    // 服务器周期 Update：每 10 秒广播一次全量装饰（新玩家/重连玩家自动补上）
    public static void Update()
    {
        try
        {
            if (!CustomMoodles.IsServer() || WorldGeneration.world == null) return;
            if (Time.time - _lastSync < 10f) return;
            _lastSync = Time.time;
            BroadcastSync();
        }
        catch { }
    }

    // ---- 服务器 → 客户端 广播 ----
    private static void BroadcastAdd(string id, Vector2 pos, float rot)
    {
        try
        {
            if (!CustomMoodles.IsServer()) return;
            string msg = DecorPrefix + JsonConvert.SerializeObject(
                new DecorPayload { op = "add", id = id, x = pos.x, y = pos.y, rot = rot }, Formatting.None);
            Chat.Server_ChatAnnouncement(ref msg);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 装饰广播失败: {e.Message}");
        }
    }

    private static void BroadcastRemove(Vector2 pos)
    {
        try
        {
            if (!CustomMoodles.IsServer()) return;
            string msg = DecorPrefix + JsonConvert.SerializeObject(
                new DecorPayload { op = "remove", x = pos.x, y = pos.y }, Formatting.None);
            Chat.Server_ChatAnnouncement(ref msg);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 装饰广播失败: {e.Message}");
        }
    }

    // 全量同步：当前世界所有 DecorTag 装饰
    public static void BroadcastSync()
    {
        try
        {
            if (!CustomMoodles.IsServer()) return;
            var list = new List<DecorPayload>();
            foreach (var tag in UnityEngine.Object.FindObjectsOfType<DecorTag>())
            {
                if (tag == null || tag.transform == null) continue;
                string id = tag.name.Replace("(Clone)", "").Trim();
                if (id.Length == 0) continue;
                var p = tag.transform.position;
                list.Add(new DecorPayload { id = id, x = p.x, y = p.y, rot = tag.transform.eulerAngles.z });
            }
            if (list.Count == 0) return;
            string msg = DecorPrefix + JsonConvert.SerializeObject(
                new DecorPayload { op = "sync", list = list }, Formatting.None);
            Chat.Server_ChatAnnouncement(ref msg);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 装饰全量同步广播: {list.Count} 个");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 装饰全量广播失败: {e.Message}");
        }
    }

    // 客户端接收并生成本地装饰
    public static void ClientSpawn(string id, float x, float y, float rot)
    {
        try
        {
            var prefab = Resources.Load<GameObject>(id);
            if (prefab == null) return;
            var go = UnityEngine.Object.Instantiate(prefab, new Vector3(x, y, 0f), Quaternion.Euler(0f, 0f, rot));
            if (go != null) ProcessAsDecor(go);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 客户端装饰生成失败: {e.Message}");
        }
    }

    // 客户端全量重建：先清本地 DecorTag 装饰，再按列表生成
    public static void ClientSyncAll(List<DecorPayload> list)
    {
        try
        {
            foreach (var tag in UnityEngine.Object.FindObjectsOfType<DecorTag>())
                if (tag != null && tag.gameObject != null)
                    UnityEngine.Object.Destroy(tag.gameObject);
            if (list == null) return;
            foreach (var d in list)
                ClientSpawn(d.id, d.x, d.y, d.rot);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 客户端装饰同步: {list.Count} 个");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 客户端装饰同步失败: {e.Message}");
        }
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
