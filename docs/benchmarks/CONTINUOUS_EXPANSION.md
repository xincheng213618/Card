# 持续扩展调度

当前优先级为[技能边界整批迁移](../SKILL_BOUNDARY_MIGRATION.md)，新增武将暂停。第一批已提交 `040b9a98`：删除旧被动/主动接口、公开命令别名、拼点人物入口和历史内容指纹投影。第二批整批统一 Program 模型和执行器，删除旧格式解析分支；当前规则 168 / schema 58 / presentation 3，见[第二批记录](2026-09-26-shared-executor-cutover.md)。三个 Sol 按公共能力分工，开发只做受影响检查，整合运行一次 Full，失败后只重跑修复范围。后续仍须清理 C# 技能绑定和模式分派，不把编译通过视为全部解耦完成。

第五批已完成冻结整合验收：SP乐进2015、界周瑜2014、潘璋马忠（将三，2015公告版）。rules166/schema56/经典1.141.0/Checkpoint3；Core547/547、WPF123/123，Release构建0警告0错误。独立交付约39–45分钟，父审另修复骁果天香响应父帧与致胜边界清理。详见[第五批实测](2026-09-26-observer-and-gift-generals.md)。后续核对纠正原报告的84人数：当前完整注册表106位武将，正式身份5/8人模式池均为83位，界限模式池均为49位；历史包清理前后这些ID集合一致。

曹昂（星火燎原-天府，慷忾）已完成单武将交付：schema62/最低规则176/经典包1.146.0。新增用牌窗口观察距离事实 ownerEventTargetDistance、目标自指条件 cardActionTargetIsOwner 与受赠者用牌 useBoundCardByTarget；定向检查 CaoAngChecks 5项，当时 Core 545/545、0警告0错误。官方立绘8皮肤入 general-art-catalog，classic:cao-ang 不再缺立绘（同断言的 xu-sheng 属另一并行批次）。详见[曹昂批记录](2026-09-27-cao-ang-kangkai.md)与[Runtime v57](../content/skill-composition/RUNTIME_V57.md)。

神司马懿（神话再临-山，2010）已完成单武将交付：schema62/最低规则177/经典包1.147.0。新增归属标记计数 ownerAttributedMarkerCount、击杀者事实 deathKillerIsOwner 与挂起额外回合 pendExtraTurn（挂起槽位单一、回合收尾消费、额外回合内可连锁），并修复 ProgramKillTriggerWindowFrame 的可选触发候选回填；定向检查 ShenSimaYiChecks 7项，当时 Core 551/552（唯一失败为孟津系并行批次既有回归）。官方立绘3皮肤入 general-art-catalog，图鉴 god 组新增神司马懿（同断言的 xu-sheng 属另一并行批次）。适配说明：官方拜印奖励“极略”因完杀、放逐、集智等借用药技未收录而未实现，连破并入觉醒奖励并记录于来源 versionBoundary。详见[神司马懿批记录](2026-09-27-shen-sima-yi.md)与[Runtime v58](../content/skill-composition/RUNTIME_V58.md)。
曹丕（神话再临-林，2010）已完成单武将交付：schema62/最低规则179/经典包1.149.0。新增死亡遗留牌申领操作 claimDeathCleanupCards、触发事实 deathVictimHasCards，并把 characterDied 窗口能力位扩为 Common|Death，BeginPlayerDeath 的死亡清理移动统一记录进 DeathResolution.CleanedUpCardIds；放逐复用 turnOver 与 ownerLostHp 无新表达式。定向检查 CaoPiChecks 4项，Release构建0警告0错误；当时 Core 全量 562/565（3失败与更早中间态的借刀失败均属并行批次在制面：绑定戳/天香对应其体力伤害时机改动、计数对应其新增鬼龙斩月刀），WPF 全量被鬼龙斩月刀缺牌面阻断，本批 WPF 面按过滤器验证通过（缺立绘断言不含 classic:cao-pi、complete matches 含图鉴分类通过；format 仅报并行在制的钟会/王异文件）。官方立绘（gid 44，经典形豢104401）入 general-art-catalog；顺带修复该目录既有遗留：zhang-song 条目改以JPEG直接登记导致 --phase verify 中断，已无损转为PNG。主公技“颂威”需势力触发事实与非持有者决策权两类新语义，本批未实现并记录于边界。详见[曹丕批记录](2026-09-28-cao-pi-xingshang-fangzhu.md)与[Runtime v59](../content/skill-composition/RUNTIME_V59.md)。

