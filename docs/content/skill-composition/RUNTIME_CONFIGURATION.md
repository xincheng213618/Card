# 技能配置、条件与顺序步骤

本页与[运行时入口](RUNTIME.md)一起使用。以下示例是技能或效果片段；完整根对象的格式版本取当前 `SkillProgramCatalog`，可直接参照仓库正式规则文件。

## 主动入口

`activations[]` 用稳定 `id` 定位主动绑定。`minCards`/`maxCards` 限定初始材料数量，`sourceZones` 限定拥有者来源区，`minTargets`/`maxTargets` 与 `targetKind` 限定目标；`usesPerTurn` 等显式额度限定其使用作用域。数量、来源和目标组合必须被对应能力接受。

例如，青囊的效果片段先支付真实手牌，再令所选受伤角色回复：

```json
{
  "id": "discard-and-heal",
  "minCards": 1,
  "maxCards": 1,
  "sourceZones": ["hand"],
  "minTargets": 1,
  "maxTargets": 1,
  "targetKind": "anyWounded",
  "usesPerTurn": 1,
  "effects": [
    { "op": "discardSelected", "target": "owner", "amount": 1 },
    { "op": "recover", "target": "selectedTarget", "amount": 1 }
  ]
}
```

该片段来自[主动技能演示规则](../../../src/Characters/_shared/Skills/standard-active-cutover.rules.json)。同名正式人物规则可能具有不同的次数或效果，应引用各自稳定 ID。

## 规则条件

条件通过 `kind` 声明。基础条件包括 `always`、`ownTurn`、`notOwnTurn`、`wounded`、`hpAtLeast`、`handCountAtLeast`；`all`、`any`、`not` 通过 `children` 组合。主体通常是技能拥有者，指定事件/目标事实的条件按各自定义读取。

条件按其实际求值时点读取状态，不把当前 HP、手牌数量或目标资格写成一个会永久失真的绑定。`hasOwnedCardCategory` 仅用于拥有牌选项条件，要求非空 `zones`，且区域只能是 `hand`、`equipment`；不能把它作为裸主动或触发条件。它支持 `cardCategories` 与 `cardKinds`：至少一项非空，两者同时给出时取交集；支付步骤也必须使用相同牌过滤。选项可用不等于已经支付。

`condition` 与触发器 `condition` 的可用种类不同。触发条件可读取冻结的 CardAction、Damage、Death、移动及实际阶段事实；不得把宿主输出事件或相邻移动拼成不存在的原生动作。

## 值与对象引用

`amount` 表达固定值；`numberExpression` 表达被该操作明确允许的数量，例如 `ownerLostHp`、`boundCardCount`。动态表达式需要的来源绑定必须已存在且类型正确，不能与互斥的固定数量字段混写。

`target` 指定 `owner`、`selectedTarget` 或受支持的集合/参与者；部分操作用 `targetRef` 指定 `eventSource`、`eventTarget`、有序选择成员等。`selectedTarget` 需要先有合法选择。事件来源不等于当前回合角色，也不等于提供材料的角色。

`resultBind` 产生具名的牌或参与者结果，`sourceBind`/`exceptBind` 等引用已经产生的结果。解析与资源验证拒绝前向引用、类型错误、重复消费以及不符合操作合同的引用。

## 顺序与子流程

`effects` 按数组顺序执行。`draw`、`recover`、`loseHp`、`giveSelected`、`discardSelected`、区域支付、判定及原生用牌均通过对应核心流程执行。可能暂停的步骤把结果和返回点保存在原程序帧；继续时读取原收据，不重新执行整段。

材料移动、装备离场、获牌、回复、伤害、濒死等都可能开启子窗口。需要等待这些后果的操作必须显式使用相应续接能力；不能因为一次 `MoveBatch` 返回就假设全部原生后果已经完成。

`optional` 决定触发询问是否可跳过，`condition` 决定资格，两者不是同一含义。已发行效果的续接也不等同于再次检查初始发动资格。

## 展示与 AI

展示 JSON 提供名称、说明、发动选项、触发/主动标签与布尔状态文字。展示不声明规则，也不改变玩法指纹。WPF 消费已发布的私有 `SkillPrompt`、材料和目标选择，不复制技能名规则。

AI 从自己的合法动作和当前选择评估成本/收益。新的操作必须声明足够的资源和交互语义；不能让 AI 读取可信诊断或他人私有牌面来补足描述符缺失。
