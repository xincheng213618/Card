# 张昭张纮批次：直谏置装禁替换与固政弃牌阶段终界窗口

- 日期：2026-09-29
- 批次：classic:zhang-zhao-zhang-hong（张昭张纮，吴，3 体力，神话再临-山 2011）单武将
- 运行时：schema 62，规则版本 187，经典包 1.158.0（张郃批落 186/1.157.0）
- 状态：**已完成**——引擎扩展、注册、定向检查与全量回归全部落地

## 官方文本与口径

来源：`docs/content/sources/zhang-zhao-zhang-hong-2026-09-29.json`（候选池版本，先行预提交；本批按实现终态回填 versionBoundary）。

> 【直谏】出牌阶段，你可以将手牌中的一张装备牌置于一名其他角色的装备区里，摸一张牌。
> 【固政】其他角色的弃牌阶段结束时，你可以令该角色获得弃牌堆里的其于此阶段内弃置的一张手牌，若如此做，你可以获得弃牌堆里的其余于此阶段内弃置的牌。

- 十周年官方武将库 gid 56 为主口径；BWIKI 二手核对体力（官方页不渲染，3 勾玉）与"不得替换原装备"细则。直谏的禁替换按 BWIKI 补充固化进引擎约束（解析期形态校验+执行期取消+激活资格门三重），描述文案标注"（不得替换其原有装备）"。
- 固政第二段"你可以获得弃牌堆里的其余…牌"实现为交还一张后自动获得其余（严格有利、无决策空间），固化记录于 Runtime v64。

## 实现设计

| 技能 | 形态 | 依赖 |
| --- | --- | --- |
| 直谏 | activation `equip-target`（无次数限制）：hand 装备牌 → selectedTargetCorrespondingZone（禁替换）→ draw 1 | E2/E3 |
| 固政 | trigger `restore-at-discard-phase-end`：discardPhaseEnded 窗口（otherLiving）→ restorePhaseHandDiscards | E1/E2 |

时机口径：固政窗口挂在弃牌阶段的两个收尾点（RunOneStep 的 AutoDiscard 之后与 SubmitDiscardCards 应答之后），多拥有者按座位距离排序；弃牌阶段被跳过（乐不思蜀/巧变）时不开窗——被跳过的阶段没有"结束时"。阶段弃牌集按本回合移动记录（Hand→弃牌堆、hand-limit 原因）+ 仍在弃牌堆过滤，不引入水位线状态，天然防跨回合与防多名固政重复提供。

## 引擎扩展清单（已落地）

- **E1 新窗口 discardPhaseEnded**：枚举/双解析白名单/`turnOwnerScope` 白名单扩容（拒收文案同步，SP Le Jin 契约文案更新）/`eventSource` 兼收/边界帧校验（须留在弃牌阶段）/`CanRunProgramTrigger` 臂/`TryBeginDiscardPhaseEndedProgramWindow`（continuation `EndTurnAfterDiscardPhase`）/`ProgramEntryCapabilities` 两处/选择分发与 AI 分发接线。
- **E2 操作 restorePhaseHandDiscards**：新 op 枚举 + 描述符（phaseOwnerRef 必须 eventSource）+ 执行器接口/host/引擎实现（提示、放弃、交还+其余归拥有者、失效取消）+ `ProgramCompositionDefinitionChecks` fixture 补录 + AI 估值（+4）与选择器（单张可还即放弃）。
- **E3 prohibitReplacingEquipment**：解析四条形态约束（单一 hand 源、equipment 类别、selectedTarget 引用；反向强制 hand 源对应区移动必须带标志）；选择枚举过滤占用槽；执行期占用即取消技能；激活资格门 `HasFreeEquipmentSlotForOwnedHandEquipment`；组合 AI 新形态估值分支。
- **顺带根治**：青龙偃月刀跟进杀在多目标续接上的结算竞态（`MultiTargetContinuationDefersQinglong` 门 + 不变式通用尾部容忍活跃外层持牌），详见 Runtime v64 与下文验证节。

## 内容与测试

- `src/CardGame.Content.Standard/SkillPrograms/classic-zhang-zhao-zhang-hong.rules.json`（schema 62，minimumRulesVersion 187）
- `src/CardGame.Content.Standard/SkillPrograms/classic-zhang-zhao-zhang-hong.presentation.json`（schema 3）
- `tests/CardGame.Core.Tests/ZhangZhaoZhangHongChecks.cs`（三项：定义与触发器 schema + 解析拒收面；直谏装槽交接+回放一致；固政还一张得其余+回放一致）
- 注册：`StandardClassicGeneralPackage.cs` 资源常量 + 懒加载目录 + `ZhangZhaoZhangHongProgram` + 技能块（WithActiveActionMetadata/WithOptionalTriggerMetadata）+ `AddGeneral`（吴，3 体力，附加技 guzheng）+ `CurrentGeneralIds`；包版本 1.157.0→1.158.0；`Replay.cs` 规则 186→187
- `GeneralGalleryCatalog.cs` 神话再临·山组新增；`sync_general_art.py` CLASSIC_HEROES 加 `"zhang-zhao-zhang-hong": 56`
- 立绘：`official-zhang-zhao-zhang-hong.png`（105601 经典，191KB），catalog 98 武将/877 PNG verify 通过

