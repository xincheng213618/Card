# 王基、蒯越蒯良、卢植工程契约

依据2026-10-02当前main源码与本目录已冻结的www普通hero 362/404/407主文。没有重复抓网页。以下“尚缺”表示现有已读入口不能直接表达，不等于已证明整个引擎不存在所有相似代码；实施先核对对应descriptor，复用能够满足完整语义者。

## 共用边界与分工

禁止人物ID/name/characterId引擎分支。人物内容声明规则；新增能力通过ProgramOperationDescriptor、typed instruction/resources、host、AI policy及composition validator注册。不得在角色实现中新增parallel pending、use-ID sidecar或赠牌追踪字典。

共用转换：SkillRuntimeState.cs已有GetConversionState/ToggleConversionState及Turn/Phase usage reset。其键是(owner,skill)，不是instance。当前承略实际读取AlternatingSuitStateCommittedEvent历史（GameEngine.AlternatingSuitAndCompletedTop.cs:49），并非调用Store；前预检“许攸共用Store”说法应以此更正。蒯/卢共用一个通用读取/提交阴阳能力，按owner+skill共享同名技能阴阳（保持既有store约定），事实记录精确SkillInstanceId、绑定、父帧和动作；不可把承略专用AlternatingSuitDrawDiscard直接当任意转换技能。转换只在已接受的有效发动点提交一次；取消不翻转，子帧恢复不再次翻转。审时/贞良两个不同skill绝不互串；禁止顺带迁移承略或改变旧内容相同fingerprint语义。

同技能多来源：资格/candidate/事件必须保留SkillInstanceId；同名技能计数与阴阳维持引擎既有owner+skill语义，不能按重复grant生成多次同一触发或重复成本。pile以(owner,skill,instance)定位。跨技能读取如进趋读奇制计数、贞良读明任pile，必须声明依赖并按同源授予映射解析；不能读取owner的任意第一个pile，不能跨另一个grant错误合计。若实施选择instance隔离计数，应作为公共明确语义先冻结，不能人物各自猜测。

所有暂停状态位于ProgramSkillFrame拥有的typed draft/bindings，runtime push/replace/complete及typed return推进。跨回合使用可序列化、具有Source/CreatedTurn/due boundary/continuation的类型化机制，不保持一个活跃ProgramSkillFrame跨完整回合。DeferredHandAlignment是可借用框架，不是现成审时能力：它仅从TurnEnding安排，且在phase Finished后的实际回合结束执行；审时须当前回合结束阶段，不能硬套成回合结束或目标下个回合结束。

事实用AdvanceEventRulesAndQueueFact；状态用AdvanceRulesAndPublishState。commit前freeze事件collection、prepared snapshot。所有玩家视图CreateSnapshot(viewerSeat)，ResolutionStack只供可信测试诊断。选择代币须revision/prompt/frame绑定，隐私查看不可公开手牌ID/花色。新能力须公开声明AI policy和可表达的strict resources。

## 王基362

可复用：CardUseTargetsFinalized窗口及ProgramCardTriggerWindowFrame.Action；ProgramParticipant/EventTarget；ProgramOwnedCards/ProgramOtherOwnedCardDiscards私密opaque选牌；DrawProgramCards/AwaitProgramBoundCardMovements；turn-scoped usage store；事件历史/真实CardMoved ledger；独立弃牌owned-card选择。现有其它角色弃牌入口默认“除选择者外”，不能直接等同“本次用牌目标之外”。

尚缺公共表达：在自己回合、非装备、已完成所有改目标后的完整target set之外选择有可弃HE牌角色（可以选自己，只要自己不是此牌目标）；一张真实弃置后给同一角色摸一；按owner+skill记录本回合有效奇制发动次数，并给结束阶段另一技能读取；摸二完成后动态弃手牌至X。SkillProgramActivationLedger仅累加activation的selectedCards且Phase scope，不能当奇制触发次数器；通用Draw/Discard fixed amount不能伪装动态弃至。

