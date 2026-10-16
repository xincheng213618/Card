# 界刘表：当前规则与执行入口

当前人物 ID 为 `boundary:liu-biao`，势力 `qun`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/liu-biao/boundary/Content/boundary-liu-biao.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:zishou-current` | 自守 | 摸牌阶段，你可以多摸X张牌，你以此法摸牌的结束阶段，若你本回合使用过伤害牌，你弃置X张牌（X为全场势力数）。 |
| `boundary:zongshi-current` | 宗室 | 锁定技，全场每有一个势力，你的手牌上限便+1。其他角色对你造成伤害时，防止此伤害改为令其获得你区域内的一张牌，每个势力限一次。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:zishou-current` | `drawPhaseStarting / extra-draw-debt` | `drawExtraAndArmTurnDamageUseDebt` |
| `boundary:zishou-current` | `turnEnding / ending-damage-use-debt` | `selectTurnDamageUseDebtPayment`、`moveBoundCards`、`awaitBoundCardMovements` |
| `boundary:zongshi-current` | `modifiers` | 规则文件中的显式过滤、状态与策略 |
| `boundary:zongshi-current` | `beforeDamageApplied / source-faction-yield` | `preventDamageAndConsumeSourceFaction`、`selectAndMoveOwnedCard` |

正式规则文件：

- [boundary-liu-biao.rules.json](../../../src/Characters/liu-biao/boundary/Skills/boundary-liu-biao.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

## 工程默认

以下为未取得当前普通OL FAQ裁定时的工程默认，不冒充官网正文补充。

1. 自守 X：首次5200额外draw发行点计算存活角色的公开有效势力数；5201自己的实际结束阶段开始支付时再计算一次并冻结。更早候选的孩子改变势力或死亡会影响尚未发行的X，选牌/付款孩子不再改已经冻结的X。本稿没有在窗口初建时冻结attempt-faction或X。
2. 本回合存在至少一张真正以5200额外draw取得的实体才产生结束债务。牌堆/弃牌堆实际都空时接受可选节点仍记录请求数与实际0，之后伤害牌Use不使其补生债务。相同实际回合多个额外Draw阶段的同实例债务合为一次结束X；正常额外回合是新actualTurn。跳过结束阶段则不付款，下一actualTurn不继承。
3. 弃置X的区域按普通弃牌语义取手牌和装备；不足X则弃全部合法HE，零HE不虚造选择或实体。HE混合成本通过现有原子多来源移动，原技能失效或owner死亡后先排清已付子结算，取消尚未支付/剩余指令。原实例失去后再获新实例不继承自守债务。
4. 新伤害牌分类为Slash/FireSlash/ThunderSlash、Duel、BarbarianAssault、ArrowBarrage、FireAttack及Lightning；是否实际命中、被无懈或防伤不改“使用过”。Lightning作为伤害牌的分类是工程默认，尚无当前FAQ确认；其伤害无来源，不为宗室提供虚构来源势力。普通实体/多材料Slash Response不计为Use；没有真实Use owning frame的纯程序Damage/LoseHp不计。
5. 宗室来源势力在5202真正发行时查询并冻结；更早候选子链改变其势力，消费改变后的公开有效值。God等已公开选定势力读取既有effective查询的选定值，拒绝从原武将printed faction重建。每个新公开势力可消费一次，同一势力不同角色共用 victim/skill Game exact key。
6. 宗室Game额度跨该技能来源失效/重新获得保留，不调用ResetSkill清整个技能。HEJ空仍防伤并消费该已冻结来源势力，省略无实体转交；有真实死source的已接受伤害仍防止并消费，随后得牌因source死亡取消。无source、自伤、未公开有效来源势力不防止且不消费，没有猜测或隐藏身份推断。
7. 国战未明置势力不计入新公开X/handLimit，也不写入新public event或SkillRuntimeState的usage ID；来源首次明置后第一次才有可消费公开额度。身份模式未公开武将同样不把其私有势力纳入新public查询。旧livingFactionCount及旧相关技能默认保持。本稿只静态审查此边界，未做国战真实命令行为检查。
8. 转交由真正的source选择 victim HEJ。手牌用隐藏slot，装备/判定牌用其公开实体。发行后整个伤害已被防止，source死亡、victim死亡或原技能失效可取消尚未付款的得牌尾部，不能撤销已发行防伤或补还Game额度。付款完成后沿精确owning parent排清装备回复、gain/HP/Dying/death孩子；已经获得原实体不因尾部取消重复移动或再次付款。

上述默认均独立于classic自守的自目标限制；本稿没有复用其整个classic技能或改写旧内容。

## 规则来源

- [fenglin-twenty-fifth-626-source-2026-10-04.json](../sources/fenglin-twenty-fifth-626-source-2026-10-04.json)
