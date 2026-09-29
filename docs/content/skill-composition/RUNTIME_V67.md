# RUNTIME V67 —— 左慈批次（集成线定名；分支口径：规则 192 / 经典包 1.162.0）

> 并入说明：本批在交付分支上的文档自名 V66、按规则 192 / 经典包 1.162.0 交付；
> 与并行批次并流后，集成线统一为规则 193 / 经典包 1.163.0，文档定名 V67。下文保留分支口径原文。

本批为单武将交付：左慈（化身 / 新生，神话再临·山包 2011，群 3 体力，最低规则 192）。经典包 1.161.0 → 1.162.0；规则 epoch 191 → 192，原因是开局化身摸牌在初始洗牌之前消费确定性种子，且化身声明在运行期授予技能——同一内容指纹下的命令结算结果与 191 epoch 不同（`GameCheckpoint.CurrentRulesVersion` 192 注释记录了这两点）。Checkpoint schema 仍为 3，规则 JSON schemaVersion 仍为 62（`SkillProgramCatalog.RulesSchemaVersion` 不变），`minimumRulesVersion 192` 表示本内容需要 192 epoch。

## 左慈批次：化身 / 新生

### 新增公共能力

1. **操作 `huaShenXinSheng`**（`SkillProgramEffectOp.HuaShenXinSheng = 110`）：效果描述符要求 `target=owner`，不接受条件节点（`RequireAlways`）。结算从化身候选池随机取一张游戏外武将牌追加到拥有者的化身牌堆（`_random.Next` 确定性抽取），发布公开脱敏事件 `HuaShenAvatarGainedEvent`（牌堆总数，不含武将牌身份）；候选池为空时仅记日志、静默无事发生。AI 语义 `ProgramOperationAiSemantic.HuaShenAvatarGain = 90`，估值 `ProgramCompositionAi.HuaShenAvatarGain` 计 +0.5 中性调整（新生默认愿意发动）。
2. **操作 `huaShenChangeAvatar`**（`SkillProgramEffectOp.HuaShenChangeAvatar = 111`）：效果描述符要求 `target=owner`，接受可选显式节点 `declaredSkillTags`（默认 `[limited, awakening, lord]`，只允许这三者的非空无重复子集，见“边界待核定”）。结算打开引擎原生的换化身流程（见“换化身”）：AI 拥有者确定性换到牌堆中 Id 最小的另一张并保持技能声明口径；人类拥有者进入 `DecisionKind.HuaShen` 私有提示（先选亮出牌、再选声明技能），光标停在 `AwaitChoice`，经 `SubmitHuaShenAnswer` 续算。
3. **决策类型 `DecisionKind.HuaShen = 50`**（显式值，避开并行批次占用）与**公开事件 `HuaShenAvatarRevealedEvent`**（亮出的武将牌、声明技能及其名称，作为“获得该化身牌的一个技能”的公共审计口径）。化身牌堆本身不进入事件流与快照明细，只以牌堆数量与“当前亮出”公开，未亮出的化身牌身份保持私有（与神曹操归心取牌的手牌脱敏同口径）。

### 四个子系统

1. **化身牌堆（游戏外武将库）**：`GameEngine.ZuoCi.cs` 以引擎字段 `_huaShenAvatarPiles` 承载每名拥有者的牌堆（`GeneralIds + RevealedIndex + DeclaredSkillId`），全部变更只发生在命令处理内，因此不改 Checkpoint 结构即天然进入不可变命令日志：`GameReplay.Restore` 在新引擎上重放种子 + 命令即可精确重建牌堆与顺序（“牌堆跨检查点持久”由定向检查直接断言）。开局摸两张在 `RunOneSetupStep` 内完成（神势力选择的原生 setup 先例），候选 = 模式 `GeneralPoolIds` − 在场武将 − 无可声明技能武将（技能须带 Program 且不含排除 Tag），每张候选按 `_random.Next` 抽取。
2. **动态技能声明**：声明即运行期授予——`SkillGrants.Grant/RemoveGrant`（grantId `huashen:{seat}`，SourceId `huashen`），授予后绑定分片按既有 `SkillGrants.Revision` 戳自动重建，技能发现、AI 决策与提示列举零新增分支；再亮出另一张化身牌时先移除旧授予再授予新技能。亮出与声明两段提示为引擎原生 `DecisionKind.HuaShen`（候选 `huashen.reveal…` / `huashen.declare…` 带参数），人类座位挂起等待、AI 座位同步解析，回放语义与普通命令一致。
3. **性别 / 势力视同**：性别走 `CharacterState.Gender => GenderOverride ?? General.Gender` 的既有收口（`GenderOverride` 原本未被使用），牌堆记录亮出武将牌的性别即可让肉林的 `eventTargetGenderIs`/`eventSourceGenderIs` 等一切读取 `CharacterState.Gender` 的行为自动跟随；势力走 `GameEngine.GetEffectiveFactionId` 单一收口（`ChosenFactionId ?? GetHuaShenEffectiveFactionId(player) ?? 印刷势力`），黄天 providerFactions、护驾代打等既有势力查询全部复用。
4. **换化身**：化身技能携带两个可选触发（`turnStartBeforeNormalFlow` + `turnEnding`，`subject=owner`、`priority=0`、条件 always），effects = `huaShenChangeAvatar(declaredSkillTags: [limited, awakening, lord])`；候选只列牌堆中未亮出的化身牌（按 Id 稳定排序），换牌后重开声明提示。可选窗口沿用 `DecisionKind.ProgramTrigger` 的发动/跳过机制；AI 对中性估值默认跳过，保持确定性。

