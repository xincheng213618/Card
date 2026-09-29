# 姜维（挑衅/志继）单武将交付批记录

日期：2026-09-29。武将：经典姜维（蜀，4体力，神话再临·山 2011；官方 gid 53）。技能：挑衅、志继（觉醒）。前序：孙策/蔡文姬/鲁肃批（1.152.0→1.154.0）、邓艾沙摩柯批（并入 HEAD，规则 185 前基线为规则 184/经典包 1.154.0）；并行批次在制面见文末。本批规则 184→185（Replay.CurrentRulesVersion）/ 经典包 1.154.0→1.155.0。

## 官方口径

- 挑衅：出牌阶段限一次，你可以选择一名攻击范围内含有你的其他角色，该角色需对你使用一张【杀】，否则你弃置其一张牌。
- 志继：觉醒技，准备阶段开始时，若你没有手牌，你选择一项：1.回复1点体力；2.摸两张牌。然后你减1点体力上限，获得"观星"。
- 来源与体力口径见 [jiang-wei 来源记录](../content/sources/jiang-wei-2026-09-29.json)（官方页不渲染体力，4勾玉取 BWIKI 二手核对口径；包归属神话再临·山，图鉴入 myth-mountain 组）。

## 公共能力与实现要点

1. **目标谓词零增量**：预检的"攻击范围内含有你的其他角色"谓词 `otherLivingWhoseAttackRangeIncludesOwner`（目标类型 24）已存在且双路径接线（主动资格枚举 GameEngine.SkillPrograms 与触发/选择枚举 GetProgramTargetSeats），本批直接复用，未新增目标类型。
2. **新操作 `requestSlashByTarget`**（EffectOp 追加值 `RequestSlashByTarget`）：预检确认引擎无"强制出杀"可复用原语（useBoundCardByTarget 为装备赠牌精确绑定、useVirtualCard 仅限拥有者虚拟牌且禁主动激活），按批任务回退条款新增最小操作：目标须 `selectedTarget`、须 `resultBind`、无条件（Parse AllowOnly op/target/resultBind/condition；Resources=ReadSelectedTarget+CreateChoiceResult[used-slash, declined]）。结算：目标无手杀→自动提交 declined 续接；有手杀→发起私有 ProgramTrigger 提示（program-action `request-slash`/`request-slash-decline`，逐杀卡选项），拒绝→declined；出杀→以 `ResolveSlashCore(..., countsTowardSlashLimit:false, programSkillCardUseFrameId:frame.Id)` 走**真实嵌套用牌**（出牌/闪避/伤害/濒死全管线），攻击结算后经 CompleteAttackAfterCardResolution 回续程序帧。
3. **既有 `selectAndMoveOwnedCard` 复用承载弃牌分支**：条件 `choiceIs(tiaoxin-answer, declined)` 门控（曹昂先例），chooser=owner、cardOwner=selectedTarget、zones=hand+equipment、count 1、destination discardPile、skipIfNoCards。
4. **接线面四处（逐处既有先例同型）**：ResolveProgramTriggerChoice 的 program-action 白名单追加 `request-slash`/`request-slash-decline`；AssertProgramSkillState 的 ChoiceBinding 生产者白名单追加 RequestSlashByTarget（选项 used-slash/declined，chooser=selectedTarget）；composition kernel 目录解析夹具表（ProgramCompositionDefinitionChecks）补该操作样例（目录合同要求每声明操作恰一夹具）；AI 估值 ProgramCompositionAi.RequestSlashByTarget（出杀与弃牌对半估：targetDraw-0.5/ownerHpLoss+0.5）+ ResolvePendingAiProgramTrigger 暂停指令分派臂（优先出杀选项）。
5. **志继为纯组合**：turnStartBeforeNormalFlow 触发（subject owner、optional=false、usageScope game/usageLimit 1、条件 currentHandCount==0）+ chooseOption(recover/draw-two) + choiceIs 门控 recover(1)/draw(2) + changeMaximumHp(-1) + grantSkills[classic:guanxing]，承载形制与魂姿觉醒先例一致；观星复用 judgment-cutover 现行 classic:guanxing（下次准备阶段生效，本回合不回扫）。
6. **真实嵌套杀不计入杀次数**（countsTowardSlashLimit:false），与借刀"该角色需对你使用一张【杀】"的响应性质一致；弃牌移动 reason=skill-program.classic:tiaoxin.*，公开可审计。
7. **AI 发动**：挑衅为主动激活，AI 经 BuildProgramActions+ProgramAiHint 正常枚举；目标资格谓词天然排除攻击范围外目标，空集回退保持既有取消语义。

## 内容与验证

