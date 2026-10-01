# 袁术、周妃及张绣从谏工程契约

只读预检，2026-10-02。生产依据是当前 main 的 ce9fc853501919c1ad707120860f0cb64d48bcbf；第四批作者源码只提供接口参考，父整合与最终冻结之前不得作为最终 ABI。本文没有实现、构建、测试、WPF 或 Git 操作，不代表行为验收。

## 来源及版本边界

依据普通 www.sanguosha.com 的冻结正文和图鉴 index，不混用移动版、十周年、界版或其他年代文本。复用了已有 HTTP 原始文件，没有重新抓网页；本次已用 Get-FileHash 核对下列 HTML。HTTP 抓取成功不等于浏览器视觉验收。

| 来源 | 本地冻结文件 | SHA256 |
| --- | --- | --- |
| 袁术100 | Card-NextBatch-preflight-resume-20261002/100.html、100.json | 622b9216c63a2ec1092f08878fc60a812b18ddefa3df7214aa562301e850de24 |
| 周妃411 | 同目录411.html、411.json | abbeb2f0a2f893c340dd75ca787e32ac41a299acd5fab6a82bfe39e624355b80 |
| 张绣415 | Card-FengLinFourth-20261002/next-preflight-zhang-xiu/415.html、415.json | 4f1c85073b7dd756e6aadaa393ca2f5a4e3cdd11202b893040080325cfa7dc24 |
| 官方阴雷归属公告 | Card-NextBatch-preflight-resume-20261002/official-yin-roster.html | b02eb18e93681aea86f5c580d3be4ebc70a10f3330518075f5addf2db279a7dc |
| 雷包公告 | 同目录official-lei-roster.html | 59ee53d28ff3a0257ecff74d1810d095c16a4674780cb3f491cd46ed6473ebc3 |

100/411于2026-10-01 UTC抓取，415为父19:09:33 UTC fresh HTTP。index对应袁术群4HP、周妃吴3HP、张绣群4HP。本文不复制全局工程版本数字，也不提出逐将修改规则epoch、schema或package版本。

官方雷包：诸葛瞻、陈到、袁术、周妃、郝昭、张绣、毌丘俭、陆抗。剩余工作应写成“袁术、周妃两名新将，加张绣缺失的从谏”，不能按张绣名称已注册而计完整。main StandardClassicGeneralPackage.cs的 classic:zhang-xiu 当前仅 classic:xiongluan、4HP，无 AdditionalSkillIds；classic-zhang-xiu.rules.json也仅雄乱。父已独立确认从谏缺失。本批保留雄乱现有配置、区域废除、定向本回合政策及手牌禁止，不以图鉴补字替代从谏真实实现。

## 袁术100：庸肆与伪帝

冻结正文：庸肆为锁定技，摸牌阶段多摸X张；弃牌阶段开始弃X张，X为全场势力数。伪帝为锁定技，视为拥有主公的主公技。

庸肆必须修改真正正常摸牌计划，保留正常摸牌替代、跳过、牌堆方向等已有顺序；不能另开Draw伪装多摸。摸牌触发时按当前存活角色有效势力去重取X，并在弃牌开始重新计算X，不能固定四势力或使用存活人数。弃牌开始的额外强制弃置是实体弃牌及完整movement child，再进入通常弃牌；不能减手牌上限代替。可弃区域、不足X的取尽行为应明确冻结，不能默默把HE、手牌和不可弃牌混同。

现有 GetLivingFactionCount 及 LivingFactionCount 数值表达式可复用；AdjustNormalDraw descriptor当前只接受固定量或 SelectedTargetCount，SelectOwnedCards动态数表达式也未包含 LivingFactionCount。因此需要通用descriptor/host扩展及严格资源验证，而非人物分支或虚假op。SelectOwnedCards、MoveBoundCards及awaitMovementTriggers提供实体支付/恢复基础。一次阶段操作将已承诺数量及选中实体留在所属typed frame，恢复不重算已经完成的支付；不同阶段独立取新X。

伪帝不能按文本创建外挂技能或把所有Lord标签都放开。现有 CharacterSkillSet 的 SkillGrant/SourceId/SkillInstanceId/IsEnabled、Revision与 MatchSkillBindingIndex 可承载来源映射；但实际Lord身份资格、打印主公技与后天grant的入口不同，部分host/contribution及AI仍直接检查 Role.Lord。必须审计真正技能入口、被动贡献、转化、AI和provider资格，定义“实际主公的哪个有效主公技source”复制给伪帝owner，且其阵营/主公技受援资格按原规则运行。

