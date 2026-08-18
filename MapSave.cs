using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CU_ServerPilot;

// ============================================================================
//  MapSave - 地图存档/加载 v3（完整复刻 SuperGodFistTool.SaveManager 的全部项目）
//  ---------------------------------------------------------------------------
//  保存内容:
//   1. 方块 worldBlocks（UInt16[width,height] 反射）
//   2. 流体 FluidManager.main.fluid（byte[]，Base64）
//   3. 物品全状态（FindObjectsOfType<Item>）：
//      id(对象名去(Clone))/x/y/rot/condition/favourited
//      + AmmoScript.rounds / GunScript.roundsInMag|magCapacity|hasMag|safe|racked
//      + BatteryItem.maxCharge / WaterContainerItem.stack（液体）
//   4. 建筑 BuildingEntity（id字段/x/y/rot/health）
//   5. 世界元数据 biomeDepth/totalTraveled/lootRarityMultiplier/trapRarityMultiplier/realTimeElapsed
//
//  加载流程（同 SuperGodFistTool.LoadMap + 比它更完整——物品也重建）:
//   反序列化 → 写回元数据 → 写回 worldBlocks → 写回流体 → 刷新区块
//   → 清空现有 Item/Building → 重建物品（含状态） → 重建建筑
// ============================================================================

public static class MapSave
{
    public class SaveData
    {
        public int version = 3;
        public string saveName;
        public string saveDate;
        public int width;
        public int height;
        public int biomeDepth;
        public int totalTraveled;
        public float lootRarityMultiplier;
        public float trapRarityMultiplier;
        public float realTimeElapsed;
        public string fluidsBase64;              // FluidManager.main.fluid
        public ushort[] blocks;                  // 扁平化 worldBlocks
        public List<BuildingSave> buildings;
        public List<ItemSave> items;
    }

    public class BuildingSave
    {
        public string id;
        public float x, y, rot, health;
    }

    public class ItemSave
    {
        public string id;
        public float x, y, rot;
        public float? condition;
        public bool? favourited;
        public float? ammoRounds;
        public float? gunRoundsInMag, gunMagCapacity, batteryMaxCharge;
        public bool? gunHasMag, gunSafe, gunRacked;
        public List<LiquidSave> liquidStacks;
    }

    public class LiquidSave
    {
        public string liquidId;
        public float amount;
    }

    public static string SaveFolder => Path.Combine(Paths.PluginPath, "CU_ServerPilot", "saves");
    public static string ExportFolder => Path.Combine(Paths.PluginPath, "CU_ServerPilot", "exports");

    public static string[] ListSaves()
    {
        if (!Directory.Exists(SaveFolder)) return Array.Empty<string>();
        return Directory.GetFiles(SaveFolder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // ==========================================================================
    //  保存
    // ==========================================================================
    public static bool SaveMap(string name)
    {
        var world = WorldGeneration.world;
        if (world == null) return Fail("世界未生成");
        name = Sanitize(name);
        if (name.Length == 0) return Fail("存档名无效");

        var blocks = GetWorldBlocks(world, out int w, out int h);
        if (blocks == null) return Fail("反射 worldBlocks 失败");

        var flat = new ushort[w * h];
        Buffer.BlockCopy(blocks, 0, flat, 0, w * h * sizeof(ushort));

        var data = new SaveData
        {
            saveName = name,
            saveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            width = w,
            height = h,
            biomeDepth = world.biomeDepth,
            totalTraveled = world.totalTraveled,
            lootRarityMultiplier = world.lootRarityMultiplier,
            trapRarityMultiplier = world.trapRarityMultiplier,
            realTimeElapsed = world.realTimeElapsed,
            fluidsBase64 = CaptureFluids(),
            blocks = flat,
            buildings = CaptureBuildings(),
            items = CaptureItems()
        };

        Directory.CreateDirectory(SaveFolder);
        string path = Path.Combine(SaveFolder, name + ".json");
        File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.None));
        Plugin.Log.LogInfo($"[CU_ServerPilot] 地图已保存: {name}（{w}x{h}，建筑 {data.buildings?.Count ?? 0}，物品 {data.items?.Count ?? 0}）");
        ConsoleManager.SendFeedback($"地图已保存: {name}（{w}x{h}，建筑 {data.buildings?.Count ?? 0}，物品 {data.items?.Count ?? 0}）");
        return true;
    }

