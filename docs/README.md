# 文档目录

本文档按当前功能和开发职责导航。运行项目见 [仓库 README](../README.md)；人物规则以对应内容定义、实现和保留的来源依据为准。

## 玩家操作

| 主题 | 文档 |
| --- | --- |
| 开局与模式 | [本地对局](LOCAL_PLAY.md)、[模式与规则](MODES_AND_RULESETS.md)、[团队对局](TEAM_EXPERIENCE.md)、[国战体验](NATIONAL_WAR_EXPERIENCE.md) |
| 选牌与响应 | [目标选择](CARD_TARGET_SELECTION.md)、[手牌响应](HAND_RESPONSES.md)、[技能交互](SKILL_INTERACTION.md)、[武圣响应](WUSHENG_RESPONSES.md)、[铁索与重铸](IRON_CHAIN_RECAST.md) |
| 提示与辅助 | [决策说明](DECISION_CONTEXT.md)、[玩家指南](PLAYER_GUIDE.md)、[新手演练](NEW_PLAYER_TUTORIAL.md)、[出牌建议](PLAY_ADVICE.md) |
| 国战显示 | [双将体力](NATIONAL_DUAL_HEALTH.md)、[座位信息](NATIONAL_SEAT_PRESENTATION.md) |
| 界面与反馈 | [牌桌交互](UI_PLAYABLE_ITERATION.md)、[战斗反馈](BATTLE_FEEDBACK.md)、[音频](BATTLE_AUDIO.md)、[武将图鉴](GENERAL_GALLERY.md) |
| 本地数据 | [存档恢复](SAVE_AND_RESUME.md)、[声音与动画设置](PLAYER_PREFERENCES.md)、[历史战绩](MATCH_HISTORY.md)、[结算统计](MATCH_SUMMARY.md) |
| 推进与观战 | [对局速度与观战](PLAYBACK_AND_SPECTATING.md)、[AI 决策](TACTICAL_AI.md) |

## 核心与内容开发

| 主题 | 文档 |
| --- | --- |
| 整体结构 | [架构说明](ARCHITECTURE.md)、[核心控制](CORE_CONTROL_DESIGN.md)、[内容模块](CONTENT_MODULE_ARCHITECTURE.md) |
| 用牌与结算 | [卡牌生命周期](CARD_LIFECYCLE_CONTRACT.md)、[用牌流程](CARD_USE_LIFECYCLE_DESIGN.md)、[使用中实体材料](CARD_USE_MATERIALS.md)、[结算协议](RESOLUTION_PROTOCOLS.md) |
| 状态归属 | [区域与状态](REGION_STATE_AUDIT.md)、[运行机制](RUNTIME_REFACTOR_IMPLEMENTATION.md) |
| 人物与注册 | [人物目录](../src/Characters/README.md)、[人物模块与注册](CHARACTER_MODULES.md)、[技能能力边界](SKILL_BOUNDARY_MIGRATION.md) |
| 技能配置 | [配置格式与节点](content/skill-composition/RUNTIME.md)、[技能组合](SKILL_COMPOSITION_DESIGN.md)、[可组合规则](COMPOSABLE_RULE_ENGINE.md)、[技能实现范围](SKILL_MIGRATION_PLAN.md) |
| 验证与排错 | [开发门禁](DEVELOPMENT_GATES.md)、[异常材料](EXCEPTION_REPORTS.md)、[场景矩阵](content/SCENARIO_MATRIX.md) |

## 内容与素材资料

| 主题 | 文档 |
| --- | --- |
| 内容与能力范围 | [内容清单](CONTENT_BACKLOG.md)、[能力缺口](SANGUOSHA_GAP_PLAN.md)、[共享能力请求](content/CORE_API_REQUESTS.md)、[开发分工](MASTER_PLAN.md)、[内容开发队列](LUNA_CONTENT_BACKLOG.md) |
| 规则依据 | [参考数据](content/BWIKI_REFERENCE_DATA.md)、[人物参考与范围](content/BWIKI_CONTENT_BACKLOG.md)、[内容清单与来源](content/CONTENT_MANIFEST.md) |
| 牌区与选将 | [内容移动契约](content/CARD_MOVEMENT_CONTRACT.md)、[身份选将](content/IDENTITY_DRAFT.md) |
| 素材 | [资源目录](RESOURCE_CATALOG_DESIGN.md)、[卡牌美术](content/card-artwork.md)、[音频同步](content/game-audio-sync.md)、[动态皮肤](content/DYNAMIC_SKIN_RESEARCH.md) |

规则格式、存档格式、内容包与 AI 策略的版本来源统一见 [开发门禁](DEVELOPMENT_GATES.md)。
