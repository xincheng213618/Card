# 杨婉单武将批次

当前 OL 杨婉（蜀势力、3 体力、女、称号"融沫之鲡"，官网 499 号，璀璨星河-女史包、传说品质、2022-07-16 上线）接入：诱言（出牌和弃牌阶段各限一次，当你的牌因弃置置入弃牌堆后，你可以从牌堆中获得与弃置牌花色不同的牌各一张）、追还（结束阶段，你可以秘密选择一名角色，直到其下个准备阶段，此期间内对其造成过伤害的角色：若体力值大于其，受到其造成的2点伤害；若体力值不大于其，随机弃置两张手牌）。官方立绘 49900 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-yang-wan-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 技能 | 实现口径 |
| --- | --- |
| 诱言 | youyan-gain：discardPileReceived 可选触发，movementDiscardOnly + discardOwnerScope own + sourceZones hand/equipment/judgment（"你的牌因弃置"直接复用共享 `GetProgramDiscardSource` 语义，经 Processing 的完成式弃置同样归属来源）；usageScope=phase + usageLimit=1 表达"出牌和弃牌阶段各限一次"。新 op `youyanGainSuitCards`（7268）从窗口批次重扫本批"你弃置"的实体牌取花色并集，按 黑桃→红桃→梅花→方块 固定顺序从牌堆顶为每个其余花色取一张入手牌，`ProgramYouyanGainEvent` 只记录公开花色掩码与张数（获得的牌进私人手牌，事件不含牌 id）。 |
| 追还 | zhuihuan-arm：turnEnding 可选触发 → 新 op `zhuihuanArm`（7269）弹出 `IsPrivate` 秘密选择提示（候选含自己的全部存活角色）；arm 事件只含拥有者与回合号，所选座位存运行期映射 `_zhuihuanArmChoices`，冷恢复按接受的私密选择命令前缀重建（与闭境标记同机制），不进事件/快照。zhuihuan-resolve-own / zhuihuan-resolve-other：turnStartBeforeNormalFlow 两条强制触发（own / `turnOwnerScope=otherLiving`）→ 新 op `zhuihuanRetaliate`（7270）在所选角色的下个准备阶段一次性扫描事件史开伤害账本，逐个清算：体力值大于所选角色者受到**以所选角色为来源**的 2 点反伤（子结算挂起、指令游标回绕重入，与"依次执行"一致），其余者随机弃置至多 2 张手牌（不足全弃）；`ProgramZhuihuanResolvedEvent` 记录证据并关闭 arm。 |

## 共享能力扩展

- 新增 EffectOp 7268–7270（只占用分配的 7268–7275 段）；描述符按反射目录自动注册；AI 语义复用既有 GainCards / ChooseOption / Damage，未新增 `ProgramOperationAiSemantic` 成员。未新增 PlayerMarkerKind、ConditionKind 或 schema 节点。
- `SkillProgramTriggerWindow.TurnStartBeforeNormalFlow` 支持 `turnOwnerScope`（parser 白名单追加该窗口；`CanRunProgramTrigger` 门控按 Own/OtherLiving 区分，OtherLiving 要求当前回合角色存活）。枚举零新增（复用既有 `OtherLiving = 1`）。
- `TryBeginTurnStartProgramWindow` 在内容依赖开关（`ProgramDependencies.HasTriggerOperation(ZhuihuanRetaliate)`）开启时枚举全部存活角色的技能（own 候选只取回合角色、foreign 候选只取其他存活角色，按座位距回合角色距离排序），每个候选的条件用其拥有者的冻结事实评估；开关关闭时保持原单拥有者枚举路径逐字节不变，既有内容指纹不受影响。
- `BeginProgramSkillDamage` 新增可选参数 `explicitSourceSeat`（追还反伤的伤害来源是被秘密选择的角色，不是程序参与者之一）；未传参时行为与原先完全一致。
- cards-moved/capture 无新增接线：诱言的候选匹配完全走 discardPileReceived 的既有通用过滤器。

## 边界口径

- 诱言："各限一次"由共享 phase 账本执行——任意一个出牌阶段内至多一次、任意一个弃牌阶段内至多一次（账本随阶段切换过期）；其他阶段（如他人的出牌阶段被拆桥弃牌）同样按其所处阶段段计一次，与 BWIKI 规则集问答"有时其他阶段也可以触发"的口径一致。"因弃置"不含使用/打出结算完成、重铸、阵亡清理、五谷余牌、判定完成。同批多张弃置牌取花色并集；每个其余花色从牌堆顶取最靠近顶端的一张，牌堆中该花色无牌时跳过该花色（物理上限，日志注明）。获得的牌进私人手牌，事件只记花色掩码与张数，暗手牌 id 不进普通快照。
- 追还：秘密选择候选为全部存活角色（含自己——为自己上"反伤"标记是该技能的正常用法）；arm 的公开日志不泄露座位，所选座位只存运行期映射并按接受命令回放重建。结算点=所选角色的下个准备阶段（turnStartBeforeNormalFlow，早于其回合正常流程，期间伤害都计入账本）；"造成过伤害"按去重来源座位计，无来源伤害与 0 点伤害不计。反伤=以所选角色为来源的 2 点普通伤害；多个清算对象按账本顺序依次结算。体力值比较取清算时点的实时体力值；所选角色在清算中途死亡时剩余清算取消并记日志；杨婉死亡后其未决追还不再结算（程序触发统一要求拥有者存活，平台既有边界）；反伤被护甲等防止时仍记为已清算。随机弃置不足两张时弃置全部手牌，无手牌时记日志跳过。
- 追还的证据与重建：arm/resolution 事件均为标量公开记录；冷恢复按命令前缀确定性重放，私密选择由命令流重建，历史扫描（arm → damage → resolved 的开合账本）在重放后得到相同清算队列。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查。
- Release 构建（`dotnet build CardGame.sln -c Release` 全解决方案）：0 error；20 条 warning 全部为基线既有文件（SkillProgramExecutor.cs、GameEngine.ActualHandGainPrograms.cs、GameEngine.EndingHistoricalUses.cs、GameEngine.LiangXingPrograms.cs、GameEngine.OwnedDeathBenefitReturns.cs、GameEngine.PublicPilePreparation.cs、GameEngine.SameTypeActualUseAid.cs 及 tests/ 下既有文件），本批新文件无任何 warning。
- 内容静态校验：`tools/Inspect-SkillProgram.ps1` 对 ol-yang-wan.rules.json + presentation.json 解析通过，三个触发器（zhuihuan-arm/zhuihuan-resolve-own/zhuihuan-resolve-other）与诱言触发器均按描述符目录解析成功。
- 例行 `tools/Test-Changed.ps1`（无过滤，ArtifactsPath 独立目录 `artifacts-yang-wan`）：全解决方案构建 12.2s、Core 214 项过滤联合执行 102.9s（266 通过 / 57 失败 / 0 跳过，共 323 项）、WPF 18 通过 / 0 失败（18.5s）。57 项失败名单与严畯批基线（同一基线提交 1fc7b1d6 的 `artifacts-yan-jun` 实测）逐项 diff 完全一致，无新增失败；全部失败均为基线既有的在制特性检查（狂斧、离魂、强武、源咒、SP 群雄等），无任何杨婉相关失败。
- 本批在独立 worktree（batch/ol-yang-wan）开发，与清河公主、芮姬两路并行。
