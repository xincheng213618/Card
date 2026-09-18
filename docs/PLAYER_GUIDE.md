# 战局指南与卡牌图鉴

本轮把一页静态玩法说明改成可随牌局查阅的指南。玩家可通过顶栏「指南」、操作区「操作提示」、开局面板或 F1 打开；Esc 先关闭指南，返回原来的开局设置或牌桌选择。

## 玩家体验

- **出牌建议**：轮到自己出牌时，打开「当前操作」并点击「看看出牌建议」，查看推荐行动、手牌、目标和依据；原有选牌不变，返回牌桌后自行决定。详见 [`PLAY_ADVICE.md`](PLAY_ADVICE.md)。

- **历史战绩**：开局、牌桌和结算页均可查看最近 50 局的胜负和全员数据，重启后仍保留；浏览时暂停推进，Esc 返回原来的选牌。详见 [`MATCH_HISTORY.md`](MATCH_HISTORY.md)。

- **铁索目标与重铸**：新局可点选一到两名存活武将（含自己），主按钮或 Enter 确认；也可不选目标，点击「重铸换牌」。旧档按原规则显示，指南说明每名目标会进入或解除连环。详见 [`IRON_CHAIN_RECAST.md`](IRON_CHAIN_RECAST.md)。
- **对局节奏与观战**：右下角选择从容／标准／快速；阵亡后点击「快速观战」继续看到最终结果，取消自动推进可暂停。速度随存档保留，玩家决策不会被自动代选。
- **终局回顾**：结算页显示各武将的阵营、存活状态与伤害／承伤／回复／救援／击败数据，自己的行高亮；列标题可查看统计口径。详见 [`MATCH_SUMMARY.md`](MATCH_SUMMARY.md)。
- **当前操作**：根据开局、选将、选牌和目标、弃牌、响应、救援或终局给出说明与下一步。自己的手牌列出当前使用条件，点击牌名直接打开对应图鉴。
- **阵营与回合**：2v2 下显示公开队伍、配合与胜负规则，身份局下说明四种身份的胜利目标、回合顺序、响应与濒死；明确武将势力与身份阵营不同，以及本项目仍采用部分简化规则。
- **卡牌图鉴**：收录当前 27 种已实现牌，按牌名或效果搜索，也可按基本牌、锦囊牌、装备牌筛选。空结果可直接清空；牌面规则使用现有内容定义。
- **操作与存档**：说明确认、取消、数字选牌、静音、自动保存与继续对局。

灰色手牌可通过悬停查看原因，包括体力已满不能用桃、本回合杀次数已用完、已有酒效、仅能响应或没有合法目标。能通过武圣、龙胆转化的牌先按实际合法动作判断，避免把可用转化误说成不能使用。

若你是急救者，濒死窗口会把自己的红色非桃手牌列为“当作【桃】”的私有救援选项；牌面仍按实体牌移动，其他玩家看不到该候选。

杀／闪响应、救援、无懈和火攻可直接选中亮起的手牌，再点击主按钮或按 Enter；Esc 只取消选牌。一个实体牌有多个效果时，在中央明确选择。响应操作和群体结算修复见 [`HAND_RESPONSES.md`](HAND_RESPONSES.md)。

主动技能选牌时，指南显示技能规则、牌与目标的数量要求和已选目标。选齐后点击「发动技能名」或按 Enter；Esc 取消整次选择。教程返回保留技能选择并恢复手牌可用状态，详见 [`SKILL_INTERACTION.md`](SKILL_INTERACTION.md)。

指南打开期间禁用后方牌桌和开局面板，自动推进计时器暂停。关闭后保留手牌与目标选择，并遵循此前的自动推进开关；不会替玩家选牌、响应或结束出牌。F1/Esc 的窗口处理先判断指南，指南内输入数字或 Enter 不会触发牌桌快捷动作。

## 实现边界

`GameEngine.GetHumanHandGuidance()` 只读本地玩家的手牌、其自己的当前询问，以及 `GetHumanLegalActions()`。返回 `HandCardGuidance` 的理由与文案，不提交命令、不推进帧、不消耗随机数、不发布事件。AI 专用对局返回空集合。实际是否可打仍由合法动作决定，弃牌多选仍使用原来的精确子集规则。

