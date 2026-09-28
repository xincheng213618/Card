# Runtime v59：死亡遗留牌申领

本批消费方为曹丕（神话再临-林，2010，魏3体力）。统一 schema62、最低规则179（`Replay.CurrentRulesVersion`）、经典包1.149.0；Checkpoint格式3不变。历史内容不改写，新入口只对声明了最低规则179及以上的内容开放。工作区内并行批次的被动伤害/体力程序（HpChangePrograms 等，规则178）与徐盛、张松改动未纳入本批验证，源码文件互不覆盖；本批在其之上叠加，不回改。

## 死亡遗留牌申领

新增效果操作 `claimDeathCleanupCards`（`ProgramDeathClaimOperations.cs`）：在 `characterDied` 窗口内，让非死者把死者刚被清入弃牌堆的全部牌收入自己手牌。约束与语义：

- 仅活动帧内有效（帧归属/技能不符抛错）；要求 `ProgramContextCapability.Death`。
- 经活动帧的 `ParentFrameId` 定位 `ProgramKillTriggerWindowFrame`，再按其 `DeathFrameId` 沿 `Parent` 链回溯到本次死亡的 `DeathResolution`；持有者即死者时直接抛错（官方行殇只看“其他角色死亡”）。
- 只申领此刻仍在弃牌堆的清理牌：死亡后已被其他结算移走的牌不再追索。
- 移动原因 `skill-program.{skillId}.{bindingId}.claim-death-cleanup`，目标 `Hand(owner)`；无可申领牌时静默结束。
- 配套：`BeginPlayerDeath` 的七处清理移动统一记录进 `DeathResolution.CleanedUpCardIds`（在死亡流程标记死亡之后、OwnerDied/CharacterDied 窗口之前），供上述回溯与事实冻结使用。

新增触发条件 `deathVictimHasCards`（事实 `DeathVictimCleanupCardCount`，值20）：characterDied 窗口冻结的“死者清理牌数>0”事实，解析器对其他窗口拒收，避免死者无牌时的无意义询问。

窗口能力：`ProgramEntryCapabilities.For(characterDied)` 由 `Common` 扩为 `Common | Death`——击杀者窗口内现在允许声明 Death 类效果操作（行殇在 KillerWindow 中申领）。

## 消费方：曹丕“行殇 / 放逐”

- 行殇（可选）：`characterDied` + 条件 `deathVictimHasCards`，激活后 `claimDeathCleanupCards`。
- 放逐（可选）：`afterDamageApplied`（perDamage）+ 既有 `selectTarget(otherLiving)` → `turnOver` → `draw(selectedTarget, ownerLostHp)`，X 为曹丕已损失体力值（官方现行文本即“先翻面，再摸X张”），无新增表达式。

## 验证口径

定向检查 `tests/CardGame.Core.Tests/CaoPiChecks.cs`（4项，自然命令与真实决策应答）：

1. 定义与触发schema：注册表存在 classic:cao-pi（魏3体力、双技能、Wei池）、行殇/放逐关键触发属性断言；三类schema拒收样例（deathVictimHasCards 声明在 ownerDied、claimDeathCleanupCards 指向 selectedTarget、无申领条件外的非法组合）。
2. 行殇申领死者全部牌并回放：击杀 → 激活行殇 → 申领移动与死亡清理集合逐一对应（弃牌堆→持有者手牌、原因含 claim-death-cleanup）、牌实际进入曹丕手牌、绑定开始事件可见；Checkpoint 还原后事件与状态全等。
3. 拒绝行殇：死者清理牌全部留在弃牌堆，无申领移动。
4. 放逐翻面并按已损失体力摸牌：目标翻面且摸牌数 == 曹丕已损失体力、原因含技能ID。

验证结果：定向4/4通过（详见批次记录）。

## 边界与未覆盖

- 主公技“颂威”（其他魏势力角色的黑色判定牌生效后，其可令曹丕摸一张牌）未实现：需要势力类触发事实与“判定角色持有决策权”两类新引擎语义，超出本批公共能力；经典黄天式的 contributions 机制形状不符（颂威是触发型摸牌而非出牌阶段交牌）。
- 多角色同时死亡（如一个伤害源连及多目标）时每个 characterDied 窗口独立询问行殇，与官方逐个死亡结算一致；连环传导下的合并申领未逐一专测。
- AI 估值（行殇申领 +10）只为让 AI 稳定激活，不宣称最优。
