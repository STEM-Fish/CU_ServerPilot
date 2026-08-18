using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using Newtonsoft.Json;
using UnityEngine;

namespace CU_ServerPilot;

// ============================================================================
//  CustomMoodles - 自定义状态（服务器端：记录 + 广播）
//  ---------------------------------------------------------------------------
//  配置: plugins/CU_ServerPilot/moodles.json（多状态）
//  架构（双端同步）:
//   服务器: 状态应用 → 本地立即显示（主机）+ 通过 KrokMP 聊天通道广播
//           "[CUSP-MOODLE]{target,ids}" 消息
//   客户端: MoodleClient 拦截该消息 → 本地渲染（需客户端也装 CU_ServerPilot）
//  本地保持: 游戏每 0.5s 重建状态（ClearMoodles），patch UpdateMoodles postfix
//           在游戏重建后重新添加（服务器/客户端各自保持自己的）
//  AddMoodle 参数: (backgroundIcons索引, icons字典key, tipName, tipDesc, critical, side)
// ============================================================================

public class MoodleDef
{
    public string id = "";
    public string title = "服务器状态";
    public string desc = "";
    public int moodleType = 0;         // backgroundIcons 数组索引（外框图标）
    public int iconIndex = 0;          // icons 字典第 N 个 key（内图标，iconKey 为空时用）
    public string iconKey = "";        // 直接指定 icons 字典 key（优先，如 "impendingdoom"）
    public bool important = true;
    public bool applyOnJoin = false;   // 进服自动应用
}

// 服务器 → 客户端 状态同步消息
public class MoodlePayload
{
    public string target = "";         // "@a 全体" 或 玩家名
    public List<string> ids = new List<string>();
}

// 标记组件：挂在自定义状态对象上，游戏 ClearMoodles 清理时跳过它（常驻显示）
public class CustomMoodleTag : MonoBehaviour
{
    public string moodleId = "";
}

public static class CustomMoodles
{
    public const string CuspPrefix = "[CUSP-MOODLE]";

    private static ConfigEntry<bool> cfgEnabled;
    private static List<MoodleDef> _moodles;
    private static readonly Dictionary<string, HashSet<string>> PlayerMoodles = new Dictionary<string, HashSet<string>>();

