# 滕芳兰单武将批次

当前 OL 滕芳兰（吴势力、3 体力、女、称号"铃兰凋落"，官网 500 号，璀璨星河-天极包、传说品质、2022-09-09 上线）接入：落宠（准备阶段或当你每回合首次受到伤害后，你可以选择一项，令一名角色：1.回复1点体力；2.失去1点体力；3.弃置两张牌；4.摸两张牌。每轮每项每名角色限一次）、哀尘（锁定技，当你进入濒死状态时，若"落宠"选项数大于1，你移除其中一项）。官方立绘 50000 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-teng-fang-lan-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 技能 | 实现口径 |
| --- | --- |
| 落宠 | luochong-preparation：turnStartBeforeNormalFlow 可选触发（自己回合的准备阶段边界，与闭境重铸同窗口）；luochong-damage：afterDamageApplied 可选触发 + `damageInstancesTakenThisTurn == 1` 条件（石壁同款"每回合首次受到伤害"口径，任何回合内自己的第一次伤害，含自己的回合）。两者共用新 op `luochongResolve`（7276）：一个公开合并提示同时列出全部可用的（选项 × 目标）组合与"不发动"退出项；目标为全部存活角色（含自己）。选项合法性：回复要求目标已受伤、弃置要求目标手牌+装备区至少一张牌、失去体力与摸牌恒可选；哀尘已移除的选项不再出现。每轮账本（每项限一次 + 每名角色限一次，两项独立）从已提交事件史按 OwnerSeat+RoundNumber 推导（诗怨/择才同款 committed-history 口径），触发前 CanRun 门控按可用组合是否存在放行。分支执行：回复/失去体力走共享 Recover/LoseHp 原语（失去体力致 0 时濒死子结算挂起、指令游标回绕重入后落账），摸牌走 DrawCards + cards-moved 窗口，弃置为目标逐张自选提示（闭境惩罚同款帧内状态机）。`ProgramLuochongResolvedEvent` 记录选项 id、目标座位、回复/失去体力值、弃置牌 id、摸牌张数。 |
| 哀尘 | aichen-remove：dyingEntering 强制触发（进入濒死、求援前的完整程序窗口，每次濒死进入恰好一次；dyingEntered 被平台限制为仅自动摸牌，不能承载提示）。新 op `aichenRemoveOption`（7277）：剩余选项 = 4 − 已移除（永久，事件史推导），大于 1 时弹出公开选择，拥有者从剩余选项中选一项永久移除；小于等于 1 时静默跳过。`ProgramAichenRemovedEvent` 记录被移除选项与移除后剩余数；移除立即影响后续落宠候选，本次濒死未被救回（角色死亡）不回滚。 |

## 共享能力扩展

- 新增 EffectOp 7276–7277（只占用分配的 7276–7283 段）；描述符按反射目录自动注册；AI 语义复用既有 GainCards / ChooseOption，未新增 `ProgramOperationAiSemantic` 成员。未新增 PlayerMarkerKind、TriggerFactKind、ConditionKind、触发窗口或 schema 节点；rules.json/presentation.json 均为既有节点。
- 落宠的组合校验资源声明使用既有 `RequireTriggerWindows`（"其中之一"语义）声明 turnStartBeforeNormalFlow / afterDamageApplied 两个窗口。
- 其余全部为既有共享口径复用：每回合首次受伤（石壁条件口径）、濒死进入窗口（dyingEntering）、逐张弃牌提示（闭境惩罚模式）、可选触发估算（GainCards 语义 + EstimateCompositionForAi 通用路径）、每轮账本（事件史推导，无新增序列化运行时状态）。无新增共享机制。

## 边界口径