孙策（神话再临-山，2011）已完成单武将交付：schema62/最低规则181/经典包1.151.0。新增触发条件 cardActionCardIsRed（用牌动作实体牌全红花色，CaptureProgramTriggerFacts 统一捕获、仅用牌窗口可声明）；激昂以 cardUseBeforeTargetEffects 四触发器承载（使用/成为决斗与红色杀的目标；cardUseTargetsFinalized 的 cardKinds 白名单不收锦囊故不用）；魂姿觉醒 changeMaximumHp(-1) 后 grantSkills 复用现行 classic:yingzi/yinghun，授予零新增内容。定向检查 SunCeChecks 4项，Release 构建0警告0错误，Core 全量 574/574 全绿，WPF 面按过滤器验证（缺立绘名单不含 sun-ce、complete matches 通过）。官方立绘（gid55，经典形象105501）入 general-art-catalog，图鉴神话再临·山组新增孙策。顺带修复注册表池变化暴露的两处既有缺陷：AssertCoreInvariants 伤害游标断言不认骑跨嵌套的移牌/体力窗口（DamageCursorEffectiveTop 校验链接后走查），与苦肉濒死 fixture 起手无桃的种子脆弱（SelectGeneral 增可选 fixtureFilter）。主公技“制霸”需拼点与非持有者决策族语义，同颂威未实现并记录边界。另如实记录：全仓 format 检查在 HEAD 基线即 2749 条 WHITESPACE（无 .editorconfig、行尾混杂的历史漂移，既往门槛实为范围化检查）；本批编辑文件逐文件报错数与 HEAD 完全一致零新增，新文件已修复至 0 条。详见[孙策批记录](2026-09-28-sun-ce-jiang-hunzi.md)与[Runtime v60](../content/skill-composition/RUNTIME_V60.md)。

蔡文姬（神话再临-山，2011）已完成单武将交付：schema62/最低规则182/经典包1.152.0。新增触发条件 damageCardIsSlash（伤害 EffectiveCardKind 属杀系含虚拟转化，仅伤害窗可声明）、startJudgment 的 sourceRef（判定来源=伤害来源等事件参与者，续接校验按刚执行指令解析）、伤害窗 eventTarget 目标解析与 AI 估算上下文修复（EstimateCompositionForAi 传入挂起候选上下文，悲歌入池后 AI 选将必触发的既有缺陷）、新操作 chooseOwnCardDiscard（来源等参与者自选弃自己的手牌/装备区牌，逐张无 decline）、turnOver 的 targetRef；校验器 eventSource 兼收 judgmentFinalized。悲歌=费用弃一牌→受害者判定（sourceRef eventSource）→四花色分支（回1/摸二/来源弃二/来源翻面），分支以判定原因限定；断肠复用并行批次已提交内容并随武将首次注册。定向（含断肠）5/5、Release 0警0错、提交态（cf28aab2 隔离 worktree 复核）Core 全量 578/578 全绿；驱虎荀彧一处为牌池位移暴露的种子脆弱断言，改为意图保持的稳健断言（池位移归因有移出复验）。顺带修复一处并行批次既有缺陷：ShenSimaYiChecks marker 拒收样例的 Replace 模式被 82d39a92 的 raw string 改写 whitespace 化而空转（基线 worktree 复现同败），改为无空白敏感等价模式。官方立绘（gid58，经典形象105801）入 general-art-catalog，图鉴 myth-mountain 组新增蔡文姬；WPF 全量仍被既有鬼龙斩月刀缺牌面阻断，按过滤器验证缺立绘名单不含蔡文姬（余 4 项为并行批注册先行的既有缺口）、complete matches 通过；format 沿用范围化口径零新增，新文件 0 条。详见[蔡文姬批记录](2026-09-28-cai-wen-ji-beige-duanchang.md)。

