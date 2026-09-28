# 曹昂批：慷忾的观察距离、目标自指与受赠者用牌

状态：经典曹昂（星火燎原-天府，2017，魏4体力）单武将交付完成。schema62 / 最低规则176 / 经典包1.146.0 / Checkpoint3；新人物只在1.146注册。本批为单对话交付，无并行Sol；工作区内另一对话的被动伤害/体力程序批次（HpChangePrograms 等）与徐盛、张松改动未纳入本批验证，源码文件互不覆盖。

## 技能与官方口径

慷忾：当一名角色成为【杀】的目标后，若你与其距离1以内，你可以摸一张牌，然后交给其一张牌并展示之；若此牌为装备牌，该角色可以使用此牌。官方FAQ（bwiki 转录官方规则集问答）：曹昂自己被【杀】可以发动（自己与自己距离为0），但自己不能交给自己牌，故只摸一张牌、无赠牌与使用结算。两条结论分别编码为 target 分支与 observer 分支的触发条件，来源记录见 [cao-ang-2026-09-27](../content/sources/cao-ang-2026-09-27.json)。

## 公共能力

- `ownerEventTargetDistance`（触发事实/触发值）：用牌窗口内持有者到 eventTarget 的冻结战斗距离（`GetCombatDistance`，含马匹修正；eventTarget 缺席时 int.MaxValue）。仅限带目标的用牌触发窗口，解析器拒绝其他窗口。
- `cardActionTargetIsOwner`（触发事实/条件）：该次用牌目标集合是否含持有者本人，供 target 分支与 observer 分支互斥；窗口限制同上。
- `useBoundCardByTarget`（效果操作）：selectedTarget 以标准装备用牌流程使用其手牌中一张已公开（Public、恰好1张）的绑定单牌；牌离开手牌或非装备则取消技能剩余结算；使用后挂 `PendingMovementContinuation`，无候选移动窗口时立即 `CompleteAwaitedProgramMovement` 兜底。AI 语义 `UseBoundCardByTarget` 对受赠者加正向估值。
- 曹昂按既有 observer 触发、`selectAndMoveOwnedCard` 公开转交（`revealBeforeMove`+`awaitMovementTriggers`）与 `chooseOption` 组合消费上述能力，无人物专用引擎分支。

## 内容与验证

内容：`classic-cao-ang.rules.json`（skill classic:kangkai，revision 1，两个触发）、`classic-cao-ang.presentation.json`（含 use-equipment/keep 选项标签）；`StandardClassicGeneralPackage` 注册武将（portraitKey cao_ang）与技能，版本 1.146.0，`CurrentGeneralIds` 追加 classic:cao-ang。

定向检查 `tests/CardGame.Core.Tests/CaoAngChecks.cs`（5项，自然命令与真实决策应答）：

1. 定义与schema：注册表存在 classic:cao-ang / classic:kangkai、魏4体力、portraitKey cao_ang；通用schema加载/拒收样例（距离值与目标自指条件仅限用牌窗口）。
2. 距离1内赠牌：曹昂出【杀】指定邻座，发动慷忾公开转交八卦阵，受赠者AI选择“使用装备”并实际装备；Checkpoint 还原后两条会话事件序列与状态全等；断言手牌→装备两次移动、`EquipmentChangedEvent`、`ProgramCardsRevealedEvent`（bind=gift）与选项事件。
3. 非装备赠牌：不出现 use-equipment 选项，赠牌留在受赠者手牌。
4. 距离2不触发：曹昂装青釭剑（范围2）对距离2角色出【杀】，无任何慷忾事件（攻击范围不等于距离）。
5. 自己被【杀】：只摸一张牌，无赠牌展示与选项。

验证结果：定向5/5通过；当时冻结 Core 全量 545/545、0警告0错误（`dotnet run --project tests/CardGame.Core.Tests --no-build`）。WPF：图鉴缺立绘断言中 classic:cao-ang 已消除（官方 gid 404，默认皮肤140401入 general-art-catalog，8皮肤可切）；同断言剩余 xu-sheng 属另一并行批次未交付，本批不代做。`tools/sync_general_art.py --phase verify` 对全目录校验会命中既有 zhang-song JPG 条目（本批之前的遗留），非本批引入。

## 边界与未覆盖

- 借刀杀人转移目标、多目标【杀】下多名观察者的结算顺序、赠牌与【乐不思蜀】等时序叠加未逐一人物专测；全部走既有通用移动/用牌路径。
- 受赠者“使用装备”在装备区已占同槽位时的替换走标准装备流程，未单独断言。
- AI 估值（+8）只为让受赠者在装备与保留间产生稳定偏好，不宣称最优。
- 立绘经官方页面下载并记SHA-256；未做实机多DPI人工验收（与既有各批一致）。
