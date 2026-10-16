# 公共时机与规则上下文

技能通过 `SkillProgram` 的窗口、条件和操作组合。当前入口是否提供某种上下文由 `ProgramEntryCapabilities` 声明，具体条件与操作还须通过加载器、descriptor 和组合校验；枚举名称不能代替真实流程接入。

## 用牌与响应

| 窗口 | 当前含义 |
| --- | --- |
| `cardUseCommitted` | 使用已接受后的规则窗口，携带冻结的 `CardActionContext` |
| `cardUseTargetsFinalized` | 最终目标确定后的窗口，读取本次有效牌身份、真实指定目标及来源 |
| `cardUseBeforeTargetEffects` | 当前目标效果前的适配窗口，可由相应能力使本目标效果无效 |
| `cardEffectBeforeApply` | 所属牌效果应用前的规则入口 |
| `slashTargetRedirecting` | 杀的目标转移入口 |
| `slashBeforeResponse` | 本次杀目标响应前的规则入口 |
| `slashFullyDodged` | 本次杀按原生规则被完整闪避后的入口 |
| `cardResponseAccepted` | 响应被接受后，材料和转换来源已经确定 |
| `cardResponseCompleted`、`cardSupplyCompleted` | 响应或供牌完成，按原生返回协议恢复所属请求 |
| `cardUseCompleted` | 整张使用完成，携带冻结动作及实际伤害结果 |

`CardActionContext` 保留动作 ID、父动作 ID、动作类型、实际使用者、材料提供者、请求/响应角色、有效牌名、目标、实体原位置及完整转换链。真实使用、单纯打出和向他人提供响应材料由原生流程区分，不能按弹窗是否属于“响应”统一判定。

牌种通过自身适配打开窗口并保存类型化继续点。普通基本牌、装备、杀、锦囊、延时牌及响应有不同原生流程；新增条件或操作需核对相应牌种的实际接入位置。用牌目标、真实指定目标与逐目标效果分别保存，不能把当前目标窗口直接当作所有牌种的完整阶段屏障。实现见 [用牌生命周期](CARD_USE_LIFECYCLE_DESIGN.md)。

## 伤害、体力与角色状态

| 窗口 | 当前事实与边界 |
| --- | --- |
| `beforeDamageApplied` | 原生伤害应用前，可使用已实现的防止、转移及数值能力 |
| `damageAppliedBeforeDying` | 实际伤害扣血后的受限事实窗口，位于后续濒死之前 |
| `afterDamageApplied` | 按伤害触发策略进入收益窗口，可声明按次或按点机会 |
| `afterHpLost` | 实际失去体力及变化前后数值，按失去体力流程续接 |
| `afterHpRecovered` | 实际回复量、变化前后体力及回复来源 |
| `afterHealthChanged` | 已实现的体力变化订阅入口，按所属变化帧推进 |
| `characterTurnedOver`、`characterTurnedFaceUp` | 原生翻面或恢复正面后的对应窗口 |
| `characterEnteredChain` | 角色实际进入连环后的窗口 |
| `dyingEntering`、`dyingEntered`、`dyingExited` | 濒死流程各自规定的边界 |
| `selfDyingResponse`、`dyingResponse` | 自救或当前响应者的濒死救援机会 |
| `ownerDied`、`characterDied` | 死亡窗口，由死亡帧保留来源、清理材料和父返回 |

伤害与失去体力分别记录。伤害上下文保留来源、目标、属性、请求量、实际量、来源牌/技能及连环、转移关系；防止伤害不产生实际伤害收益。体力变化的原事实与救援后的当前体力也分别读取。回复满血而没有实际变化时，不以请求量制造实际回复收益。

技能抑制和封牌由具体政策参与有效技能、牌动作、牌区和目标查询；当前已有非锁定技能抑制、实体限制、手牌颜色/类别限制及定向目标禁令等能力。新技能仍须核对政策的对象、动作类型、期限及来源规则，不能仅凭禁止响应视为其他技能失效。

## 牌移动与获得

`cardsMoved` 按来源区域观察移动，`cardsGained` 按声明的手牌区或装备区目的地观察进入，`discardPileReceived` 提供进入弃牌堆的对应事实。实体在自己的手牌与装备间移动时，区域变化与所有权变化分别判断。装备区进入支持按批次或逐牌；依赖手牌获得阶段资格的配置仍按手牌规则校验。

| 配置或事实 | 当前契约 |
| --- | --- |
| `movementOccurrence` | 支持按原子批次、实体牌、来源角色或拥有者整批等粒度；第三方得牌、拥有者来源得牌及他人失去最后手牌等粒度具有专门窗口/操作约束。一次上层摸牌可能产生多个批次 |
| `movementReasons`、`excludedMovementReasons` | 精确匹配账本的 namespaced 原因；白名单与黑名单不重叠 |
| `ignoreOwnSkillMovements` | 按拥有者、技能及技能实例排除本实例造成的移动 |
| `movedCardCount` | 本次经过拥有者、区域和原因过滤的实体张数 |
| `sourceZoneCountBefore/After` | 来源区在本批移动前后的数量 |
| `destinationZoneCountBefore/After` | 目的区在本批移动前后的数量 |
| `hpChangeOccurrence` | 体力变化按事件或按实际点数产生机会 |
| `hpChangeAmount`、`hpBeforeChange`、`hpAfterChange` | 原体力变化事件的冻结事实 |

移动与体力子窗口由所属帧和指令返回协议决定续接。程序内需要等待子触发的操作在下一条指令前恢复；原生用牌和其他流程在自己的安全边界推进，不能假设每次底层移动都会同步弹出选择。

## 候选、暂停与内容校验

窗口打开时冻结候选身份、条件事实和出现游标；暂停后复查存活、技能实例、限次以及当前支付/目标资格。子事件完成后恢复原事件。可能递归的得牌摸牌、继续失血等组合须按具体规则声明原因、条件或次数限制。

窗口由原生机制提供能力与父返回，操作由 descriptor 声明资源要求。内容检查同时覆盖完整上下文、有限选择、费用单次提交、私密选择、公开事实、材料清理和命令重放。开发通过名称过滤验证受影响行为，完整验证按用户明确要求执行。

## 源码入口

- [入口能力](../src/CardGame.Core/ProgramEntryCapabilities.cs)、[规则模型与加载器](../src/CardGame.Core/SkillPrograms.cs)
- [动作上下文](../src/CardGame.Core/CardActions.cs)、[用牌生命周期](../src/CardGame.Core/GameEngine.CardUseLifecycle.cs)
- [候选驱动](../src/CardGame.Core/ProgramTriggerWindowDriver.cs)、[窗口返回](../src/CardGame.Core/GameEngine.ProgramWindowReturns.cs)
- [牌区与移动](../src/CardGame.Core/CardZones.cs)、[规则观察](../src/CardGame.Core/ProgramRuleObservers.cs)