必要缺口：动态且稳定的 Lord source → Weidi source grant关系；源技能获得/失去、停用/压制、实际主公死亡、伪帝自身失去/重授均刷新并使binding缓存失效；只撤销对应映射，不误删原生或其他复制grant，多grant不重复；避免递归复制/循环。GeneralWeapon候选明确过滤Lord技能，不能用其随机复制替代伪帝。没有实际Lord、owner本身就是实际Lord、源压制如何传播应在实施前冻结；本文没有声称整条复制链已验收。

固定fixture建议：两个或三个有效势力，摸牌后改变势力或死亡，再验证弃牌X独立重算；跳过/替代正常摸牌及不足X；弃牌movement触发嵌套获得、死亡、checkpoint恢复。伪帝用固定实际Lord及一个真实主公技provider，测试有效资格、AI/人类入口、双source、源停用/失去/重授、owner失去/重授与replay。纯注册部分沿用catalog检查，不加重复定义快照。

## 周妃411：良姻与箜声

冻结正文明确“每回合首次有牌移出/移入游戏后”，不是整局各一次。父已认可按每个实际回合、全桌跨多人追踪，两方向首次分别记账。移出时双方各摸一；移入时双方各弃一；完成这些真实子链后，可令其中手牌数等于当前箜声牌数X的人回复1HP。first是全局真实移动事实，不能以周妃是否发动/接受/持有技能为准；首次decline以后同方向不得补发。额外阶段不重置；新实际回合重置。

关键缺口：规则“游戏外”不是 CardZoneKind.OutsideGame 一个物理zone。箜声置武将牌上的规则域关系需明确，而当前 PublicPersistentPile 有自己的实体zone和source地址，不能仅监听永久销毁OutsideGame，也不能为监听另建实体账本。建议建立通用规则域分类/跨域movement事实，在实体CardZoneStore移动上判断outside→inside和inside→outside；是否所有其他武将牌上pile均属规则外，以及特殊储存区例外，是待冻结工程解释，尚无本次官方FAQ裁决，不能冒称FAQ。

同一多牌批次的首次归属、两个方向并存及子批次先后必须规定：建议按已提交真实movement顺序确定每方向首个跨域事实，并按原子批次触发一次；要保留立即parent身份、稳定turn和batch信息，不按observer到达顺序取first。域内换pile不产生跨域；处理区/牌堆正常使用不自动视为游戏外。建议与父冻结后实现，再用批次和嵌套fixture验证。

箜声：准备阶段可存任意张牌（拟HE可选、零张decline边界须冻结）；按owner+skill+instance存入独立公共persistent pile，公开count/card可见性依正文及既有快照契约冻结。结束阶段先获得非装备牌，完整gain children结束后再选择角色，逐张真实使用剩余装备，最后失去1HP并完整处理死亡/濒死。不能直接装入装备区代替使用，也不能为套用旧操作先虚构交到手牌。顺序/装备选择、不能合法使用的剩余牌、被替换装备、使用者死亡或区域废除后如何结束，以及无剩余装备时是否仍选人并失去HP，均需明确实施合同，正文没有在此给出FAQ补充。

现有多来源PublicPersistentPile及source-local快照、计数和清理可复用，不能和书/荣共用总数。现有CollectPublicPile限Authority、1..2；书操作容量1..16，不能套用任意张的箜声；需要通用不限容量存放/分区获得。真实CardMovementBatchContext/Record、OriginSkill/instance及CommittedEventProjection提供事实基础，但尚缺跨域first通用事实。

装备使用可参考真实BeginCardUse/processing/equipment completion恢复。现有UseBoundCardByTarget确实使用装备，但强制单张公开binding且该牌已在用牌者手牌；RandomDeckEquipment只从牌堆随机选一张，两者都不是“指定pile剩余装备逐个使用”的完整接口。需要typed所属frame保存来源地址、候选实体/次序、游标、使用者及完成阶段；每次真实使用校验合法性，移动前推进支付/游标后等待children，恢复不重复用牌或loseHP。非装备gain引发良姻、装备移出pile亦可能引发良姻，不能递归顺序失控。

固定fixture建议：任意其他角色先移出/移入、同方向第二次、decline、双向同批及嵌套batch；新实际回合/额外阶段；混合两件同槽装备+非装备存牌，真实gain子链、替换装备、区域废除/禁止用牌、使用者/owner死亡与loseHP濒死暂停恢复；另有书/荣source不污染X。多viewer/JSON/checkpoint和新集合深冻结必须覆盖。

## 张绣415：补从谏，保留雄乱

