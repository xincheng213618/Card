# 技能程序 schema 53：目标集合、私密置底与伤害参与者

本批对应 rules 163、`standard-classic-generals@1.139.0`，Checkpoint schema 仍为 3。顾雍（2014 原版）及李典（2014 身份版）作为首批消费者。全量结果和真实开发耗时见 `docs/benchmarks/2026-09-26-new-generals.md`，不能把定义加载等同于行为验收。

## 可复用能力

- `selectTargets` 的 `anyLiving` 包含自己；`numberExpression: currentHandCount` 在选择开始时限制人数，结合显式最小／最大值。当前桌面支持最多八人，最大值 8 是该选择器的桌面人数边界。`draw` 的 `selectedTargets` 对选定的仍存活角色逐人执行；顾雍先选目标、公开绑定的整手牌，同色才摸牌。AI 按公开关系评分，不读取他人的暗手牌。
- 摸牌阶段的 `moveBoundCards` 支持 `drawPileBottom`，私密顺序选择必须是仍处于绑定来源的完整排列。恂恂私密看四张、取两张、剩余置底；零／一张剩余牌无需制造顺序选择。`selectCardSubset.allowFewerWhenInsufficient` 是显式选项，默认 false，恂恂开启以处理牌源不足；不改变其他子集选择的最低张数。
- `afterDamageApplied` 的 `damageSource` 主体用于造成任意伤害的拥有者；`owner` 用于承受伤害的拥有者。`perDamagePoint` 分别产生可接受／拒绝的机会。`otherDamageParticipantAlive` 只在对方存在、不是自己且仍存活时为真。
- 伤害后固定量 `draw` 可声明 `targetRef.kind: eventSource / eventTarget`。JSON 仍用 `target: owner` 占位，不能同时声明动态数量、结果牌绑定或目标集合；解析器拒绝含糊组合。事件参与者取自当前伤害事实，不依赖卡动作窗口，也不能冒用别的事件的参与者。

## 伤害顺序

伤害扣减体力后，先执行 `damageAppliedBeforeDying`，再完成濒死／救援／死亡，最后开启 `afterDamageApplied` 收益窗口。前置窗口只允许强制的拥有者归属标记操作 `changeAttributedMarker`；不能询问、摸牌或再造成伤害。当前武魂用该窗口记录致死伤害的梦魇标记，死亡后的判定仍通过已有拥有者死亡窗口执行。

不能仅以 `Hp > 0` 判断忘隙候选：这会漏掉成功获救者，或错误处理仍存活的零体力角色。伤害对象完成濒死后再读取其存活状态；同一 Damage 帧记录救援已结算，避免再次打开相同濒死循环。狂骨所需距离在伤害发生时冻结，避免死亡后座位环和装备变化影响判断。

刚烈的嵌套伤害使用相同两阶段窗口。外层技能及触发游标在内部结算期间保留，内部完成后只恢复一次；内部伤害技能和判定仍必须属于当前嵌套攻击并满足帧栈父子关系。不能通过撤掉不变量校验来绕过真实续接错误。

## 版本与证据

1.138.0 及更早的武魂资源保持原文和指纹；1.139.0 选择独立的 `nightmare-death-skills-v53` 资源。旧枚举值不重排。开发存档只接受当前规则版本、内容签名、内容哈希和 Checkpoint schema 全部精确匹配，不承诺迁移旧开发存档。

定向覆盖顾雍重复支付、自己／多目标、同色／混色／空手、失效目标；李典私密取牌、置底顺序、牌源不足、双向逐点触发、致死及救回、刚烈嵌套、暂停／完成回放；武魂致死标记及狂骨近／远距离击杀。引擎目前没有可运行的无来源伤害生产路径，不将静态排除说明写作无来源动态测试通过。

资料锁定：[顾雍官方原版说明](https://www.sanguosha.com/news/20161121_8378_3715)、[李典官方原版说明](https://www.sanguosha.com/news/20141017_5727_5513)。伤害时序参考的[规则流程重印](https://gltjk.com/sanguosha/rules/flow/damage.html)属于第三方重印，不是官方域名。新武将继续优先复用本批能力；纯内容新增不逐人递增规则、schema 或包版本。
