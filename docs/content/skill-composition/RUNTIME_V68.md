# RUNTIME V68 —— 规则 190（不变）/ 经典包 1.162.0

本版本号为神周瑜批次（琴音 / 业炎）使用。规则版本保持 190，Checkpoint schema 仍为 3，技能 JSON schema 62 不变——本批为**零引擎增量**，只做内容与注册；经典包 `1.161.0 → 1.162.0`。旧规则检查点的版本边界不因本批改变。

## 神周瑜批次：琴音 / 业炎

单武将交付：神周瑜（神，男，4 勾玉，神话再临·火 2011，最低规则 187）。四个技能语义全部由现行原语承载，OL 线 `ol:qinyin` / `ol:yeyan` 已实现同形结构，本批把经典文本与经典 id 入册。

### 消费的能力（无新增）

1. `discardPhaseEnded` 触发窗口 + `SkillProgramTurnOwnerScope.Own` + `AllowOwnDiscardPhaseEnded`：以 `turnOwnerDiscardPhaseHandDiscardCount >= 2` 的 Compare/GreaterThanOrEqual 条件读取本阶段自身弃置手牌数事实。
2. `chooseOption` + `ChoiceIs` 门控：一绑定（`qinyin-melody`）两选项各带 presentation 标签，分别接 `recoverAllLiving(1)` 与 `loseHpParticipants(anyLiving, 1)`。
3. 限定技多档激活：同一技能内 `small` / `two` / `three` / `two-and-one` 四档激活按 `minTargets/maxTargets` 与伤害分配区分；`selectDistinctSuitHandDiscards` 承担「四张花色各不相同手牌」支付，`loseHpUnclamped` 承担自身 3 点体力支付（不受下限夹逼），`damageParticipants` 以 `nature: fire` 结算火焰伤害，`ContinueAfterOwnerDeath` 保留濒死链。

### 消费方

- 琴音：`classic:qinyin`（`WithOptionalTriggerMetadata`，可选触发）。
- 业炎：`classic:yeyan`（`SkillTag.Limited` + `SkillExecutionForm.State` + `SkillActionForm.Active`）。
- 武将：`classic:shen-zhou-yu`，势力 id 取 `god`（与神关羽/神吕蒙/神赵云/神曹操及全部 OL 神将一致；WPF 徽章、着色、图鉴排序均以 `god` 为键），入 `identity:classic` 武将池。

## 验证口径

- 定向检查 `ShenZhouYuChecks` 4 项全绿；同批 `JiaXuChecks` 4 项全绿。
- 全量 Core **729 / 729**（隔离输出目录构建，规避并行测试进程的 `bin/` 文件锁）。
- 两处夹具修正记录在批次文档：目标选择须按 `maxTargets` 截断；可选触发按「先发动、后选项」两步提示序列探测。
- 批次记录见 [2026-09-30-shen-zhou-yu](../../benchmarks/2026-09-30-shen-zhou-yu.md)（含并行在制面收敛过程与整树快照提交理由）。
