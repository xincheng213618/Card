# 判定、伤害、回复与死亡

所有这些操作进入原生结算帧并沿类型化父返回继续。技能字段的完整合法组合由当前解析器校验，入口见[运行时](RUNTIME.md)。

## 判定与改判

`startJudgment` 显式指定判定主体、稳定 `judgmentReason` 和必填的 `resultBind`；即使后续步骤不消费结果，也需要该绑定。`visibility` 必填且只接受 `public`，最终牌事实以公开绑定保存。技能拥有者、判定发起者和判定主体是不同对象。原牌或响应父帧等待完整判定、改判、最终结果和后续子结算后再继续。

`judgmentReplacing` 触发器可订阅 `owner` 或 `any`，以 `excludedReasons` 排除稳定原因。`replaceJudgment` 的 `zones` 显式限定手牌/装备成本，`suits` 限定选牌时的有效花色，`oldCardDestination` 指定旧判定牌去弃牌堆或拥有者手牌。

替换先整体验证旧/新实体及来源，以原子移动建立新的有效判定，再处理装备/手牌离场后果。不能让子窗口观察到成本已经离开、判定却仍指向旧牌的状态。改判候选按统一排序冻结，私有选牌仅向响应者发布。

`judgmentFinalized` 读取改判后的有效花色、点数及最终实体。可用 `judgmentReasons`、`excludedReasons`、`judgmentSource` 等显式过滤；中间被换走的牌不触发最终结果收益。判定结果绑定用于后续领取、伤害或直接死亡，不能在牌已被其他窗口领取后重复移动。

正式示例见[经典张角](../../../src/Characters/zhang-jiao/classic/Skills/classic-zhang-jiao.rules.json)、[界张角](../../../src/Characters/zhang-jiao/boundary/Skills/boundary-zhang-jiao.rules.json)和[国战张角](../../../src/Characters/zhang-jiao/national/Skills/national-zhang-jiao.rules.json)。三者的主体、分支、旧牌去向及贡献过滤分别定义。

## 伤害事实与触发

伤害先经过其原生前置修正、替代与防止，再提交真实扣减和相应事实。触发器用 `subject` 区分受伤者、伤害来源或其他允许角色；`damageOccurrence` 区分一次伤害与每点伤害。伤害性质、实际来源、实际量、动作材料和原生因果身份不能由最终 HP 差倒推。

`damageAppliedBeforeDying` 是受控的伤害已落地、濒死前窗口，不能任意开询问或新伤害。原生濒死/救援/死亡完成后，伤害后窗口按已冻结和当前仍有效的资格继续。具体专门能力可保留致死前标记等精确合同；不得把所有伤害后技能都提前到濒死之前。

距离、装备、目标体力或牌种等需要在伤害发生时判定的事实由拥有帧冻结。死亡后的存活座位环或装备变化不应反向改写已成立的伤害资格。嵌套伤害保留外层游标，子链完成后只恢复一次。

## 回复与体力变化

`recover`、`recoverTo`、`loseHp`、上限变化与伤害分别进入对应流程，不能互相替代。实际回复受目标状态和上限约束，回复被替代或触发 HP 子窗口时，原回复收据留在其拥有帧。

用牌回复修正按真实用牌者冻结，普通、转换及零实体桃均保留同一动作的回复收据。濒死酒按实际饮酒者归属；外部材料提供者不把自己的修正传给使用者。[战意](../../../src/Characters/zhu-ling/ol/Skills/ol-zhu-ling.rules.json)通过显式 `recoveryBonus` 与到期策略使用该能力。

无有效体力值的隐匿状态不能作为普通受伤/回复目标或以内部数值参与上限计算。实际登场、首次扣减及其子流程按对应原生合同处理。

## 死亡与牌区清理

`characterDied` 提供精确死亡事实及允许的死亡牌区清理窗口。`claimDeathCleanupCards` 只申领该死亡流程中仍可取得的实际遗留实体，不扫描弃牌堆或复制另一候选已取得的牌；普通暗手牌仅向获得者开放牌面。

拥有者死亡技能在 `ownerDied` 等明确窗口保留合法控制者和选择主体。`maximumAttributedMarker` 只选择该来源归属、仍存活且标记为正的最大并列角色；公开总数不暴露内部来源桶。

`causeDeath` 或 `causeDeathUnlessBoundCardKind` 进入统一死亡帧，不制造 Damage、失去体力、求桃或击杀来源。嵌套死亡技能按父链返回；游戏已结束时只完成必要的原生收尾，不再发行未来选择。参考[当前武魂规则](../../../src/Characters/guan-yu/Skills/nightmare-death-skills-v53.rules.json)。

角色死亡、技能失效或游戏结束时，已经支付和已经发行的子树按各自合同完成，尚未发行的后续效果重新检查资格。全局“死亡就取消整个程序”会丢失已付义务。