`MainViewModel.PlayerGuide` 使用玩家快照和现有选择状态产生当前说明；图鉴来自 `CardCatalog.ImplementedCards`。`PlayerGuidePanel` 承担搜索、分类、滚动和导航。指南状态不写入规则存档，本轮不改变 Checkpoint 格式、命令重放或 AI 策略版本。

指南负责情境说明和规则查阅；另有四步新手演练用于练习杀、闪、桃和弃牌，详见 [`NEW_PLAYER_TUTORIAL.md`](NEW_PLAYER_TUTORIAL.md)。目前没有教学奖励或持久化完成进度，桌面输入与多 DPI 验收仍待进行。

## 验证

```powershell
dotnet build .\CardGame.sln -c Release --artifacts-path .artifacts/guide-final
& '.\.artifacts\guide-final\bin\CardGame.Core.Tests\release\CardGame.Core.Tests.exe'
& '.\.artifacts\guide-final\bin\CardGame.Wpf.Tests\release\CardGame.Wpf.Tests.exe' "$PWD\artifacts\guide-review\ui-final"
& '.\.artifacts\guide-final\bin\CardGame.Wpf\release\CardGame.Wpf.exe'
```

- 本轮指南阶段的全解 Release 构建为 0 警告、0 错误，当前 Core **120/120**、WPF **29/29**；规则版本 5 的致命伤害后触发窗口沿既有伤害技能 Prompt 和濒死流程回归，目标手牌不透明牌位按钮、主动技能扩展按钮（含回春双牌/多目标选择）与合法性提示另有离屏回归，马术的技能说明和公开距离文本、奇才的锦囊说明和远距离目标选择、急救红牌当桃的私有濒死选项也由离屏回归覆盖，苦肉濒死后的私有救援续接与 v12 酒仅自救、v3–v11 跨座位兼容回放由 Core 确定性回归覆盖，加入四步演练后的当前结果见新手演练文档。
- Core 新增检查覆盖多种实际发牌、只读性、私有边界、合法动作一致性、转化、桃/酒/杀限制、真实响应/弃牌和命令重放后的提示一致性。旧 AI Checkpoint 固定样本继续通过。
- WPF 加载实际控件，验证搜索与分类绑定、27 种牌的描述、手牌到图鉴的跳转、空结果、模态输入隔离和选择保留；使用真实 Dispatcher 计时器检查暂停及恢复原来的自动推进策略。
- 五场完整界面对局实际出牌 49 次、回答响应 30 次，均完成结算与存档重建。其间在 11 种边界查阅指南并比对完整状态：选将、出牌、弃牌、杀响应、闪响应、救援、五谷丰登、火攻展示、无懈、刚烈反制、终局。
- 所有控件按 1440×860 或 1120×740 离屏渲染，测试不调用 `Window.Show()`。快捷键检查直接调用窗口的按键分发函数；这不等同于实际鼠标、键盘、焦点恢复或多 DPI 验收。此前桌面自动化被物理 Esc 中止，本轮未恢复该操作。
- 本轮全仓 `dotnet format --verify-no-changes --no-restore` 通过；本轮新增的 WPF 急救控件回归文件已单独格式化，未批量改写其他历史文档或无关脏改动。

日志：[Core](../artifacts/guide-review/core-final.txt)、[WPF](../artifacts/guide-review/wpf-final.txt)、[全仓格式报告](../artifacts/guide-review/format-final.txt)。

## 界面对比

原来的静态玩法面板：

![原静态玩法](../artifacts/guide-review/00-guide-before.png)

当前真实选牌与目标对应的指南：

![当前操作](../artifacts/guide-review/ui-final/21-guide-current.png)

1120×740 小窗口中的全部卡牌与使用时机：

![卡牌图鉴](../artifacts/guide-review/ui-final/26-guide-all-cards.png)

其他证据：[开局说明](../artifacts/guide-review/ui-final/20-guide-setup.png)、[搜索命中](../artifacts/guide-review/ui-final/22-guide-cards.png)、[空结果](../artifacts/guide-review/ui-final/23-guide-empty-search.png)、[真实响应](../artifacts/guide-review/ui-final/24-guide-response.png)、[身份与回合](../artifacts/guide-review/ui-final/25-guide-basics.png)、[操作与存档](../artifacts/guide-review/ui-final/25-guide-keys.png)。