    private static string CaptureFluids()
    {
        try
        {
            if (FluidManager.main == null || FluidManager.main.fluid == null) return null;
            var fluid = FluidManager.main.fluid;   // byte[,]
            var flat = new byte[fluid.Length];
            Buffer.BlockCopy(fluid, 0, flat, 0, fluid.Length);
            return Convert.ToBase64String(flat);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 保存流体失败: {e.Message}");
            return null;
        }
    }

    // 枚举建筑（BuildingEntity.id 字段非空才存）
    private static List<BuildingSave> CaptureBuildings()
    {
        var list = new List<BuildingSave>();
        try
        {
            foreach (var be in UnityEngine.Object.FindObjectsOfType<BuildingEntity>())
            {
                if (be == null || be.transform == null || string.IsNullOrEmpty(be.id)) continue;
                var p = be.transform.position;
                if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y)) continue;
                list.Add(new BuildingSave
                {
                    id = be.id,
                    x = p.x,
                    y = p.y,
                    rot = be.transform.eulerAngles.z,
                    health = be.health
                });
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 保存建筑失败: {e.Message}");
        }
        return list;
    }

    // 枚举全部物品（含枪械/电池/液体状态，仿 SuperGodFistTool）
    private static List<ItemSave> CaptureItems()
    {
        var list = new List<ItemSave>();
        try
        {
            var bodies = UnityEngine.Object.FindObjectsOfType<Body>();
            foreach (var it in Item.allItems.ToArray())
            {
                // 只存地面物品：容器内（背包/箱子）与手持的不存 → 读档后原地保留
                if (it == null || it.transform == null || it.container != null) continue;
                bool held = false;
                foreach (var b in bodies)
                {
                    if (b != null && b.HoldingItem(it)) { held = true; break; }
                }
                if (held) continue;
                var p = it.transform.position;
                if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y)) continue;

                var s = new ItemSave
                {
                    id = it.name.Replace("(Clone)", "").Trim(),
                    x = p.x,
                    y = p.y,
                    rot = it.transform.eulerAngles.z
                };
                // condition（默认 1 不存）
                if (Math.Abs(it.condition - 1f) > 0.001f) s.condition = it.condition;
                if (it.favourited) s.favourited = true;

                // 液体容器
                var wc = it.GetComponent<WaterContainerItem>();
                if (wc != null && wc.stack != null && wc.stack.Count > 0)
                {
                    s.liquidStacks = wc.stack.Select(ls => new LiquidSave { liquidId = ls.liquidId, amount = ls.amount }).ToList();
                }
                // 弹药
                var ammo = it.GetComponent<AmmoScript>();
                if (ammo != null) s.ammoRounds = ammo.rounds;
                // 枪械
                var gun = it.GetComponent<GunScript>();
                if (gun != null)
                {
                    s.gunRoundsInMag = gun.roundsInMag;
                    s.gunMagCapacity = gun.magCapacity;
                    s.gunHasMag = gun.hasMag;
                    s.gunSafe = gun.safe;
                    s.gunRacked = gun.racked;
                }
                // 电池
                var bat = it.GetComponent<BatteryItem>();
                if (bat != null) s.batteryMaxCharge = bat.maxCharge;

                list.Add(s);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 保存物品失败: {e.Message}");
        }
        return list;
    }

    // ==========================================================================
    //  加载
    // ==========================================================================
    public static bool LoadMap(string name)
    {
        var world = WorldGeneration.world;
        if (world == null) return Fail("世界未生成");
        name = Sanitize(name);
        if (name.Length == 0) return Fail("存档名无效");

        string path = Path.Combine(SaveFolder, name + ".json");
        if (!File.Exists(path)) return Fail("存档不存在: " + name);

        SaveData data;
        try
        {
            data = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            return Fail($"存档解析失败: {e.Message}");
        }
        if (data == null || data.blocks == null) return Fail("存档数据为空");

        var blocks = GetWorldBlocks(world, out int w, out int h);
        if (blocks == null) return Fail("反射 worldBlocks 失败");
        if (data.width != w || data.height != h)
            return Fail($"存档尺寸 {data.width}x{data.height} 与当前世界 {w}x{h} 不符");

        ConsoleManager.SendFeedback("正在加载地图，请稍候...");

        // 1) 世界元数据
        world.biomeDepth = data.biomeDepth;
        world.totalTraveled = data.totalTraveled;
        world.lootRarityMultiplier = data.lootRarityMultiplier;
        world.trapRarityMultiplier = data.trapRarityMultiplier;

        // 2) 方块
        Buffer.BlockCopy(data.blocks, 0, blocks, 0, w * h * sizeof(ushort));
        RefreshChunks(world);

        // 3) 流体
        RestoreFluids(data.fluidsBase64, data.width, data.height);

        // 4) 清空现有实体（照 SuperGodFistTool：Item 直接销毁，建筑 health=-100 标记后销毁）
        ClearWorldEntities();

        // 5) 重建物品（含状态）——比 SuperGodFistTool 更完整（它只存不读）
        RebuildItems(data.items);

        // 6) 重建建筑
        RebuildBuildings(data.buildings);

        Plugin.Log.LogInfo($"[CU_ServerPilot] 地图已加载: {name}（{w}x{h}，建筑 {data.buildings?.Count ?? 0}，物品 {data.items?.Count ?? 0}）");
        ConsoleManager.SendFeedback($"地图已加载: {name}（{w}x{h}，建筑 {data.buildings?.Count ?? 0}，物品 {data.items?.Count ?? 0}）");
        return true;
    }

    private static void RestoreFluids(string base64, int w, int h)
    {
        try
        {
            if (string.IsNullOrEmpty(base64) || FluidManager.main == null) return;
            var bytes = Convert.FromBase64String(base64);
            var fluid = new byte[w, h];
            Buffer.BlockCopy(bytes, 0, fluid, 0, Math.Min(bytes.Length, w * h));
            FluidManager.main.fluid = fluid;
            Plugin.Log.LogInfo("[CU_ServerPilot] 流体数据已恢复");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 恢复流体失败: {e.Message}");
        }
    }

    // 加载前清空现有建筑 + 物品（仿 SuperGodFistTool）
    private static void ClearWorldEntities()
    {
        try
        {
            // 只清地面物品（容器内/手持的保留 → 读档后背包物品不丢失）
            var bodies = UnityEngine.Object.FindObjectsOfType<Body>();
            foreach (var it in Item.allItems.ToArray())
            {
                if (it == null || it.container != null) continue;
                bool held = false;
                foreach (var b in bodies)
                {
                    if (b != null && b.HoldingItem(it)) { held = true; break; }
                }
                if (held) continue;
                UnityEngine.Object.Destroy(it.gameObject);
            }

            foreach (var be in UnityEngine.Object.FindObjectsOfType<BuildingEntity>())
            {
                if (be == null) continue;
                be.health = -100f;   // 标记，避免销毁时触发正常逻辑
                UnityEngine.Object.Destroy(be.gameObject);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 清空实体失败: {e.Message}");
        }
    }

    // 重建物品（Utils.Create + 恢复枪械/电池/液体/耐久）
    private static void RebuildItems(List<ItemSave> items)
    {
        if (items == null) return;
        foreach (var s in items)
        {
            try
            {
                var go = Utils.Create(s.id, new Vector2(s.x, s.y), s.rot);
                if (go == null)
                {
                    Plugin.Log.LogWarning($"[CU_ServerPilot] 物品不存在: {s.id}");
                    continue;
                }
                var item = go.GetComponent<Item>();
                if (item == null) continue;

                if (s.condition.HasValue) item.condition = s.condition.Value;
                if (s.favourited.HasValue) item.favourited = s.favourited.Value;

                // 液体
                if (s.liquidStacks != null && s.liquidStacks.Count > 0)
                {
                    var wc = go.GetComponent<WaterContainerItem>();
                    if (wc != null)
                    {
                        wc.DrainAll();
                        foreach (var ls in s.liquidStacks)
                            if (!string.IsNullOrEmpty(ls.liquidId)) wc.AddLiquid(ls.liquidId, ls.amount);
                    }
                }
                // 弹药
                if (s.ammoRounds.HasValue)
                {
                    var ammo = go.GetComponent<AmmoScript>();
                    if (ammo != null) ammo.rounds = (int)s.ammoRounds.Value;
                }
                // 枪械
                if (s.gunRoundsInMag.HasValue)
                {
                    var gun = go.GetComponent<GunScript>();
                    if (gun != null)
                    {
                        if (s.gunRoundsInMag.HasValue) gun.roundsInMag = (int)s.gunRoundsInMag.Value;
                        if (s.gunMagCapacity.HasValue) gun.magCapacity = (int)s.gunMagCapacity.Value;
                        if (s.gunHasMag.HasValue) gun.hasMag = s.gunHasMag.Value;
                        if (s.gunSafe.HasValue) gun.safe = s.gunSafe.Value;
                        if (s.gunRacked.HasValue) gun.racked = s.gunRacked.Value;
                    }
                }
                // 电池
                if (s.batteryMaxCharge.HasValue)
                {
                    var bat = go.GetComponent<BatteryItem>();
                    if (bat != null) bat.maxCharge = s.batteryMaxCharge.Value;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[CU_ServerPilot] 重建物品 {s.id} 失败: {e.Message}");
            }
        }
    }

    // 重建建筑（仿 SuperGodFistTool.FinalizeLoad: Resources.Load(id) → Instantiate → health）
    private static void RebuildBuildings(List<BuildingSave> buildings)
    {
        if (buildings == null) return;
        foreach (var b in buildings)
        {
            try
            {
                var prefab = Resources.Load<GameObject>(b.id);
                if (prefab == null)
                {
                    Plugin.Log.LogWarning($"[CU_ServerPilot] 建筑预制体不存在: {b.id}");
                    continue;
                }
                var go = UnityEngine.Object.Instantiate(prefab, new Vector3(b.x, b.y, 0f), Quaternion.Euler(0f, 0f, b.rot));
                var be = go.GetComponent<BuildingEntity>();
                if (be != null) be.health = b.health;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[CU_ServerPilot] 重建建筑 {b.id} 失败: {e.Message}");
            }
        }
    }

    // ==========================================================================
    //  导出/导入（saves ↔ exports）
    // ==========================================================================
    public static bool ExportMap(string name)
    {
        name = Sanitize(name);
        string src = Path.Combine(SaveFolder, name + ".json");
        if (!File.Exists(src)) return Fail("存档不存在: " + name);
        Directory.CreateDirectory(ExportFolder);
        string dst = Path.Combine(ExportFolder, name + ".json");
        File.Copy(src, dst, true);
        ConsoleManager.SendFeedback($"已导出到 {dst}");
        return true;
    }

    public static bool ImportMap(string fileName)
    {
        fileName = Sanitize(fileName);
        string src = Path.Combine(ExportFolder, fileName + ".json");
        if (!File.Exists(src)) src = Path.Combine(Paths.PluginPath, fileName + ".json");
        if (!File.Exists(src)) return Fail("导出目录中没有: " + fileName);
        Directory.CreateDirectory(SaveFolder);
        string dst = Path.Combine(SaveFolder, fileName + ".json");
        File.Copy(src, dst, true);
        ConsoleManager.SendFeedback($"已导入 {fileName}（可加载）");
        return true;
    }

    // ==========================================================================
    //  工具
    // ==========================================================================
    private static ushort[,] GetWorldBlocks(WorldGeneration world, out int w, out int h)
    {
        w = h = 0;
        try
        {
            var f = AccessTools.Field(typeof(WorldGeneration), "worldBlocks");
            var blocks = f?.GetValue(world) as ushort[,];
            if (blocks == null) return null;
            w = blocks.GetLength(0);
            h = blocks.GetLength(1);
            return blocks;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 读取 worldBlocks 失败: {e.Message}");
            return null;
        }
    }

    private static void RefreshChunks(WorldGeneration world)
    {
        try
        {
            var chunksField = AccessTools.Field(typeof(WorldGeneration), "chunks");
            var chunks = chunksField?.GetValue(world) as Tilemap[,];
            var updateChunk = AccessTools.Method(typeof(WorldGeneration), "UpdateChunk");
            if (chunks == null || updateChunk == null)
            {
                Plugin.Log.LogWarning("[CU_ServerPilot] 区块刷新不可用（chunks/UpdateChunk 反射失败）");
                return;
            }
            int cw = chunks.GetLength(0), ch = chunks.GetLength(1);
            for (int x = 0; x < cw; x++)
            for (int y = 0; y < ch; y++)
                updateChunk.Invoke(world, new object[] { new Vector2Int(x, y) });
            Plugin.Log.LogInfo($"[CU_ServerPilot] 区块刷新完成 {cw}x{ch}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 区块刷新失败: {e.Message}");
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static bool Fail(string msg)
    {
        ConsoleManager.SendFeedback(msg);
        return false;
    }
}
