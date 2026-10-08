# 刘宏单武将批次

当前 OL 刘宏（群势力、4 体力、男、称号"汉灵帝"，官网 507 号，璀璨星河-天极包、史诗品质、2022-08-28 上线）接入：鬻爵（出牌阶段限一次，你可以废除一个装备栏，然后令一名有手牌的其他角色交给你一张手牌，其直到你的下回合开始获得"执笏"）、图兴（锁定技，当你废除一个装备栏时，你增加1点体力上限并回复1点体力。当你的所有装备栏首次被废除后，你减少4点体力上限，并令你本局造成的伤害+1）、执笏（锁定技，每回合限两次，当你对其他角色造成伤害后，你摸两张牌；授予技能，鬻爵令其他角色获得）。官方立绘 50700 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-liu-hong-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 技能 | 实现口径 |
| --- | --- |
| 鬻爵 | yujue-launch：playPhaseStarting 强制触发（吕凯图南/黄承彦接疹同款"出牌阶段限一次"窗口，每回合恰好一次）；"你可以"的退出项在 op 提示内（择才同款）。新 op `yujueResolve`（7302）一条串起三段提示：①拥有者选废除的装备栏（五栏枚举序，仅列容量>0 栏 + "不发动"退出）；②拥有者选交牌角色（有手牌的其他存活角色）；③目标自选一张手牌交出。挂起状态 `YujuePendingState` 挂程序帧（落宠同款）。废除走共享装备栏容量机制 `SetEquipmentSlotCapacity(slot, 0)`（陆抗决阉同款，发布 EquipmentSlotCapacityChangedEvent；栏内装备牌随废除入弃堆并走 cards-moved 窗口续接）；交牌后经 `AcquireRuntimeSkills` 给目标授予 ol:zhihu（源 `acquired:zhihu:{拥有者座位}:{帧id}`，择才同款自定义前缀 + 择时清除）；`ProgramYujueInvokedEvent` 记录废除栏、目标、给出牌 id。发动前 `CanRunYujueResolve` 门控（存在未废除栏且存在有手牌的其他存活角色）。 |
| 图兴 | 引擎无"装备栏废除"触发窗口，两子句由鬻爵废除流程内联结算：废除后 +1 体力上限（`ChangeProgramMaximumHp`）+ 回复1点（共享 Recover 原语）；五栏容量全部为 0 且事件史中无该座位的 `ProgramGameDamageBonusArmedEvent` 时，-4 体力上限并武装全局伤害 +1（"首次"防重按 committed-history 推导，择才/落宠同款）。保险 catch-up：ol:tuxing 另声明 turnStartBeforeNormalFlow 强制触发（条件 compare(currentAvailableEquipmentSlotCount==0)，触发事实 1400 既有）+ 新 op `tuxingArmGameDamage`（7304）幂等补武装，覆盖"鬻爵之外废除"的未来能力（当前内容中不存在）。全局伤害 +1 经共享武装事件接入伤害管线（见共享能力节）。 |
| 执笏 | 纯内容表达，无 bespoke 结算 op：zhihu-draw = afterDamageApplied + subject any + damageOccurrence perDamage + condition all(damageSourceIsOwner, damageTargetIsOther)（典韦耀冥同款"对其他角色造成伤害后"口径）+ usageScope turn + usageLimit 2 + effects draw(2)（既有 draw op）。失效：zhihu-expire-own（own 回合）与 zhihu-expire-other（turnOwnerScope otherLiving）两条 turnStartBeforeNormalFlow 强制触发共用新 op `zhihuExpire`（7303）：解析授予源里的拥有者座位，等于当前回合座位时移除授予——即"直到你的下回合开始"，期间其他角色的回合开始不移除。 |

## 共享能力扩展

- 新增 EffectOp 7302–7304（分配段 7300–7307 内；7300/7301 在基线已被 OL 杨修/SP 庞德批占用，取号前逐一核实空闲后从 7302 起）；描述符按反射目录自动注册；AI 语义复用既有 GainCards / ChooseOption，未新增 `ProgramOperationAiSemantic` 成员。未新增 PlayerMarkerKind、TriggerFactKind、ConditionKind、触发窗口或 schema 节点。
- **新增共享能力：全局伤害加成武装**。`ProgramGameDamageBonusArmedEvent(FrameId, SkillId, BindingId, SkillInstanceId, OwnerSeat, Amount)`（通用命名，任意内容技能可复用）+ 伤害管线一处通用接入：`FinalizeAttackDamageAmount` 的 programDamageModifiers 串联 `GetArmedGameDamageBonuses`（按伤害来源座位读取已提交武装事件，返回 `(CardUseEffectSource, Amount)`，含传导伤害、不限牌种；`ProgramCardDamageModifiedEvent` 与伤害日志照常发布）。此前引擎只有回合级 `grantTurnCardDamageModifier`（两种过期语义）与声明式 damageModifiers（OwnerUsed 路径排除传导伤害且要求具体牌种），没有"本局剩余时间对一切伤害 +N"的表达；图兴②的 OL 文本（无传导排除）需要该能力。缺口已补记 `docs/content/CORE_API_REQUESTS.md`。
- 其余全部为既有共享口径复用：装备栏废除（共享装备栏容量机制）、体力上限增减（ChangeProgramMaximumHp/MaximumHpChangedEvent）、回复（共享 Recover 原语）、技能授予（AcquireRuntimeSkills + 择才同款自定义源前缀与择时清除）、"对其他角色造成伤害后"（耀冥条件口径）、每回合限两次（usageScope/usageLimit）、组合提示状态机（落宠同款帧内 pending）。

