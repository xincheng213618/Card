# 界徐晃、界董卓、界贾诩共享能力

项目采用的正文与工程默认见[来源档案](../sources/fenglin-twenty-first-preflight-2026-10-03.json)。

| 能力 | 所有权与返回合同 |
| --- | --- |
| ActualDrawStageEnded / DrawPhaseObligation | 原实际回合、阶段、owner及对应摸牌帧；普通摸牌继承其实际rootless获牌批次ID，逐批完成再进入结束观察者；额外摸牌单独帧 |
| LeastHandMarkerOrDraw / 辎收益凭据 | 最少手牌并列合法；当前目标存活；已签发原实例归属保持；摸一张是真实子结算 |
| NoDamageWholeTurnSupplyDistance / DamageApplied账本 | 真实整回合任意造成伤害取消距离豁免；失血不计；实际黑色HE转换付款 |
| UnlimitedAlcoholUse / 原CardUse消费记录 | 未消费效果仍不可叠加；真实实体或转换酒只付款一次；实际酒杀原动作保存消费事实 |
| SuppressOwnSkillAfterAlcoholSlashDamage | 当前原伤害候选、原杀动作及消费事实；明确命名自身技能，含锁定技；仅本实际回合 |
| StartOwnedDamagePointJudgment | 当前AfterDamage候选、点序、原伤害父、所属主公实例及精确公共判定绑定 |
| Owned finalized judgment continuous subtree | 原判定公共实体/花色/成功结果，Finalized窗口当前候选及收益程序；只允许严格连续typed子链 |
| PreventCurrentDamageAndDrawMultiple / 4000 | 原BeforeDamage及防止值、实际摸牌账本和continuous子树；subject必须damageTarget |
| DyingOthersNonLockedSuppression / ExclusiveTurnPeachUse | 当前actualTurn来源与innermostDying，源修订/HP/alive进入资格依赖；仅动态查询 |
| ProhibitBlackTrickTarget | 原动作实际颜色、角色克隆与真实闪电转移实体；0实体无花色占位不推断黑色 |
| RequestLegalSlashByNearest / 4002 | 冻结每参与者最近集合，答复重验；原付款者/转换实例、激将结果及一次失血续点；仅主动出牌程序 |
| OfferUnlimitedVirtualSlash / 4003 | owner实际出牌阶段与0实体动作；免距离/次数扣减，保留实际用牌历史；仅主动出牌程序 |

配置字段、操作和入口能力见[当前技能配置](RUNTIME.md)；程序续接和帧权限见[结算协议](../../RESOLUTION_PROTOCOLS.md)。
