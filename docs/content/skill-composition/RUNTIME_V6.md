# 可执行技能配置 v6：精确牌动作发起判定

规则文件使用 `schemaVersion: 6`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v6`，最低引擎规则版本为 84。schema 1～5 的字段、玩法哈希及 rules 79～83 路径保持不变；schema 6 内容不会进入旧规则存档。

v6 为用牌／打出牌窗口增加直接有效牌型过滤 `cardKinds`，并增加 `startJudgment` 效果。这样技能可以在一张【闪】真正成为已接受响应后，或【闪电】完成用牌目标后，发起以技能拥有者为判定对象的独立判定；判定及其后续订阅完成后，再返回原牌 action。

设计继续遵循 [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/技能概念介绍)：技能标签、候选资格、发动选择和已开始效果的续接分别建模。“使用／打出牌”读取已提交动作的有效牌型，不能靠中文技能名或最终牌面反推；已经发动的判定保存自己的父帧和稳定原因，不因随后技能不可用而清空。

## 当前配置

```json
{
  "schemaVersion": 6,
  "skills": [
    {
      "id": "example:self-judgment",
      "revision": 1,
      "triggers": [
        {
          "id": "after-dodge",
          "window": "cardResponseAccepted",
          "cardKinds": ["dodge"],
          "optional": true,
          "effects": [
            {
              "op": "startJudgment",
              "target": "owner",
              "judgmentReason": "skill.example.self-judgment"
            }
          ]
        },
        {
          "id": "after-lightning",
          "window": "cardUseTargetsFinalized",
          "cardKinds": ["lightning"],
          "optional": true,
          "effects": [
            {
              "op": "startJudgment",
              "target": "owner",
              "judgmentReason": "skill.example.self-judgment"
            }
          ]
        }
      ]
    }
  ]
}
```

字段和结构约束：

- `cardKinds` 只属于牌动作触发窗口，并按已接受动作的 `EffectiveKind` 精确匹配。直接触发的技能拥有者必须是该 action 的 actor；provider、requester、responder 和 opponent 仍由动作上下文分别保存。
- `cardKinds` 与 `sourceSkillId`／`sourceViewAsId` 互斥。前者监听实际有效牌动作；后者继续只监听指定转换技能和绑定，不能用直接牌型过滤替代 SP 龙胆／冲阵的来源身份。
- 当前用牌窗口接受普通／火／雷【杀】和【闪电】，响应窗口接受普通／火／雷【杀】和【闪】。空数组、重复项、窗口不支持的牌型和混合两类来源都会在装载时拒绝。
- `startJudgment` 只用于牌动作窗口，`target` 固定为 `owner`，必须提供 1～128 字符的稳定 `judgmentReason`；它不接受数量、伤害属性、选牌区、花色或改判字段。
- 一个绑定的 `optional` 只控制本次判定是否发动。判定已经开始后，改判、最终判定订阅和判定牌收尾按各自游标继续。

## 父牌流程与暂停恢复

实体响应牌在触发询问和技能判定期间留在 `Processing`。判定完成后，原响应才继续弃置实体牌并恢复杀、决斗或群体锦囊的父流程。八卦阵产生的无实体【闪】也进入同一个直接动作入口；护驾／激将等场景仍由 actor 和 provider 的分离字段决定技能属于谁。

【闪电】在用牌目标冻结、实体牌进入 `Processing` 后开放触发。技能判定完成后恢复原【闪电】的集智／无懈窗口和置入判定区步骤；它不需要伪造一个攻击对象。延时牌 action 和响应 action 都保存 `ProgramCardTriggerWindowFrame`、程序／绑定 ID、玩法哈希、效果游标及父帧关系，Checkpoint 恢复不会重复发起判定或重复支付牌。

`startJudgment` 复用完整判定链：公开翻牌、既有和配置化改判、`judgmentFinalized` 订阅、天妒及判定牌收尾。schema 5 的回复、选目标和类型化伤害可以继续订阅 schema 6 发起的最终判定；嵌套伤害、濒死或死亡完成后恢复判定，再恢复原牌 action。WPF 复用通用技能发动询问和判定反馈，不新增武将专页或专用 Prompt 类型。

## 本块没有声称完成的能力

- 正式界张角内容注册；本页只提供界雷击绑定 A 所需的通用动作和判定续接能力。
- 从牌动作先选择另一名角色并令其成为判定主体；该能力已由后续 [schema 8](RUNTIME_V8.md) 提供，不属于 v6 的“拥有者自己判定”。
- 经典／界黄天的跨技能拥有者主动入口、主公身份／势力资格和“每提供者每出牌阶段一次”账本；该能力已由后续 [schema 7](RUNTIME_V7.md) 提供，不属于 v6 本身。
- 结构化主公、锁定、限定、觉醒或阴阳转换标签，以及玩家自选多个同时机技能的顺序。
- 任意锦囊／装备牌型的直接动作订阅；当前白名单只覆盖已经有真实父流程回归的杀、闪和闪电。

因此本页完成 D3d 的“精确牌动作发起自身判定”薄切片，但不等于完整界雷击或界张角已经进入正式内容包。跨拥有者贡献已在后续 schema 7 单独实现；正式内容仍须按张角迁移规格整包注册。
