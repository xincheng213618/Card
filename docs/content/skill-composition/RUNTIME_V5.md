# 可执行技能配置 v5：最终判定选目标与伤害

规则文件使用 `schemaVersion: 5`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v5`，最低引擎规则版本为 83。schema 1～4 的字段、玩法哈希及 rules 79～82 路径保持不变；schema 5 内容不会进入旧规则存档。

v5 扩展 `judgmentFinalized`，允许一个触发绑定先选择一名合法角色，再对该角色造成明确数值和属性的伤害。目标选择、伤害、连环传导、伤害后技能、濒死、死亡和父判定续接都进入既有类型化流程；配置不能直接改体力、伪造死亡或绕过伤害事件。

概念边界继续参考 [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/技能概念介绍)：技能标签与状态技／触发技执行形态分别建模，触发后已经开始执行的效果按结算帧继续，不因后续技能状态变化被全局清空。当前[三国杀 OL 界张角页](https://www.sanguosha.com/hero/448)将梅花结果的回复和后续可选伤害写成不同强制性，因此正式配置必须拆成“强制回复绑定”和“可选选目标伤害绑定”，不能用一个总开关同时跳过两者。

## 当前配置

```json
{
  "schemaVersion": 5,
  "skills": [
    {
      "id": "example:final-judgment-effects",
      "revision": 1,
      "triggers": [
        {
          "id": "club-recover",
          "window": "judgmentFinalized",
          "subject": "owner",
          "suits": ["club"],
          "minimumRank": 1,
          "maximumRank": 13,
          "excludedReasons": [],
          "optional": false,
          "effects": [
            { "op": "recover", "target": "owner", "amount": 1 }
          ]
        },
        {
          "id": "club-thunder-damage",
          "window": "judgmentFinalized",
          "subject": "owner",
          "suits": ["club"],
          "minimumRank": 1,
          "maximumRank": 13,
          "excludedReasons": [],
          "optional": true,
          "effects": [
            {
              "op": "selectTarget",
              "target": "selectedTarget",
              "targetKind": "anyLiving"
            },
            {
              "op": "damage",
              "target": "selectedTarget",
              "amount": 1,
              "nature": "thunder"
            }
          ]
        }
      ]
    }
  ]
}
```

字段和结构约束：

- 只在 `judgmentFinalized` 中接受 `selectTarget` 和 `damage`，且需要 schema 5。
- 只要一个最终判定绑定包含选目标或伤害，它必须恰有一个位于首位的 `selectTarget`，并在其后至少有一个 `damage`；不能在未选目标时直接伤害。
- `selectTarget.target` 固定为 `selectedTarget`，`targetKind` 复用 `otherLiving`、`anyLiving`、`otherWounded`、`anyWounded`。选择步骤不接受 `amount` 或 `nature`，也不接受结算中途会变化的条件。
- `damage.target` 固定为 `selectedTarget`，`amount` 为 1～20，`nature` 接受 `normal`、`fire` 或 `thunder`；它不重复声明 `targetKind`。
- `draw`／`recover` 仍只作用于 `owner`。一个技能可用多个稳定绑定组合强制和可选效果，每个绑定的 `optional` 只控制自己的触发询问，不从锁定技等展示标签推导。

## 目标、伤害与暂停恢复

可选绑定在首次可执行效果不存在合法目标时不发布空询问；发动后发布仅技能拥有者可见的精确座位 Choice，提交时按当前存活／受伤状态重新校验。伪造座位、过期 Prompt 或目标已经失效都会拒绝，不会提前写入效果游标。

伤害来源是技能拥有者，数值和属性由已冻结的效果节点提供。引擎复用统一伤害帧、护甲和伤害修正、连环属性传播、伤害后候选、濒死救援、死亡清理与胜负检查，而不是从技能中文名进入雷击专用分支。伤害完成后恢复原程序判定帧，再继续剩余触发候选、天妒和判定牌收尾。

若该程序伤害发生在刚烈等伤害技能自身的判定中，子伤害和濒死帧可以位于外层伤害技能之上；完成后恢复外层攻击引用和原伤害技能游标。Checkpoint 仍重放已接受命令前缀：目标询问和嵌套濒死两个暂停点都保存程序 ID、绑定 ID、玩法哈希、目标座位及父帧关系，恢复后不会重复造成伤害。

WPF 复用通用座位目标控件，AI 复用只读取公开关系和体力的目标评分。其他玩家快照不公开私有目标询问；公开事件只在目标提交和伤害请求提交后产生。

## 本块没有声称完成的能力

- 本 D3c 交付本身不含正式界张角内容注册；后续 A52 已在经典包 1.66.0 复用本页能力完成注册。
- “使用／打出【闪】或使用【闪电】后，令自己判定”的绑定 A。它需要可配置的发起判定效果及准确的用牌／打出来源窗口，不能由 `judgmentFinalized` 反推。
- 玩家自选多个同时机技能的顺序、任意嵌套伤害触发窗口的通用堆栈化，或死亡技能和直接死亡。
- 结构化主公／锁定／限定／觉醒／转换标签；这些仍与本页的触发执行契约分离。

因此本页只定义 D3c 的最终判定目标和伤害薄切片。由精确牌动作发起自身判定的后续能力见 [RUNTIME_V6.md](RUNTIME_V6.md)，完整界张角的后续落地见[张角判定链迁移规格](../generals/ZHANG_JIAO_JUDGMENT_MIGRATION.md)。
