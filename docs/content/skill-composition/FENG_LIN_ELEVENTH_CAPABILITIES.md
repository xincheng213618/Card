# 第十一批：界甄姬、界黄月英

共同基线为 a1763931fcf2b385cce9f2cd57cef3b5bddb6992。baseline-manifest.json SHA 54d6be0e8042584e2451eb9e4e9e76ede6c89f6ce5909caafe24f8c538a453b5，5069个tracked工作字节；各worker只在指定副本写实现，主仓、另一worker及既冻证据只读。source-preflight/evidence-manifest.json SHA f59d7a8b13066295af4c5dcc7a131ae3b2644547f571dd245cbeda48b366a698，51项来源已验证。普通OL官网444/441当前完整正文是内容依据，不混移动/十周年/历史版。

正式general为 boundary:zhen-ji、boundary:huang-yueying；portrait boundary_zhen_ji、boundary_huang_yueying；resource44400/44100；魏/蜀、HP3/3。Female沿已有classic同角色明确值，官网结构化gender仍null。独立内容module及rules/presentation交付；root拥有最终统一注册、art、gallery/catalog/sync、文档、主区合并验证及本地commit，无push。

## 界甄姬

倾国复用classic:qingguo。新技能boundary:luoshen，判定reason保留skill.luoshen，不能因换reason重开成熟天妒排除。新repeatJudgment optional claimHandLimitExemption:'actualTurn'只给新optin；旧null缺省调用、JSON省略、最终花色/移动/提示/RNG等保持。

新能力只在合法owner准备阶段执行。真实FinalizeJudgment点冻结owning ProgramRepeatedJudgment最终花色、JudgmentId和实际实体；不要after-child重新算来源技能已经变化的EffectiveSuit或误读Finalize前pending.Suit。真实Judgment→owner Hand的移动receipt在任何后续gain/movement child前冻结，只有实际入手的最终判定实体发exact-card actual-turn豁免；改判旧实体、失败红牌、其他claim/转移抢走、OutsideGame重定向、原有手牌及该回合其它获得牌绝不计入。

豁免独立typed actual-turn scalar grant store，实际turn owner/number+CardId+JudgmentId+movement receipt身份冻结；不是+张数，也不是全回合得牌过滤。GetDiscardEligibleHand只排当前手内本owner已实际获得的exact IDs；已获奖励不因技能失去/临时无效/来源死亡撤销，actual turn结束清理。帧和暴露嵌套集合深冻结，无use-ID sidecar或从历史猜receipt。旧null store/query保持无影响。

工程解释：实体本回合离手再回原owner仍豁免，转到别人手不豁免；黑色最终牌被别的真实child取走时仍依被冻结黑色结果提供继续/停止，未入owner Hand不发grant。二者非用户明确裁决或额外官网FAQ；记录为工程默认。死亡/真实回合变化有限结束，不向后来turn发旧豁免。实际源丢失的成熟暂停行为与已付款receipt生命周期分别检查，必要时只为newoptin最窄typed return，不全局放宽旧program。

## 界黄月英

新skills boundary:jizhi-current / boundary:qicai-current；现batch9-support boundary:jizhi/qicai字节与fingerprint不改。新奇才Locked，集智optional。

新增nullable true-only trigger资格requireNoCardConversion，仅查该真实frozen CardAction ConversionChain.Count==0；不要物理kind==effective替代。正常trueUse采用CardUseTargetsFinalized actor +完整Trick category，包含三延时；无懈采用成熟CardResponseAccepted actor +精确Nullification，仅真实该无懈action的typed return，不把其它Response/Played算使用，不扩现committed includeResponseUses允许操作，不重复触发或付cost。旧资格null短路、serialized旧形状/RNG等保持。

集智先纯现成 draw1 actual resultBind receipt→keep/discard选项，discard condition all(boundBasic, boundCardCountAtLeast1)→strict move本receipt至discard→本actualTurn handLimit add1。真实gain child拿走receipt/零Draw只keep；原手牌或兄弟gain不能替代。选项发布与提交再验真实位置，只有真正弃本张Basic才能奖励；回合外无懈奖励当前实际回合，不永久、不迁移下个自己回合。优先完全复用旧节点，无真实可达失败证据不加新的available-subset/observer/return ABI。source loss/移动child/死亡/恢复必须核一次付款奖励；若有真实不变量失败，原accepted prefix、错误日志、typed链先冻，再报root批准最窄修复。

奇才增加仅newpolicy启用的Armor/Treasure foreign Discard保护，用实际actor、owner、physical entity/from和明确Discard intent统一predicate。不能从reason、ToDiscardPile、当前turn或任意栈frame猜actor。候选与支付前都查：actor!=owner、From该owner Equipment、actual slot Armor/Treasure、owner存活及有效locked policy才防止；先防止再物理Move/Processing/移除钩子/receipt，不能事后回滚。没有该newpolicy的旧路径候选次序、付款、RNG保持。

必须覆盖Dismantlement/真实其他角色HE弃置选择、generic参与者付款、outsideRange、寒冰剑、批量弃equipment等设计报告列出的实际foreign discard入口；自弃成本、被要求本人自行弃牌、Snatch/Obtain、equipment replacement/transfer、死亡清理皆通过。仅有受保护装备而无其它合法弃牌不提供该弃牌动作；多牌强制弃置不足按真实可弃子集有限完成；冻结后新保护使选择失效沿成熟reject/cancel/skip机制，无秘密换牌。以上候选/不足处理是工程默认，未称官网FAQ。优先纯query/既有帧冻结意图；如果newpayload含集合必须纳入CommittedEventProjection。

## 验证及交付

