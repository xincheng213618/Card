# 可执行技能配置 v4：判定牌替换

规则文件使用 `schemaVersion: 4`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v4`，最低引擎规则版本为 82。schema 1～3 的字段、玩法哈希及 rules 79～81 路径保持不变；schema 4 内容不会进入旧规则存档。

v4 增加 `judgmentReplacing`：判定牌亮出后、判定结果生效前，技能拥有者可从自己的手牌区和／或装备区选择符合条件的实体牌替换当前判定牌。旧牌去向、替换牌来源区和替换提交后的条件效果都由配置显式声明，不从技能名称或中文描述推断。

概念边界参考 [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/技能概念介绍)。该页把技能标签、状态技、触发技分别定义，并把“判定牌生效时更换判定牌”和“判定牌生效后”区分为不同窗口。当前[三国杀 OL 界张角页](https://www.sanguosha.com/hero/448)与[2019 年界张角上线公告](https://www.sanguosha.com/news/20191212_3649_0318)均写明鬼道以黑色牌替换、替换牌为黑桃 2～9 时摸一张牌，没有写获得旧判定牌；正式当前版本应使用 `discardPile`。`ownerHand` 是运行时支持的自定义或另行核实的历史版本政策，不能据此改写当前官方界鬼道。

## 当前配置

```json
{
  "schemaVersion": 4,
  "skills": [
    {
      "id": "example:configured-guidao",
      "revision": 1,
      "triggers": [
        {
          "id": "replace-black-card",
          "window": "judgmentReplacing",
          "subject": "any",
          "excludedReasons": [],
          "optional": true,
          "effects": [
            {
              "op": "replaceJudgment",
              "target": "owner",
              "zones": ["hand", "equipment"],
              "suits": ["spade", "club"],
              "oldCardDestination": "discardPile"
            },
            {
              "op": "draw",
              "target": "owner",
              "amount": 1,
              "replacementSuits": ["spade"],
              "minimumReplacementRank": 2,
              "maximumReplacementRank": 9
            }
          ]
        }
      ]
    }
  ]
}
```

字段均为显式必填：

- `subject` 接受 `owner` 或 `any`。`owner` 只订阅技能拥有者自己的判定，`any` 可订阅任一角色的判定。
- `excludedReasons` 按稳定的判定原因 ID 精确排除，可为空数组但不能重复。
- `effects[0]` 必须是 `replaceJudgment`；其后只允许 `owner` 的 `draw` 或 `recover`。
- `zones` 至少包含 `hand`、`equipment` 之一且不能重复；不允许直接从牌堆、处理区或判定区选择替换成本。
- `suits` 是选牌时相对技能拥有者计算的有效花色，至少一项且不能重复。黑色牌通常写为 `spade` 与 `club`。
- `oldCardDestination` 只接受 `discardPile` 或 `ownerHand`。前者把旧判定牌置入弃牌堆；后者交给技能拥有者。
- 后续效果的 `replacementSuits`、`minimumReplacementRank`、`maximumReplacementRank` 检查替换提交时相对判定主体冻结的判定牌有效花色与实体点数，闭区间必须在 1～13 内。选牌资格仍按技能拥有者持有该牌时的有效花色检查，因此“能否打出”和“提交后的判定花色”不会混成同一个时点。
- `draw`／`recover` 数量为 1～20；满体力回复可以产生零实际回复，不影响后续判定续接。

schema 4 继续接受 v1 修正器、转换、主动步骤，v2 牌动作触发和 v3 最终判定触发。判定替换字段不能混入牌动作窗口或 `judgmentFinalized`。

## 原子提交、顺序与隐私

收集候选时保存技能程序 ID、绑定 ID 和玩法哈希，并与既有鬼才／鬼道候选进入同一稳定判定替换游标。当前顺序按优先级、从判定对象座位起算的相对座次、技能种类和稳定候选 ID 确定；本版本没有实现玩家自选同一时机技能顺序。

发动时重新检查拥有者存活、技能仍有效、判定原因、条件和候选实体牌。替换提交先在一个牌区批次中验证并移动旧牌与新牌，再把新牌从处理区置入原判定区；当前判定帧立即改指新实体牌并冻结提交花色，然后才开放白银狮子、木牛流马、枭姬、连营等装备或手牌离场后果。配置不会看到“成本已移走但判定仍指向旧牌”的中间状态。

候选牌 ID 和发动／跳过选择只发布给响应者；其他座位只能看到已经提交的公开移动、判定替换和类型化结算事件。跳过会推进同一替换游标；发动后先执行符合冻结牌条件的摸牌／回复，再继续剩余改判候选、最终判定结果窗口、天妒和判定牌收尾。

Checkpoint 仍重放已接受命令前缀。暂停在私有选牌时可恢复精确程序、绑定、玩法哈希、候选实体牌和当前判定帧；完成回放不会重复移动旧牌或执行追加效果。rules 81 及更早版本不收集配置改判候选。

## 本块没有声称完成的能力

- 正式界张角内容注册；v4 只是其鬼道所需的通用能力，内容版本仍需独立配置、描述和场景验收。
- `judgmentFinalized` 后按黑桃／梅花选择其他目标、造成雷电伤害，以及伤害、濒死、死亡子流程后的程序续接。
- 玩家自选多个同时机技能的顺序，或覆盖任意历史版本的判定优先级争议。
- 通用伤害后、死亡、标记、技能获得／失去和结构化主公／锁定／限定／觉醒／阴阳转换状态。

因此本页是 D3b 的改判基础能力，不等于完整界张角或界雷击已经迁入配置运行时。D3c 的目标选择、雷电伤害及暂停恢复现见 [可执行配置 v5](RUNTIME_V5.md)。
