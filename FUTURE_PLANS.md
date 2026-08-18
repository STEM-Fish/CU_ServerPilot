# CU_ServerPilot 新功能规划 v2 —— 运行时机制深挖（非世界参数）

> 依据：`Assembly-CSharp.dll` 深度反编译（Body/Limb/WorldGeneration/PlayerCamera/Moodle 等核心类）
> 已排除：自定义世界（RunSettings）里能调的静态参数（战利品/腐烂/温度基础值等）
> 保留：生物生成倍率（自定义世界没有）
> 日期：2026-08-17 | 纯侦察，未动代码

---

## 侦察确认的自定义世界参数边界

`RunSettings` 系统（RunSettingFloat/Bool/Dropdown + presets）覆盖的只是**开局静态参数**。
以下所有机制都是**游戏运行时的动态状态机/行为**，创建世界时不可调 —— 全部是模组的落脚点。

---

## 一、玩家生理机制（Body 动态状态机）

| # | 机制（反编译依据） | 创意玩法 | 成本 |
|---|---|---|---|
| 1 | **心脏骤停/心室颤动**：`Body.inCardiacArrest` / `TryStartFibrillation(bool)` / `HandleCirculation(Painkillers)` / `fibrillationRising` / `TryLastStand()` | 「心脏强化」：骤停保护（低于阈值自动 CPR）/ 心搏可视化（ECG 曲线 + 心跳音效）/ 心脏骤停竞技模式 | 中 |
| 2 | **脑死亡/最后抵抗**：`Body.brainDying` / `TryLastStand()` | 「最后抵抗」增强：脑死前 X 秒无敌挣扎（真实 Last Stand 玩法） | 低 |
| 3 | **凝血/出血倍率**：`bleedClottingSpeed` / `bleedingSpeedMultiplier`（**可写属性**） | 「凝血增强」：出血自动快速止住 / 反之「放血模式」 | 低 |
| 4 | **代谢率**：`BaseHungerRate` / `BaseThirstRate(ml)` / `thirstBloodPressure` | 「代谢控制」：饥饿口渴速度倍率（+喝水回血关联血压） | 低 |
| 5 | **体温调节**：`bonusTemperatureOffset` / `baseTemperatureLerpRate` / `currentTemperatureMovementMult` | 「恒温体质」：体温偏移锁定，或体温影响移速倍率可视化 | 低 |
| 6 | **锻炼机制**：`Body.DoWorkout(WorkoutType)` / `exercising` | 「锻炼强化」：锻炼收益倍率 / 自动锻炼 / 锻炼图标（Moodle） | 中 |
| 7 | **睡眠质量**：`SleepQualityToRegen(SleepQuality)` / `BumpUpSleepQuality` / `canTakeNap` / `SleepingBagUse` | 「睡眠强化」：睡眠恢复倍率（睡一觉满状态）/ 秒睡 | 低 |
| 8 | **爪子生长**：`clawGrowthRate`（变异机制） | 「变异加速」：爪子生长速度 / 变异可视化 | 低 |

## 二、伤口/医疗机制（Limb 肢体系统）

| # | 机制（反编译依据） | 创意玩法 | 成本 |
|---|---|---|---|
| 9 | **感染机制**：`Limb.GetInfectionSpeed()` / `SetDisinfect(single)` / `Limb.Update()` | 「无菌体质」：感染速度倍率 / 免疫感染（伤口永不感染） | 低 |
| 10 | **自愈倍率**：`Limb.MuscleHealRate` / `SkinHealRate` / `injuryHealTime` | 「快速愈合」：肌肉/皮肤愈合倍率（比自定义世界更细） | 低 |
| 11 | **弹片机制**：`Limb.hasShrapnel` | 「无弹片模式」：中弹不留弹片 / 弹片自动排出 | 低 |
| 12 | **骨折/脱臼**：`BreakBone()` / `Dislocate()` / `MendBone()` | 「金刚骨」：免疫骨折脱臼（扩展 SuperGodFistTool 的防 CPR 骨折） | 低 |
| 13 | **护甲减伤**：`Limb.GetArmorReduction()` / `DamageWearables(single)` | 「护甲强化」：护甲减伤倍率可视化 | 中 |

## 三、世界动态事件（WorldGeneration 运行时）

| # | 机制（反编译依据） | 创意玩法 | 成本 |
|---|---|---|---|
| 14 | **地震事件**：`WorldGeneration.earthquakeTime` | 「地震控制」：地震频率/开关/手动触发地震（现成计时器，改值即触发） | 低 |
| 15 | **环境光照**：`WorldGeneration.ambientLight`（Light2D 组件） | 「光照控制」：永远白天/夜晚，或服务器自定义光照强度 | 低 |
| 16 | **温度曲线**：`temperatureCurves`（AnimationCurve[]，逐层）+ `bonusTemperatureOffset` | 「自定义各层温度」：运行时覆盖每层温度曲线 | 中 |
| 17 | **层时间限制**：`maxTimePerLayer` / `layerTimeSpent` | 「层时间控制」：层倒计时显示 / 取消限制 / 无限层 | 低 |

## 四、交互/UI 机制

| # | 机制（反编译依据） | 创意玩法 | 成本 |
|---|---|---|---|
| 18 | **Moodle 自定义状态**：`MoodleManager.AddMoodle(int, string, string, string, bool, bool)` | 「服务器状态图标」：给玩家显示自定义 Moodle（如"服务器增益"图标），效果可见度高 | 低 |
| 19 | **威胁商人**：`PlayerCamera.TraderTryThreaten()` | 「商人互动增强」：威胁成功率/价格联动 | 中 |
| 20 | **拾取系统**：`TryPickupFromWorld` / `HandleDragging` | 「拾取范围增强」：扩大拾取半径 / 自动拾取 | 中 |
| 21 | **血迹系统**：`GroundBlood.CheckGround()` | 「血迹清理」：一键清血迹（性能）/ 血迹保留开关 | 低 |

## 五、敌人/生物（保留「生物生成倍率」）

| # | 机制（反编译依据） | 创意玩法 | 成本 |
|---|---|---|---|
| 22 | **生物生成倍率**（保留） | 生成数倍率（自定义世界没有） | 低 |
| 23 | **敌人行为**：`CrystalEnemy` / `SpiderHandler` / `TailScript` | 敌人 AI 改造：攻击性/速度/掉落/被动模式 | 中 |
| 24 | **敌人掉落物**：`SpiderHandler.AnimalDeath`（触发 `Skills.AddExp`） | 击杀经验倍率联动 / 掉落加成 | 低 |

---

## 推荐 Top 5（高创意 + 低成本 + 立竿见影）

1. **🫀 心脏系统可视化 + 骤停保护**（机制 1）——游戏最独特的生理模拟，做成"心电监护仪"面板 + 自动除颤保护，服务器特色拉满
2. **🩸 凝血增强/出血控制**（机制 3）——一行属性赋值，实战体验质变
3. **🦴 金刚骨（免疫骨折脱臼）+ 无菌体质（免疫感染）**（机制 9/12）——SuperGodFistTool 防截肢的兄弟功能
4. **🌡️ 恒温体质 + 光照控制**（机制 5/15）——环境舒适度自由定义
5. **🌀 地震事件控制**（机制 14）——服务器可手动"天灾"整活

> 决策入口：说"做 1、3、5"或"全部 Top 5"，我开工。