## 验证结果

- 提交链（分支 zhang-zhao-zhang-hong，基线 043b3eb3，隔离工作树交付）：`7f64adf2` 批主体 → `199b9f5c` 立绘 → `5b61f0d4` 青龙刀竞态根治 → `e13a81c2` 定向检查。对象事故后链上五个提交经 `commit-tree` 重建（树对象逐一按原哈希复原，见诚实归因），现链为 `6070f4a7`（重建的批主体）→ `0889952d` → `7d62bcca` → `f7c6bb6c` → `7e2b863c` → 合并提交 `b2380144`（并入 main 共享复核批）。
- 定向检查：ZhangZhaoZhangHongChecks 3/3。
- Debug 全量 Core：**617/617 全绿**（含本批新增 3 项；合并后曾因 main 侧 Program.cs 裁剪短暂掉到 327 项，已按仓库"不得以减少测试数为目标删改测试"约定整体恢复，见诚实归因）。
- Release 构建：0 error；2 warning 均为赵云批 `GaoDaYiHaoChecks` 既有 CS8602（非本批引入，未代改他人文件）。
- 版本分层：Checkpoint SchemaVersion 3 不变；规则版本 186→187（新增触发窗口改变同指纹重放行为）；经典包 1.157.0→1.158.0。
- 合并边界：与并行复核批（18320fa，I力场 preventIncomingTrickDamage 与 selectedTargetCorrespondingZone 公示校验）同用规则 187/经典包 1.158.0；`GameEngine.ProgramCardPayments.cs` 双能力合一（本批 prohibitReplacingEquipment/correspondingZoneSeat + 对方 canSelect 公示谓词，hand 源对应区移动放行）；测试套件整体保全 617 项（对方批未新增检查方法，纯裁剪已回退）。

## 诚实归因

- **青龙刀竞态的观测史**：蔡文姬批后全量曾报"Processing 滞留牌"不变量违规，当时二分确认与本批技能无关（单技能注册复现、牌号一致）。本批以临时诊断（外层帧/移动踪迹注入异常消息，已还原）定位为青龙刀跟进杀压在多目标续接已发布的响应窗口上——预存缺陷，借本批交付根治。修复包含一处不变式容忍面（外层活跃用牌帧持牌），另有一次中间态"容忍面过宽"（置于专用分支之前）曾连累濒死救援等 17 项，已收敛至通用尾部单点并全量复验。
- **仓库对象损坏事件**：本批中途主仓 pack 单对象（diao-chan 皮肤 PNG 502504）CRC 损坏，曾两次中断工作树创建（其中一次伴随 Desktop 隔离工作树目录被外部删除，未提交成果全损后由脚本化重放恢复）。修复方式：从 origin/main 浅克隆提取同哈希对象，以 loose object 遮蔽坏拷贝（fsck --full 确认全仓仅此一处损坏，其余对象完好）。损坏成因未查明（疑似并行会话 git 维护竞态），建议并行会话静止后安排一次 `git gc`/repack 以物理剔除坏段。
- **对象事故升级与全量复原（合并期间）**：坏 pack 在并行会话的 `git multi-pack-index`/gc 维护中进一步失控（pack/idx 计数不一致、midx 悬挂、idx 被删），波及 606 个历史对象；坏包被改名弃置前已字节级留底（%TEMP%\card-pack-repair\copy.pack），并解析其 multi-pack-index 快照定位 3,545 个属内对象，逐条 zlib 解压提取可读对象 1,968 个写回 loose。并行会话同期从 origin 回填新 pack（pack-4e2858ea），双方合力后坏链 606→25。剩余缺口全部来自本批五提交：并行 gc 将未推送的 `7f64adf2`（批主体）连同其派生树/对象误判不可达而清除。复原方式：确认分叉基线 043b3eb 后，用重放脚本在干净基线上逐位重演 51 处编辑，`git mktree`/`hash-object` 逐一与幸存树对象比对——`808cd162`（立绘提交树）、`c4d5a5eb`（docs）、`4126f21d`（tests）、`4e1967db`（Content.Standard）、`a5854caa`（Core）、`b1c7fc82`（src）全部按原哈希复原；两个丢失 blob（张郃批时代 SpLeJin/ProgramComposition fixture 中间态）以"合并结果+对方增量反向合并"重组并按原哈希验证。仅 `7f64adf2` 提交对象本身（作者时间戳）不可原样恢复，以新提交 `6070f4a7` 重建并在提交信息中注明；后代四提交保留原树、原元数据、原提交信息，仅父链换接。
- **并行会话测试裁剪的回退**：合并期间发现 main 侧 2112fd0/344b8846 两个 "update" 提交对测试目录做了大面积裁剪（检查项 617→324，波及 100+ 检查文件、约 267 个检查方法，未新增任何方法），首次合并后套件缩至 327 项。按仓库 AGENTS.md"不得删除历史兼容代码或以减少测试数为目标改动"的约定，已将测试目录整体回取为本分支版本（617 项），并核验对方条目集为本分支子集（无新增丢失）。此改动保留在合并修正提交中，供仓库所有者复核裁剪本意。
- 隔离工作树从 Desktop 迁至 `%TEMP%\card-batch-zhao-hong`，规避 Desktop 目录被外部清理的重复风险。

