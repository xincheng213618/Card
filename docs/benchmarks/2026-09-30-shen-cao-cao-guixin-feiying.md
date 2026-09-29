# 神曹操（归心/飞影）单武将交付批记录

日期：2026-09-30。武将：经典神曹操（神，3 体力，神话再临·林包 2010；官方 gid 205）。技能：归心、飞影。前序：张昭张纮批（经典包 1.158.0／规则 187）、神吕蒙批与共享选牌能力（同一 1.158.0 层）。本批经典包 1.158.0 → 1.159.0；**规则 epoch 仍为 188**，因为新增操作只被新内容消费，同一内容指纹下的既有命令结算、事件顺序、随机数消费与暂停恢复语义都没有改变（见 [Runtime v65](../content/skill-composition/RUNTIME_V65.md)）。

## 选型说明

用户指定「神系列」后，在尚未注册的神话再临神将中选择了唯一能做到「一个技能全新代码、另一个技能零代码」的神曹操：归心（每点伤害从每名其他角色区域各随机夺一张牌，然后翻面）与飞影（其他角色计算与你的距离 +1）。神周瑜（琴音/业炎）、神陆逊（军略/摧克/绽火）、神诸葛亮（七星/狂风/大雾）、神吕布（狂暴/无谋/无前/神愤）都还各自缺少「对全体角色的伤害/回血」「伤害分配」「全局加成型标记」「他人区域弃牌」等语义，留作后续批次。

## 官方口径

- 归心：当你受到1点伤害后，你可以随机获得每名其他角色区域里的一张牌，然后你翻面。
- 飞影：锁定技，其他角色计算与你的距离+1。
- 来源与 FAQ 口径见 [shen-cao-cao 来源记录](../content/sources/shen-cao-cao-2026-09-30.json)（bwiki curid=2637 转录；3 勾玉取 bwiki 经典体力口径；包归属神话再临·林包，图鉴入 `god` 组）。

## 公共能力与实现要点

1. **新操作 `takeRandomCardFromEveryOtherCharacter`**：`SkillProgramEffectOp` 加值；效果描述符要求 `target=owner` 与显式 `zones`（`hand`/`equipment`/`judgment` 的非空无重复子集），并拒绝条件节点（`RequireAlways`）。结算按当前回合顺序（以拥有者为原点）遍历每名其他存活角色：按声明区域汇总候选、按牌 Id 稳定排序后以 `_random.Next` 抽一张，经 `From → Processing → Hand(owner)` 两段移动进入拥有者手牌，移动原因 `skill-program.<skillId>.TakeRandomCardFromEveryOtherCharacter`；区域里没有牌的角色自然跳过，不产生空窗口。
2. **公开脱敏事件 `ProgramRandomCardsTakenFromCharactersEvent`**：只发布拥有者、参与座位、声明区域与总数。手牌身份不进入事件流（与突袭 `ProgramRandomHandCardsTakenEvent` 同一脱敏口径），装备区/判定区牌原本公开，其离开由公开牌区与移动账本体现。
3. **AI 估值 `ProgramCompositionAi.TakeRandomCardFromEveryOtherCharacter`**：按“每名其他存活角色各得一张”计入 `ownerDraw`，声明了公开区域时再加中性调整；公开上下文没有合格人数时以 1 为保守下限。
4. **触发与翻面零增量**：`afterDamageApplied` + `subject=owner` + `damageOccurrence=perDamagePoint` + 可选 Choice 复用神司马懿忍戒、曹丕放逐的既有链；`turnOver(owner)` 复用放逐/悲歌语义（暗置时翻回正面）。
5. **飞影零 Core 代码**：纯规则查询 `{"query":"incomingDistance","operation":"add","value":1}`，与义从的入距分支同型，由既有 `RuleQueryService.EvaluateDirectionalDistance` 生效。

## 内容与验证

内容：`classic-shen-cao-cao.rules.json`（classic:guixin 触发 / classic:feiying 规则查询，revision 1，最低规则 188，schemaVersion 62）+ presentation（schemaVersion 3，两技能官方逐字文本）。注册：经典包 1.159.0（CurrentVersion、两个嵌入资源 const、ClassicShenCaoCaoCatalog 惰性目录、ShenCaoCaoProgram 帮助方法），guixin 按可选触发元数据、feiying 按 `SkillTag.Locked + SkillExecutionForm.State`；`AddGeneral(classic:shen-cao-cao, 神曹操, god, 3 体力, AdditionalSkillIds=[classic:feiying])`；`CurrentGeneralIds` 追加（进入 identity:classic-5/8 共享武将池）。图鉴 `god` 组新增 shen-cao-cao；官方立绘（gid 205）默认皮肤 120501“经典形象*神曹操”入 `general-art-catalog.json`（另含 420501“炼狱枭魂”、520501“一统江山”两个官方皮肤），哈希与尺寸经离线校验，`GeneralPortraitChecks` 的缺立绘名单不含 `classic:shen-cao-cao`。

