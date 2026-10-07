# 蒋干单武将批次

当前 OL 蒋干（魏势力、3 体力、男、称号"锋镝悬信"，2019-09-02 荟萃-计将安出）接入：伪诚（当你交给其他角色手牌后，或你的手牌被其他角色获得后，若你的手牌数小于体力值，你可以摸一张牌。）、盗书（出牌阶段限一次，你可以选择一种花色，获得其他角色的一张手牌。若此牌与你选择的花色：相同，你对其造成1点伤害且此技能视为未发动过；不同，你交给该角色一张其他花色的手牌（若没有需展示所有手牌）。）。官方立绘 46700 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-jiang-gan-2026-10-07.json)。

| 技能 | 实现口径 |
| --- | --- |
| 伪诚 | weicheng/hand-transfer-draw 纯配置触发：cardsMoved + `movementOccurrence: perOwnerSourceHandGain` + `sourceZones: [hand]`（济园既有边界），可选触发，比较条件 currentHandCount < currentHp 在窗口捕获时求值（转移之后、摸牌之前），通用 draw 1。未新增任何触发事实、条件或 schema 节点。 |
| 盗书 | daoshu-guess 出牌阶段限一次主动程序（usesPerTurn 1，激活级目标 otherLivingWithHand）：新 op `daoshuGuessAndTake`（7172）先弹四花色公开选择，再按暗手牌口径随机取得目标一张手牌（Processing 两跳，公开证据只含座位），按拥有者手牌中的有效花色与所选花色分支。相同 → 回退 `_programUses` 本回合账本（"视为未发动过"，可立即再次发动）并经 `BeginProgramSkillDamage` 造成 1 点伤害（伤害管线随后续接并完成程序帧）；不同 → 拥有者在其手牌中选择一张与所获牌花色不同的手牌交给该角色（无则把手牌整体绑定为公开牌集并 `ProgramCardsRevealedEvent` 展示）；结算后按诱敌口径进入 cards-moved 窗口。 |

## 共享能力扩展

- 新增 EffectOp 7172 `daoshuGuessAndTake`（只占用分配的 7172–7179 段中一个）；描述符按反射目录自动注册。未新增 TriggerFactKind、PlayerMarkerKind、ConditionKind、触发窗口或 schema 节点。
- 共享文件仅三处插入：`SkillPrograms.cs` 枚举成员、`GameEngine.SkillPrograms.cs` 激活门禁一行（`CanStartDaoshuGuess`：仍有其他有手牌的存活角色）、`GameEngine.ProgramLifecycle.cs` 两个 program-action 派发行与一行 AI 选择派发，均贴近吕凯批分组行。

## 边界口径

- 盗书：花色比较与"其他花色"都以牌在蒋干手牌中的有效花色判断（BWIKI FAQ 明确口径）；"其他花色"相对所获得的牌而非所选花色（BWIKI FAQ："是与获得的牌不同的花色"），与派发提示（相对所选花色）在"交出所选花色牌"这一情形不同，本批以 BWIKI 为准。所获牌自身因花色相同天然不在可交集合内。
- 盗书：猜中时本回合发动账本立即回退，同阶段可继续发动（BWIKI FAQ："如果一直猜对可以一直发动"）；账本是引擎内 `_programUses` 字典的确定性增减，冷恢复按接受命令前缀重放重建。取得本身不触发伪诚（来源是目标手牌而非拥有者手牌），交回按每张牌一次触发伪诚，经共享 cards-moved 窗口去重结算。
- 伪诚：交给其他角色与被其他角色获得是同一物理移动形态（拥有者手牌 → 其他角色手牌区），perOwnerSourceHandGain 边界统一表达；装备自己、进入处理区、弃置等不满足"另一角色手牌区"的移动不触发。手牌数与体力值在转移后即时比较。激活级目标先于选花色（引擎主动技能目标惯例），与官方"先选花色"的叙述顺序为外观差异。

## 验证

- 本批按用户指令不含新增行为检查；已注册共享机制检查（目录门禁、组合校验器、池签名、图鉴完整性）随例行范围自动加载新内容。
- 临时诊断冒烟（worktree 外的控制台 harness，交付前删除，非测试文件）：种子 126 身份局（蒋干主公）实测——同花色分支造成 1 点伤害且同阶段再次发动被接受（账本回退）、异花色交回移动与目标手牌区落位、交回后伪诚在 hand<hp 时弹出并摸一张（3→4）、取得的暗手牌在其他座位快照中不可见、前缀冷恢复快照完全一致。展示分支（无其他花色手牌）未在自然场景中触达，逻辑与交回分支共用同一揭示调用（`ProgramCardsRevealedEvent` 既有路径）。
- Release 构建（`dotnet build CardGame.sln -c Release`）：0 error、0 warning；Debug 增量编译的 8 条既有 warning 均位于本批未触碰的文件（SkillProgramExecutor、EndingHistoricalUses、LiangXingPrograms、OwnedDeathBenefitReturns、PublicPilePreparation、SameTypeActualUseAid），非本批引入。
- 例行 `tools/Test-Changed.ps1`（无过滤）：结果见提交信息与最终汇报（分支基线 dd5945d5 自带约 46 个已知失败项）。
- 本批在独立 worktree（batch/ol-jiang-gan）开发，与周鲂、潘濬两路并行。