冻结正文：成为锦囊牌目标时，目标数大于1，可交给其中一名目标一张牌，然后摸一；真实给出装备牌则改摸二。使用真正最终目标窗口，owner必须属于最终目标集合，distinct最终目标数>1；不是选牌面理论targets，也不是每个目标各发动或每个物理支付实体各发动。基础牌/装备使用不触发，真实锦囊的转化使用按action类型判断；支付牌是否Equipment按实体CardKind而非转化后的牌名。是否允许给自己需冻结：正文未写“其他”，不可未经说明套otherLiving；若同owner手牌原地没有真实交牌，则不能伪造成功支付。

已有 CardUseTargetsFinalized、ownerRelation Target、CurrentCardUseTargets，以及 CardUseDesignatedTargetCount（取EffectiveDesignatedTargetSeats去重）、BoundCardsMatchCategories可用，需确认所用窗口的final集合与计数一致。SelectOwnedCards HE + 真实MoveBound到SelectedTargetHand + awaited movement child + Draw构成基础链；最终目标经过重定向后必须仍合法。命令非法选择原子拒绝；decline不交牌不摸；交牌之后被真实替代/销毁或收牌人死亡时，按实际receipt及已冻结给牌语义判定，不能仅按选中牌认定成功。owner死亡不继续摸，恢复不能再次支付。保存来源skill/instance、action/frame与支付receipt；不加useID sidecar。

建议新增必要focused fixture：普通单target阴性、真实多target锦囊阳性、最终target改变、给Hand实体Draw1、给Equipment实体Draw2、非法非目标/非法HE原子拒绝、嵌套交牌引发owner死亡及checkpoint replay、多grant仅一次。以小固定fixture/已验证seed执行，不扫seed或跑整局。

## 实施与验证约束

纯内容注册沿用whole catalog、Features/Legality引用键及唯一GeneralModules RegisterBundle；新共用能力补descriptor、typed draft、严格资源/窗口检查。命令commit/prepared snapshot、隐私、CardZoneStore实体唯一性、多来源source-local计数保持；新集合事件必须CommittedEventProjection深冻结。新规则事实入口AdvanceEventRulesAndQueueFact，新状态入口AdvanceRulesAndPublishState；不把QueueGameEvent/PublishState恢复为规则入口。

复用现有runner过滤。main已登记的相关回归包括：Zhang Xiu Xiongluan abolishes areas and blocks hand without armor bypass；printed Lord skills follow identity and rules 96 replay ownership boundaries；skill program multi-target triggers wait for every Liuli redirection；nested card movement batches retain immediate parent identity。选择针对新能力的必要行为checks，加真实移动/暂停恢复/来源清理覆盖，禁止恢复已裁剪suite。本预检没有运行上述checks，不使用历史绿结果冒称第五批验证；最终Full、routine测时及WPF由父在整合批次执行。
## 父任务实施冻结（优先于上文待裁决设计）

第四批已主区验收并提交25e4d368f2986470602c1f3d977d050ffb071a1d。一次Full Core367/367、WPF46/46，133.834秒；无过滤日常Core101/101、WPF12/12，含增量构建43.031秒。以此精确源字节为共同基线，不按旧历史次数恢复裁剪。袁术新增枚举1500–1539，周妃1540–1579，张绣1580–1599；已有能力足够则不新增空节点。三个作者只改各自副本；父负责官方立绘、图鉴、必要新共享UI、整合、Full、routine计时及本地提交。不自动push或发布。

庸肆：当前存活角色的有效势力去重。正常摸牌加X遵守既有正常draw计划/替代/跳过，不另开假Draw。弃牌阶段开始重新冻结X，强制弃自己的合法HE实体min(X,可弃数量)，完整children后进入通常弃牌；不调整手牌上限代替。可弃为空则继续；已承诺数量及已付款留owning frame，不在child恢复重算/重付。现有数字表达式可通用扩展，只新增合法资源能力，不改变已有固定amount语义。

伪帝：按实际存活Role.Lord的有效Lord-tag原生和后天真实grants映射，排除本能力的派生来源避免递归；无实际Lord、非identity或owner本身就是实际Lord时不做自映射。真实Role/faction/身份HP/胜负、AI阵营关系保持。派生技能保持真实SkillId与稳定来源关系，来源含actualLord seat+grant/instance和consumer能力grant/instance。来源禁用/压制、模板资格或死亡使派生资格即时不可用；本地对派生grant的禁用独立保持，不因源重启无操作refresh而擅自恢复。实际移除只撤销对应关系，不碰原生/其他复制；源获得、源重授、owner失去/重授都精确刷新。same owner+named skill使用/转换既有语义不重置，重复grant不放大一次action收益。资格只放行该真实派生instance的Lord技能gate，菜单、response/provider、card policy/passive、contribution/host及运行时AI一致；不是全局Role.Lord替换。SkillsChanged单点不足，binding跨owner alive/Revision变化必须收敛；纯snapshot/AI/read不能产生rule事件。所有同步在规则/command session边界并保留拒绝命令/恢复语义。可用typed来源元数据，无useID/cardID sidecar或parallel pending。

