# 可执行技能配置 v10：持续牌身份与动作距离修正

规则文件使用 `schemaVersion: 10`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v10`，schema 默认最低引擎规则版本为 94；schema 8～10 可用 `minimumRulesVersion` 显式要求更晚但不超过当前引擎的规则版本。schema 1～9 的字段、玩法哈希和 rules 79～93 路径保持不变；包含 schema 10 程序的内容注册表不会装入 rules v93 或更早存档，正式 `classic:wushen` 因依赖神势力开局选择而显式要求 rules v95。

v10 将状态技的“牌持续视为另一牌名”与该有效动作上的规则修正分成两类显式绑定：

- `cardIdentities` 是持续状态，不发布“发动／跳过”询问，也不保留物理牌原名的普通使用入口；
- `modifiers.query: slashDistanceLimit` 只修改由指定牌身份绑定形成的【杀】动作的距离上限，不修改次数、目标数、响应、防具、抵消或伤害属性；
- `locked` 等技能标签仍只是内容元数据，不会自动生成上述任一执行规则。该建模边界依据 [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/%E6%8A%80%E8%83%BD%E6%A6%82%E5%BF%B5%E4%BB%8B%E7%BB%8D)；目标用例依据 [BWIKI 神关羽](https://wiki.biligame.com/sgs/%E7%A5%9E%E5%85%B3%E7%BE%BD)与[武神](https://wiki.biligame.com/sgs/%E6%AD%A6%E7%A5%9E)保存的正式服 I 版文本。

## 配置

```json
{
  "schemaVersion": 10,
  "skills": [
    {
      "id": "classic:wushen",
      "revision": 1,
      "minimumRulesVersion": 95,
      "modifiers": [
        {
          "query": "slashDistanceLimit",
          "operation": "unlimited",
          "value": 0,
          "sourceCardIdentityId": "heart-hand-as-slash",
          "condition": { "kind": "always" }
        }
      ],
      "cardIdentities": [
        {
          "id": "heart-hand-as-slash",
          "zones": ["hand"],
          "inputKinds": [],
          "inputSuits": ["heart"],
          "outputKind": "slash",
          "condition": { "kind": "always" }
        }
      ]
    }
  ]
}
```

`cardIdentities` 每项必须有稳定 `id`。schema 10 当前只接受拥有者自己的 `hand` 区，以及 `slash`／`dodge` 两种输出；`inputKinds` 和 `inputSuits` 至少一项非空，两组同时存在时按交集匹配。先限制可证明的小集合，后续扩展其他区域或牌名时必须另增 schema 和场景，不能悄悄改变 v10 解释。

`slashDistanceLimit` 当前只接受 `unlimited`、`value: 0`，并且必须以 `sourceCardIdentityId` 指向同一技能中输出普通【杀】的身份绑定。它不是拥有者全局攻击距离，也不会让原生非匹配【杀】或无实体【杀】越过距离。

## 执行契约

- 引擎只在实体牌仍位于技能拥有者手牌区时匹配持续身份；装备区、判定区、木牛粮区、牌堆、处理区、弃牌堆以及其他角色区域继续读取物理牌名。
- 匹配后不再发布物理牌原名的普通出牌或响应入口，也不再叠加可选 `viewAs`／旧式转换入口。红桃装备因此不能从手牌装备，红桃桃不能以桃出牌、无懈或参与濒死救援。
- 选定动作时以 `CardConversionSource` 冻结技能 ID、身份绑定 ID、拥有者座位和技能实例；随后实体牌离开手牌进入 Processing，同一次 action 仍保持冻结的有效牌名。
- `CardActionAcceptedEvent` 同时保存实体牌 ID／原牌名、有效牌名和身份绑定链。移动、展示、花色、点数与回放继续使用实体牌，合法目标、响应和伤害读取有效牌名。
- 由 `heart-hand-as-slash` 形成的普通杀可按对应 `slashDistanceLimit` 跨距离选择目标，但仍增加通常出杀计数；同阶段第二张仍被过滤，目标仍可闪避并经过防具与普通杀结算。
- 若装备朱雀羽扇，冻结后的普通杀仍可进入既有普通杀改火杀分支；武神只提供初始普通杀身份和距离来源，不直接改变伤害属性。
- 私有合法动作、AI 和 WPF 继续消费同一 `LegalAction`／`PromptChoice`，不增加武神专属 Frame、Prompt 或界面。

## 兼容与验证边界

- schema 10 默认最低版本仍为 rules v94，正式武神显式提高到 v95，因此 1.67.0 注册表会拒绝 rules v94，而 1.66.0 及更早包仍可沿原规则恢复；D5a 后当前引擎规则版本为 96，但没有反向提高 v10 或武神的最低版本。
- schema 9 仍映射 `skill-program-v9`／最低 rules 93；旧配置的规范化 JSON、运行时版本串和玩法哈希不变。
- 受控全红桃装备牌场景验证：物理赤兔只生成有效【杀】、不能装备、可攻击距离 2 目标、动作审计保留两种身份、通常一次出杀限制不变、暂停前后 Checkpoint/Replay 一致。
- A53a 先完成通用运行时；A53b1 以 rules v95 完成神势力开局选择；A53b2 已由 `standard-classic-generals@1.67.0` 注册正式 `classic:wushen`、类型化通用 `classic:wuhun` 和完整神关羽。真实牌堆已经验收红桃桃按杀、濒死无桃入口、通常次数、武魂整链和 Replay；视觉绑定仍留给 C21。