内容：`classic-jiang-wei.rules.json`（classic:tiaoxin 激活 taunt / classic:zhiji 触发 awakening，revision 1，最低规则185，schemaVersion 62）+ presentation（schemaVersion 3，两技能官方逐字文本；zhiji optionLabels 回复/摸牌两支）。注册：经典包 1.155.0（CurrentVersion、两个嵌入资源 const、ClassicJiangWeiCatalog 惰性目录），tiaoxin 按 quhu/dimeng 先例裸 `builder.AddSkill(WithActiveActionMetadata(...))`，zhiji 按魂姿先例 `WithStructuredSkillMetadata(SkillTag.Awakening, SkillExecutionForm.Trigger)`，AddGeneral（classic:jiang-wei，shu，4体力，AdditionalSkillIds=[classic:zhiji]），CurrentGeneralIds 追加（姜维进入 identity:classic-5/8 共享武将池）；规则版本 184→185。官方立绘（gid 53，经典形象105301，574×761，sha256 见目录）入 general-art-catalog（97 武将 876 PNG，--phase verify 通过），图鉴 myth-mountain 组新增姜维，WPF 资源经 csproj 通配自动收录。

定向检查 `tests/CardGame.Core.Tests/JiangWeiChecks.cs`（6项，自然命令与真实决策应答，注册于 Program.cs 蔡文姬之后、鲁肃之前）：

1. 定义与触发schema：蜀4体力、身份池；挑衅 taunt 激活形态（requestSlashByTarget→条件 selectAndMoveOwnedCard，choiceIs declined）；志继 hand==0 条件、options[recover,draw-two]、changeMaximumHp(-1)+grantSkills[guanxing]；schema拒收样例（owner 自目标、缺 resultBind、未声明 optionId、条件移动带 resultBind、requestSlashByTarget 带条件）。
2. 挑衅强制出杀并回放：范围内带杀邻座为目标，出杀为真实 CardUsedEvent（Source=目标、Target=姜维、物理手牌支付 Hand→Processing、非 tiaoxin 弃置），ProgramSkillResolved，checkpoint 暂停→还原→同决策全等。
3. 挑衅无法响应弃牌：无杀牌堆下范围外目标不在合法目标集；对无杀目标发动→无杀 CardUsedEvent、恰一张手/装备区牌以 tiaoxin 弃置、回放全等。
4. 志继回复支觉醒并获观星：装备消耗驱动手牌归零（手牌上限=体力≥1，纯弃牌不可达，装备区合法蓄零），SkillAwakenedEvent（MaxHp-1、AcquiredSkillIds=[guanxing]）、ProgramOptionChosen(zhiji-choice)、回复+1、摸牌阶段后恰余常规摸牌2张、下一回合观星真实提示并可跳过、回放全等。
5. 志继摸二支觉醒并回放：同上，摸牌阶段后 0+2(常规)+2(志继)=4 张、体力不变。
6. 志继手牌未清空保持蛰伏：首出牌阶段持牌→无 SkillAwakenedEvent。

## 门禁

