# 可执行技能配置 v2：转换后的触发

规则文件使用 `schemaVersion: 2`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v2`，最低引擎规则版本为 80。v1 的文件格式、玩法哈希和 rules 79 路径保持兼容；v2 内容不能装入旧规则存档。

v2 在 v1 查询修正、牌转换、主动步骤的基础上加入 `triggers`。各数组可省略；不认识的字段、枚举或绑定仍拒绝加载。它目前专门解决“通过某个转换绑定用牌／响应后执行步骤”，尚不是完整的技能事件语言。

概念参考 [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/技能概念介绍)。主公／锁定／限定／觉醒／转换是标签，不能代替执行时机或可选性；阴阳转换状态也不是本页的牌型转换。完整标签、死亡后的状态清理与已开始触发继续执行的边界，仍由后续版本单独建模和验收。

## 两个窗口

| `window` | 时点与对手 |
| --- | --- |
| `cardUseTargetsFinalized` | 转换后的杀完成流离目标变更之后、烈弓／铁骑／雌雄与闪响应之前；为每个最终目标收集候选 |
| `cardResponseAccepted` | 转换实体牌已经支付到 Processing，尚未弃置或完成原响应；杀响应的对手为决斗另一方或群体牌使用者，闪响应的对手为来杀使用者 |

多目标杀先完成所有目标的流离询问，再按最终目标推进候选；每次响应是独立的一次接受记录，无双要求的第二张响应不会重用第一张的记录。护驾／激将区分名义响应者与实际供牌者；转换绑定属于供牌者。借刀、青龙和主动激将产生新的用牌流程，不冒充普通打出响应。

## 可复用的配置

下例展示“杀闪互换 + 转换响应后获得对方一张手牌”。它是机制示例，不能视为已验收的正式 SP 赵云版本。

```json
{
  "schemaVersion": 2,
  "skills": [
    {
      "id": "example:swap",
      "revision": 1,
      "viewAs": [
        { "id": "to-slash", "inputKinds": ["dodge"], "inputSuits": [], "outputKind": "slash", "forPlay": true, "forResponse": true },
        { "id": "to-dodge", "inputKinds": ["slash", "fireSlash", "thunderSlash"], "inputSuits": [], "outputKind": "dodge", "forPlay": false, "forResponse": true }
      ]
    },
    {
      "id": "example:take-after-swap",
      "revision": 1,
      "triggers": [
        {
          "id": "after-response",
          "window": "cardResponseAccepted",
          "sourceSkillId": "example:swap",
          "sourceViewAsId": null,
          "optional": true,
          "effects": [
            { "op": "obtainOpponentHandCard", "target": "owner", "amount": 1 }
          ]
        }
      ]
    }
  ]
}
```

展示文件必须为两个 ID 分别提供 `name` 与 `description`；武将绑定两个 ID 即可。主动杀也要触发时，增加一个 `cardUseTargetsFinalized` 绑定，不能靠描述隐式扩大适用范围。`sourceViewAsId` 为 null 时匹配该技能的任意合法转换绑定；指定 ID 时只匹配该绑定。来源必须在同一规则目录中存在，并支持对应使用／响应窗口。

触发步骤可以使用 `draw`、`recover`（目标 `owner` 或 `opponent`，数量 1～20）以及 `obtainOpponentHandCard`（仅 `owner`，数量恰好为 1）。步骤按顺序执行，使用 v1 的拥有者条件语法。可选触发先询问发动／跳过；发动后的取牌步骤只显示对方手牌位置，不公开实体 ID 或牌面。对方为空手时不会产生空选择。

## 身份、暂停与恢复

合法动作保存 `CardConversionSource` 的 SkillId、BindingId、OwnerSeat、SkillInstanceId；WPF、命令和响应 ChoiceId 保留精确来源。同一实体牌可由多个技能转换时，不按最终牌型猜测来源。当前没有获得／失去技能的实例生命周期，实例 ID 以座位与技能 ID 确定；未来新增技能转移时必须扩展实例生成规则。

`CardActionContext` 区分 actor、provider、requester、responder、opponent、父动作和支付实体牌。来源记录只描述已获准的转换，不授权额外的转换链。`ProgramCardTriggerWindowFrame` 保存候选、玩法哈希、效果游标及明确的原动作续接类型。执行步骤前推进游标，响应牌保留在 Processing，窗口结束后才弃置并继续杀／决斗／群体效果。

存档依然通过重放已接受的命令前缀恢复，不直接反序列化内部对象或委托。内部接受事件及结算帧是可信宿主诊断数据；玩家快照不含这些结构，战斗提示与战报采用公开事件白名单。

## 尚未覆盖

- 通用伤害后、判定／改判、死亡、标记及技能获得／失去窗口。
- 鬼道交换旧判定牌、改判后摸牌、按最终花色分支和伤害效果，属于 D3。
- 武魂的伤害后梦魇标记、模式结束门槛、已死拥有者选人、直接死亡与嵌套死亡，属于 D4。
- 结构化主公技／锁定技／限定技／觉醒技／转换技标签，不能从 `optional` 推导。
- 虚拟无实体杀、任意支付组合，以及尚未实现的目标技能时序。

正式武将是否入池由独立版本合同与场景验收决定。资料索引中的已抓取条目不是可运行武将，也不会自动注册到选将池。
