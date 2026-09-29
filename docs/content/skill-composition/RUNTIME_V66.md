# RUNTIME V66 —— 规则 192 / 经典包 1.162.0

本批为单武将交付：于吉（蛊惑，风包 2010，群、男、3 体力，最低规则 192）。经典包 1.161.0 → 1.162.0；规则 epoch 191 → 192，因为同一内容指纹下的既有命令结算会因新增质疑窗口而不同：`usePlacedCardAsDeclared` 引入"声明后、结算前"的公开质疑阶段，规则 JSON schemaVersion 仍为 62（`usesPerAnyTurn` 为纯可选新增节点），Checkpoint schema 仍为 3。缠怨 `classic:chanyuan`（锁定技 + `SkillSuppressionRule(1)`）已作为纯定义先行注册，本批不重复注册，只由蛊惑在运行时授予。

## 于吉批次：蛊惑

### 新增公共能力

1. **操作 `usePlacedCardAsDeclared`**：`SkillProgramEffectOp` 加值；描述符要求 `target=owner` 且条件恒真（`RequireAlways`），配对校验强制激活恰好 `minCards=maxCards=1`、`sourceZones=[hand]`。执行时暂停程序帧，发布**私有**声明 Choice（扣置牌保持留在拥有者手牌，不进 Processing，避免出现无活动结算的中间态）。
2. **蛊惑质疑子系统**（`GameEngine.ProgramDeclaredCardUses.cs`）：声明落定后按座次顺序向每名其他存活角色依次发布公开质疑 Choice（`program-action=guhuo-doubt`）；全部不质疑或质疑结束后按声明结算。为假：扣置牌直接从隐藏手牌进弃牌堆（`skill-program.<skillId>.GuhuoVoid`），此牌作废；为真：质疑者经既有 `AcquireRuntimeSkills` 获得缠怨（发布 `SkillsAcquiredEvent`），扣置牌按声明的牌名正常进入结算——杀走 `ResolveSlashCore`、桃走 `ResolvePeach`、酒走 `BeginCardUse+BeginSimpleCardUse`（与 `ResolveAlcohol` 同一承诺语义），普通锦囊走 `BeginCardUse+BeginJizhiOrNullificationWindow`（与普通锦囊转化同型）。
3. **声明选项复用**：基本牌声明集（杀/桃/酒）在本文件内构建，杀的目标合法性复用虚拟杀谓词 `CanUseVirtualSlashTarget`（含次数与距离）；普通锦囊声明集完整复用 `BuildProgramOrdinaryTrickUseOptions`。
4. **激活限额 `usesPerAnyTurn`**：`SkillProgramActivation` 可选节点（正整数或 null，usageGroup 一致性并入既有校验），配 `_programAnyTurnUses` 账本，在每个回合开始全量清空——实现"每名角色的回合限一次"（对比 `_programUses` 只在拥有者回合开始清空）。
5. **公开事件**：`ProgramCardDeclaredEvent`（声明座位、声明牌名、质疑候选座位；不含扣置牌 id，保持私密）与 `ProgramGuhuoCardFlippedEvent`（质疑者、翻开牌 id、声明/实际牌名、真假）；翻开只公开牌面，不移动牌。
6. **AI 估值与决策**：`ProgramCompositionAi.UsePlacedCardAsDeclared` 以保守中性估计（按无中生有期望收益折半计入 `ownerDraw`）参与激活评估；质疑方确定性策略只质疑声明为无中生有的扣置（公开信息口径），拥有者优先声明与实际牌一致的牌名。
7. **诊断面**：`GameEngine.GetLivePlayer(seat)` internal 只读视图（含技能授予），供检查以引擎同款 `MatchSkillBindingIndex` 验证运行时授予技能的压制行为。

### 消费方

