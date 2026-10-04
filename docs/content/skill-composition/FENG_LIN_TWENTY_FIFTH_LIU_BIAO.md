# 第25批界刘表静态交付

界刘表626，普通OL当前正文的完整暂存实现。没有编译、运行测试或发布；主区源码未改。

来源复用 docs/content/sources/fenglin-twenty-fifth-626-source-2026-10-04.json。官网 hero/626 和 API gid=626 均为 HTTP200，原始字节 SHA、时间、正文及群3体力已在父源台账冻结。本稿没有再次抓取网页或媒体。男性身份沿用仓库既有刘表身份补充；当前 API 未提供性别字段。

自守新增5200 DrawExtraAndArmTurnDamageUseDebt 与5201 SelectTurnDamageUseDebtPayment。5200只能作为可选、加法、自己摸牌阶段开始的单节点。真实 DrawCards 账先支付，标量 receipt 挂在当前 ProgramSkillFrame；每牌一个真实 movement batch，gain/HP/Dying 子链逐边返回同一原程序，然后继续正常摸牌。首次额外摸牌产生的公开标量事实按 actualTurn/source instance/hash 查询结束阶段债务，不另存 pending 或 use-ID 表。

5201只能组成强制自己的 TurnEnding 三节点：冻结当时公开势力数并选HE → 原子 MoveBoundCards discardPile（awaitMovementTriggers=true）→ AwaitBoundCardMovements。每张牌的私有选择先齐，再真正付款；HP、移动、濒死孩子先返回，后续资格失效仅取消剩余程序。已发行付款标量事实阻止同一实际回合/原实例重复付款。

实际伤害牌 Use 在精确 CardUseDeclared owning frame 捕获。物理、转换与具有真实 typed producer 的零实体使用读取冻结 effective kind；普通 Slash/Dodge Response 不计。新伤害牌集合仅新能力使用，没有修改 MouLuMeng 等旧谓词。无懈是实际锦囊使用，但不属于本能力伤害牌。

宗室新增5202 PreventDamageAndConsumeSourceFaction。仅强制 damageTarget BeforeDamageApplied 两节点：在5202发行时查询实际公开有效来源势力 → existing SkillRuntimeState Game exact usage key 消费一次并防止当前整个伤害 → 成熟 source chooser/owner victim 的 HEJ 单实体转交。真正来源已阵亡仍可发行防伤和势力额度，得牌尾部随后取消。真实传导目标仅为5202增加 rangeChainOnly opt-in gate；旧技能没有变化。

HEJ转交复用 SelectAndMoveOwnedCard。别人手牌以 slot 提示，不给 source 的AI手牌实体ID；所选实体真正 HEJ→Processing→source Hand，装备回复和双方移动/gain观察者先结清再归原 damage parent。新观察者只认 exact receipt、当前candidate的owner/skill/binding/instance/hash、原指令、真实ledger及连续parent ancestry；进入Dying的那条边先证明，再复用成熟 boundAlcohol whole-tail。零实体或任意 frame presence 不能替代这些证明。

新 SkillRuleValueExpression.PublicLivingFactionCount=5200 只用于 additive handLimit；新公共查询、上下文和AI估分均按公开有效势力。旧 LivingFactionCount 没有改动。新增事件和receipt全为标量，没有新暴露集合；所有选牌集合、CardSet与事件沿用现有 prepared view/CommittedEventProjection 冻结路径。

四项必要行为检查（完整真实命令稿；尚未运行）：

- ExtraDrawOwnsGainChildAndEndingDebtFreezesNewFactionCount：真实额外draw4→gain→recover→HP暂停，死亡导致 ending X=3，真实物理杀Use，私有三张选择先冻结后付款，四视角cold、拒绝无效选择与成本一次。
- OrdinaryPaidSlashResponseDoesNotBecomeDamageCardUse：selected foreign actor真实零实体Duel，真人实体Slash响应与之后实际伤害，结束时无自守债务；四视角cold。
- SourceFactionQuotaOpaqueHeJAndEquipmentChildrenAreExact：native source的opaque hand slot转交、source skill loss/regain保留Game faction次数，真实SilverLion转交→HP→recipient gain typed return，另一势力且HEJ空仍消费；四视角cold。
- NativeExtraDrawAndEmptyDeckDoNotInventDrawDebt：原生AI真实主动接受额外draw；固定16牌真实耗尽后extra draw0，随后物理伤害牌Use不制造债务；四视角cold。

