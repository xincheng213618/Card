# 界陈宫：当前规则与执行入口

当前人物 ID 为 `boundary:chen-gong`，势力 `qun`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/chen-gong/boundary/Content/boundary-chen-gong.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:mingce-current` | 明策 | 出牌阶段限一次，你可以交给一名其他角色一张【杀】或装备牌，然后其选择一项：1.视为对你选择的另一名角色使用一张【杀】，若造成伤害，执行另一项；2.你与其各摸一张牌。 |
| `classic:zhichi` | 智迟 | 锁定技，当你于回合外受到伤害后，本回合【杀】和普通锦囊牌对你无效。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:mingce-current` | `active / offer-command` | `selectOwnedCards`、`giveBoundCardThenOfferVirtualSlashOrSharedDraw` |
| `classic:zhichi` | `afterDamageApplied / outside-turn-immunity / perDamage` | `grantTurnCardEffectImmunity` |

正式规则文件：

- [boundary-chen-gong.rules.json](../../../src/Characters/chen-gong/boundary/Skills/boundary-chen-gong.rules.json)
- [classic-chen-gong.rules.json](../../../src/Characters/chen-gong/Skills/classic-chen-gong.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

## 工程默认

1. The selection uses printed Slash/equipment identity and the already-issued intrinsic hand Alcohol-as-Slash policy. Optional Wusheng or other selectable ViewAs conversions are not intrinsic entity identity. Nontransferable general weapons are excluded only in the new 5100 selection/payment path. Historical classic selection remains unchanged.
2. The recipient's actual ordinary Slash target eligibility, distance and prohibitions reuse `GetProgramVirtualSlashOfferTargets`, plus the existing colorless card-target prohibition. The recipient cannot target itself. The skill owner may choose itself when ordinary eligibility permits. If no legal Slash target remains before an option is offered, only the shared-draw option is published. No new range/number exemption is invented.
3. The gift is committed before recipient choice. If the exact source instance, owner or recipient becomes invalid before a choice/use is accepted, only the unissued continuation is canceled. Once the real use has been accepted, its full completion and already-entitled living participants' rewards retain their exact origin despite subsequent source suppression.
4. Rewards are issued owner then recipient. This order and count-zero handling for dead characters or a determined winner are explicit implementation defaults; the supplied official source does not include a complex FAQ deciding those nested cases.
5. Source/prompt/card legality is revalidated before payment or issuance; payment material is never reconstructed from later current cards. Temporary card identity does not change the physical gift's printed kind.

## 规则来源

- [fenglin-twenty-fifth-597-source-2026-10-04.json](../sources/fenglin-twenty-fifth-597-source-2026-10-04.json)