鲁肃（神话再临-林，2010）已完成单武将交付：schema62/最低规则184/经典包1.154.0（183 为并行邓艾/沙摩柯在制占用）。新增数值表达式 handHalfFloor（floor(手牌/2)，限手牌区）与 selectedPairHandDifference（选中对手牌数差，X=0 走既有空绑定路径）、目标类型 otherLivingPair（任意两名其他存活角色，枚举/结算按手牌升序，复用 unequalHandPair 管线放宽不等约束，主动技资格枚举与 GetProgramTargetSeats 双路径支持）与 otherLivingLeastHandCount（并列最少全候选）、新操作 exchangeSelectedTargetHands（互换两选中目标全部手牌，升序经 Processing 成对移动，cardsMoved 窗口管线收尾）；moveBoundCards 目的地白名单扩容 selectedTargetHand。好施=摸二+grantTurnSkills 挂本轮给予技（afterNormalDraw 强制结算，拒绝多摸即无给予，与官方"然后"因果一致）；缔盟=pair 选目标→差值弃牌→整区交换。定向 5/5、Release 0警0错；主树全量 587/591，4 失败全为并行批次在制测试（邓艾×2/沙摩柯×2，其作者迭代中），另按意图保持修复其在制 selectTarget 拼点豁免误删（破坏已提交驱虎，恢复 pindianWon||cardActionCategory 双豁免并记录）。官方立绘（gid50，经典形象105001）入 general-art-catalog，图鉴 myth-forest 组新增鲁肃（并行预检 BWIKI 口径纠正了林包归属）；WPF 过滤验证缺立绘名单不含鲁肃、complete matches 通过；编辑文件 format 0 条。包归属采纳 BWIKI 神话再临·林（2010），官方页系列标注与二手资料不一致处以 BWIKI+图鉴既存分组为准。详见[鲁肃批记录](2026-09-29-lu-su-haoshi-dimeng.md)。

邓艾（神话再临-山，2011）与沙摩柯（一将成名2015）已完成双武将并行交付：schema62/最低规则183/经典包1.153.0（184/1.154.0 由并行鲁肃批在其上层占用，两批分层共存）。新增数值表达式 negatedOwnedZoneCount（判定区外拥有牌计数，屯田减距）、触发值 cardsUsedOrRespondedThisTurn（本回合已用/已响应牌数，计数改为扫当前回合起点之后的 flushed+pending 事件，修正外来回合误判）、数值表达式 currentAttackRange（攻击范围，仅用牌/响应边界窗可声明，CaptureProgramTriggerFacts 补注入 Facts——修复条件恒读 0 的缺陷）与条件 ownerIsTurnPlayer；cardsMoved/CardsGained 能力位扩 Common|Judgment，viewAs 源区接受 authority（field 牌可作转化源，snatch 限定 forPlay 单输入）；顺带修复 FindOwnedPlayableCard/FindOwnedCardLocation 不认 authority 区与 ResolveSnatch 虚拟转化牌型校验不一致。屯田=回合外失牌判定非红心置于武将牌上（field），出牌距离按 field 数递减；凿险=回合开始 field≥3 觉醒减 1 体力上限并授予急袭；急袭=field 牌当顺手牵羊（play-only）；蒺藜=本回合用/响应牌数等于攻击范围时摸等量牌。定向 8/8（DengAiChecks 5 + ShaMoKeChecks 3）、Release 0警0错、Core 全量 591/591 全绿（含并行鲁肃 5 项）；WPF 缺立绘名单不含本批、complete matches 通过；format 新文件 0 条、编辑文件报错行不在本批区间。官方立绘（邓艾 gid52、沙摩柯 gid650）入 general-art-catalog，图鉴山组新增邓艾、一将成名2015 组新增沙摩柯。工作树由鲁肃会话以 5b6368d7 整体提交并入本批。详见[邓艾沙摩柯批记录](2026-09-28-deng-ai-sha-mo-ke.md)与[Runtime v61](../content/skill-composition/RUNTIME_V61.md)。

最新协作约束：最多3个Sol；用户随后要求两个对话停止互发消息，今后以隔离副本、已提交基线和写回哈希保护并行修改。下一批先落实场景前置检查（正式模式前缀、选将候选、真人/AI暂停），再筛选能力复用候选；不把只读方案视为已取得的提速。下文是历史批次记录，其旧版本/未提交/后续候选状态以各自当时为准。

第四批朱治2015、界甘宁2014、界许褚2014首发已完成整合验收：rules165/schema55/经典1.140.0/Checkpoint3，冻结Full为Core525/525、WPF116/116，构建0警告0错误。三路独立交付约35–45分钟，包含新增公共能力与返工；首次Full拦住延时锦囊续接兼容问题，修复后第二次通过。详见[第四批实测](2026-09-26-movement-and-target-generals.md)。本任务保留其他任务仍在实施的音频改动，并按用户授权及时本地提交。

前三批首页/图鉴及八名武将已合并提交2674abd6；立绘和界面修复另有973df66c、a6d43cf，副本准备工具提交25a69906。下文“未提交”等表述是各历史时间点记录。下一批候选预检结果在本地第四批证据的next-candidates.md和next-reuse-candidates.md，尚未实施；当前未找到可诚实标为零Core的候选，不为凑并发数混用不同年份技能。