时序：每个实际card action的最终指定目标窗口一次，排除全部最终目标，不按每个target重复。限定actor是本人、current turn owner是本人、effective kind非装备；无目标动作资格按现有窗口语义冻结，不能凭CardUseDeclaredEvent虚造目标。玩家接受发动并成功承诺有效选择后记录一次计数；被拒命令/decline不记，随后嵌套死亡不补记或重记。真实HE→processing→discard及其movement child全部结束，再对同一存活角色摸一；不能以手牌计数-1模拟。进趋结束阶段先可选发动、摸二及nested gains完成，再以该本回合次数冻结X，弃max(0,currentHand-X)手牌；X=0弃全手，X>=手牌不弃。游戏开始/额外出牌阶段不重置turn计数，新实际TurnStarted重置。

最小固定夹具：3~4席固定deck/现有verified seed，给一个多目标非装备动作带一次Liuli重定向，确认一次奇制且不选任何最终目标；装备使用不触发，自己非目标可选；HE opaque选牌、弃牌触发gain/death子帧暂停，checkpoint恢复成本一次和计数一次；decline/拒绝不计，进趋X=0/2/大于手牌，额外Play不重置、下回合重置；同名多grant去重与跨来源依赖。

## 蒯越蒯良404

可复用：CardUseTargetsFinalized针对本人最终目标资格；AfterDamageApplied带event source；ProgramSkillFrame/BeginProgramSkillDamage、dying/death typed return；SelectOwnedCards与MoveBoundCards真实赠牌；DrawProgramCards及nested movement；opaque/private prompt基础；同技能usage与转换基础。

尚缺公共表达：全场最少手牌（含自己）/其他人最多手牌并列可选；只向查看者展示整个来源手牌的短期private observation（RevealTargetHandCard公开单牌不满足）；赠牌后条件保留到当前实际结束阶段，并从真实movement历史判定“曾失去”，不是结束时还在手中的简单contains；伤害本次导致目标死亡才给另一个选择摸至四；generic draw-to-hand-limit。采用typed delayed condition record保存Source(owner,skill,instance,binding)、turn number、recipient、physical card reference及赠入手的真实movement ordinal，结束阶段消费一次；这是有类型生命周期债务，不允许一个字典/cardId sidecar附加在引擎角色分支。

荐降时序：其他角色使用牌使本人成为最终目标后，一个对应动作/本人目标资格触发，选择手牌最少的存活者；并列不能要求unique，选择执行时以冻结候选与既有资格复查约定处理。draw后嵌套effects完成再回原牌效果。自己使用牌不触发。
审时阳：出牌阶段限一次，选择当前其他存活最多手牌者，选择本人可交出的实体牌（主文不应擅自限手牌，合法自有牌区按标准give语义HE），赠入对方手牌和movement children先完成，再技能伤害1；若该伤害确实导致其死亡、dying最终死亡链结束且技能拥有者仍合法，才能选择一存活者摸至四。存活被救、gift child先杀死目标、别的后续死因不能冒充本次伤害死亡收益。
审时阴：仅其他角色造成实际伤害后，source仍存活且可视手牌时进入私密查看，之后选择本人实体HE牌赠给source；无可给牌不得排延迟。记录真实交入手牌的ordinal作为追踪起点；此前自己的弃牌/移出无关。对方之后任何一次真实离开该手牌区均视已失去（用、打出、弃、装、转移等）；离手再回来仍失败，非法尝试或未移动不算失去。在CreatedTurn相符的Ending阶段消费，满足未失去则owner摸至四；与其他结束阶段触发按现有座次/候选顺序调度；owner死亡、target死亡、source grant丢失/抑制的取消规则沿用/明确类型化机制，不能默默采取HaoZhao next-actual-turn语义。

最小固定夹具：4席固定deck，荐降并列最少含自己；阳并列最多、真正赠牌movement先于damage，阳→阴→阳、同Phase阴触发后再回阳仍限一次；伤害救活无收益/确切死亡摸至四/赠牌child杀死不收益；阴私密四种viewer snapshots（owner/source/旁观者/AI），赠牌未移动成功、离手回手失败、非法移动无影响、同回合多个延期独立、跳过Ending/死亡/失去来源与恢复replay均一次消费。

## 卢植407

可复用：SkillProgramTriggerWindow.GameStarting窗口、DrawProgramCards；PublicPersistentPileSource keyed owner+skill+instance（GameEngine.PublicPileSources.cs）、真实pile CardLocation、容量1；ExchangePublicPileHand及typed PublicPileDraft；OtherLivingInAttackRange；owned HE cost选择/有效色计算；BeginProgramSkillDamage；CardsMoved批及ProgramMovementSourceCounts中的真实记录/专门discard-origin；conversion/usage基础。

