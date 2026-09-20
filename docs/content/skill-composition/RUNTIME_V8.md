# 可执行技能配置 v8：选定判定主体与发起者绑定

规则文件使用 `schemaVersion: 8`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v8`，最低引擎规则版本为 86。schema 1～7 的字段、玩法哈希及 rules 79～85 路径保持不变；schema 8 内容不会进入旧规则存档。

v8 补齐“一个已接受的牌动作触发技能，技能拥有者先选另一名角色，再令该角色判定”的通用链。它同时让判定完成订阅精确约束判定主体、判定发起者与稳定原因，并允许伤害直接落到本次判定主体。这样经典雷击不需要张角专用 Prompt、专用判定续接或第二次目标选择。

依据[一将成名官网经典张角](https://x.sanguosha.com/hero/33.html)及 [BWIKI 雷击](https://wiki.biligame.com/sgs/%E9%9B%B7%E5%87%BB)，经典雷击的顺序是“成功使用或打出闪 → 可发动 → 选择另一名角色 → 该角色判定 → 按最终花色对同一角色结算”。[BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/%E6%8A%80%E8%83%BD%E6%A6%82%E5%BF%B5%E4%BB%8B%E7%BB%8D)用于继续分离技能标签、发动选择和已经开始的判定效果。

## 配置

```json
{
  "schemaVersion": 8,
  "skills": [
    {
      "id": "example:classic-leiji",
      "revision": 1,
      "modifiers": [],
      "viewAs": [],
      "activations": [],
      "contributions": [],
      "triggers": [
        {
          "id": "after-dodge",
          "window": "cardResponseAccepted",
          "cardKinds": ["dodge"],
          "optional": true,
          "effects": [
            { "op": "selectTarget", "target": "selectedTarget", "targetKind": "otherLiving" },
            { "op": "startJudgment", "target": "selectedTarget", "judgmentReason": "skill.example.classic-leiji" }
          ]
        },
        {
          "id": "spade-damage",
          "window": "judgmentFinalized",
          "subject": "any",
          "judgmentSource": "owner",
          "judgmentReasons": ["skill.example.classic-leiji"],
          "suits": ["spade"],
          "minimumRank": 1,
          "maximumRank": 13,
          "excludedReasons": [],
          "optional": false,
          "effects": [
            { "op": "damage", "target": "judgmentSubject", "amount": 2, "nature": "thunder" }
          ]
        },
        {
          "id": "club-recover-damage",
          "window": "judgmentFinalized",
          "subject": "any",
          "judgmentSource": "owner",
          "judgmentReasons": ["skill.example.classic-leiji"],
          "suits": ["club"],
          "minimumRank": 1,
          "maximumRank": 13,
          "excludedReasons": [],
          "optional": false,
          "effects": [
            { "op": "recover", "target": "owner", "amount": 1 },
            { "op": "damage", "target": "judgmentSubject", "amount": 1, "nature": "thunder" }
          ]
        }
      ]
    }
  ]
}
```

## 执行契约

- 卡牌动作窗口中的 `selectTarget` 仅在 schema 8 可用，必须是 effects 第一项，目标为 `selectedTarget`，并且后面至少有一项以 `selectedTarget` 为主体的 `startJudgment`。
- `targetKind: otherLiving` 排除技能拥有者自己；候选为空时不发布无效发动询问。目标选择使用已有 `ProgramCardTrigger` 私有 Choice，WPF 不新增武将专页。
- 选择提交时再次核对目标仍存活且仍在候选中；`ProgramCardTargetSelectedEvent` 记录 action、程序、绑定、拥有者和最终目标。Checkpoint 保存选定座位，Replay 从命令前缀重建。
- `startJudgment.target: selectedTarget` 把所选角色写成判定主体；技能拥有者仍是判定发起者。原【闪】实体牌继续停留在 Processing，完整判定、改判、判定后订阅和伤害结束后才恢复原响应父流程。
- `judgmentFinalized.subject: any` 允许技能拥有者订阅其他角色的最终判定；`judgmentSource: owner` 再要求这次判定由该技能拥有者发起，防止另一名拥有同配置技能的角色误订阅。
- `judgmentReasons` 是稳定原因白名单；空数组或省略表示不增加白名单。它与 `excludedReasons` 不得重叠。
- `damage.target: judgmentSubject` 不再发布第二次选目标询问，直接使用这次判定冻结的主体；伤害来源仍是技能拥有者，并进入统一雷电伤害、连环、濒死、死亡和父流程续接。
- AI 在 `selectTarget` 使用既有雷击关系评估；旧 card trigger 仍保持原选择策略。

## 本块没有声称完成的能力

- 正式经典张角配置迁移。D3f 只补齐 A51 仍缺的通用执行链；鬼道、黄天和经典包内容版本仍在 A51 一次组装。
- 任意事件图、任意判定成功条件或动态表达式。v8 只扩展已受控的牌动作、判定和伤害基础能力。
- 界雷击的“本人任意判定后再任选角色”不使用本页的 `judgmentSubject` 直伤；它继续使用 schema 5 的判定后 `selectTarget`。
- 国战明置／暗置资格和双将技能归属。
