# 界卧龙诸葛亮、界曹植、界郭淮共享能力

项目采用的正文与工程默认见[来源档案](../sources/fenglin-twenty-third-preflight-2026-10-04.json)。

## 随机展示、有效颜色成本与精确无懈节点

4400 RandomRevealColorFireAttack仅覆盖声明该政策的火攻。真实use在材料移动前冻结来源，目标真实手牌由运行时随机源展示一张；展示以前不将手牌发布给其他视角。成本按当时有效颜色选真实手牌或装备牌，原CardUse帧保存展示和支付标量回执。装备移除引发的回复、HP及移动观察先结清，随后原火攻伤害并清理实际实体。旧普通火攻与classic:huoji流程保留。

4401 UnrespondableNullification在付款前冻结精确来源与材料。原NullificationWindow保存已付ActionId、链深度和父CardUse归属；付款产生的回复、HP、获牌及committed/accepted/completed response-use子窗返回以后，只结束这一次无懈节点。原锦囊的EffectNullified依真实链深度返回；此前已经发生无懈时，当前节点可能恢复原效果。629通过可选partial接口提供本人的真实领取实体来源，空材料不能取得这一资格。

4400 OwnerTrickUsesThisActualTurn以实际用牌和实际无懈使用记录标量历史。无懈的历史Action语法为Response，但规则上计锦囊使用；普通杀、闪响应不计。藏拙读取当前实际回合的本人记录，复用既有3101手牌牌种豁免与到期机制。八阵直接复用classic:bazhen，不复制人物定义检查。

## 弃牌来源、持有实体与零实体酒

629已集成4500 ClaimDiscardedEntityWithProvenance、4501 UseVirtualAlcohol、4502 OfferFaceUpForOutsideClaims。实际弃置或判定进入弃牌堆冻结单实体来源，领取走真实移动和获牌子窗口。标签依原来源实例、原实体claim与随后持有轨迹证明；背面使用本人的标签实体时冻结不限距离与不能响应，实体离开本人HE/木牛流马粮区终止标签。声明转换的例外只承认准确成功声明的尚未消费付款回执、对应Hand→Processing移动及原producer；任意Processing实体不能借此恢复标签。无懈复用当前精确节点政策，不能把原锦囊整体设置为不可响应。

酒诗先完成真实翻背及翻面观察，再签发零实体酒；原程序保存已付成本和用牌返回，恢复不重复翻面。回合外领取数量按实际回合段、来源实例及当前体力上限判断，实际翻正结束该段。

## 实际回合花色与有效牌类

658已集成公共标量actual-use历史与4600数值表达式，复用普通Draw及其typed获牌返回。本人手牌的真实use读取冻结EffectiveSuit，一种花色只贡献一次；有效Basic、Trick、Equipment类别独立去重。零实体真实use可以计类别但不制造手牌花色；旧神速Action=null保留原Action合同，使用真实producer归属；无懈计实际使用，普通杀/闪响应不计。每个真实PlayEnding读取该实际回合的冻结类别数，额外出牌阶段共享历史，实际新回合重新计数。

精策的手牌上限读取当前有效技能实例；失技或抑制时贡献停止，已经发生的实际使用历史保留。该连续数值口径为工程默认。

## 青龙与赠牌交叉

已有经典忠勇在杀完成后发布尚未选领取者的私密提示，青龙追加杀将该提示保存在原CardUse的typed continuation。新父链证明只承认连续的原完成用牌、准确候选窗口、原赠牌Program和原使用者的同目标追加杀，核对冻结提示、完整选择、实例、hash、实际声明与一次付款。暂停时保存原状态，再清除实时提示，防止新AI闪继承人类应答状态；恢复原提示时还原原状态。

领取者可以立即使用刚领取的同一张红色杀。新证明要求准确赠牌事实、原Program的直接typed use返回、真实领取者和付款；旧完成窗口及Processing一致性只按这张实际再用实体处理，不容忍任意子帧或任意残留。原提示按精确 PromptId 和原状态恢复；领取者的实际再用牌完成后回到原赠牌程序，材料只支付一次。

配置字段、操作和入口能力见[当前技能配置](RUNTIME.md)；程序续接和帧权限见[结算协议](../../RESOLUTION_PROTOCOLS.md)。