尚缺公共表达：游戏开始“摸二后从手中选一存到自己容量1 public pile”（StoreTopCardInPublicPile是牌堆顶，不能替代）；贞良绑定同来源明任牌颜色、阳真实同色HE弃置后damage；阴在回合外“使用或打出”的实体最终实际入弃牌堆时匹配Action identity与明任颜色。CardResponseAccepted只是接受响应，CardUseCompleted也不能保证最终弃牌，普通CardsMoved又太宽；须通用action provenance与真实 movement join，不用外置actionID lookup sidecar。可扩展拥有action的帧/typed movement context传递冻结的物理成本引用与使用/打出来源。

时序：开始摸二及所有nested gains结束，选手牌一张、hand→自己的明任pile真实移动；初始空手/死亡/嵌套移走选择需重新合法检验。结束阶段可用一张手牌替换任，走已有ExchangePublicPileHand原子交换与movements，不能将任牌当弃置，PublicPileId/instance保持正确；同名多个来源任相互独立。
阳：当前Play限一次、当前阴阳阳、当前攻击范围目标，按有效颜色同任选择本人HE真实弃置；冻结合法任来源与成本有效颜色，成本movement子帧完成后再damage；重入不重复支付，cost child死亡停止余效。阴：回合外使用/打出动作的physical entities真实进入discard时才判断，排除明任存入/替换、普通弃置、recast、赠牌以及已被别技能放牌堆顶/取得/装备而未进discard的实体。虚拟牌无实体不触发；多子牌要按一次牌动作冻结统一颜色/实体集合，不能按每片移动重复翻转或奖励。原主文“此牌”与复杂混色virtual语义不能各agent猜：沿用现有CardAction effective color，颜色不明确则不可匹配，并在公共资源合同中锁定。触发时读取该来源当前任，冻结颜色供后续draw child；若需游戏产品专门例外才回官方核对，本轮没有追抓。

最小固定夹具：固定deck三席，开始draw child暂停后选真实任、拒绝任非手牌；结束任交换原子movement及多个source隔离；阳攻击范围边界、同色HE实弃、cost child移走任/杀死owner、checkpoint恢复一次成本；阴真实杀/闪打出/响应使用，无效接受响应不提前奖励，已移牌顶无奖励，普通弃/recast/换任无奖励；一张双实体同色virtual只一次，混色不匹配，奖励draw child死亡/同名source丢失；正常AI公开任决策不能读对手私手。

## 固定夹具与实际已有runner名

现有helpers：FengLinWangPingChecks.Start(mode)/Driver/Hand/Reach/Choose/Replay/Conserve，FengLinXuYouChecks现有固定native配置，FengLinGuanqiuJianChecks多source fixture，FengLinHaoZhaoChecks的deferred/private replay fixture。只重用小fixture模式与公共helper，不复制整个历史suite，不在default循环找seed/跑整局。新增behavior checks用现有tests/Core Program.cs (Name, Action)注册；目前没有王基/蒯/卢已注册检查，不把设计名称冒充实际runner。

下列名称已从当前 tests/CardGame.Core.Tests/Program.cs逐字读取，后续可通过现有--filter=<name>按需运行（本轮未运行）：
- skill runtime usage and conversion states reset by declared scope
- skill program multi-target triggers wait for every Liuli redirection
- skill program card triggers require exact conversion sources and replay
- card movement programs compose atomic per-card and per-batch triggers with replay
- nested card movement batches retain immediate parent identity
- target-card prompts expose opaque slots and replay without hidden identities
- command projection preparation failure preserves prior commit
- command checkpoints restore a paused private prompt deterministically
- shared public pile multiple sources isolated replay
- shared public pile independent source thresholds
- Feng Lin Guanqiu Jian gain payment field nested replay
- Feng Lin Xu You actual response use and first category
- Feng Lin Xu You multi entity order and nested draw
- Feng Lin Wang Ping given card nested movement replay
- FengLin Hao Zhao exact grant suppression maturity
- FengLin Hao Zhao ordered nested native owner
- FengLin Hao Zhao skipped extra actual ends

