# 自定义状态（Moodle）配置教程

> 文件：`BepInEx/plugins/CU_ServerPilot/moodles.json`
> 功能：给玩家状态栏添加自定义图标状态（标题/描述/图标可配），支持多状态、指定玩家、进服自动。

---

## 一、配置文件格式

`moodles.json` 是一个 JSON 数组，每个元素 = 一个状态：

```json
[
  {
    "id": "serverbuff",          // 状态唯一标识（英文/数字，不能重复）
    "title": "服务器增益",        // 状态名称（悬停显示的大标题）
    "desc": "由服务器管理员加持，本局愉快游戏",  // 状态描述（悬停显示的小字）
    "moodleType": 1,             // 外框图标索引（0~N-1，对应游戏 backgroundIcons 数组）
    "iconKey": "impendingdoom",  // 内图标 key（游戏 icons 字典的 key，优先于 iconIndex）
    "iconIndex": 0,              // 内图标索引（iconKey 为空时用：取 icons 字典第 N 个）
    "important": true,           // 重要状态（true 会带红色文字 + 上下浮动动画）
    "applyOnJoin": false         // 进服自动应用（true = 联机玩家进服自动获得该状态）
  }
]
```

> 所有字段均可省略：省略 `iconKey`/`iconIndex` 默认用第一个图标；省略 `important` 默认 true；省略 `applyOnJoin` 默认 false。

## 二、如何查看可用的图标

游戏没有自定义贴图，图标只能从**游戏内置图标池**里选。查看全部可用 key：

1. 进游戏世界
2. 打开面板 → 「状态」标签页 → 点「**列出全部可选图标**」
3. 查看 BepInEx 日志（`BepInEx/LogOutput.log`）：
   ```
   [CU_ServerPilot] 状态图标表: 外框 N 个, 内图标 keys = amputation, arrythmia, bleeding, ...
   ```
4. 把想要的 key 填进 `iconKey`（如 `"iconKey": "impendingdoom"`）

**已知常用 key**（游戏内置）：
| key | 含义 |
|---|---|
| `impendingdoom` | 不详预感 |
| `focused` | 专注 |
| `horrified` | 惊骇 |
| `amputation` | 截肢 |
| `arrythmia` | 心律失常 |

> 提示：日志列出的完整 key 列表是权威来源（每个版本可能有差异）。

## 三、使用方式

### 3.1 手动应用
面板 → 「状态」标签页：
- 目标下拉选「@本机 / @a 全体 / 具体玩家」
- 每个状态「应用给目标」按钮 → 只应用那一个
- 「应用全部状态给目标」→ 全部应用

### 3.2 进服自动
把状态设为 `"applyOnJoin": true` → 联机玩家每次进服自动获得（服务器端自动广播同步）。

### 3.3 联机同步
状态通过 KrokMP 聊天通道广播给客户端，**客户端也需要安装 CU_ServerPilot** 才会显示（两端装同一个 dll 即可）。

## 四、示例配置

```json
[
  { "id": "serverbuff", "title": "服务器增益", "desc": "由服务器管理员加持，本局愉快游戏", "moodleType": 1, "iconKey": "impendingdoom", "important": true, "applyOnJoin": true },
  { "id": "vip", "title": "VIP", "desc": "尊贵的 VIP 玩家", "moodleType": 0, "iconKey": "focused", "important": false, "applyOnJoin": false },
  { "id": "hardcore", "title": "硬核模式", "desc": "死亡即删档", "moodleType": 2, "iconKey": "horrified", "important": true, "applyOnJoin": false }
]
```

## 五、修改后生效

改完 `moodles.json` **保存** → 面板「状态」页会实时读新配置（点击应用即可看到新状态）。不需要重启游戏（除非改坏格式导致解析失败，会回退到内置默认）。

## 六、限制说明

- **图标只能用游戏内置池**（无自定义贴图资源）；想用完全自定义图标需要单独做资源型 MOD（两端安装）
- 状态是纯 UI 图标，**本身不带数值效果**（如回血/加属性）——想带效果需要额外写逻辑（可提需求）
- 鼠标不放在状态栏时游戏会自动隐藏状态图标——本插件已强制常驻显示