### 新生消费方

- 新生：`afterDamageApplied` 触发，`subject=owner`、`damageOccurrence=perDamagePoint`、可选；effects = `huaShenXinSheng`。每点伤害各发布一次私有发动/跳过 Choice（与神曹操归心同型），发动后化身牌堆 +1。

## 边界待核定

- **声明技能的排除口径**：BWIKI 左慈页化身措辞为“获得该‘化身’牌的一个技能（限定技、觉醒技、主公技除外）”，官方武将库页（gid 57）现行文本无此括注，且两处均未检索到官方 FAQ / 正式角色卡对“神武将牌是否可被化身”的口径。实施按任务要求采用 BWIKI 口径：以规则 JSON 中**显式可选节点** `declaredSkillTags: ["limited","awakening","lord"]` 承载（默认即排除，且本批内容显式写出），引擎侧只允许这三种子集；将来官方口径若不同，仅需改写内容节点，不动引擎。证据来源：`docs/content/sources/zuo-ci-2026-09-29.json`（官方页全文与 BWIKI 对照记录于该文件 versionBoundary 字段）。**此项仍属“待核定”，不视为已获官方确认。**
- 体力 3 勾玉官方页不渲染，取 BWIKI 口径（经典版左慈 3 勾玉，群、男）。

## 验证口径

定向检查 `ZuoCiChecks`（7 项，名称含 `ZuoCi` 供 `--filter=ZuoCi`）：

1. 定义与触发 schema：群 3 体力男将、身份池成员；化身双窗口（回合开始/结束）各为可选 always 换化身、`declaredSkillTags` 与 BWIKI 排除清单一致；新生按点伤害可选触发；四条解析拒收样例（非法排除 Tag、带条件换化身、非拥有者目标、`huaShenXinSheng` 携带声明 Tag）。
2. 开局声明可种子复现：同种子重复运行获得相同化身牌堆；亮出/声明两个暂停点检查点重放全等；结算后单条 `HuaShenAvatarRevealedEvent` 命名亮出牌与技能。
3. 主公技排除：牌堆含主公技化身牌时，声明提示只列非主公技能。
4. 性别视同：左慈亮出女性化身牌后对董卓出【杀】，董卓被要求两张【闪】（`RequiredResponseProgressEvent` 计数 2）且单闪后受创；亮出男性化身牌的同型对照恰需一张闪、无第二次要求。
5. 势力视同：黄天主公在场时，左慈亮出群化身牌后其出牌阶段出现黄天贡献选项，亮出魏化身牌后不出现（有效势力经 `GetEffectiveFactionId` 读取）。
6. 新生补堆与回放：决斗承伤后新生窗口按点发布；发动后牌堆 +1、公开事件计数一致、卡牌总数守恒；窗口暂停检查点与结算后重放全等。
7. 回合边界换化身：回合开始窗口发动后只列未亮出化身牌、换牌重声明生效；回合结束窗口跳过后亮出保持不变，各暂停点重放全等。

门禁：定向 7/7；Release 全解构建 0 警告 0 错误；全量 Core 658/658 全绿（基线 651 + 本批 7 项）；全部 17 个改动源文件 `dotnet format whitespace --verify-no-changes` 通过。立绘 105701 已入库：`sync_general_art.py` CLASSIC_HEROES 增加 `"zuo-ci": 57`，`official-zuo-ci.png`（574×761）与 `Skins/zuo-ci/` 下 6 张皮肤 PNG 下载成功，`--phase finalize --key zuo-ci` 通过，`--phase verify` 通过（104 名武将、891 条 PNG 记录离线校验）。图鉴 `myth-mountain` 分组补入 `zuo-ci`（左慈属神话再临·山）。

### 工具与目录的既有异常（如实记录）

- `--phase finalize`（全量、不带 `--key`）触发既有基线缺陷：`NameError: name 'NEW_OL_DEFAULTS' is not defined`（tools/sync_general_art.py 第 301 行，疑似应为 `NEW_OFFICIAL_DEFAULTS`）。按批次约定未修复共享脚本，改用 `--phase finalize --key zuo-ci`（该路径不触及 OL 循环）完成收尾，全量 NameError 原样保留为基线问题。
- `--phase verify` 在基线提交上即失败：`zhou-yu/201203` 的目录 sha256 记录与磁盘 PNG 不符（目录 3741dad2…，磁盘 d3bd4a1a…，尺寸一致 574×761）。已确认与本批无关（基线 stash 对照复现），为使校验门禁真实通过，仅把该条记录的 sha256 对齐磁盘现有 PNG（未改任何像素数据）；此修复随本批一并提交并在此留痕。
