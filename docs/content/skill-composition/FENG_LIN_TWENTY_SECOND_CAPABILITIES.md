# 界典韦、界蔡文姬、界张春华共享能力

界典韦、界蔡文姬与界张春华采用[来源台账](../sources/fenglin-twenty-second-preflight-2026-10-04.json)选定的普通 OL 正文。

## 实际回合目标与受伤后收益

4100 ReceiveOwnerDamage支付真实无来源伤害，进入通常的伤害、濒死与死亡链。4101 ConsumeDistinctTurnTarget在付款前冻结技能实例、内容指纹、实际回合及出牌阶段和目标；两种强袭分支共享阶段额度，同回合额外出牌阶段仍排除此前指定的目标。来源死亡保留已付成本和目标记录，取消尚未执行的目标伤害。

新增EventTargetDamageInstancesTakenThisTurn读取拥有该候选的实际伤害窗口冻结值。一笔两点伤害仍是一笔受伤；先行观察者引发第三笔伤害不能改写原第二笔候选。4102 DrawOwnerAtAppliedDamage仅接受严格的AfterDamage两指令结构：真实摸牌，再选择受伤目标装备或判定区的牌弃置。实际获牌、装备移除及回复观察返回原父帧，来源失效时保留已摸牌并取消未付弃牌尾部。

## 判定后可选支付与独立匹配领取

4200 JudgeDamageTargetThenOfferSuitDiscard只能是optional/any/AfterDamageApplied/PerDamage绑定的唯一指令。它锁定实际杀类伤害、用牌、窗口、候选、技能实例和点序号，在受伤者真实判定并完成最终结果观察后，才让持有人选择真实HE牌支付。

有CardAction的用牌保留精确ActionId验证。既有神速Action=null的零实体杀，以实际CardUse/attack及直接暂停的UseVirtualCard生产帧证明；冻结TargetsConfirmed验证原选择，当前use/attack验证实际受伤者，不补写旧Action。

原帧保存标量判定、成本及两种领取移动回执。判定花色和点数在最终结果时冻结，支付牌使用支付时的有效花色和物理点数。红桃回复、方片摸牌、梅花来源弃两张、黑桃来源翻面走真实子窗口；同花色只取仍在Processing的本判定实体，同点数只取仍在DiscardPile的本支付实体。其他观察者已取得的牌不再被抢回或再次清理。

来源在支付前失效取消支付，只清理本判定；支付后保留原子效果和领取尾部，按死亡、游戏结束及实体存活约束收束。描述符声明内部claim-or-cleanup最终消费临时结果，保留共享组合验证器的全消费检查。断肠直接复用现有死亡来源失技能能力。

## 双私密选色、原子支付与实际决斗

4300 ChoosePrivateColorsDiscardAndDuel是零激活材料、一个其他目标、出牌阶段一次的单指令能力。双方私密选色依次收集，第一次承诺不产生可供第二人推知的公开颜色；双方完成后冻结各自有效颜色对应的实际手牌实体集合。

全部匹配牌以一个混合持有人原子移动批次支付，然后才运行失牌、回复、HP及伤逝获牌观察。胜负使用已付实体数量，不受收益后的新手牌影响。较多的一方以typed ProgramDualColorDuelOrigin签发真实零实体决斗，保留通常用牌、无懈、伤害及完成流程；平局无决斗但消耗本次额度。公开事实只含标量，草稿嵌套集合及四视角选择参数冻结。

配置字段、操作和入口能力见[当前技能配置](RUNTIME.md)；程序续接和帧权限见[结算协议](../../RESOLUTION_PROTOCOLS.md)。