- 蛊惑：激活 `guhuo-declared-use`（恰好一张手牌、`usesPerAnyTurn:1`、恒真条件），effects 仅 `usePlacedCardAsDeclared`。不注册 viewAs，普通 `PlayCardCommand` 无法绕过质疑流。
- 缠怨：锁定技（持有者 `EffectiveSkillIds` 含缠怨即不进入质疑序列，与体力无关）；体力 1 时其余技能失效由既有 `SkillSuppressionRule(1)` 分片机制消费，本批零增量。

### 复用与零增量

- 质疑候选与杀/桃/酒/锦囊合法性全部复用既有谓词与选项构建；缠怨授予复用 `AcquireRuntimeSkills`；普通锦囊结算复用 `BeginCardUse + BeginJizhiOrNullificationWindow` 通行链。
- 内容全部由 `classic-yu-ji.rules.json` / `.presentation.json` 承载；注册只增加资源常量、惰性目录、一条 `AddSkill`、一条 `AddGeneral` 与武将池条目（`identity:classic-5` / `identity:classic-8`）。
- 版本号仅写入 `src/CardGame.Core/Replay.cs`（192）、`src/CardGame.Core/SkillPrograms.cs`（schema 62 未动）与经典包 `CurrentVersion`（1.162.0）。

### 边界（如实记录）

- 声明集覆盖"使用"路径；官方文本中的"或打出"（响应窗口中打出）本批未实现——`BuildProgramActions` 限激活仅在拥有者自己出牌阶段可用，打出路径需响应窗口挂接，留待后续批次。
- 质疑罚则采用风包 2010 首发口径（质疑成假无惩罚），与目标版本角色卡正式口径（为真流失一体力）不同，按来源规格 `docs/content/sources/yu-ji-2026-09-29.json` 记录的版本边界执行。

## 验证口径

定向检查 `YuJiChecks`（5 项，名称含 YuJi 供 `--filter=YuJi`）：

1. 定义与 schema：群/男/3 体力入双身份池；缠怨保持锁定 + `OwnerHpEquals=1` 压制规则；激活恰好一张手牌、每名角色回合限一次、单效果；四条解析拒收样例（非拥有者目标、多张扣置、装备区来源、非正每次限一次）。
2. 质疑为真：每名其他存活角色依次进入质疑序列，首位质疑者翻开的牌与声明一致、获得缠怨恰一次，扣置牌消耗且拥有者摸二，暂停检查点与结算完成后状态/事件流重放全等。
3. 质疑为假：翻开的牌与声明不符，不授予缠怨，扣置牌从隐藏手牌直达弃牌堆（GuhuoVoid），声明后蛊惑当回合耗尽，重放全等。
4. 每回合限一次：同一回合内再次发动不可用，跨过其他角色回合后重新可用，第二次声明暂停点重放全等。
5. 缠怨持有者跳过质疑：缠怨持有者被排除在后续质疑序列外（活体持有者，排除唯一归于缠怨），质疑窗口越过其到下家并授缠怨，重放全等；随后将其杀至体力 1，快照保留已获缠怨，且以引擎同款 `MatchSkillBindingIndex` 分片验证缠怨保留、天妒（体技）失效。

门禁：定向 5/5；Release 全解构建 0 警告 0 错误；全量 Core 656/656 全绿（基线 651 + 本批 5 项）。入池位移（AI 于吉在开放池对局中发动蛊惑）暴露并修复一处本批真实缺陷：声明为酒时只 `BeginCardUse` 未 `BeginSimpleCardUse`，卡牌使用帧与 Processing 中的酒悬留，破坏"空置空化窗口与 Processing 一致"及"活动结算中不得终局判平"两条核心不变量；修复后与原生 `ResolveAlcohol` 同型收尾。编目合同检查同步补齐 `usePlacedCardAsDeclared` 解析夹具。立绘经 `tools/sync_general_art.py` 全套入库（official-yu-ji.png 与 Skins/yu-ji/ 六张），finalize 阶段干净通过。