每将五组focused机制检查，覆盖新能力而非每人物重复definition snapshot；固定小pool/显式deck/verified seed，真实accepted commands、非法输入不前进、cold replay、actual receipt/conservation、四viewer隐私、最终来源资格、旧null/config边界。直接host探针与真实command集成证据标签分开。只有新UI行为才增共享WPF检查。既有裁剪、fixture/版本源不得恢复或改号；普通内容/新optional ABI不升rules/schema/package。

新fact用AdvanceEventRulesAndQueueFact，state用AdvanceRulesAndPublishState；保持command commit/recovery、typed owning frames/returns、一次cost、深冻结/隐私。不得用反射造pending宣称真实集成。

worker仅开发focused tools/Test-Changed.ps1；整批Full及无filter measured routine由root最终执行。delivery冻结source/<path>，manifest baselineHead/baselineManifestSha256/contractSha256、files[{path,baselineSha256,finalSha256}]，new baseline null；validation/evidence每文件SHA及真实命令耗时/结果。未交付不得写pending为通过；失败与修复分immutable目录，冻结交付后修复另delta。不在worker Git commit/push/cleanup或开用户新thread。source manifest不包含root专属注册/art/docs变更。


# 明确绑定牌的真实弃置选择者

原 CONTRACT.md 字节不改。共享 HE 选牌辅助函数已确认同时服务弃置、取得、转移，所有调用必须显式传实际 intent；不得从默认 destination 或 reason 推断动作。独立审查 manifest 9f1ac29999bb031fa5c1fb14b7b2ddaa3c9e765bf38236ab2a29f354a54459f8、addendum 54b1313e9d0f8dc28e96874ac84e0388ab4682bd07dc393e5b2fc70f2d98f502 共89项已由root逐SHA核对，六独立入口与三非弃置调用均已同步。

准许 ProgramSkillCardSetBinding 的 nullable scalar SelectionActorSeat，在 registry 宣告新 PreventForeignEquipmentDiscard capability 时由真正已执行选牌 producer 冻结。SelectOwnedCards 真实选择者为其 CardOwnerSeat/PendingDecision.PlayerSeat，自动本人选择沿本人；SelectSourceCard 选择者是实际 frame.OwnerSeat。subset/filter 必须继承已冻结原始选择者，不能按同bind静态配置猜曾经执行了哪个条件分支。freeze/with clone保留scalar；只在公开合法入口写，信任不变量核真实actor/producer及有效seat，不加并行pending/sidecar。

gating 是registry声明新policy而非当前owner启用，因为选择后可以获得保护；该registry拥有不同content fingerprint。旧无此capability registry字段null并序列化省略，旧操作未选此语义的缺省分支保留。MoveBoundCards 明确Discard时用冻结chooser；非选择producer沿明确owning discard instruction actor。Transfer/Obtain/Replace/Cleanup即使某辅助默认目的地写DiscardPile也不阻止。

这解决新奇才的真实foreign discard与participant自弃互操作，不授权全局 movement拦截、observer松绑或旧规则epoch/schema/package变更。新增检查合并在现五focused组中，至少真实participant SelectOwnedCards→boundDiscard自弃通过与foreign SelectSourceCard→boundDiscard保护，两条接受命令链及cold replay/一次cost分开记录。


# 新洛神最终判定子树校验

原 CONTRACT 字节不改。实际死亡子技能失败的 review01 manifest 36e6828509f93fa9665bd63005e9bb86f342a61c953a35e06f0f48e700afe558 共14项、review02 manifest acae1b58b51848cdfc5932bb5fd4d15acbccca2d3edc9a622048e7f81c4b44a7 共4项由root逐SHA核对。失败命令前接受链与失败中的真实faulted typed stack区分：TurnStart17→Luoshen18→Judgment19→FinalWindow21→finalized ProgramChild22→Dying24（parent22）。原339–362校验仅识别top ProgramSkillFrame，真实子树使该条件失败；未取得判定牌，没有claim receipt。

批准只新增 new ActualTurn repeat opt-in 的 OR resolvingProgram qualification。要求判断/window真实相邻，exact repeated owner等于Judgment.ParentFrameId、拥有当前repeat instruction，FinalOutcome.JudgmentFrameId/CardId/Kind/FinalSuit对应窗口冻结判定；window后第一ProgramChild为该窗口的JudgmentFinalized child，ParentFrameId精确等于window.Id。保留原pending、实体、cursor/prompt全部校验，可进一步核Activated/current candidate与child技能/trigger/instance一致。

不得仅Any ActualTurn祖先/非null outcome就许可，也不能要求ownerAlive/sourceLive来拒绝合法死亡及随后实体清理。旧null、旧判断、observer、typed parentreturn与executor不变；死亡、救援、source-loss行为须沿真实接受命令和cold restore验证，再宣布相应路径通过。回合没有真实结束不冒称expire事件发生；已获receipt与未领取实体清理分别验证。


# 新洛神的可选 AI 发动

正式optional洛神在首次原生AI夹具中沿 RepeatJudgment 旧descriptor空估值，Score为0而合法跳过，因此没有实际receipt；mandatory generic probe不能替代正式技能AI完成证明。失败夹具及其改为mandatory的中间记录保留且区分。

批准仅 RepeatJudgment descriptor 的新 ClaimHandLimitExemption.ActualTurn 分支，以成熟 context.Draw(new SkillProgramEffect(Draw, Owner, 1, effect.Condition))给正公开取牌估值。正式黑两花色在均匀四花色先验的连续判定期望为1张；不读取实际私有牌堆、对手手牌或使用RNG。旧null仍原no-op，不修改全局 ProgramCompositionAi 或旧repeat政策。

在原五组中的AI边界改为正式 boundary:luoshen optional：证明实际activation、真实判定获得scalar receipt、原生AI正常弃牌和四viewer cold replay。移除重复mandatory probe，不加人物runner/框架/版本号。
