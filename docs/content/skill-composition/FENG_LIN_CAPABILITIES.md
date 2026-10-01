# 风林火山下一批公共能力契约

状态：2026-10-01冻结当前三国杀OL普通王平、陆绩、郝昭的官网正文与索引字段，三人已完成整合，父fresh构建、Core全量283/283；完整WPF仍因既有赤血青锋原牌面门禁失败，同次DLL其后37项通过，源码漂移为空。上一批2017八人已整合，详见[来源](../sources/fame-2017-2026-10-01.json)与[验证记录](../../benchmarks/2026-10-01-fame-2017.md)。本批当前官网分组为风林火山，不称原创2018。正文、HP、势力、原始字节SHA及推断见[本批来源](../sources/fenglin-2026-10-01.json)。

| 当前普通版 | 复用 | 新能力预留 |
| --- | --- | --- |
| [王平](https://www.sanguosha.com/hero/401)，蜀4HP | 真实HE付款、目标本人私选牌、移动与获牌子链 | 1260–1279：付款后相对区域数量选目标、opt-in程序目标声明、整局首次每目标记录 |
| [陆绩](https://www.sanguosha.com/hero/402)，吴3HP | 来源标记、真实失血、伤害防止、正常摸牌调整 | 1280–1299：指定参与者橘标记增减/付款、事件目标标记事实、每事件一次门禁 |
| [郝昭](https://www.sanguosha.com/hero/408)，魏4HP | 真实摸牌、受令者自己的弃牌选择、回合事件 | 1300–1319：实际回合结束后可挂起续接、两次延迟手牌对齐 |

## 王平

飞军恰好付款一张本人HE，先真实弃置并完成移动观察子链，再比较目标手牌/装备数量严格大于本人。只公布座位、数量和模式；目标自己选HE交牌或EQ弃置，不替他选择隐私实体。付款后无合格目标不退牌和次数，这是实现选择。声明目标后冻结资格；兵略摸二不能否定该声明，但后续仍核验存活、来源和实体。

仅新操作发布programTargetCommitted窗口，携带真实程序父帧、owner、sourceProgram/activation、target、mode与声明序号，不冒充CardUse。兵略是独立锁定trigger，必须检查其自身有效来源、压制状态与首遇事实；飞军声明账本先登记，即使兵略当时无效也不能把以后同目标误作首次。整局历史按owner/sourceProgram/activation/target保存并审计源实例，跨临时失去/重新授予继承。目标声明→兵略奖励子链→目标选牌是本批冻结的工程时序，并非已取得官方FAQ。

## 陆绩

游戏开始本人三橘；有橘的实际伤害目标消耗一枚并防止整次伤害；有橘的实际摸牌阶段主人多摸一张。遗礼在本人PlayStarting先选择失血或消耗本人一橘，再选其他存活者加一橘；整论仅本人无橘可跳过本次正常摸牌并加一橘。选择、伤害与正常摸牌均依真实父帧及subject，不能误用技能owner标记事实代替event target。

扩展既有来源标记，稳定按来源消费并保持aggregate/source合计一致。来源死亡沿用既有清理该来源贡献；压制/技能失去停止其保护和多摸效果，不能清除其他来源贡献。多个有效怀橘拥有者对同一实际伤害/摸牌subject只结算一次：需要显式通用事件作用域账本，并在live状态重新核验一枚付款，不根据旧capture重复扣除或多加摸牌。寿命与多来源是记录的工程选择。伤害防止不能防失去体力；使用真LoseHP和濒死子链，不以扣HP标量替代。

## 郝昭

Ending选一名其他存活者，建立当前本人回合结束和目标下个实际回合结束两项due。两个时点各自读取本人当前手牌数：不足时最多摸至五张，超出则弃到本人当前数量；本人8、目标6无变化，本人8、目标10弃二。单次delta在开始冻结，等待移动/获牌子链后不能无限重新对齐。

Due仅新配置在真实TurnEnded清理后、换currentSeat前进入可挂起父续接；旧无due路径保持既有事件/暂停顺序。包含翻面跳过及实际额外回合，不能用Ending高优先级或目标TurnStarted冒充actual end。重叠due按创建序，目标死亡/胜负终结/来源死亡或精确grant失效取消；到期时来源压制则消耗该due而不执行。该寿命规则与翻面时点是本批明确的实现选择，非官方已裁定。

## 公共工程与验收边界

从2017最终脏源码及裁剪测试构造同一SHA冻结baseline，各worker独立，不从Git HEAD还原。新增通用descriptor、资源校验、host、正常AI和JSON，禁止人物ID引擎分派；新增opt-in字段默认省略，旧内容不产生新事件、不放宽Processing、死亡/响应父链、隐私和移动约束。不改变全局版本来源，不复制当前版本数。正式检查夹具ModeId使用identity:classic-前缀。

聚焦检查覆盖真实付款与数量改变、正常AI、私有决策/所有observer、嵌套窗口、暂停checkpoint/accepted command JSON回放、非法输入原子拒绝。使用小固定fixture与现有filter，避免逐武将定义快照、seed搜索及恢复旧suite。各worker只做相关检查，不跑Full；父整合后统一WPF、素材、资料和一次适当整批验证，测量并保留失败。

## 已接入的公共接口与父定向验证

王平实际使用1260 `selectRelativeZoneDemandTarget`、1261 `drawOnFirstProgramTargetEncounter`，新1260 `programTargetCommitted`窗口及公开声明身份。资源`ReadCompletedActivationDiscard`证明单一本人HE费用、capture→完全弃置并等待→第三节点声明；首次记录跨源和奖励实例失去重获继承。仅显式`moveBoundCards.awaitMovementTriggers=true`新增允许OwnerHand，真实赠予后等待CardsGained，默认旧内容原路径不变。

陆绩实际使用1280 `changeParticipantMarker`、1281 `consumeMarkerPreventDamage`、1282 `addMarkerSubjectNormalDraw`，事件subject橘数事实及`ProgramMarkerEventAppliedEvent`真实父事件去重。`PlayerSnapshot.Markers`沿用既有公开Name/Count，来源总量始终一致。父修复既有通用标记付款prompt缺少SkillPrompt的展示问题，使付款选择进入共享WPF控件，不修改实际付款规则。

郝昭实际使用1300 `scheduleDeferredHandAlignment`、1301 `resolveDeferredHandAlignment`及opt-in `deferredTurnEndOnly`，真实TurnEnded之后的typed父续接、冻结单次delta、recipient私有实体选择；`PlayerSnapshot.DeferredHandAlignments`仅投影recipient公开待办，null省略。来源存在与enabled分离，压制不提前取消due。父补OtherLiving选人及owner/own Ending静态契约，保留精确runtime父帧、grant和游标核验。

三份交付依共同baseline逐SHA核对，17/16/18文件，三方冲突仅enum追加和生命周期facts构造两处，均合并全部分支。父Core定向33项（新行为20项及相关共享13项）、WPF实际标记/原生对齐私选两项通过；两个先前付款UI失败记录保留。离线render已检视，完整WPF与桌面实战不计为通过。入口、schema和包版本来源不变。

整批验证产物与边界见[本批记录](../../benchmarks/2026-10-01-fenglin.md)。Full后只补新反例模板必填optional与合法加载对照，生产源码不变；郝昭7项及composition4项fresh复验通过，不重复整批Full。