## 边界口径

- 图兴①"当你废除一个装备栏时"由鬻爵废除流程内联结算：当前内容中刘宏装备栏废除仅来自鬻爵，覆盖全部可观察行为；未来若出现其他废除能力，①的即时结算需要共享"装备栏废除"触发窗口（本批未新增窗口枚举，如实记录）；武装子句有回合开始幂等补账兜底。
- 执笏失效点为刘宏下回合的 turnStartBeforeNormalFlow（回合开始、正常流程前）；刘宏死亡时其回合永不开始，执笏按字面持续存在（官方无明确裁定，如实记录）。
- 废除栏内有装备牌时牌随废除移入弃牌堆（共享容量机制语义）；官方文本未描述装备牌去向，按本仓机制口径实现并记录。
- 全局伤害 +1 作用于来源座位的一切伤害（含铁索传导、程序伤害），与 OL 文本一致；十周年"不为传导伤害"排除未采用（版本差异记录于来源档案）。
- "出牌阶段限一次"以 playPhaseStarting 触发表达（每回合恰好一次），时点为出牌阶段开始而非阶段内任意时点（图南/接疹既有口径）。
- 冷恢复：武装状态与执笏授予/清除全部由已提交事件史与命令重放推导（AcquireRuntimeSkills 在命令重放中重执行、清除 op 在回合开始窗口重执行），无运行期私有映射；执笏摸的牌进持有者手牌（公开移动账本 reason=skill-program.ol:zhihu.Draw），不泄漏暗手牌 id。
- AI 策略：鬻爵废除按 宝物→防御坐骑→进攻坐骑→防具→武器 保栏偏好、空栏优先；交牌目标用共享 ScoreProgramTarget 按 TargetDraw=2 计价；目标交牌按选择 id 稳定取第一张；描述符 GainCards 语义按拥有者 +1 牌计价。图兴/执笏为锁定触发，ChooseOption 空计价。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查；协调者合并流程亦不含测试套件运行。唯一门槛为 Release 构建通过。
- Release 构建（`dotnet build CardGame.sln -c Release` 全解决方案，含 `--no-incremental` 复核一遍）：0 error；20 条 warning（非增量去重后 20 处：SkillProgramExecutor、ActualHandGainPrograms、EndingHistoricalUses、LiangXingPrograms、OwnedDeathBenefitReturns、PublicPilePreparation、SameTypeActualUseAid 及 tests/ 既有文件）全部位于本批未新增/未改动的既有文件（SkillProgramExecutor.cs:1114 等已在基线提交 60fe1225 逐行核对相同）；本批新增/修改文件无任何 warning。
- 内容静态校验：`tools/Inspect-SkillProgram.ps1` 对 ol-liu-hong.rules.json + presentation.json 解析通过——ol:yujue（yujue-launch=PlayPhaseStarting）、ol:tuxing（tuxing-arm-catchup=TurnStartBeforeNormalFlow）、ol:zhihu（zhihu-draw=AfterDamageApplied、zhihu-expire-own/-other=TurnStartBeforeNormalFlow）均按描述符目录解析成功（load-and-resource-contracts-only，非行为测试）。
- 开发期临时诊断（已删除，非测试套件）：临时控制台工程以 ContentRegistry.Build 完整注册验证 ol:liu-hong（qun/4/Male/character:liu-hong）与三技能全部加载、playable=true；并以受限自建模式驱动 2–4 人身份局实际对局（种子 1/2/3/5/7/11）：鬻爵五段提示链（选栏→选目标→目标交牌）、装备栏废除、执笏授予与"下回合开始"失效、图兴 +1 体力上限并回复、五栏全废后 -4 与全局伤害 +1（`ProgramGameDamageBonusArmedEvent` 恰一次、`【图兴】令本次伤害 +1` 伤害日志、含传导）均按预期发生；执笏"对其他角色造成伤害后摸两张牌"出现 reason=skill-program.ol:zhihu.Draw 的公开移动。该诊断据此修掉两处缺陷：图兴门控误用鬻爵帧的实例 id（改为 EnabledContentSkillIds 存在性检查）、武装事件技能归属误记鬻爵（改为显式 ol:tuxing）。
- 本批在独立 worktree（batch/ol-liu-hong）开发，与另两路 OL 普通版批次并行；本批不写任何测试、合并流程不跑测试套件为用户/协调者指令。

## 后续协同整合修正

以上是原开发分支的历史实现与验证记录。本工作区后续修复了被累积改动遮蔽的接线、鬻爵主动入口及拒绝和可用装备栏选择、付款子窗与图兴来源归因，并增加针对缺陷的真实命令检查；当前实现和实际验证边界见[第十一轮协同整合记录](2026-10-08-content-continuation-round11.md)。原分支的阶段开始入口与构建记录不代表新行为已经验收。