- 定向：Jiang Wei 6/6 全绿。
- Release 全解构建：0警告0错误（--artifacts-path 独立目录）。
- 邻接过滤（测试名为子串匹配，"LuSu|CaiWenji|SunCe" 以空格名等价跑）：Lu Su 5/5、Cai Wenji 5/5、Sun Ce 4/4、composition kernel 13/13 全绿（composition kernel 目录夹具表按合同补 requestSlashByTarget 样例后通过）。
- **收尾环境净化说明**：共享主工作区在批后被并行会话新一波在制重构污染（技能程序校验器收紧+赠予语义迁移，其 Core 中间态与 HEAD 内容全面不兼容，叠加本批后大面积红，非本批回归）。本批最终门禁改在隔离 worktree（batch/jiang-wei-final，基线 5b6368d7 纯 HEAD+本批覆层）完成：共享大文件全部 `git checkout HEAD --` 回退后仅重放本批 hunks；并行已提交注册（deng-ai/sha-mo-ke 入池与图鉴条目）保留，并行在制条目（zhang-he 映射等）不带出；FakeRuntime 桩保持 HEAD 签名；tests/Program.cs 采用收尾协调者修复版。
- 全量 Core：**597/597 全绿**。基线复核：纯化后首跑 593/597，4 失败 = 预期 3 处入池漂移 + 1 处 HEAD 既有夹具缺陷（见第 4 条）。
- 三处入池漂移根因与修复（identity:classic 共享池加入姜维改变候选枚举 RNG，种子扫描夹具选中全新牌桌组合，命中既有脆面；三者在纯 HEAD 均绿，非本批语义回归）：
  1. **Qixi（FormalQixiFlow）**：新桌（gan-ning/man-chong/boundary:diao-chan/hua-tuo/shen-guan-yu）下装备奇袭的默认拆牌目标持有装备/判定牌，结算先于无懈询问进入不透明牌位选择（SelectTargetCard 人机决策），旧夹具未驱动；且持无懈者为 shen-guan-yu 时其无懈被武神 cardIdentity 声明占用（GetNullificationCards 排除带程序牌身份的手牌），无懈窗无询问即静默收束（NullificationRequestedEvent 为 0）。修复：夹具种子试验化——装备提交+牌位驱动后校验无懈询问确已发出，否则弃该种子续扫；并补牌位决策确定性驱动。无懈契约断言原文未弱化。
  2. **鲁布无双双夹具（FormalWushuangFlow）**：目标硬编码五武将白名单（吕蒙/张飞/徐晃/甘宁/典韦）在新池 16384 种子内不再命中。修复：删除 GeneralId 白名单改纯性质谓词——闪夹具（手闪≥2、无八卦阵/仁王盾、排除倾国/龙胆/护驾/八阵/雷击/流离/毅重/贞烈/慷慨等全部闪响应转化或闪窗干预技），决斗夹具（手杀恰 1、排除全命名空间杀转化响应技 classic:wusheng/sp:guan-yu-wusheng/classic:longdan/sp:longdan/boundary:longdan/classic:jijiang/classic:wushen/ol:wushen/classic:jinjiu/classic:fuhun/classic:lihuo/classic:jiang/classic:zhenlie/classic:kangkai），种子上限 16384→65536。诊断实证两处新池脆面：SP 关羽 sp:guan-yu-wusheng 红牌转化响应（经典武圣白名单不覆盖 sp 前缀）、高顺禁酒 cardIdentity 将全部手牌声明为杀。
  3. **经典羽配置武圣（ConfiguredWushengSources）**：武圣杀致濒死（victim=owner）后 AI 以桃救援；救援用牌作为程序将的程序用牌，其自身 card-trigger 窗（ProgramCardTriggerWindowFrame，CommittedSimpleCard 续接）合法嵌套于救援 CardUseFrame 与 DyingFrame 之间，既有"濒死续接栈顶须为 DyingFrame 或濒死窗 ProgramSkillFrame"不变式未覆盖该形（诊断转储实录：stack `CardUseFrame#352(Slash) ^ DamageFrame#360 ^ DyingFrame#361(victim 0) ^ CardUseFrame#362(Peach, DyingResponse) ^ ProgramCardTriggerWindowFrame#364(parent 362, Use, actor 3, CommittedSimpleCard)`，续接帧 361）。修复（GameEngine.cs 濒死续接不变式臂，仿伤害触发多形态先例补第三形态）：栈顶为 ProgramCardTriggerWindowFrame 时沿连续父链（beneath.Id == current.ParentFrameId）下行走链，要求锚点为活动濒死续接帧本身，或其救援用牌（CardUseFrame.DyingResponse.ResolutionId == 续接 FrameId，且该 DyingFrame 紧叠于救援用牌之下）；非链上窗口仍立即抛错。
- HEAD 既有夹具缺陷（非本批引入，纯 HEAD scratch worktree 复现实证）：2026 神司马懿定义夹具的 `damageSkill.Replace("\"target\":\"owner\",\n\"targetRef\"...", ...)` 跨行匹配在 CRLF 检出环境失配（C# 原样字符串逐字保留源码行尾），Replace 静默无效→组合未被改写→Load 成功→Reject 误报（LF 检出环境该测试通过，故 HEAD 基线机器相关）。修复：改为不跨行界的唯一 token 替换（damageSkill 内 `"target":"owner"` 恰一次出现），行尾无关，拒收语义不变。
- 格式：本批全部变更文件 dotnet format whitespace --verify-no-changes 0 条。
- 批内修复：presentation 补 zhiji optionLabels（校验器要求 chooseOption 每选项有标签）；新操作 const 选项常量与目录夹具合同对齐。

## 批次边界与并行状态说明

- 挑衅无既有"强制出杀"原语，按批任务回退条款新增 `requestSlashByTarget` 最小操作（含真实嵌套用牌参数），未复用/改动借刀与决斗路径；目标谓词、弃牌操作、观星、觉醒承载全部零增量复用。
- 志继回复支测试断言"摸牌阶段后余2张"：觉醒发生在准备阶段、常规摸牌随后发生，任何可观测停点都已含常规摸牌，属观测点选择而非语义偏差。
- 挑衅弃牌不足场景（目标无手牌无装备）：skipIfNoCards 既有语义直接续接，未实现官方细则备选；AI 挑衅发动/应答为保守估算（出杀优先），记录为后续改进面。
- 收尾在隔离 worktree（batch/jiang-wei-final）完成：批后共享主工作区被并行会话在制重构污染（校验器收紧+赠予语义迁移，Core 中间态与 HEAD 全面不兼容），为不劫持其迁移，纯化（`git checkout HEAD --` 共享大文件）后仅重放本批 hunks 并在纯 HEAD+本批上重验；共享树未做任何写操作。
- tools/sync_general_art.py 的 --phase finalize 在 HEAD 即存在 NEW_OL_DEFAULTS 未定义的 NameError（先于本批），本批以 --phase classic/--phase verify 完成立绘入库与校验，未修该既有缺陷（留待工具属主）。
- 共享池入池的洗牌漂移与三项既有夹具脆面已在门禁节完成根治（Qixi 种子试验化、鲁布双夹具性质谓词化、濒死续接不变式补救援用牌窗口臂），连带修复 HEAD 既有的神司马懿 CRLF 行尾敏感夹具；全量 597/597。
