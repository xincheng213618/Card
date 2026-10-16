# 战局指南与卡牌图鉴

玩家可以通过顶栏「指南」、操作区「操作提示」、开局面板或 F1 打开指南。Esc 关闭最上层指南，返回原来的设置或牌桌选择。

## 查阅内容

- **当前操作**：根据开局、选将、费用与目标、弃牌、响应、救援和终局说明下一步，列出自己的手牌使用条件；点击牌名进入对应图鉴。
- **出牌建议**：出牌阶段查看推荐行动、费用、目标与依据，查看后仍由玩家选择和确认。详见 [`PLAY_ADVICE.md`](PLAY_ADVICE.md)。
- **阵营与回合**：显示身份目标、2v2 公开队伍或国战势力胜负，以及模式的实际简化边界。武将势力与身份阵营分别说明。
- **卡牌图鉴**：使用 `CardCatalog.ImplementedCards`，支持按牌名或效果搜索、按基本牌／锦囊牌／装备牌筛选，空结果可清除筛选。
- **操作与存档**：说明确认、取消、快捷键、声音、速度与保存恢复。

灰色手牌悬停可查看不能使用的原因，如体力已满、杀次数已用完、只有响应用途或没有合法目标；转化先依据实际合法动作判断。技能选择时，指南列出规则、费用与目标范围、已选内容和确认方式，详见 [`SKILL_INTERACTION.md`](SKILL_INTERACTION.md)。

铁索通过牌桌选择一至两名存活角色，或无目标时独立重铸，详见 [`IRON_CHAIN_RECAST.md`](IRON_CHAIN_RECAST.md)。借刀杀人由中央完整有序组合选择持武器者与被杀目标，持武器者随后通过自己的私有候选选择杀、转换、协助或交出武器。

杀／闪、救援、无懈和火攻的单牌响应可以选亮起的手牌后确认；一张牌存在多种效果或费用来自其他区域时使用中央精确候选，详见 [`HAND_RESPONSES.md`](HAND_RESPONSES.md)。

## 状态与隐私

指南打开期间隔离后方牌桌与开局输入，自动推进暂停。关闭后保留选牌、目标及原自动推进设置，不代替玩家提交。指南内的数字与 Enter 不触发牌桌快捷操作。

`GetHumanHandGuidance()` 读取本人手牌、本人当前询问及合法动作，返回 `HandCardGuidance`，不提交命令、推进结算、消耗随机数或发布事件。AI 专用对局返回空集合，实际可执行操作始终由合法动作与提交校验决定。

`MainViewModel.PlayerGuide` 使用玩家快照和当前选择生成说明，`PlayerGuidePanel` 处理搜索、分类、滚动与导航。指南状态不加入规则存档。

四步实操教学见 [`NEW_PLAYER_TUTORIAL.md`](NEW_PLAYER_TUTORIAL.md)，阵亡观战见 [`PLAYBACK_AND_SPECTATING.md`](PLAYBACK_AND_SPECTATING.md)，终局统计与历史记录见 [`MATCH_SUMMARY.md`](MATCH_SUMMARY.md) 和 [`MATCH_HISTORY.md`](MATCH_HISTORY.md)。