    public static void Init(Plugin plugin)
    {
        cfgEnabled = plugin.Config.Bind("CustomMoodles", "Enabled", true, "启用自定义状态模块");
        _moodles = LoadJson();
        if (_moodles.Count == 0)
            _moodles = Defaults();
        Plugin.Log.LogInfo($"[CU_ServerPilot] 自定义状态: {_moodles.Count} 个定义");

        try
        {
            var harmony = new Harmony("com.cuserverpilot.moodles");

            // 1) 游戏每 0.5s 重建状态：ClearMoodles 销毁 moodles 全部子对象
            //    拦截它：跳过带 CustomMoodleTag 的对象（我们的状态），只清游戏的
            var targetClear = AccessTools.Method(typeof(MoodleManager), "ClearMoodles");
            var prefix = typeof(CustomMoodles).GetMethod("ClearMoodles_Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(targetClear, prefix: new HarmonyMethod(prefix));
            var infoC = Harmony.GetPatchInfo(targetClear);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 状态清理拦截 hook 完成 | prefix 数量={infoC?.Prefixes?.Count ?? -1}");

            // 2) 游戏重建完游戏状态后，我们把记录的自定义状态补上（只创建一次）
            var target = AccessTools.Method(typeof(MoodleManager), "UpdateMoodles");
            if (target == null)
            {
                Plugin.Log.LogError("[CU_ServerPilot] 找不到 MoodleManager.UpdateMoodles");
                return;
            }
            var postfix = typeof(CustomMoodles).GetMethod("UpdateMoodles_Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            var info = Harmony.GetPatchInfo(target);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 状态保持 hook 完成 | postfix 数量={info?.Postfixes?.Count ?? -1}");

            // 3) Moodle.Update 每帧按 showSideMoodles 禁用 img/img2/flash（鼠标移开就隐藏）
            //    拦截它：带 tag 的对象每帧强制 enable → 真正常显
            var targetMoodle = AccessTools.Method(typeof(Moodle), "Update");
            if (targetMoodle != null)
            {
                var postfixM = typeof(CustomMoodles).GetMethod("MoodleUpdate_Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(targetMoodle, postfix: new HarmonyMethod(postfixM));
                Plugin.Log.LogInfo("[CU_ServerPilot] 状态常显 hook 完成");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[CU_ServerPilot] 状态保持 hook 失败: {e}");
        }
    }

    public static bool GetEnabled() => cfgEnabled?.Value ?? true;
    public static void SetEnabled(bool v) { if (cfgEnabled != null) cfgEnabled.Value = v; }

    public static List<MoodleDef> GetMoodles() => _moodles ?? (_moodles = Defaults());

    public static bool IsServer()
    {
        try { return KrokoshaScavMultiplayer.is_server || KrokoshaScavMultiplayer.is_dedicated_server; }
        catch { return true; }
    }

    // 进服自动状态（MpGifts 调用）：记录 + 广播
    public static void ApplyJoinMoodles(Body body)
    {
        if (body == null || !GetEnabled()) return;
        var ids = new List<string>();
        foreach (var m in GetMoodles())
            if (m.applyOnJoin) { AddRecord(body, m); ids.Add(m.id); }
        if (ids.Count > 0)
            BroadcastMoodles(GetPlayerName(body) ?? "@本机", ids);
    }

    // 给指定 Body 应用状态（记录 + 广播 + 主机本地立即显示）
    public static void ApplyToBody(Body body, MoodleDef def)
    {
        if (body == null || def == null) return;
        AddRecord(body, def);
        BroadcastMoodles(GetPlayerName(body) ?? "@本机", new[] { def.id });
        var mm = GetMoodleManagerFor(body);
        Plugin.Log.LogInfo($"[CU_ServerPilot] [诊断] ApplyToBody: body={body.name}, MoodleManager={(mm != null ? "找到" : "未找到")}");
        if (mm != null) ImmediateAdd(mm, def);
    }

    // 服务器广播状态同步消息（is_server 时）
    public static void BroadcastMoodles(string target, IEnumerable<string> ids)
    {
        try
        {
            if (!IsServer()) return;
            var list = ids.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (list.Count == 0) return;
            string payload = CuspPrefix + JsonConvert.SerializeObject(new MoodlePayload { target = target, ids = list }, Formatting.None);
            string msg = payload;
            Chat.Server_ChatAnnouncement(ref msg);
            Plugin.Log.LogInfo($"[CU_ServerPilot] 已广播状态同步: target={target}, ids=[{string.Join(",", list)}]");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 状态广播失败: {e.Message}");
        }
    }

    private static float _diagTime = -999f;

    // 游戏每 0.5s 重建状态后，重新添加本玩家记录的自定义状态（服务器/主机保持）
    private static void UpdateMoodles_Postfix(MoodleManager __instance)
    {
        if (!GetEnabled() || __instance == null) return;
        var body = AccessTools.Field(typeof(MoodleManager), "body")?.GetValue(__instance) as Body;
        if (body == null) return;
        string key = BodyKey(body);
        bool has = PlayerMoodles.TryGetValue(key, out var ids);

        // 诊断（2s 节流）
        if (Time.time - _diagTime > 2f)
        {
            _diagTime = Time.time;
            Plugin.Log.LogInfo($"[CU_ServerPilot] [诊断] UpdateMoodles postfix: body={body.name}, key={key}, 记录命中={has}, 记录玩家数={PlayerMoodles.Count}");
        }

        if (!has || ids == null || ids.Count == 0) return;
        foreach (var id in ids.ToList())
        {
            var def = GetMoodles().FirstOrDefault(m => m.id == id);
            if (def != null) ImmediateAdd(__instance, def);
        }

        // 统一重排：自定义状态排在游戏状态之后，间隔 70px（参照原版排布，防重叠）
        RepositionCustom(__instance);
    }

    // 每帧强制显示：Moodle.Update 会按 showSideMoodles 禁用 img/img2/flash，
    // 带 tag 的状态对象在这里重新启用（不破坏游戏自身状态的显隐逻辑）
    private static void MoodleUpdate_Postfix(Moodle __instance)
    {
        try
        {
            if (__instance == null) return;
            var tag = __instance.GetComponent<CustomMoodleTag>();
            if (tag == null) return;
            foreach (var img in __instance.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                img.enabled = true;
        }
        catch { }
    }

    // 重排自定义状态：x = (游戏状态数 + 自定义序号) * 70，y 不动（保留 critical 浮动）
    private static void RepositionCustom(MoodleManager mm)
    {
        try
        {
            var moods = mm.moodles;
            if (moods == null) return;
            int nGame = 0;
            var customs = new List<Transform>();
            foreach (Transform child in moods)
            {
                if (child == null) continue;
                if (child.GetComponent<CustomMoodleTag>() != null) customs.Add(child);
                else nGame++;
            }
            for (int i = 0; i < customs.Count; i++)
            {
                var rt = customs[i].GetComponent<RectTransform>();
                if (rt == null) continue;
                var pos = rt.anchoredPosition;
                pos.x = (nGame + i) * 70f;
                rt.anchoredPosition = pos;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 状态重排失败: {e.Message}");
        }
    }

    // 记录：玩家 → 状态
    private static void AddRecord(Body body, MoodleDef def)
    {
        string key = BodyKey(body);
        if (key.Length == 0) return;
        if (!PlayerMoodles.TryGetValue(key, out var set))
            PlayerMoodles[key] = set = new HashSet<string>();
        set.Add(def.id);
    }

    internal static string BodyKey(Body body)
    {
        if (body == null) return "";
        try
        {
            var local = NetPlayer.LOCAL_PLAYER;
            if (local != null && !string.IsNullOrEmpty(local.playername)) return "p:" + local.playername;
        }
        catch { }
        return "b:" + body.name;
    }

    // 玩家显示名（广播 target 用）
    internal static string GetPlayerName(Body body)
    {
        try
        {
            var local = NetPlayer.LOCAL_PLAYER;
            if (local != null && !string.IsNullOrEmpty(local.playername)) return local.playername;
            var players = ServerMain.AllPlayersExceptHost;
            if (players != null)
                foreach (var p in players)
                    if (p?.body == body && !string.IsNullOrEmpty(p.playername)) return p.playername;
        }
        catch { }
        return null;
    }

    // 找到属于指定 Body 的 MoodleManager
    internal static MoodleManager GetMoodleManagerFor(Body body)
    {
        try
        {
            var bodyField = AccessTools.Field(typeof(MoodleManager), "body");
            foreach (var mm in UnityEngine.Object.FindObjectsOfType<MoodleManager>())
            {
                if (bodyField?.GetValue(mm) as Body == body) return mm;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 查找 MoodleManager 失败: {e.Message}");
        }
        return null;
    }

    // 直接调用 AddMoodle（创建一次；带 tag 的对象留在 moodles 容器内，
    // ClearMoodles 拦截后跳过它 → 位置正确 + 常驻不闪烁）
    internal static void ImmediateAdd(MoodleManager mm, MoodleDef def)
    {
        if (mm == null || def == null) return;
        try
        {
            // 已存在（带 tag 且 id 匹配）→ 排到末尾并跳过，避免每 0.5s 重复创建
            var moods = mm.moodles;
            if (moods != null)
            {
                foreach (Transform child in moods)
                {
                    var tag = child != null ? child.GetComponent<CustomMoodleTag>() : null;
                    if (tag != null && tag.moodleId == def.id)
                    {
                        child.SetAsLastSibling();
                        return;
                    }
                }
            }

            int idx = def.moodleType;
            if (mm.backgroundIcons != null && mm.backgroundIcons.Length > 0)
                idx = Mathf.Clamp(idx, 0, mm.backgroundIcons.Length - 1);
            else
                idx = 0;
            string iconKey = GetIconKey(mm, def.iconIndex, def.iconKey);
            mm.AddMoodle(idx, iconKey, def.title, def.desc, def.important, false);

            // 挂标记（ClearMoodles 跳过）+ 绕过"鼠标不在屏幕底部就自动隐藏"（enable Image）
            if (moods != null && moods.childCount > 0)
            {
                var last = moods.GetChild(moods.childCount - 1);
                var tag = last.gameObject.AddComponent<CustomMoodleTag>();
                tag.moodleId = def.id;
                foreach (var img in last.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    img.enabled = true;
                last.SetAsLastSibling();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 应用状态失败: {e.Message}");
        }
    }

    // 拦截 ClearMoodles：跳过我们的状态对象（带 tag），其余照常销毁 + 计数归零
    private static bool ClearMoodles_Prefix(MoodleManager __instance)
    {
        try
        {
            var moods = __instance.moodles;
            if (moods != null)
            {
                var toDestroy = new List<GameObject>();
                foreach (Transform child in moods)
                    if (child != null && child.GetComponent<CustomMoodleTag>() == null)
                        toDestroy.Add(child.gameObject);
                foreach (var go in toDestroy)
                    UnityEngine.Object.Destroy(go);
            }
            AccessTools.Field(typeof(MoodleManager), "moodleCount")?.SetValue(__instance, 0);
            AccessTools.Field(typeof(MoodleManager), "mainCount")?.SetValue(__instance, 0);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 状态清理拦截失败: {e.Message}");
            return true;   // 失败则走游戏原逻辑
        }
        return false;      // 跳过原方法（已自行清理）
    }

    // 从 MoodleManager.icons 字典取 key：iconKey 优先（存在才用），否则取第 iconIndex 个
    internal static string GetIconKey(MoodleManager mm, int iconIndex, string iconKey)
    {
        try
        {
            if (mm.icons != null && mm.icons.Count > 0)
            {
                if (!string.IsNullOrEmpty(iconKey) && mm.icons.ContainsKey(iconKey))
                    return iconKey;
                int i = Mathf.Clamp(iconIndex, 0, mm.icons.Count - 1);
                int n = 0;
                foreach (var k in mm.icons.Keys)
                    if (n++ == i) return k;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 读取 icons 失败: {e.Message}");
        }
        return "";
    }

    // 列出全部可选图标 key（诊断/配置用）
    public static void ListIcons()
    {
        try
        {
            var body = PlayerCamera.main?.body;
            var mm = body != null ? GetMoodleManagerFor(body) : null;
            if (mm == null) mm = UnityEngine.Object.FindObjectOfType<MoodleManager>();
            if (mm == null)
            {
                Plugin.Log.LogInfo("[CU_ServerPilot] 列出图标: 找不到 MoodleManager（需在世界中）");
                ConsoleManager.SendFeedback("找不到 MoodleManager（需在世界中）");
                return;
            }
            var keys = mm.icons != null ? string.Join(", ", mm.icons.Keys) : "(空)";
            int bg = mm.backgroundIcons != null ? mm.backgroundIcons.Length : 0;
            Plugin.Log.LogInfo($"[CU_ServerPilot] 状态图标表: 外框 {bg} 个, 内图标 keys = {keys}");
            ConsoleManager.SendFeedback($"已输出图标表到日志（共 {mm.icons?.Count ?? 0} 个内图标）");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 列出图标失败: {e.Message}");
            ConsoleManager.SendFeedback($"列出图标失败: {e.Message}");
        }
    }

    // 给目标应用单个状态
    public static int ApplyToTarget(string target, MoodleDef def)
    {
        var bodies = TargetPicker.ResolveBodies(target);
        if (bodies.Count == 0)
        {
            ConsoleManager.SendFeedback($"找不到目标: {target}");
            return 0;
        }
        foreach (var b in bodies) ApplyToBody(b, def);
        ConsoleManager.SendFeedback($"已给 {target} 应用状态 [{def.title}]（{bodies.Count} 人，已广播同步）");
        return bodies.Count;
    }

    // 给目标应用所有状态
    public static int ApplyAllToTarget(string target)
    {
        var bodies = TargetPicker.ResolveBodies(target);
        if (bodies.Count == 0)
        {
            ConsoleManager.SendFeedback($"找不到目标: {target}");
            return 0;
        }
        var allIds = GetMoodles().Select(m => m.id).ToList();
        foreach (var b in bodies)
            foreach (var m in GetMoodles()) AddRecord(b, m);
        BroadcastMoodles(target == TargetPicker.AllTarget ? "@a 全体" : target, allIds);
        foreach (var b in bodies)
            foreach (var m in GetMoodles())
            {
                var mm = GetMoodleManagerFor(b);
                if (mm != null) ImmediateAdd(mm, m);
            }
        ConsoleManager.SendFeedback($"已给 {target} 应用全部状态（{bodies.Count * allIds.Count} 条，已广播同步）");
        return bodies.Count;
    }

    private static List<MoodleDef> LoadJson()
    {
        try
        {
            string path = Path.Combine(Paths.PluginPath, "CU_ServerPilot", "moodles.json");
            if (!File.Exists(path)) return new List<MoodleDef>();
            var list = JsonConvert.DeserializeObject<List<MoodleDef>>(File.ReadAllText(path));
            Plugin.Log.LogInfo($"[CU_ServerPilot] 状态配置加载: {path}（{list?.Count ?? 0} 条）");
            return list ?? new List<MoodleDef>();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 状态配置解析失败: {e.Message}");
            return new List<MoodleDef>();
        }
    }

    private static List<MoodleDef> Defaults()
    {
        return new List<MoodleDef>
        {
            new MoodleDef { id = "serverbuff", title = "服务器增益", desc = "由服务器管理员加持，本局愉快游戏", moodleType = 0, iconIndex = 0, important = true, applyOnJoin = true },
            new MoodleDef { id = "vip", title = "VIP", desc = "尊贵的 VIP 玩家", moodleType = 1, iconIndex = 1, important = false, applyOnJoin = false },
        };
    }
}
