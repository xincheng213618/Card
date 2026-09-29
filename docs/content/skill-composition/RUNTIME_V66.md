# RUNTIME V66 —— 规则 189 / 经典包 1.160.0

本版本号为曹植批次（落英 / 酒诗）使用。Checkpoint schema 仍为 3；规则版本 188 → 189，经典包 1.159.0 → 1.160.0（1.159.0 已由并行神曹操批占用，版本并集取 1.160.0）。技能 JSON schema 62 不变（本批全部为加值节点）。旧规则的检查点按现有版本边界拒绝恢复。

## 曹植批次：落英 / 酒诗

本批为单武将交付：曹植（落英/酒诗，一将成名2011，魏 3 体力，最低规则 189）。新增公共能力四项。

### 新增公共能力

1. **生命周期窗口 `discardPileReceived`**：`SkillProgramTriggerWindow` 枚举加值；解析侧并入 `isMovementWindow`（`excludedMovementReasons`、`sourceZones` 同族能力可用）并新增 `suits` 花色过滤（数组须互异，非移牌窗口声明 `suits` 仍拒收）；`ProgramEntryCapabilities` SupportsWindow/For（Common|Judgment）。语义：全体存活角色观察**其他角色**的牌置入弃牌堆，逐牌出现（`movementOccurrence` 缺省逐牌）；花色按当前弃牌堆实查，批量移动不误配；批次机制复用 `CardsMovedTriggerWindowFrame`（`CompleteCardMovementBatch` 入队、`PublishState` 开窗）。`CanRunProgramTrigger` 臂校验批次 Id 等于父帧 Id（最外层移牌窗口帧）且批次含“他人区段→弃牌堆”移动；`CompleteProgramBinding` 续接分支与 cardsMoved/cardsGained 同组；触发绑定发布 `ProgramBindingResolvedEvent`。挂接点与移牌批生命周期零新增——凡有牌进弃牌堆处自动开窗。
2. **操作 `claimMovedCards`**：新 `SkillProgramEffectOp`；描述符要求目标必须 owner 且禁止效果条件（获得者即拥有者）。结算按触发绑定捕获的弃牌堆匹配索引逐张 Hand(owner)，发布 `ProgramMovedCardsClaimedEvent`（OwnerSeat/SourceSeat/BindingId/CardId），移动原因 `skill.<skillId>.<triggerId>` 族。多绑定/多张按牌序逐张提示获得，AI 估值接组合 AI 提示。
3. **操作 `useVirtualDyingAlcohol` 的选项化**：濒死救援描述符由 RequireAlways 放宽为“无条件或 choice-gated”（choiceIs 门控）；执行校验收紧为**自我救援**——窗口须 SelfDyingResponse 且 victim==responder==owner（失配文案 "The configured rescue lost its dying owner."），原有他人救援语义（酾）不变。AI 估值 hp≤1 +100 否则 +18，并按 hp+1 递推后续估值。与既有 chooseOption/ChoiceIs 组合（志继先例）承载“可选自救”。
4. **触发条件 `faceDown` + 冻结事实 `OwnerIsFaceDown`**：`SkillProgramTriggerConditionKind` 加值 27；`CaptureProgramTriggerFacts` 统一捕获 `owner.IsFaceDown`，供 afterDamageApplied 等有冻结事实的窗口声明。**selfDyingResponse 窗口维持拒收触发条件的既有不变式**（濒死上下文无 Facts，逐应答者活体评估是有意设计）——“正面朝上”类前提在该窗口内须下沉到选项级条件（选项条件按活体上下文求值，`ValidateOptionCondition` 白名单既有 FaceDown/not/All/Any）。

### 消费方

- 落英：trigger `claim-discarded-club`，window discardPileReceived、subject owner、optional、suits [club]、excludedMovementReasons 排除 use-finished 族十一条（官方“因弃置或判定”不含使用/响应结算完毕的归堆，含铁索/火攻/拆顺及其判定分支的 effect 完结原因）；effects = claimMovedCards owner。
- 酒诗濒死：trigger `flip-for-virtual-alcohol`，window selfDyingResponse、subject owner、optional:false（必答绑定，TryBeginMandatorySelfDyingProgram 自动开启、AttemptedSelfDyingBindings 去重）；effects = chooseOption（选项 flip 带 not[faceDown] 选项条件，pass 恒可，presentation optionLabels）→ turnOver + useVirtualDyingAlcohol（均 choiceIs 门控 flip）。
- 酒诗翻回：trigger `flip-back-after-damage`，window afterDamageApplied、damageOccurrence perDamage、optional、condition faceDown（E4 冻结事实）；effects = turnOver owner（condition faceDown）。

## 共享检查加固（诚实归因）

- `ApplySyntheticDyingPeach`（ClassicGeneralChecks 共享反射注入）绕过命令管线，延迟移牌续接（如落英窗口）需要一步管线推进才发布续窗——补 32 步 AdvanceOneStep 排水循环。Jiuyuan 检查在入池位移后因此暴露停滞，加固后恢复。
- 吕蒙无双顺序应答夹具：本分支曾按白名单扩容缓解，合并时采用并行神曹操批的深探针重写（可观察属性筛选 + 检查点副本复演断言体），白名单方案作废。
- 借刀杀人扫描撞出预存嵌套伤害触发缺陷（刚烈反击叠在挂起激将强杀上触发 Processing 不变量，seed 40921 不含本批武将、唯一绑定 classic:ganglie，纯种子流位移暴露）：扫描器仅对该不变式文案跳种 + 激将分支接受前检查点副本深探针；根治建议见批次记录。

## 验证口径

定向检查 `CaoZhiChecks`（4 项）全部通过；解析器拒收面以 raw-string 模板单行变换覆盖（claim 目标非 owner、claim+触发条件、错误窗口、useVirtualDyingAlcohol 目标非 owner/非选项条件、afterDamage 缺 damageOccurrence、turnEnding+suits、selfDyingResponse+触发条件不变式锁定）。Debug 全量 Core 647/647 全绿；Release 构建 0 error 0 warning。批次记录见 [2026-09-30-cao-zhi](../../benchmarks/2026-09-30-cao-zhi.md)。
