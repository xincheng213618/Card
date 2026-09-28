# 曹丕批：死亡遗留牌申领与放逐翻面

状态：曹丕（神话再临-林，2010，魏3体力）单武将交付完成。schema62 / 最低规则179 / 经典包1.149.0 / Checkpoint3；新内容只在1.149注册、声明最低规则179。本批为单对话交付；工作区内并行批次的被动伤害/体力程序改动（规则178）与徐盛、张松改动未纳入本批验证，源码文件互不覆盖，本批在其之上叠加。

## 技能与官方口径

- 行殇：当其他角色死亡时，你可以获得其所有的牌。
- 放逐：当你受到伤害后，你可以令一名其他角色翻面，然后该角色摸X张牌（X为你已损失的体力值）。
- 颂威（主公技）：当其他魏势力角色的黑色判定牌生效后，其可以令你摸一张牌。（本批未实现，见边界）

官方现行文本取自 sanguosha.cn 武将详情页（hero-detail-43），放逐的X核验为曹丕自己已损失的体力值（2010年林包初印为“补X张牌然后翻面”，现行文本为先翻面再摸牌，按现行实现）。行殇按死亡清理完成后从弃牌堆申领实现——死者的手牌、装备、判定区与特殊区牌在死亡流程先行移入弃牌堆，“获得其所有的牌”与“从弃牌堆获得死者刚清理的全部牌”结算等价。来源记录见 [cao-pi-2026-09-28](../content/sources/cao-pi-2026-09-28.json)。

## 公共能力

- `claimDeathCleanupCards`（效果操作）：characterDied 窗口内把死者刚清入弃牌堆、此刻仍在弃牌堆的全部牌移入持有者手牌；经击杀窗口帧的 DeathFrameId 回溯 DeathResolution，持有者即死者时拒收；原因 `skill-program.{skillId}.{bindingId}.claim-death-cleanup`；AI 估值 +10。
- `deathVictimHasCards`（触发条件/事实）：characterDied 窗口冻结的“死者清理牌数>0”，解析器对其他窗口拒收；死者无牌时不再发起无意义询问。
- 窗口能力：characterDied 窗口能力位由 Common 扩为 `Common | Death`，击杀者窗口内允许 Death 类效果操作。
- `BeginPlayerDeath` 的七处死亡清理移动统一记录进 `DeathResolution.CleanedUpCardIds`（标记死亡之后、OwnerDied/CharacterDied 窗口之前），作为申领回溯与事实冻结的依据。
- 放逐完全复用既有 `selectTarget`/`turnOver`/`draw(ownerLostHp)` 语汇，无新增表达式。

## 内容与验证

内容：`classic-cao-pi.rules.json`（行殇/放逐，revision 1，最低规则179）、`classic-cao-pi.presentation.json`（行殇描述为官方逐字现行文本）；`StandardClassicGeneralPackage` 注册武将（faction wei、portraitKey cao_pi、BaseHp 3），版本 1.149.0，`CurrentGeneralIds` 追加 classic:cao-pi。官方立绘（gid 44，经典形象104401，574×761）入 general-art-catalog，WPF 资产 `official-cao-pi.png`，图鉴神话再临·林组（myth-forest）新增曹丕；`tools/sync_general_art.py --phase verify` 全目录通过。本批顺带修复该目录既有遗留：zhang-song 条目此前以 JPEG 文件直接登记导致 verify 中断，已无损转为 PNG 并更新记录（视觉内容不变）。

定向检查 `tests/CardGame.Core.Tests/CaoPiChecks.cs`（4项，自然命令与真实决策应答）：

1. 定义与触发schema：注册表、魏3体力、身份池、行殇/放逐关键触发属性；三类schema拒收样例（deathVictimHasCards 声明在 ownerDied 等）。
2. 行殇申领死者全部牌并回放：申领移动与死亡清理集合逐一对应、牌进入曹丕手牌、原因含 claim-death-cleanup；Checkpoint 还原后事件与状态全等。
3. 拒绝行殇：死者清理牌全部留在弃牌堆。
4. 放逐翻面并按已损失体力摸牌：目标翻面、摸牌数 == 已损失体力、原因含技能ID。

验证结果：定向4/4通过（新构建复验）。Release 全解构建0警告0错误。Core 全量 562/565：3个失败均为工作区内并行批次的在制面——绑定戳失败（"HP/max HP/身份揭示不得使绑定失效"）与天香伤害转移回归对应其体力/伤害公共触发时机改动，"implemented card content"计数失败（Expected 45, got 48）对应其新增武器牌鬼龙斩月刀；曹丕4项全部通过。更早一次并行中间态构建上的全量唯一失败（借刀杀人）在其后续编辑后自愈，两次构建间曹丕代码未变，均非本批面。WPF 全量套件当前被并行批次的鬼龙斩月刀缺牌面阻断（CardArtworkChecks 首项即中断）；本批 WPF 面按过滤器验证：`--filter="general portraits"` 的缺立绘断言不含 classic:cao-pi（列出的 xu-sheng、zhang-xiu、ol:shen-guan-yu 属并行批次未交付立绘）；`--filter="complete matches"`（含图鉴系列顺序、myth-forest 分类与5局完整UI对局）通过。`dotnet format --verify-no-changes` 全解仅6处报错，全部位于并行在制的 ZhongHuiUiChecks.cs / WangYiUiChecks.cs，本批文件零报错。

## 边界与未覆盖

- 主公技“颂威”未实现：需要势力类触发事实与“判定角色持有决策权”两类新引擎语义；经典黄天的 contributions 机制形状不符（颂威是判定生效后的触发型摸牌，不是出牌阶段交牌）。后续如收录需新增公共能力，不在本批凑合。
- 多角色同时死亡时每个 characterDied 窗口独立询问行殇；连环传导下的合并申领未逐一专测。
- 立绘经官方页面下载并记SHA-256；未做实机多DPI人工验收（与既有各批一致）。