2026-09-26 起，本对话统一负责架构、审查、整合与耗时记录。用户授权持续 goal 和暂停原持续迭代任务；尝试四个 `gpt-6-sol` 后，因当前工具容量限制，用户最终同意同时运行三个 Sol。这一新授权覆盖 goal 建立时的两路上限。

- 原任务「澄清迭代目标」已在 rules 162 / schema 52 / 经典包 1.138.0 完成程普迁移并暂停。交接 Core 466/466；源码和未提交成果全部保留。
- 第一批：顾雍、李典已于 14:26:37 整合到主工作区。父审发现的公共伤害时序及嵌套续接已修复，rules 163／schema 53／经典包 1.139.0，Core 478/478、WPF 109/109。独立实现约 20/30 分钟，整批含公共修复约 86 分钟，记录见 [本批实测](2026-09-26-new-generals.md)。下一批为界司马懿、2019 界郭嘉和界貂蝉；后两人的部分技能复用已有 SkillKind，不称全 JSON。
- 已尝试第 4 个子进程，工具返回 `agent thread limit reached`，当前按协调者 + 3 个 Sol 执行。四任务候选队列按空闲槽位滚动安排，不把排队计为活跃并发。
- 同一批实现使用相同、可复现的源码快照及独立输出目录；主工作区共享机制由协调者统一修改和整合。
- 派发前冻结技能版本、分类、缺失能力与验收场景。现有能力足够时只增加内容、注册和必要测试，不增加人物专用引擎、加载器或 UI 分支。
- 不让工作进程争抢 schema 或全局规则版本。确有共享语义变化时由父任务按整批统一分配，完成一批才跑整批 Solution/Core/WPF。
- 将资料筛选、工作进程实现、父审返工、整合等待、整批验证分别计时。首个可编译草稿不等于可交付；失败重试不从总耗时剔除。不将两路并行墙钟除以二作为单名延迟。
- 对比下一批纯组合样本与首批能力扩展样本；没有匹配的优化前实验时不报告提速倍数。
- 后续优先消除已观察到的耗时：派发前确认入口/节点组合、复用实际检查名称、避免向旧 schema bundle 塞入新节点；将源码换行统一后做三方整合，减少无意义冲突。
- 用户于本批完成后明确授权及时提交；以后每批验收后做本地提交，保留其他任务工作，不自动推送、发布或清理未提交成果。旧heartbeat保持暂停；持续推进由当前goal管理，用户要求暂停时停在安全点。

下一次接续先读取本批记录和运行产物，确认上一批验证与源文件哈希后再派发，避免重复新增或覆盖其他工作。

第二批已于 **14:55:58** 验收并写入主目录：界司马懿、2019 界郭嘉、2019 界貂蝉；Core **486/486**、WPF **111/111**。三名 Sol 独立开发约 12～14 分钟，最早派发前标记到整批验收为 26 分 38 秒，首轮全量即通过；详见[三路实测](2026-09-26-three-generals.md)。rules 163／schema 53／经典包 1.139.0 不变，未提交或发布。证据在 `%TEMP%\Card-NewGenerals-20260926-batch2\acceptance-measurements.json` 与 `integration-applied.json`；三个 `batch2-src` 保持交付冻结。

第三批已于 **15:51:33** 写回：朱桓2014、界曹操2014选择版、界张辽2018。冻结副本Full为Core503/503、WPF113/113，rules164／schema54／包1.139／Checkpoint3。公共AI修复影响既有拒战及经典突袭，因此统一升规则版本。三路独立17～28分钟，派发到写回49分34秒，详细分项见[第三批实测](2026-09-26-capability-generals.md)。写回与「查找并统一立绘样式」协调，保留其新GeneralArt并追加三人映射；该对话随后完成结合新皮肤服务的14项WPF定向检查及本机运行包验证，摘要在 `.artifacts/portrait-refresh/verification-summary.json`。武将验收快照单独提交，其并行立绘工作区改动保留。

用户再次明确本对话最多3个Sol，授权和立绘对话持续协调。本对话负责武将、Core、Content与架构；对方负责素材、详情和皮肤，共享注册／映射／测试入口写入时约定安全点。下一批候选朱治2015、界甘宁2014、界许褚2014已完成只读资料预检，尚需明确公共接口、准备稳定基线后派发，勿按已实现计数。

独立待复现：雷击致胜路径可能在完成游戏后未恢复外层 Slash 的清理。静态对照显示主仓旧实现已有此路径，本批没有改动；尚无命令级复现，不能写成已确认缺陷或已修复。
