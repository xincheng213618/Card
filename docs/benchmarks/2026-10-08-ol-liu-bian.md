# 刘辩单武将批次

当前 OL 刘辩（群势力、3 体力、男、称号"弘农怀王"，官网 487 号，武将包 荟萃-皇家贵胄，2020-12-28 上线）接入：诗怨（每回合每项限一次，当你成为其他角色使用牌的目标后，若该角色的体力值大于/等于/小于你，你可以摸三/二/一张牌）、毒逝（锁定技，①当你死亡时，你令一名其他角色获得此技能；②当你进入濒死状态时，其他角色不能在此次濒死结算中对你使用【桃】）、余威（主公技，锁定技，其他群势力角色的回合内，"诗怨"改为"每回合每项限两次"）。官方立绘 48700 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-liu-bian-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 技能 | 实现口径 |
| --- | --- |
| 诗怨 | target-draw：cardUseTargetsFinalized、ownerRelation target、onlyDesignatedCardTargets（借刀偶数目标口径同荐降）、可选、条件 not(cardActionActorIsOwner) → 新 op `shiYuanTargetDraw`（7236）按用牌者与你的实时体力值（大于/等于/小于）取摸三/二/一；每回合每项用量从 `ProgramShiYuanTargetDrawEvent` 账本按（回合号, 摸牌数）重建，余威生效时上限 2 否则 1。可用性在候选资格（`CanRunShiYuanTargetDraw`，用尽的项不出提示）与执行时双重校验；只有接受摸牌才计入账本，账本为已提交事件推导、无序列化运行时状态。 |
| 毒逝 | ① death-grant：ownerDied、subject owner、强制 → selectTarget(otherLiving) + 新 op `duShiGrantSkill`（7237）经共享 `AcquireRuntimeSkills` 让一名其他存活角色获得完整"毒逝"（含两条），`ProgramDuShiGrantEvent` 记录传承；继承者死亡时同触发再次传播。② cardPolicy `dyingSelfRescueOnly`（新共享策略，仅【桃】）：共享救援闸口 `CanUsePeachToRescue` 在"救援者非濒死者且濒死者持有该策略"时拒绝；濒死者仍可自救。逐次救援判定即"此次濒死结算"范围；策略随技能实例（含传承获得的运行时实例）生效，无需触发器。 |
| 余威 | qun-turn-activation：playPhaseStarting、subject owner、turnOwnerScope otherLiving、强制、条件新尾部条件 `turnOwnerFactionIs(["qun"])` → 新 op `yuWeiMarkActiveTurn`（7238）提交 `ProgramYuWeiActiveTurnEvent`；诗怨按"当前回合号存在该事件"取上限 2。注册带 SkillTag.Lord \| SkillTag.Locked（循 boundary 暴虐 模式）。 |

## 共享能力扩展

- 新增 EffectOp 7236–7238（只占用分配的 7236–7243 段）；描述符按反射目录自动注册，AI 语义复用既有 GainCards/GrantSkills/ChooseOption，未新增 ProgramOperationAiSemantic 成员。
- 新增 `SkillProgramTriggerConditionKind.TurnOwnerFactionIs`（1027，枚举尾部）与依赖开关捕获的 `SkillProgramTriggerFacts.TurnOwnerFactionId` 事实（条件未启用时为 null，不影响既有指纹）。
- 新增 `SkillProgramCardPolicyKind.DyingSelfRescueOnly`（6604，枚举尾部）与 `CanUsePeachToRescue` 中一行拒绝分支：该函数是标准濒死、转化 view-as、牌堆求助等全部濒死救援路径的唯一闸口；策略缺失时行为逐位不变。
- 未新增 PlayerMarkerKind、SkillProgramTriggerFactKind 值、触发窗口或 schema 节点；rules JSON 保持 schemaVersion 62，未触碰 GameCheckpoint.CurrentRulesVersion、SkillProgramCatalog.RulesSchemaVersion 与包版本。

## 边界口径

- 诗怨：判定点为"目标定格"（CardUseTargetsFinalized），先于该牌效果结算，与荐降同时序；仅"使用"计入（响应不触发），无目标与自目标用牌不触发；多目标牌对其只提示一次。借刀杀人只认偶数位指定目标（荐降既有口径）。体力值比较取目标定格时的实时值。可选项按"摸三/摸二/摸一"独立计数：同一回合对体力更高与更低的用牌者可分别摸三、摸一；拒绝提示不占用当项机会。余威上限按摸牌结算时刻的账本判定。
- 余威：证据在"其他存活群势力角色"的出牌阶段开始时提交（turnOwnerScope 为 turnStartBeforeNormalFlow 所不接受，出牌阶段边界是覆盖该回合内全部用牌的最早共享窗口）；同回合在出牌阶段边界之前发生的用牌（判定/摸牌阶段的特殊用牌）以及出牌阶段被跳过的回合不放大上限，为如实记录的缺口。身份模式下主公技不作角色压制（暴虐/黄天等同口径）。
- 毒逝①：死亡时由死亡的刘辩本人在其他存活角色中选择继承者（classic:zhuiyi 同窗口先例）；继承获得完整技能，故毒逝可随继承者的死亡继续传播。无其他存活角色时该绑定因无合法目标整体不触发。
- 毒逝②：只限制"其他角色"对濒死中的策略持有者使用【桃】；持有者自救、对其他角色的救援不受影响。限制经共享闸口逐次救援判定，天然覆盖"此次濒死结算"范围，濒死结算结束即随状态消失。
- 全部新事件（诗怨计费、余威证据、毒逝传承）均为标量公开事实；暗手牌 id 不进任何普通快照；冷恢复按接受命令前缀确定性重建。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查。开发中以既有注册检查做只读探针（"standard package builds an immutable isolated registry"、"content registry compiles whole catalog dependencies"）：首跑暴露 `ol:yuwei` 触发器 `turnOwnerScope` 与 `turnStartBeforeNormalFlow` 不被 schema 接受（包静态初始化失败），改用 playPhaseStarting + otherLiving 后两项探针通过；`tools/Inspect-SkillProgram.ps1` 确认三技能按预期窗口与节点编译（gameplayHash 见 inspect 产物）。
- Release 构建（`dotnet build CardGame.sln -c Release --no-incremental` 全解决方案）：0 error；20 条 warning 全部为既有（src/Core 9 条：SkillProgramExecutor、ActualHandGainPrograms、EndingHistoricalUses、LiangXingPrograms、OwnedDeathBenefitReturns×3、PublicPilePreparation、SameTypeActualUseAid；测试文件 11 条），无一在本批新建或修改的文件中。
- 例行 `tools/Test-Changed.ps1`（无过滤，工作树 01f09cf5，耗时 131 秒）：Core 266 通过 / 57 失败 / 0 跳过（323 项），WPF 18/18 通过。失败集合与基线（1fc7b1d6，本批内容加入前在独立临时克隆中重跑同例行实测 266/57）逐项 diff 完全一致（57=57，通过集合亦逐项一致），无新增失败；基线 57 项的构成说明见 [2026-10-07-ol-yan-jun.md](2026-10-07-ol-yan-jun.md) 验证节。
- 本批在独立 worktree（batch/ol-liu-bian）开发，与杨仪、朱灵两路并行。
