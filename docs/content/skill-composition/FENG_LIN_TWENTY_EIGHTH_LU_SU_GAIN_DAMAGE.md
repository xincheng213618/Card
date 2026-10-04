# 485 已付获得牌后继伤害窄补口

此交付独立于原冻结 `batch28/lu-su/delivery-manifest.json`；原文件不修改。主区Core基线为 root 已准确应用原485、尚未提交的字节，基于d2990fba；e48303e7仅另外提交来源docs，未改变本次Core/test基线。逐文件before raw SHA优先于HEAD说明。

仅数据生成与静态阅读完成。未编译、未运行严格loader、未运行检查或性能测量。

- 6000/6001/6002/6003/6004 原 owning receipt、真实发行事实和真实原始ledger首先由 `HalfHandPhaseFirstChild` 校验。之后局部逐边复用 `EquipmentDonationDamageObserverEdge`，旧 `PaidTargetObserverEdge`、`PreventionDrawObserverEdge`、`PaidMountObserverEdge` 的语义保持原样。
- 在6004援助的原Slash仍为当前真实攻击时，只有已经支付一张原受赠者Hand→原ownerHand实体的root及其连续原gain/HP候选，可执行当前准确paused `Damage` 指令。目标、amount、sourceRef、nature均与该指令核对，之后原生 `AttackReturn.ParentAttackOwnerFrameId` 保存同一原Slash；不为任意frame presence放开嵌套伤害。
- 新伤害自己的 `Program→BeforeDamage/Damage→DamageWindow/Dying` 入边使用成熟明确字段/事实证明。两处现有damage cursor检查只在同已付前缀、同当前ProgramAttack及准确当前DamageWindow完整成立时新增OR。
- 真实Damage濒死的 `Dying→SelfDyingResponse/DyingResponse` 需要原DamageFrame、当前ProgramAttack、当前responder/victim、真实killer/sourceLess、Parent/DamageFrameId/SourceSeat、owner/binding/instance/hash及真实producer固定的occurrence=0和唯一BindingStarted发行事实。已接受的候选原实例由该事实保留，不在付款孩子失技后重新枚举当前来源。不会借用只承认ProgramSkill濒死的旧counterspell匹配。
- 绝情等真正替代入口仅在 `Program.AttackAttempt/AttackReturn→AttackHpLoss Dying` 精确对应、同当前ProgramAttack、同victim、killer=null、该原Program的最新实际DamageReplacedWithHpLoss事实和本次唯一PlayerDying事实下接纳。随后source字段仍按真实GetDyingAttack口径匹配，而不把killer=null误当source=null。Program可顺序造成多次HP替代，故不人为要求整局仅有一条同Program replacement事实。
- 有效SelfDying翻面保留真正paused TurnOver、CharacterTurnedOver changed事实和实际子候选。零实体自酒只在同SelfDying producer、CardId0/Action=null/zeroPhysical、真实声明+目标、准确DyingVirtualAlcohol RecoveryProducer token及完整连续HP/移动后裔证明成立后接纳。已有boundAlcohol/实体救援whole-tail先证明当前Dying入边再复用，原成本不重复。
- 已付身份/ledger核对不依赖重新取得当前来源资格。原程序结束、来源失效、死亡和winner继续沿原既有父返回与清理流程；此补丁不签发额外收益或新增sidecar、pending、集合event。

原第4方法 `HalfHandSupportUsesRealTargetsAndExpiresAtNextActualStart` 保留全部旧断言，追加小固定真实命令草稿：原半手赠牌资格→请求实体Slash→原donor支付一手牌→实际CardsGained候选ChooseOption→1点真实Damage→准确DamageContinuation Dying→fixture-only mandatory SelfDyingResponse→RecoverTo3→真实HP子窗→原Slash继续。所有关键暂停均 `g = RestoreAfterCold(...)` 后继续新实例，保留四视角、真实事件、实体ledger及一次支付/一次原Use完成断言。不新增runner，不把fixture-only pulse声称为classic Niepan。

静态未执行边界：四项原草稿及追加分支均未运行；DamageReplacedWithHpLoss、九诗翻面、零实体酒的RecoveryReplacement暂停和boundAlcohol为准确producer静态核对，未在本次追加小分支执行。不声称生产验收或全组合运行覆盖。
