# 可执行技能配置 v9：判定后的直接死亡

规则文件使用 `schemaVersion: 9`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v9`，schema 默认最低引擎规则版本为 93。schema 1～8 的字段、玩法哈希和 rules 79～92 路径保持不变；schema 9 内容不会进入 rules v92 或更早存档。

v9 在 `judgmentFinalized` 窗口新增 `causeDeath`。它用于“最终判定已经完成，指定角色直接死亡”的生命周期效果，不伪造成伤害、失去体力或极大数值扣血。技能标签仍只属于内容元数据；是否触发、是否询问、候选顺序和执行效果继续由绑定显式声明。[BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/%E6%8A%80%E8%83%BD%E6%A6%82%E5%BF%B5%E4%BB%8B%E7%BB%8D)用于约束这种标签／执行形态分离，[BWIKI 神关羽](https://wiki.biligame.com/sgs/%E7%A5%9E%E5%85%B3%E7%BE%BD)正式服 I 版及问答用于核对武魂的判定、直接死亡、改判与游戏结束边界。

## 配置

```json
{
  "schemaVersion": 9,
  "skills": [
    {
      "id": "example:judgment-death",
      "revision": 1,
      "triggers": [
        {
          "id": "failed-judgment",
          "window": "judgmentFinalized",
          "subject": "owner",
          "judgmentReasons": ["skill.example.death"],
          "suits": ["spade", "heart", "club", "diamond"],
          "minimumRank": 1,
          "maximumRank": 13,
          "excludedReasons": [],
          "optional": false,
          "effects": [
            { "op": "causeDeath", "target": "judgmentSubject" }
          ]
        }
      ]
    }
  ]
}
```

`target` 只能是当前判定主体 `judgmentSubject`，或由同一 effects 序列首项 `selectTarget` 冻结的 `selectedTarget`。后一种写法必须恰有一个首项 `selectTarget`，并在其后执行 `causeDeath`。`causeDeath` 不接受 `amount`、`nature`、牌区、花色、旧判定牌去向或嵌套判定原因等参数。

## 执行契约

- 最终判定事实、判定牌与父 `JudgmentFrame` 保持在栈上；`ProgramCauseDeathDeclaredEvent` 记录独立 cause ID、父触发窗口、判定帧、技能／绑定、来源和目标。
- 目标直接进入统一 `DeathFrame`，但没有 Damage、`DyingFrame`、求桃窗口或 killer；因此不会触发伤害防止、伤害后技能、连环传导、梦魇增量或身份击杀奖惩。
- 被直接死亡的角色仍可执行自己的合法死亡技能。嵌套死亡以内层先完成，再恢复外层 `ProgramJudgmentTriggerWindowFrame`，后续稳定候选继续执行。
- 若死亡提交已确定胜负，当前直接死亡绑定记为完成，但不再开放天妒、刚烈惩罚、后续配置候选或其他新的玩家可见动作；父判定只完成必要牌区收尾并沿已有终局路径退栈。
- 目标在效果开始前已经死亡时不重复死亡，直接继续同一触发游标。
- 暂停在嵌套死亡技能 Choice 的 Checkpoint 仍只保存已接受命令前缀；Replay 必须重建相同 cause 事件、死亡栈、判定收尾和后续候选。

## 兼容边界

- schema 9 的最低规则版本为 93；包含 schema 9 程序的内容注册表要求 rules v93 或更新，rules v92 恢复会在创建引擎前明确拒绝。当前规则版本已由后续 schema 10 提升为 94，但不改变本 schema 的最低版本与玩法哈希。
- schema 8 仍映射 `skill-program-v8`／最低 rules 86，已发布配置的规范化玩法哈希不变。
- 本块只提供通用 `causeDeath` 能力并用合成场景验证；正式 `classic:wushen`、`classic:wuhun` 和神关羽入池仍由 A53 独立完成。
