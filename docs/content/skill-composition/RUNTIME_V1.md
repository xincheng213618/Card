# 可执行技能配置 v1

本文件说明已经接入引擎的配置格式。相邻的 `skills.example.json` 是完整架构的设计草案，不能交给 v1 加载器。

实际示例在 `src/CardGame.Content.Standard/SkillPrograms/`。启动 WPF 后选择“技能组合体验”模式。它是独立内容包；旧标准内容包保持原有定义及回放指纹。JSON 目前作为嵌入资源随构建发布，修改后需要重新编译，没有热加载。

## 文件和身份

- `composed-skills.rules.json`：可执行规则，`schemaVersion: 1`，`skills` 数组。
- `composed-skills.presentation.json`：名称和描述，`schemaVersion: 1`，`skills` 是以 SkillId 为键的对象。
- `composed-generals.json`：示例武将与技能的绑定；一个武将可以绑定多个 SkillId。

规则 ID 与展示 ID 必须完全对应。规则的 `revision`、节点顺序与语义会进入 SHA-256 玩法指纹；空白、JSON 属性顺序、名称和描述不影响该指纹。已有技能 ID 和 activation ID 应保持稳定。v1 最低引擎规则版本为 79，旧规则版本不会静默执行配置技能。规则版本 80 增加了独立的 [v2 转换触发](RUNTIME_V2.md)，不改变 v1 文件的哈希。

## 一个新技能的完整规则

下面只组合已有基础效果，无需增加 `SkillKind` 或引擎分支：

```json
{
  "schemaVersion": 1,
  "skills": [
    {
      "id": "example:gift-heal",
      "revision": 1,
      "modifiers": [],
      "viewAs": [],
      "activations": [
        {
          "id": "give",
          "minCards": 1,
          "maxCards": 1,
          "minTargets": 1,
          "maxTargets": 1,
          "targetKind": "otherLiving",
          "usesPerTurn": 1,
          "condition": { "kind": "wounded" },
          "effects": [
            { "op": "giveSelected", "target": "selectedTarget", "amount": 1 },
            { "op": "recover", "target": "owner", "amount": 1 }
          ]
        }
      ]
    }
  ]
}
```

对应的展示文件：

```json
{
  "schemaVersion": 1,
  "skills": {
    "example:gift-heal": {
      "name": "赠愈",
      "description": "出牌阶段限一次，你可以交给一名其他角色一张手牌，然后回复1点体力。仅受伤时可发动。"
    }
  }
}
```

引擎不读取描述来推断效果。描述改动不修改玩法，作者需要保证文字与规则对应。新规则及其展示条目添加后，内容包会自动注册；再在武将绑定中引用它即可。

v1 没有结构化的主公技、锁定技、限定技、觉醒技或转换技标签，也不把状态技／触发技当作互斥技能种类。纯数值修正由引擎自动应用，`activations` 是玩家主动入口；展示文案里的“锁定技”等文字不会改变执行逻辑。完整标签与执行形态的拆分见[技能组合系统重构设计](../../SKILL_COMPOSITION_DESIGN.md)。

## 可组合的基础能力

| 类别 | v1 契约 |
| --- | --- |
| 数值查询 | `drawCount`、`handLimit`、`slashLimit`、`outgoingDistance`、`incomingDistance` |
| 数值操作 | `add`、`set`；`unlimited` 仅适用于 `slashLimit`，此时 `value` 必须为 0 |
| 转换 | `inputKinds` 与 `inputSuits` 筛选实体手牌，空数组表示不限；`outputKind` 为 `slash` 或 `dodge`；`forPlay` 与 `forResponse` 分别授权主动使用和响应，闪只支持响应 |
| 主动选择 | 手牌精确数量和最多一个角色；候选目标为 `otherLiving`、`anyLiving`、`otherWounded`、`anyWounded` |
| 基础效果 | `draw`、`recover`、`loseHp`、`giveSelected`、`discardSelected`，按数组顺序执行 |
| 效果目标 | `owner` 或 `selectedTarget`；引用后者必须选择恰好一个目标 |
| 条件 | `always`、`ownTurn`、`notOwnTurn`、`wounded`、`hpAtLeast`、`handCountAtLeast`、`all`、`any`、`not` |

所有条件均以**技能拥有者**为上下文，在对应查询或执行步骤发生时计算。目标是否受伤通过 `targetKind` 限定。`hpAtLeast`、`handCountAtLeast` 使用 `value`；逻辑组合使用 `children`。不支持脚本、任意表达式或反射。

程序按 SkillId 的序数排序合并，先执行全部 `set` 再执行全部 `add`；多个 `set` 以排序后的最后一项为准。同一程序内保留数组顺序。`unlimited` 优先于其他杀次数修正。摸牌数与手牌上限不低于 0。

`usesPerTurn` 必填，null 表示没有规则层次数限制，正整数表示每个拥有者、每个 activation 的回合限额，在该角色开始回合时重置。AI 另有每回合每个 activation 最多选择 16 次的决策保护，防止收益循环；这不改变人类玩家的合法动作。

每个 activation 选中的手牌必须由一个无条件的 `giveSelected` 或 `discardSelected` 步骤消费，`amount` 与精确选牌数一致。给牌始终排除自己，避免同牌区移动。所有候选、数量、持有者和动作身份均由引擎再次验证，界面不能通过伪造命令扩大选择范围。v1 不选择装备或其他隐藏牌区。

## 中途暂停和恢复

执行器推进指令位置后，才开启失去体力引起的濒死子流程。获救后从下一条继续；拥有者死亡时取消剩余效果。暂停点仍采用种子、规则版本和已接受命令日志重放，通用结算帧记录 SkillId、ActivationId、玩法指纹和指令位置。

此死亡取消策略只描述 v1 主动步骤，不代表全部技能已经发动后的通用规则。完整触发生命周期需要独立的继续政策和新执行版本，见 [BWIKI 概念对齐](BWIKI_RULE_ALIGNMENT.md)。

如果救援消耗了先前选中的牌，后续给牌或弃牌步骤会重新检查实体牌；支付无法完成时取消剩余效果，不会部分支付、重复支付或免费获得后续收益。已发生的效果不回滚。死亡接收者也不能完成给牌步骤。

与旧技能混用时，卡牌移动继续经过同一牌区接口。现有连营、枭姬等失牌触发仍沿用旧引擎在父结算结束后处理的时点；v1 未引入可在任意步骤之间插入的通用触发窗口。

## 扩展边界

未知字段、未知操作、重复 ID、非法条件、超限数量和当前不支持的转换会在加载时被拒绝。不要仅在 JSON 中写入未实现的能力名称。

事件触发、伤害来源取牌、虚拟无实体杀、标记、转换来源订阅、判定／改判和死亡技能尚未进入此执行器。因此神关羽武魂、张角判定链和 SP 赵云冲阵尚不能完整迁移为此格式。新增这些能力时应扩展通用结算操作与暂停机制，并增加相应交互／回放验证，再由配置组合正式技能。已经实现的基础组合无需新增技能专用分支。
