550 界颜良文丑：当前完整双雄；仅 6100–6101 新 opt-in。

NEW：通用 descriptor/model、真实付款转换、实际伤害实体因果与独立 Ending claim、准确付款/领取观察子树四个 Core 文件；独立 Content + rules/presentation；至多四个真实命令方法。主区登记/原图/提交由父处理。

6100 DiscardBoundCardForOppositeTurnDuel：
optional own DrawPhaseEnded，严格 SelectOwnedCards(HE,1)→独占真实弃牌节点。原 program 自持 ProgramPaidColorConversionReceipt，冻结来源原实例/hash/实际回合、实体原区、原 EffectiveSuit、真实 movement sequence/after；费用实移动一次。成熟 queued recovery→HP→movement 回返后签发带 nullable scalar PaidColorOrigin 的 TurnCardConversion。旧 origin=null Hand/bool 匹配不变；新 HE 真红黑独立枚举，不扩大旧 viewAs、普通应答或 GeneralWeapon Processing 规则。

6101 ClaimActualTurnDamageEntities：
mandatory own actual TurnEnding 单节点，无 6100 前置。规则入口只在新 op 注册时观察实际 DamageApplied；必须 exact current DamageFrame.Parent=causal attack owner、actual amount/source/target/nature、真实 DamageRequested。
普通 CardUse：原 Action.PhysicalCards；legacy action-null 虚拟零实体仅 typed producer。
Lightning：真实 held delayed ID/attack material/hit Judgment owner，不读判定材料。
program-owned virtual Duel：真实 paused UseVirtualDuel/零实体；纯技能 damage 没有实体。
新 per-material facts 均标量公开，分别保留 Actor/Provider 与实际 DamageSource/Victim。Ending 只对仍弃牌的 exact 材料去重领取。收据 CardIds 的 ctor/init/with/JSON 都复制成只读集合；公开 claim facts 每实体一个，未增加 event collection。现 CommittedEventProjection 的成熟 event/list 处理保留，不添加重复人物 Snapshot。

NEW root admission：
原 DrawPhaseEnded/Ending window+candidate/instance/hash+paid instruction+真实 movement prefix 先锁定。第一子帧仅该 prefix 原 movement batch，或该成本确为 SilverLion 的准确 AwaitedProgramMovement recovery/九援替换。之后局部复用 EquipmentDonationDamageObserverEdge，保留其 PaidTargetObserverEdge（准确 HP/movement/Dying/Death/救援/翻面），并精确证明真实 Program damage→原 BeforeDamage/Applied window。真实 Damage / AttackHpLoss dying 的 SelfDyingResponse/TurnOver 仅 union 485 同批新增 PaidObserverDamageDyingProgramMatches、PaidObserverAttackHpLossDyingMatches、PaidObserverDamageDyingFaceEdge（不改旧 ProgramSkill dying helper）。入 Dying 的 edge 先证，再接受已有 exact whole rescue/Alcohol tail 或 PaidObserverDamageVirtualAlcoholRide 完整证明。新自动 Dying 资格接收任意 continuation，但必须完整 paid root 连续路径、当前 Dying 位于 root 之后，且当前 damage window 若存在必须确在同子树。Runtime 只追加这个局部 proof OR。旧通用 edge 不变，包含集合的 Facts 不做 record Equals。

OLD 最小接线（最终仅父宣布稳定基线后生成）：
- SkillPrograms：EffectOp 尾部追加显式 6100/6101，不改变旧隐式成员；strict composition 调用。
- Resolution：ProgramSkillFrame nullable PaidColorConversion/ActualTurnDamageClaim；旧序列化默认忽略 null。
- CardUseModules：TurnCardConversion nullable scalar PaidColorOrigin；GrantConversion optional origin=null/duplicate exact origin；旧 GetConversions 排除 new origin（保持旧查询自身语义）。
- GameEngine.CardConversions：现 configured+classic turn sources后增加仅新 paid-color HE 枚举。
- GameEngine.CardActions：成熟 trackAppearance 仅追加当前实际回合 PaidColorOrigin 的同实例/绑定转换 chain 精确 OR，独立冻结本次有效花色/颜色，不改其它 Action 形状。
- GameEngine：新增设备/原生 Duel 源候选，精确其新设备 actual suit 纳入成熟 universal legality；新增 AssertPaidColorDamageClaims。
- RuleProgression：仅新 trigger operation 实际 damage observer，继续 AdvanceEventRulesAndQueueFact。
- ProgramLifecycle：仅新付款候选 HE available 门。
- SkillPrograms engine：新 receipt 断言+准确 PendingMovement 节点门。
- Runtime：新 frame paid child resume+自动 Dying exact proof OR。
不改 classic JSON，不加人物 Host 分支/runner/全局版本/平行 pending 状态或 use-ID map。

四显示名/方法：
1. Real color payment Duel back-damage and mandatory Ending claim / BoundaryYanLiangWenChouChecks.RealColorPaymentDuelBackDamageAndMandatoryEndingClaim（唯一 routine prefix）
2. Multiple original materials already moved and empty virtual responses / MultipleOriginalMaterialsAlreadyMovedAndEmptyVirtualResponses
3. Lightning held entity source loss and gain Dying cold return / LightningHeldEntitySourceLossAndGainDyingColdReturn
4. Real equipment conversion native and strict operation contracts / RealEquipmentConversionNativeAndStrictOperationContracts
都用固定 seed 31、正式选将权重/真实命令、CreateSnapshot 四视角与真正 JSON Restore 返回的新 engine 续行；未知运行表现保留 deferred。
