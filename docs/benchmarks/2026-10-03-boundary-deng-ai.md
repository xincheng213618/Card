# 界邓艾单武将批次

当前普通 OL 界邓艾（魏势力、4 体力、男性）以官方页当前文本接入：屯田（回合外失去牌判定积「田」+ 回合内弃置【杀】判定积「田」+ 逐「田」缩短计算距离）、凿险（准备阶段「田」≥3 觉醒：减 1 体力上限、获得急袭、本回合结束后执行一个额外回合）、急袭（「田」当【顺手牵羊】）。官方立绘 47100 已离线入库并登记图鉴/映射/同步脚本。来源见[来源档案](../content/sources/boundary-deng-ai-2026-10-03.json)。

| 技能 | 实现口径 |
| --- | --- |
| 屯田 | 回合外三个失去牌触发（手牌/装备区/判定区，perBatch，排除用牌与响应清理）与 `negatedOwnedZoneCount` 距离修正逐字复用已验收的 classic:tuntian 程序形状，仅换 boundary id，不改旧 classic 定义。新增"回合内弃置【杀】"分支：discardPileReceived + movementDiscardOnly + discardOwnerScope=own + cardKinds=[slash,fireSlash,thunderSlash] + ignoreOwnSkillMovements，条件 ownerIsTurnPlayer，判定链与回合外分支一致。 |
| 凿险 | 觉醒体（turnStartBeforeNormalFlow、usageLimit=1、authority≥3、changeMaximumHp -1、grantSkills）与 classic:zaoxian 一致，尾部新增 pendExtraTurn（成熟节点，语义"将在当前回合结束后获得一个额外回合"）。 |
| 急袭 | viewAs（authority 区非红桃牌视为【顺手牵羊】）与 classic:jixi 完全一致，按界系列惯例以 boundary id 自持定义。 |

## 共享能力扩展

- `MatchingDiscardPileIndexes` 补充 `CardKinds` 过滤（镜像 CardCategories 写法）。
- 触发解析器在 `DiscardPileReceived` 窗口按既有 `supportsDiscardSuitFilter` 门控同样接受 `cardKinds`（变量更名 `supportsDiscardCardFilter`），lifecycle 其他窗口继续拒绝。
- 现有内容没有在移动触发上使用 cardKinds（全库检索为零），旧行为零变化；`card movement`、`discard movement origin` 等既有共享检查全绿。

## 验证

- 界邓艾定向 5/5：定义与元数据、回合内弃杀判定入田（含跳过与距离 1→下限 1）、非杀基本牌与真实用杀不触发、回合外失去手牌判定、觉醒+额外回合+急袭真实【顺手牵羊】；关键步骤间做冷恢复四视角一致性。
- 无过滤日常范围：Core 153/153、WPF 16/16，wrapper 实测 66.1 秒（含增量构建）。
- Full 全量：Core 532/532、WPF 51/51，wrapper 实测 222.6 秒（本 worktree，合并 main 前）。

## 边界说明

- 主公体力按身份模式惯例 +1（4+1=5），觉醒后为 4；测试按该口径断言。
- 官方页仅列屯田/凿险两个页签，急袭为觉醒获得技，viewAs 与 classic 一致。
- 判定牌经 Processing 完成进弃牌堆（`skill-program.*.MoveBoundCards` 弃置来源理由），移动断言按最终完成移动记账。
- 本批为单武将批次：界公孙瓒/界华雄由 FengLinFourteenth 批次并行实施（主区另有其写回），界吕布/界袁绍已有该线设计预研；本批在独立 worktree（batch/deng-ai）开发后合并回 main，未触碰主区并行未提交工作。
