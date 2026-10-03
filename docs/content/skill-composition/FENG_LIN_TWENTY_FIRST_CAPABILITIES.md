# 第二十一批共享能力合同与证据

本批已通过父级集成验证：Full Core632/632、WPF57/57；无过滤日常Core219/219、WPF18/18。运行记录保存在[实际台账](../../benchmarks/2026-10-04-fenglin-twenty-first-validation.json)，实际耗时和输入摘要见[本批记录](../../benchmarks/2026-10-04-fenglin-twenty-first.md)。规则解释见[工程口径](FENG_LIN_TWENTY_FIRST_RULINGS.md)。

| 能力 | 所有权与返回合同 | 当前新增真实证据 |
| --- | --- | --- |
| ActualDrawStageEnded / DrawPhaseObligation | 原实际回合、阶段、owner及对应摸牌帧；普通摸牌继承其实际rootless获牌批次ID，逐批完成再进入结束观察者；额外摸牌单独帧 | 徐晃4项通过；普通摸牌→两批实际获牌→回复→HP观察→结束、消费辎与新辎等待下一结束通过 |
| LeastHandMarkerOrDraw / 辎收益凭据 | 最少手牌并列合法；当前目标存活；已签发原实例归属保持；摸一张是真实子结算 | 已有辎/非最少摸牌、来源死亡、额外摸牌被跳过以及精确父返回通过 |
| NoDamageWholeTurnSupplyDistance / DamageApplied账本 | 真实整回合任意造成伤害取消距离豁免；失血不计；实际黑色HE转换付款 | 整回合伤害、失血区别及卸装备付款子链通过 |
| UnlimitedAlcoholUse / 原CardUse消费记录 | 未消费效果仍不可叠加；真实实体或转换酒只付款一次；实际酒杀原动作保存消费事实 | 同回合二次酒与两张杀、来源失效、回合到期、非酒增伤、被闪零实体杀通过 |
| SuppressOwnSkillAfterAlcoholSlashDamage | 当前原伤害候选、原杀动作及消费事实；明确命名自身技能，含锁定技；仅本实际回合 | 崩坏本轮失效与下一轮恢复、HP/MaxHP两个真实选择通过 |
| StartOwnedDamagePointJudgment | 当前AfterDamage候选、点序、原伤害父、所属主公实例及精确公共判定绑定 | 一次两点实际伤害→两次独立本人判定、回复/领取暂停、真实酒杀下原生AI主公通过 |
| Owned finalized judgment continuous subtree | 原判定公共实体/花色/成功结果，Finalized窗口当前候选及收益程序；只允许严格连续typed子链 | 判定实体留在真实手牌、外层不再弃置；真实领取→获牌失血→濒死→醇付款→虚拟酒→HP及完成观察→原伤害返回和冷回放通过。只移除8行修复的反证运行确实失败，随后恢复原字节再通过 |
| PreventCurrentDamageAndDrawMultiple / 4000 | 原BeforeDamage及防止值、实际摸牌账本和continuous子树；subject必须damageTarget | 精确双倍防止值、实际逐张获牌批次、第一次获牌失血→濒死→实体桃救援→获牌尾→原防伤返回通过 |
| DyingOthersNonLockedSuppression / ExclusiveTurnPeachUse | 当前actualTurn来源与innermostDying，源修订/HP/alive进入资格依赖；仅动态查询 | 外来非锁定即时失效、当前濒死者和锁定豁免、窗口期间实际新增外来技能的资格、关闭后恢复通过 |
| ProhibitBlackTrickTarget | 原动作实际颜色、角色克隆与真实闪电转移实体；0实体无花色占位不推断黑色 | 黑色无显式目标的摸牌锦囊按实际使用者检查、混合实体颜色、零实体无花色、闪电实际原实体转移通过；旧刘备角色克隆检查改为实际实例调用，11项旧行为通过 |
| RequestLegalSlashByNearest / 4002 | 冻结每参与者最近集合，答复重验；原付款者/转换实例、激将结果及一次失血续点；仅主动出牌程序 | 原生、转换、丈八和激将实体付款；无人供牌后的重发请求、来源独立距离豁免；已付杀进入实际CardUseCommitted后技能失效仍完成该杀，取消未签发尾部通过 |
| OfferUnlimitedVirtualSlash / 4003 | owner实际出牌阶段与0实体动作；免距离/次数扣减，保留实际用牌历史；仅主动出牌程序 | 可选末尾0实体杀、真实用杀与出牌阶段历史、原生AI读取已提交起始历史；两种操作的非法触发式loader组合拒绝通过 |

四视角隐私、实际卡移动账本、拒绝无效输入不改状态以及检查点冷回放在新增行为检查中保留。直接DrawPhaseObligation生产HP事件的返回、3901来源死亡/胜利、4000酒救援或来源死亡，以及部分死亡观察尾部目前只有静态审查，不标为本批新增真实用例。旧兼容数据、movement边界及历史指纹路径不因测试规模而删除。