定向检查 `tests/CardGame.Core.Tests/ShenCaoCaoChecks.cs`（3 项，注册于神吕蒙之后）：

1. 定义与 schema：神 3 体力、双技能顺序与身份池；归心触发形态（窗口/主体/每点/可选/两效果/三区域）；飞影 `incomingDistance +1`；五条解析拒收样例（空区域、非法区域、重复区域、非拥有者目标、带条件的随机取牌）。
2. 归心随机取得并回放：人类神曹操用【决斗】主动承受 1 点伤害打开窗口，发动后公共事件列出的座位恰为“区域里仍有牌”的其他角色，每名被取牌角色恰有一张牌经 Processing 进入神曹操手牌、卡牌总数守恒、神曹操翻面；暂停检查点与结算完成后的状态/事件流重放全等。
3. 飞影入距：其余四名角色计算与神曹操的距离 = 座位环距离 + 1，神曹操计算与他人的距离不变，自身距离仍为 0。

## 门禁

- 定向：Shen Cao Cao 3/3 全绿。
- Release 全解构建：0 警告 0 错误（`--artifacts-path` 独立目录）。
- 全量 Core：**646/646 全绿**（基线 643 + 本批 3 项）。
- WPF 过滤验证：`general gallery combines registered series...` 通过（神组断言更新为 4 名成员，并新增 `147-gallery-god.png` 渲染）；`general portraits...` 仍按既有口径失败，缺立绘名单为 classic:xu-sheng、boundary:xu-sheng、classic:shen-lu-meng、boundary:zhao-yun、classic:zhang-xiu、ol:shen-guan-yu、classic:shen-zhao-yun、classic:gao-da-yi-hao，**不含本批武将**（其中 classic:shen-lu-meng 属并行批次未交付立绘，其余为既有缺口）；WPF 全量套件仍被既有“鬼龙斩月刀缺牌面”在 card artwork 检查中止。
- 格式：本批新文件 `ShenCaoCaoChecks.cs` 零条；`ClassicGeneralChecks.cs` 与 `ProgramCompositionDefinitionChecks.cs` 逐文件报错数与 HEAD 完全一致（39／1），零新增；其余编辑文件零报错。
- `git diff --check` 无空白错误。

## 入池漂移与既有脆面修复（诚实归因）

加入 `classic:shen-cao-cao` 改变了 identity:classic 共享池的候选枚举随机序列，暴露三处既有夹具脆面（纯 HEAD 上均绿，非本批语义回归）：

1. **`FormalWushuangFlow` 闪夹具（ClassicGeneralChecks）**：目标仍用 `GeneralId` 白名单（lu-meng/zhang-fei/xu-huang/gan-ning/dian-wei/zhang-he），池位移后 16384 个种子内不再命中。修复：删除白名单，改为可观测量谓词（手牌 ≥2 张闪、无八卦阵/仁王盾）＋**深探针**——在检查点副本上重放本检查的完整断言体（第 2 张闪窗口、`RequiredResponseProgressEvent` 1→2、两次实体响应、目标未受伤），探针不通过的候选自动跳过。未放宽任何断言。
2. **`FormalJiuyuanRecoveryBonus` 夹具（ClassicGeneralChecks）**：原实现按 `registry.Generals[...].FactionId`（印刷势力）分类提供者，神势力武将按 `ChosenFactionId` 生效的势力被错分；且候选桃可能被转化/牌面身份占用，令合成注入停在用牌窗（目标体力仍为 0）。修复：改用引擎自身的 `GetEffectiveFactionId`（反射取 `_players`）分类，并对每名候选提供者做**注入探针**（副本上合成濒死桃必须真的把主公回复到 1–2 点），自伤、非吴两支断言原文未弱化。

## 批次边界与并行状态说明

- 本批只实现官方经典归心/飞影；「回合内多次伤害按点数各触发一次」由 `perDamagePoint` 天然承载，未额外建模 FAQ 中的状态翻回分支（`turnOver` 已覆盖）。
- 随机取牌不公开牌面：手牌身份只进入拥有者私有手牌视图；判定区/装备区牌的公开身份在移动前本就公开。
- 神周瑜/神陆逊/神诸葛亮/神吕布等神将的剩余语义（全体伤害与回复、伤害分配、全局加成型标记、他人区域弃牌）仍未实现，记录为后续候选。