静态覆盖、尚无本次实际行为运行证据：势力改变后的发行点取值、未明置国战、死亡source、连环第二目标、gain/弃牌引发Dying与救援/死亡尾部、付款装备恰提供原技能、结束阶段来源失效、额外/跳过回合。另静态闭合合法获技组合的银狮付款→救援替代→HP/reward→Dying：5201/5202首子帧仅接纳exact RecoveryReplacementFrameRidesOn、Return.AwaitedProgramMovement、本人银狮producer/移动reason及原付款ledger。该吴势力获技组合尚未运行，正式群刘表不因这条兼容边改变来源资格。没有将静态审核写成已通过测试。尚未编译的固定夹具可能需父集中实测后修正。

OLD shared-wiring.patch 每文件一个 Update、可有多段，old-baselines.json 提供当前原字节和逻辑应用后SHA。父负责登记 GeneralModules、4方法、routine代表项和立绘；本稿没有这些旧入口修改。不要复制 make-wiring.py 到生产源码，也不要将暂存状态视为主区验收。

## 工程默认

以下为未取得当前普通OL FAQ裁定时的工程默认，不冒充官网正文补充。

1. 自守 X：首次5200额外draw发行点计算存活角色的公开有效势力数；5201自己的实际结束阶段开始支付时再计算一次并冻结。更早候选的孩子改变势力或死亡会影响尚未发行的X，选牌/付款孩子不再改已经冻结的X。本稿没有在窗口初建时冻结attempt-faction或X。
2. 本回合存在至少一张真正以5200额外draw取得的实体才产生结束债务。牌堆/弃牌堆实际都空时接受可选节点仍记录请求数与实际0，之后伤害牌Use不使其补生债务。相同实际回合多个额外Draw阶段的同实例债务合为一次结束X；正常额外回合是新actualTurn。跳过结束阶段则不付款，下一actualTurn不继承。
3. 弃置X的区域按普通弃牌语义取手牌和装备；不足X则弃全部合法HE，零HE不虚造选择或实体。HE混合成本通过现有原子多来源移动，原技能失效或owner死亡后先排清已付子结算，取消尚未支付/剩余指令。原实例失去后再获新实例不继承自守债务。
4. 新伤害牌分类为Slash/FireSlash/ThunderSlash、Duel、BarbarianAssault、ArrowBarrage、FireAttack及Lightning；是否实际命中、被无懈或防伤不改“使用过”。Lightning作为伤害牌的分类是工程默认，尚无当前FAQ确认；其伤害无来源，不为宗室提供虚构来源势力。普通实体/多材料Slash Response不计为Use；没有真实Use owning frame的纯程序Damage/LoseHp不计。
5. 宗室来源势力在5202真正发行时查询并冻结；更早候选子链改变其势力，消费改变后的公开有效值。God等已公开选定势力读取既有effective查询的选定值，拒绝从原武将printed faction重建。每个新公开势力可消费一次，同一势力不同角色共用 victim/skill Game exact key。
6. 宗室Game额度跨该技能来源失效/重新获得保留，不调用ResetSkill清整个技能。HEJ空仍防伤并消费该已冻结来源势力，省略无实体转交；有真实死source的已接受伤害仍防止并消费，随后得牌因source死亡取消。无source、自伤、未公开有效来源势力不防止且不消费，没有猜测或隐藏身份推断。
7. 国战未明置势力不计入新公开X/handLimit，也不写入新public event或SkillRuntimeState的usage ID；来源首次明置后第一次才有可消费公开额度。身份模式未公开武将同样不把其私有势力纳入新public查询。旧livingFactionCount及旧相关技能默认保持。本稿只静态审查此边界，未做国战真实命令行为检查。
8. 转交由真正的source选择 victim HEJ。手牌用隐藏slot，装备/判定牌用其公开实体。发行后整个伤害已被防止，source死亡、victim死亡或原技能失效可取消尚未付款的得牌尾部，不能撤销已发行防伤或补还Game额度。付款完成后沿精确owning parent排清装备回复、gain/HP/Dying/death孩子；已经获得原实体不因尾部取消重复移动或再次付款。

上述默认均独立于classic自守的自目标限制；本稿没有复用其整个classic技能或改写旧内容。

## 主区静态整合补记

root验证冻结manifest、12项NEW与13项OLD当前原字节，复制NEW并应用窄patch。十三项OLD最终原字节SHA全部与logicalAfter一致；GeneralModules、四项既有runner、两个routine代表项及官方原图已接线。两项routine分别覆盖新的Draw/Ending债务与BeforeDamage来源势力防伤，当前未测总耗时，用户醒后应测默认scope并据实调整。本稿没有执行加载器、编译、行为检查、WPF、全量或基准；正文及未核FAQ默认不等同运行验收。