- 落宠："每轮"按引擎共享轮次账本（`_roundNumber`）计，与官方轮次定义一致（额外出牌回合不推进轮次）；每轮每个选项限一次且每名角色限一次，两项账本相互独立（官网紧凑句"每轮每项每名角色限一次"与 OL 专站展开句"每轮每个选项与每名角色各限一次"语义一致，按展开句实现）。目标含自己（"一名角色"未排除自己）。
- 选项区域口径：弃置选项的区为手牌+装备区，判定区牌不可被此选项弃置（官方文本未指明区域，按本仓"弃置牌"常用区口径实现并如实记录）；"弃置两张牌"在目标只有一张牌时弃置一张（不足全弃），由目标自选。回复选项在目标满体力时不可选（无操作不进入候选）；失去体力可将目标打入濒死（此时进入完整濒死流程，哀尘等濒死窗口正常嵌套触发）。
- 账本时点：每轮账本只统计"已完成的发动"；同一提示内的并选组合互不挤占（提示时可用性已按账本过滤，选择后立即落账）。拥有者死亡后不再触发（程序触发统一要求拥有者存活，平台既有边界）。
- 哀尘："落宠"选项数 = 4 − 哀尘已移除数（每轮已用过的选项不计入——它们下一轮恢复）；移除时机为进入濒死时、求援开始前（dyingEntering），若该次濒死未被救回，移除不回滚（锁定技进入时已结算）。移除选择为公开信息（落宠的可用选项本就是公开状态）。
- 冷恢复：两个技能的全部状态（每轮账本、已移除选项）都由已提交事件史推导，无运行期私有映射；按接受命令前缀确定性重放得到相同可用性。落宠摸的牌进目标手牌，事件只记张数（暗手牌 id 不进普通快照）；弃置牌 id 为公开弃牌堆移动，逐张记录。
- AI 策略：落宠触发估算按约 2 张摸牌计价（GainCards 语义）；挂起选择用共享 `ScoreProgramTarget` 按选项定价（摸牌 TargetDraw=2、回复 TargetRecovery=1、失去体力 TargetHpLoss=1、弃牌 TargetValueAdjustment=−10），敌我关系由公开快照推导，全负分时回落到"不发动"；目标自选弃牌按选择 id 稳定取第一张。哀尘移除按 回复 → 失去体力 → 弃置两张牌 → 摸两张牌 的保留价值顺序移除估值最低项。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查。
- Release 构建（`dotnet build CardGame.sln -c Release` 全解决方案）：0 error；9 条 src/Core 既有 warning（SkillProgramExecutor、ActualHandGainPrograms、EndingHistoricalUses、LiangXingPrograms、OwnedDeathBenefitReturns×3、PublicPilePreparation、SameTypeActualUseAid）全部位于本批未触碰的既有文件，本批新增/修改文件无任何 warning；tests/ 既有 warning 与本批无关（未触碰 tests/）。
- 内容静态校验：`tools/Inspect-SkillProgram.ps1` 对 ol-teng-fang-lan.rules.json + presentation.json 解析通过——ol:luochong 两个触发器（luochong-preparation=TurnStartBeforeNormalFlow、luochong-damage=AfterDamageApplied）与 ol:aichen（aichen-remove=DyingEntering）均按描述符目录解析成功；开发中以"general content modules preserve roster and registry boundaries"注册检查做只读探针通过（据此修掉一处资源声明缺陷：落宠双窗口误用单窗口 `RequireTriggerWindow` 的 AND 语义，改为 `RequireTriggerWindows`）。
- 例行 `tools/Test-Changed.ps1`（无过滤，ArtifactsPath 独立目录 `artifacts-teng-fang-lan`）：全解决方案构建 12.7s、Core 214 项过滤联合执行 100.4s（266 通过 / 57 失败 / 0 跳过，共 323 项）、WPF 18 通过 / 0 失败（18.5s）。失败集合与严畯批基线（`artifacts-yan-jun` 实测 57 项，说明见 2026-10-07-ol-yan-jun.md 验证节）逐项 diff 完全一致（57=57，通过数亦一致 266=266），无新增失败；全部失败均为基线既有的在制特性检查（狂斧、离魂、强武、源咒、SP 群雄等），无任何滕芳兰相关失败。
- 本批在独立 worktree（batch/ol-teng-fang-lan）开发，与另两路 OL 普通版批次并行；本批不写任何测试为用户硬性指令。