实施必要focused检查补各自未覆盖行为与generic operation错误资源输入；纯定义沿用loader。WPF仅发现实际新UI行为才补，不把Core通过报成窗口验收。父最终批次按AGENTS做一次appropriate Full；本文件不授权新增测试轮次、不改变rules/package/schema版本。


## 父任务最终冻结裁决

本轮以ce9fc853501919c1ad707120860f0cb64d48bcbf已验收提交为共同基线。王基新枚举段1440–1459、蒯越蒯良1460–1479、卢植1480–1499；没有需要新增枚举时直接复用，不为人物预留而添加空节点。各worker只改自己的副本，交付来源和最终SHA；父负责media/WPF/合并/Full/无过滤日常实测/本地提交，不自动push或发布。

通用转换入口由蒯越蒯良作者负责，实现GetProgramConversionPolarity(ownerSeat,skillId)读取既有SkillRuntimeStateStore以及CommitProgramConversionPolarity(frame)提交一次翻转与带来源/父帧的标量事件，按owner+skill共享状态。卢植消费同一入口；先可继续其独立内容/牌堆/动作规则，编译验证前应用父冻结的共用overlay，不能另建另一套阴阳状态。需要通用condition/facts时由共同作者先提出精确ABI并交付给父确认，不各自重复猜字段。不要迁移或改变成略历史读取语义。generic转换作者尽早交付完整最小overlay并验证真实state/replay；未冻结前不能修改对方副本。事件事实与UI使用同一真实状态。

奇制有效接受并承诺合法选择后本回合计数一次，按owner+named skill聚合，不因技能失去/重新授予或额外Play重置，多来源同一动作不重复。进趋明确引用奇制的owner+named skill回合计数；来源和每次事件保留instance身份，但不用重复grant倍增收益。

审时阳使用一次技能在本Play限次，阴不占这个主动阳次数；阴翻回阳后本Play仍不能再次阳发动。成功赠入手牌后发出的当前结束阶段条件已成为结算义务，不因之后失去/压制源技能撤销；拥有者死亡取消，受赠者死亡导致实体离手视为失败。跳过Ending时在实际TurnEnding失效且不奖励；正常Ending按既有触发次序到期一次。赠入后的任意真实离开该hand都使条件永久失败；离手再返回不恢复资格。它是有类型的延迟结算记录，不能留下跨回合活跃ProgramSkillFrame或独立cardId/useId字典。private observation必须只有本人可见，公开事实不能带对方手牌身份。

贞良颜色沿已有CardAction有效颜色：多实体同色虚拟牌一次动作仅奖励一次，混色不明确则不匹配；若真实实体从使用/打出动作进入Discard，入堆时触发，即便后续被别的技能取得仍是已发生移动。从未实际进入Discard者不触发。非使用/打出（包括重铸、任交换、普通弃置）不触发。明任与贞良跨技能来源依照PublicPileSources的same-grant/source映射，不抓owner任意第一堆。已付款颜色与参与实体在所属typed draft冻结，子链返回不重付。

所有边界使用现有Program descriptors、typed instructions/frames、AdvanceEventRulesAndQueueFact/AdvanceRulesAndPublishState及prepared snapshots；新集合事件须CommittedEventProjection冻结。无人物ID engine/UI分支，不写侧car或parallel pending。focused checks仅覆盖实际新增/修复行为，禁止seed sweep、完整对局循环、恢复旧suite。不得跑worker Full/WPF或任意Git提交，不改rules/schema/package版本。

## 实际实现与验收边界

以上预检描述保留冻结时的设计依据。实际实现通用转换CommitConversionPolarity1460；王基DiscardNonFinalTargetCardThenDraw1440/DiscardHandToNamedTurnCount1441；蒯GiveSelectedOwnedCardAndDamage1461/ObserveDamageSourceHandAndGive1462/DrawToHandCount1463；卢StoreBoundHandInPublicPile1480/PublicPileColorDamage1481/RewardDiscardedActionColor1482/AwaitOwnedCardMovement1483，均有descriptor、strict resources、typed frame续接和AI入口。无新增历史suite或人物ID规则分支。Ending内新增审时义务仍在当前boundary待执行后缀排队，已完成前缀与当前child所属项保持。最终主区Full、无过滤日常耗时以第四批benchmark和source档案为准，离线WPF不作为现场实战。