周妃以当前hero411及2022-11-02普通OL3.5.0改后文本为准，不迁入2019旧入手牌限定或十周年/x版。良姻每实际回合两方向首次各一次，全桌真实越界时就占first，decline/当时无技能不顺延；额外phase不重置。普通Draw/Hand/Equipment/Judgment/Discard/Processing同属游戏内；使用过程Processing不算游戏外。明确武将牌储存zones PublicPersistentPile（任/书/荣/箜声等）、Authority、BuquWound、Chunlao、PrivateReserve（当前七星星）、PojunHold及经源码确认孙登真实武将牌寄存PublicDeferredPile为外；WoodenOxGrain为宝物粮外部储存域，实际移动进入/离开正常域各算越界，owner间粮域转移不重复算。OutsideGame仅实际回合内的真实移出/移入算，构造注册/开局未分发不占first。以上是显式工程域裁决，不声称捕获官方新版FAQ；不能按任意独立enum统算，新增zone/source须显式分类，未知默认正常域并提出审查。既有CardZoneStore实体账本保持唯一。

同batch每方向一次，按真实movements顺序决定双方向首次；在movement batch完成时冻结scalar turn/batch/direction/ordinal并先占first，再开启任何children。无新人物资格分支。一个方向首次所有有效同名skill按owner去重；是否发动和各instance归因按现有trigger框架。良姻选其他living角色，双方实际摸1或各弃自己的合法HE1；一方无可弃按现有顺序执行能执行部分，不伪造支付。全部movement children返回后重新读取当前双方手牌及同source箜声X，可选其中手牌等于X且能回复者回复1；X=0有效。回血可以跳过。

箜声准备可存任意合法HE，0张视为跳过；实际独立source-local任意容量pile，不复用其他堆总数。Ending先获得当时同source非装备并等待完整gain children；选living使用者并由其选择合法剩余装备顺序，逐个真实Use，完整目标/装备替换/禁止槽/支付/children处理，不能直接移动到装备或先假赠Hand。每次真实卡仍在原source，cursor在真实支付前/后按幂等规范推进，子链移走不重复取用；非法或无法用的余牌保留原pile，不新增弃/转赠。用户明确选择：**只有实际使用过至少一张装备，结束才LoseHp1**；初始空pile、取得非装后零装备、所有装备非法或第一张使用前被移走都不扣体力；已真正使用过则余牌循环结束后扣一次，死亡/比赛结束按已有边界停止。LoseHp是真正濒死/死亡可暂停链。该零装备决定来自用户本会话明确回复，不是推测官方FAQ。

张绣仅补从谏与正式additional registration，保留现有雄乱全部区域废除/定向政策和过滤回归。真实finalized Trick target集合distinct>1，owner在集合，最终redirect完成后一次候选；赠牌对象须在该最终集合且是真正不同recipient，交给自己的同手牌不作为支付，不制造无移动摸牌。此give语义显式冻结，不暗套任意其他living。选本人HE1，实际赠入对方Hand并完成全部movement children后才摸牌；实体Equipment给2，其他1，判断冻结实体类型而非虚拟输出类型。非法非最终目标/非法HE原子拒绝；decline无cost/reward，owner死亡不继续，真实receipt/child恢复不再支付。同owner同skill多grant对一次action去重，来源保留exactinstance。

新rule事实AdvanceEventRulesAndQueueFact，state推进AdvanceRulesAndPublishState；owning typed frame+typed parent return，已付cost不重付；prepared snapshots与集合event深freeze。新可选格式保持旧null省略/注册无cap阴性；不逐将改rules/schema/package版本、不删除历史compat。纯内容loader覆盖不新增逐定义snapshot。小固定fixture/已验证seed，真实movement/choice/checkpoint多viewer，必要shared能力/bug checks；无seed sweep或整局搜索。worker只做现有Core name filters；不得Full/WPF/Git/另写其他worker。冻结manifest列baseline/final SHA、source完整字节、真实验证日志、实际限度；父检验后增量合并。

## 实际整合边界

上文保留原始预检与父冻结的解释优先级。实际生产字节已由三路manifest SHA增量整合；FinalTargetGift、DomainCrossing/PileEquipment和派生Lord资格都沿typed owning frame及command/rule边界运行，未新增人物ID分支或逐将版本变更。觉醒与贡献的派生来源边界经父审查要求补修，最终行为和未测组合、主区Full及实测日常以第五批benchmark和source档案为准。
