# CU_ServerPilot 功能方案 v3（6 个功能：定义 + UI 参数 + 实现）

> 状态：方案阶段，未动代码
> 反编译依据：Assembly-CSharp.dll（MoodleManager/GunScript/Item/FreshItemDrop 等）

---

## 1. 自定义状态（Moodle 增益图标）

**功能**：给玩家状态栏添加自定义"增益/状态"图标（游戏自带系统：`MoodleManager.AddMoodle(int, string title, string desc, string icon, bool, bool)`，游戏内建如 focused/impendingdoom）。效果：玩家头顶/状态栏显示服务器自定义状态（如"服务器增益"）。

**UI 可自定义参数**：
| 参数 | 类型 | 默认 |
|---|---|---|
| 启用 | Toggle | 关 |
| 状态标题 | 文本 | "服务器增益" |
| 状态描述 | 文本 | "本局由服务器管理员加持" |
| 应用对象 | 下拉（复用 TargetPicker：@本机/@a/玩家） | @本机 |
| 刷新频率 | 秒 | 2s（每帧刷新耗性能） |

**实现**：定时调 `MoodleManager.AddMoodle(...)`（参数参考游戏 AddAllMoodles 调用模式）；需要实验确定 icon 字符串与 bool 位的含义（本地先跑一次看效果）。
**风险**：低——Moodle 只是状态图标，无副作用；图标字符串可能依赖游戏内置 sprite，实验确认。

---

## 2. 光照控制

**功能**：调整世界环境光照（`WorldGeneration.world.ambientLight`，Light2D 组件）→ 永远白天/夜晚/自定义强度/自定义色调。

**UI 可自定义参数**：
| 参数 | 类型 | 默认 |
|---|---|---|
| 启用 | Toggle | 关 |
| 光照强度 | 0.0~2.0 输入 | 1.0 |
| 光照颜色 | RGB 输入 | 白 |
| 预设 | 下拉：白天/黄昏/夜晚/自定义 | 白天 |

**实现**：定时器（1s）写 `ambientLight.intensity` + `ambientLight.color`（游戏昼夜循环会覆盖，所以持续写）。
**风险**：低——Light2D 组件标准属性；但需确认游戏是否每帧重置 ambientLight（若重置则改为 patch 它的 Update）。

---

## 3. 无限子弹（用户建议的稳健逻辑）

**功能**：枪械弹药无限。**逻辑（按用户建议，减少 BUG）**：
- **弹匣**：`GunScript.roundsInMag < magCapacity` → 随时补满
- **枪膛**：`roundInChamber` 置为"已上弹"状态（霰弹枪枪膛第二发）——实现时用「触发一次 `TryRack()`」或直接写 `roundInChamber` 字段（实现时实验确认该字段的取值，用 TryRack 更稳）
- 触发时机：**开火后下一帧检查补**（比每帧写省性能，且避免与游戏射击状态机冲突）

**UI 可自定义参数**：
| 参数 | 类型 | 默认 |
|---|---|---|
| 启用 | Toggle | 关 |
| 补弹范围 | 下拉：仅弹匣 / 弹匣+枪膛 | 弹匣+枪膛 |
| 补弹时机 | 下拉：开火后 / 持续保持满 | 开火后 |

**实现**：patch `GunScript.Fire` postfix → 标记"需要补弹"，Update 里补（或直接 patch Fire 后立即补）。
**风险**：中——roundInChamber 的取值需要实验确认（未知枚举/类）；用 TryRack() 兜底最稳（拉栓=补枪膛，用户已认可此思路）。

---

## 4. 优化：spawn 与地图编辑支持中文搜索

**功能**：物品列表/方块列表的搜索框**同时匹配中文名和英文 ID**（当前只匹配英文 ID）。

**UI 可自定义参数**：无（纯优化）。

**实现**：`Filter()` 改为：`Translations.ItemName(x).Contains(词) || x.Contains(词)`（中文名 OR ID）。地图编辑方块同理（`Translations.BlockName(b.Name)` OR `b.Name`）。
**风险**：无——纯 UI 逻辑，一处改动。

---

## 5. 超级物品（spawn 界面开关）

**功能**：spawn 出的物品：① **耐久永远 100%**（condition 恒定）；② **无重力**（实体不落地，飘浮）。

**UI 可自定义参数**：
| 参数 | 类型 | 默认 |
|---|---|---|
| 超级物品模式 | Toggle | 关 |
| 子项：耐久锁定 | Toggle | 开 |
| 子项：无重力 | Toggle | 开 |

**实现**：
- **耐久锁定**：生成时 condition=1 + 给物品附加一个标记组件（`SuperItemTag`），每帧把 `item.condition = 1`（只对带标记的物品生效，不影响其他）
- **无重力**：`Item.rb`（public 字段，Rigidbody2D）→ `rb.gravityScale = 0`（精确到单个物品；不动全局 `Item.itemFloating`——那是 FreshItemDrop 掉落动画读取的全局标志，改了影响所有物品）
**风险**：低——rb 是 public；附加组件方案干净。

---

## 6.（前瞻）自定义物品 + 伤害注入

**功能**：新增游戏没有的物品：狙击枪、有托突击步枪、背水一战超级针剂等；并深挖可注入参数（枪械子弹伤害）。

**可行性结论（已反编译确认）**：
- ✅ **枪械伤害可改**：`GunScript.structureDamage` / `animalDamage` 是 **public 字段**——可直接改实例值或做"伤害倍率"patch（伤害最终由弹药命中 `Limb.ImpactDamage(single, Vector2)` 结算，也可在这里乘倍率，全局可控）
- ✅ **物品注册可注入**：`Item.SetupItems()` 里 `GlobalItems.Add("id", new ItemInfo{...})`——patch SetupItems postfix 追加自定义 ItemInfo（克隆现有枪械改伤害/射速/射程）
- ⚠️ **同步问题**：物品的**显示名/贴图/预制体**在客户端资源里。若用"克隆现有枪械 ItemInfo + 复用现有资源名"，客户端无需新资源 → **可同步**；若要用全新外观 → 需要新资源，无法纯代码解决 → 按用户说的**单独做 MOD**（两端都装）
- ⚠️ 新物品的子弹/音效/动画：复用现有（如"步枪弹"、"枪声"）可保证稳定

**UI 可自定义参数（若实现为服务器端注入）**：
| 参数 | 类型 |
|---|---|
| 自定义物品开关 | Toggle |
| 每把武器的：名称/伤害倍率/弹匣容量/射速/弹药类型 | 输入 |

**分阶段建议**：
- 阶段 A（纯参数）：全局「枪械伤害倍率」patch（不动物品表，同步无忧）——最稳的注入点
- 阶段 B（克隆物品）：patch SetupItems 注入克隆 ItemInfo（复用现有资源名，可同步）——风险中等
- 阶段 C（全新物品 MOD）：独立 MOD 包，两端安装（用户已认可）

---

## 实现顺序建议

| 批次 | 功能 | 理由 |
|---|---|---|
| 第一批 | 4（中文搜索）、5（超级物品）、2（光照） | 零风险/低风险，立竿见影 |
| 第二批 | 3（无限子弹）、1（自定义状态） | 需一次实验确认枪膛/Moodle 参数 |
| 前瞻 | 6（自定义物品） | 先做阶段 A（伤害倍率），B/C 按需 |

> 确认方案后回复"开始"即可，我会按批次实现。
