# 王基、蒯越蒯良、卢植共享能力

项目采用的普通 OL 正文及工程解释见[来源档案](../sources/fenglin-fourth-2026-10-02.json)。

## 当前操作

| 能力 | 内容与状态合同 |
| --- | --- |
| DiscardNonFinalTargetCardThenDraw | 奇制在本人回合非装备牌最终目标之外选有合法 HE 牌的角色，可选本人；实际弃一后向同一角色摸一，一次使用只产生一次机会 |
| DiscardHandToNamedTurnCount | 进趋摸二和获牌子链完成后读取本回合奇制计数，弃手牌至该数量 |
| GiveSelectedOwnedCardAndDamage | 审时阳向其他最多手牌角色赠真实 HE 牌，再伤害；只有该次伤害实际致死才提供摸至四收益 |
| ObserveDamageSourceHandAndGive | 审时阴只向拥有者私下展示实际伤害来源手牌，赠牌后保留本回合结束阶段的条件义务 |
| DrawToHandCount | 按目标当前手牌与声明数量执行真实补牌 |
| StoreBoundHandInPublicPile | 明任在开始摸牌及子链后，将精确手牌存入本技能来源的容量一公开牌堆 |
| PublicPileColorDamage | 贞良阳读取同来源明任颜色，弃有效同色 HE 牌后对攻击范围内目标造成伤害 |
| RewardDiscardedActionColor | 贞良阴把真实使用/打出材料与进入弃牌堆事实关联，匹配有效颜色后提供收益 |
| AwaitOwnedCardMovement | 绑定操作的真实移牌子结算返回原程序，付款与实体不重复 |

通用转换入口通过 GetProgramConversionPolarity(ownerSeat, skillId) 读取 SkillRuntimeStateStore，并由 CommitProgramConversionPolarity(frame) 提交一次翻转及带来源和父帧的事实。同名技能按 owner + skill 共享转换状态；卢植和蒯越蒯良消费相同能力，承略使用自己的已提交事实路径。

奇制有效接受并承诺合法选择后本回合计数一次，按owner+named skill聚合，不因技能失去/重新授予或额外Play重置，多来源同一动作不重复。进趋明确引用奇制的owner+named skill回合计数；来源和每次事件保留instance身份，但不用重复grant倍增收益。

审时阳使用一次技能在本Play限次，阴不占这个主动阳次数；阴翻回阳后本Play仍不能再次阳发动。成功赠入手牌后发出的当前结束阶段条件已成为结算义务，不因之后失去/压制源技能撤销；拥有者死亡取消，受赠者死亡导致实体离手视为失败。跳过Ending时在实际TurnEnding失效且不奖励；正常Ending按既有触发次序到期一次。赠入后的任意真实离开该hand都使条件永久失败；离手再返回不恢复资格。它是有类型的延迟结算记录，不能留下跨回合活跃ProgramSkillFrame或独立cardId/useId字典。private observation必须只有本人可见，公开事实不能带对方手牌身份。

贞良颜色沿已有CardAction有效颜色：多实体同色虚拟牌一次动作仅奖励一次，混色不明确则不匹配；若真实实体从使用/打出动作进入Discard，入堆时触发，即便后续被别的技能取得仍是已发生移动。从未实际进入Discard者不触发。非使用/打出（包括重铸、任交换、普通弃置）不触发。明任与贞良跨技能来源依照PublicPileSources的same-grant/source映射，不抓owner任意第一堆。已付款颜色与参与实体在所属typed draft冻结，子链返回不重付。

配置字段、操作和入口能力见[当前技能配置](RUNTIME.md)；程序续接和帧权限见[结算协议](../../RESOLUTION_PROTOCOLS.md)。
