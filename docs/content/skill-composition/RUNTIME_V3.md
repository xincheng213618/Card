# 可执行技能配置 v3：最终判定结果触发

规则文件使用 `schemaVersion: 3`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v3`，最低引擎规则版本为 81。schema 1／2 的字段、玩法哈希及 rules 79／80 路径保持不变；schema 3 内容不会进入旧规则存档。

v3 的第一块能力是 `judgmentFinalized`：判定牌已经完成鬼才／鬼道等替换并发布最终结果，但仍留在判定区，天妒和普通判定牌收尾尚未执行。它订阅的是最终事实，不会对中间翻出的牌触发，也不参与改判。

概念边界参考 [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/技能概念介绍)，判定阶段划分参考[规则集的判定流程](https://gltjk.com/sanguosha/rules/flow/judge.html)。技能标签、状态技和触发技仍是不同维度；`optional` 只表示本绑定到达时是否询问，不代表锁定技、限定技、觉醒技或优先级。

## 当前配置

```json
{
  "schemaVersion": 3,
  "skills": [
    {
      "id": "example:club-reward",
      "revision": 1,
      "triggers": [
        {
          "id": "after-club-judgment",
          "window": "judgmentFinalized",
          "subject": "owner",
          "suits": ["club"],
          "minimumRank": 1,
          "maximumRank": 13,
          "excludedReasons": ["skill.leiji"],
          "optional": true,
          "effects": [
            { "op": "recover", "target": "owner", "amount": 1 },
            { "op": "draw", "target": "owner", "amount": 1 }
          ]
        }
      ]
    }
  ]
}
```

字段均为显式必填：

- `subject` 当前只接受 `owner`，表示技能拥有者是本次判定对象；尚不订阅他人的判定。
- `suits` 使用完成所有改判和有效花色修正后的最终花色，至少一项且不能重复。
- `minimumRank`／`maximumRank` 为闭区间，必须满足 `1 <= minimumRank <= maximumRank <= 13`。
- `excludedReasons` 按稳定的判定原因 ID 精确排除，可为空数组但不能重复；它不按中文日志或技能名猜测。
- `effects` 当前只接受目标为 `owner` 的 `draw` 和 `recover`，数量为 1～20。满体力回复是合法的零回复，仍继续后续步骤。
- `sourceSkillId`／`sourceViewAsId` 仅属于 v2 牌动作窗口，不能与最终判定字段混用。

schema 3 继续接受 v1 修正器、转换、主动步骤和 v2 牌动作触发。牌动作窗口仍要求精确转换来源；只有 `judgmentFinalized` 使用判定字段。

## 结算、隐私与恢复

引擎在最终判定事件之后冻结判定帧 ID、对象、原因、实体牌 ID、实体牌牌型、最终花色、点数和成功结果。候选只从判定对象仍有效的技能程序中收集，并按程序 ID、绑定 ID 稳定推进；每个候选在发动前重新检查技能、拥有者和条件。

可选绑定通过统一技能选择面显示发动／跳过，不把判定牌面或私有手牌写入其他座位的快照。`ProgramJudgmentTriggerWindowFrame` 保存冻结事实、候选游标、效果游标、玩法哈希和发动状态；回复使用正式 Recovery 帧，摸牌仍进入实体牌移动账本。窗口完成后才继续天妒或原判定清理，再恢复延时锦囊、八卦、铁骑、刚烈等父流程。

Checkpoint 仍重放已接受命令前缀；rules 81 才创建该窗口。schema 1／2 程序、rules 80 及更早存档不会被新时机追溯触发。

## 本块没有声称完成的能力

- `judgmentReplacing`、交换旧判定牌、替换牌来源区策略和改判后额外摸牌。
- 按最终结果选择其他角色、造成雷电伤害，以及伤害／濒死子流程后的程序续接。
- 多名拥有者或同一拥有者多个同时机技能的玩家自选顺序；当前仅提供稳定确定顺序。
- 判定牌取得、交给改判者或其他自定义收尾政策；现有天妒与判定清理仍走原实现。
- 通用伤害后、死亡、标记、技能获得／失去和结构化主公／锁定／限定／觉醒／阴阳转换状态。

因此本页是 D3a 的可复用最终结果订阅基础，不等于完整张角、鬼道或雷击已经迁入配置运行时。D3b 继续处理改判交换与改判后摸牌，D3c 再处理目标选择、雷电伤害及其暂停恢复。
