# CU_ServerPilot

> Casualties: Unknown Demo 联机服务器管理 MOD（BepInEx 5 插件，基于 KrokMP）

一站式服务器管理面板：初始物资、图形化 Spawn、地图编辑、地图存档、自定义状态、战斗辅助、玩家操作、背景装饰。F5 打开/关闭面板，全部功能带中英双语翻译。

## 功能总览

| 标签页 | 功能 |
|---|---|
| **初始物资** | 多选编辑开局道具（数量/参数），进服自动发放（每世界一次，睡眠不重复）、初始技能等级（STR/RES/INT）、发放聊天广播 |
| **Spawn** | 物品/液体图形化生成，目标选择（光标位置 / @a 全体 / 指定玩家），参数控制（0~1 填充比例：耐久/弹匣弹量/电量），超级物品（耐久恒 100% / 无重力） |
| **地图编辑** | 方块列表（中文名）左键选中 / 右键按住拖动连续放置 / 橡皮擦 / PgDn 取消，刷子半径 1~8 |
| **战斗辅助** | R 键给主手枪械拉栓（带双拉栓延时）、无限子弹（弹匣+枪膛）、一键清除掉落物 |
| **状态** | 自定义 Moodle 状态（`moodles.json` 配置，多状态、指定玩家、进服自动、服务器↔客户端双端同步） |
| **存档** | 地图保存/加载：方块 + 流体 + 物品全状态（枪械/弹药/电池/液体）+ 建筑 + 元数据；存档导出/导入（saves/ ↔ exports/） |
| **其他** | 玩家操作（一键拉取/昏迷/治疗）、生物生成倍率（一次性调整）、背景装饰（装饰物/物品无碰撞无重力生成 + 橡皮擦）、中文搜索 |

## 依赖

- [BepInEx 5.4.23.4](https://github.com/BepInEx/BepInEx)
- [KrokMP](https://github.com/Krokusis/Krokosha-Casualties-MP)（联机框架，`KrokoshaCasualtiesMP.dll`）
- 参考：[SuperGodFistTool](https://github.com/Krokusis/SuperGodFistTool)（存档/治疗等机制参考，未包含其代码）
- Unity 2022.3（游戏运行环境）

> 自定义状态的**双端同步**需要主机和客户端都安装本插件（同一份 dll）。

## 构建

```bash
dotnet build -c Release
```

> 需要 .NET SDK（netstandard2.1 目标）。csproj 中 `HintPath` 指向本机游戏目录，克隆后请改为你的游戏路径：
>
> - `D:\Steam\steamapps\common\Casualties Unknown Demo\CasualtiesUnknown_Data\Managed\`（Assembly-CSharp.dll / UnityEngine.UI.dll / Newtonsoft.Json.dll）
> - `D:\Steam\steamapps\common\Casualties Unknown Demo\BepInEx\core\`（BepInEx.dll / 0Harmony.dll）
> - `D:\Steam\steamapps\common\Casualties Unknown Demo\BepInEx\plugins\KrokMP\`（KrokoshaCasualtiesMP.dll）

## 安装

1. 安装 BepInEx 5 与 KrokMP（两端）
2. 复制 `bin/Release/netstandard2.1/CU_ServerPilot.dll` 到 `BepInEx/plugins/`
3. 复制插件目录下的 json 配置文件到 `BepInEx/plugins/CU_ServerPilot/`：
   - `items.zh-CN.json` / `liquids.zh-CN.json` / `blocks.zh-CN.json` / `decor.zh-CN.json` — 物品/液体/方块/装饰中文翻译（可自定义）
   - `moodles.json` — 自定义状态配置（见下方）
4. 启动游戏，游戏内按 **F5** 打开管理面板

## 配置

### 翻译文件（可自定义）

JSON 格式 `{"英文id": "中文名"}`，值为空则显示原名。修改后重启游戏生效。

### 自定义状态 `moodles.json`

```json
[
  {
    "id": "serverbuff",
    "title": "服务器增益",
    "desc": "由服务器管理员加持，本局愉快游戏",
    "moodleType": 1,
    "iconKey": "impendingdoom",
    "important": true,
    "applyOnJoin": true
  }
]
```

- `iconKey`：游戏内图标 key（面板「列出全部可选图标」可查看全部 80 个）
- `moodleType`：外框图标索引（0~8）
- `important`：重要状态（红色警告 + 浮动动画）
- `applyOnJoin`：进服自动应用

详细教程见 [CUSTOM_MOODLES_GUIDE.md](CUSTOM_MOODLES_GUIDE.md)。

### 存档

- 保存位置：`BepInEx/plugins/CU_ServerPilot/saves/<名称>.json`
- 导出目录：`BepInEx/plugins/CU_ServerPilot/exports/`（与 saves/ 之间互相复制，方便分享存档）
- 存档内容：方块、流体、物品全状态（耐久/收藏/枪械弹匣/弹药/电池电量/液体）、建筑、世界元数据

## 技术要点（给 MOD 开发者）

- 游戏是 **Unity Mono**，必须用 **BepInEx 5**（`BaseUnityPlugin` + `Awake()`），BepInEx 6 / IL2CPP 不兼容
- Harmony patch 目标为游戏主程序集 `Assembly-CSharp.dll`（`D:/Steam/.../CasualtiesUnknown_Data/Managed/`）
- 关键 API（均经反汇编确认）：
  - 生成物品 `Utils.Create(name, pos, rot)` + `Body.AutoPickUpItem(item)`
  - 放置方块 `WorldGeneration.SetBlock(Vector2Int, ushort)` + `UpdateChunk` 刷新
  - 存档 `WorldGeneration.worldBlocks`（UInt16[,] 反射读写）+ `FluidManager.main.fluid`（byte[,]）
  - 拉栓 `GunScript.TryRack()`、无限子弹 `roundsInMag / magCapacity / racked`
  - 状态 `MoodleManager.AddMoodle(backgroundIcons索引, icons字典key, 显示名, 描述, critical, side)`
  - 昏迷/治疗 `Body.consciousness` / 生理字段；传送 `NetPlayer.Server_TeleportCharacter`
  - 广播 `Chat.Server_ChatAnnouncement(ref string)`（KrokMP）
- 游戏目录**只读**原则：插件运行时仅写入 `BepInEx/plugins/CU_ServerPilot/` 下的存档/导出文件

## 致谢

- [Krokusis](https://github.com/Krokusis) — KrokMP 联机框架与 SuperGodFistTool 参考实现
- 所有测试玩家

## 许可证

MIT License
