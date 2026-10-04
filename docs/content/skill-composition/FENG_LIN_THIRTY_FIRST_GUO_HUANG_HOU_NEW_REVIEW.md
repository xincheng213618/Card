# 726 NEW 独立静态复核

本报告仅新增文件与准确成熟 producer 的窄审。原合同 review/manifest、6800 两份报告和作者原 48af 冻稿均未改。未运行编译、loader、测试、native AI、游戏、benchmark 或 HTTP；没有修改主区、作者文件或版本。

输入 `guo-huang-hou/new-files-manifest.json` SHA `48af04566f3f3b28244977a8402944ab7d5d8b25305f8ce445c659dfea57b45a`。13 NEW 与 4 support 逐名原始 SHA 全匹配。当前原文与 linked 来源 SHA 分别为 `9387c8212c44a4909ee2ace1e5657ac9d8632151f8f3093035ec0bcd7e06251e`、`f66822510b9c9716788f0d0768e79ebc40ae3933dcf29c6885400b21a05b8bda`，此次只读核 SHA，没有重新联网。窄读主线 HEAD 为 `e492e9e8d83a1d921de471d1463c4ae288b0251f`；最终 OLD 尚在作者接线窗口，本报告不认证最终 OLD。

## 确定发现与处置

**[P2] 原冻稿的两个 Action 引用比较在 JSON 恢复后失效。** `GameEngine.TieredRoundZeroReturns.cs:74` 的 `use.Action == action` 和 `:92` 的 `window.Action == accepted` 比较对象引用；主线 `CardActions.cs:49` 的 `CardActionContext` 是未覆盖 Equals 的 class。原 CardUse 与其子窗口分别保存 Action，经 JSON 恢复成为两个对象，已一致的冻结动作仍被拒绝。第一处拟接普通锦囊 typed continuation，第二处属于零实体借刀的真实实体杀孩子清理后 Completed 暂停。此项已即时同步 root 和作者。

作者保留原 48af 稿，独立冻结 `followup-01-action-value`。该 followup manifest SHA `8a87b839399d71bf432f75fda81a5b4ee6d3262eafb85669ff37a8127a77294a`、patch SHA `eadeab0e2242db65bdbda7d882729938bcf7357f9c4d4657de3aa91b6d174cfa`、有效 ZeroReturns SHA `f61c05cd904cd0bf0cb37707ef24c851be86eb2a7c513c1f5f85ec2b5d8dde55` 均已实读核对。独立纯文本重建两段 patch 与完整 preview 相同。新增局部比较 `TieredRoundActionsStructurallyMatch` 比较 Action/Parent ID、Type、Actor/Provider/Requester/Responder/Opponent、EffectiveKind、Suit/Rank/Red、FactionOrigin、ordered Targets、区分 null/显式的 ordered DesignatedTargets、ordered material costs/conversion chain。两组目标比较已蕴涵 EffectiveDesignatedTargets 一致。成本、转换、FactionOrigin 均为纯标量 record，值比较可用于冷恢复；原 issued receipt/parent/processing 门保持。修后该确定引用问题已静态收口，尚无运行回归证据。

**最终接线必须排除会离区销毁的 generated Equip 材料。** 新 level0/1 HE 配置不限制 printed kind；成熟 `GameEngine.CardConversions.cs:212` 的 configured 候选原路径可接受 generated weapon。主线 `GameEngine.cs:16585` 的真实装备移动会把其 Processing destination 改为 OutsideGame。若直接作为一实体转换付款，不能把离区销毁算作仍在 Processing 的真实用牌实体。作者确认 final generator 原尚无局部材料门，随后补入仅新 `TieredRoundConversion` rule 排除 `card.IsGeneralWeapon`。本轮已窄读其未冻结 OLD preview `GameEngine.CardConversions.cs:244`，确有 `(rule.TieredRoundConversion is null || !card.IsGeneralWeapon)`，旧转换缺政策时恒真，保留一般 HE。最终 OLD 必须保留该门及独立默认说明；这不属于原 13 NEW 清单，本报告没有宣称全部21 OLD已审查、实际合入或运行。

## 其余实际核对范围

- `TieredRoundConversionSchema.cs:9–42` 新政策严格两键、准确 tier/input count、真正 UseOnly direction、Basic/InstantTrick 分类，零实体空 source/input 集合；旧 nullable 缺省的正数 loader 路径仅在 OLD 合同承诺，本轮没有跑 loader。
- `ProgramTieredRoundConversion.cs:36–134` level0 own actual Play/Phase 与 level1/2 owner+skill+state Round 额度；真 Nullification 通过原 counterspell owner/parent action，真正 Slash-defense Dodge 包括成熟原生/其他转换/八卦，排除 group/faction/普通 Slash 打出。旧 Action-null 虚拟 Use 仅复用准确原 producer；没有造 AcceptedAction。
- `GameEngine.TieredRoundZeroUses.cs:17–68,163–287` 已发布零实体动作与接受前源/目标匹配；实际 CardId0、空 physical IDs/cost、receipt/quota 一次、普通距离/次数/目标机制和实际酒基础伤害。后续 actor 替换从原 provider/唯一 Declared 沿成熟 Previous→Actor 因果核对，未以当前 shard 反悔原发行。
- 同文件 `:71–152` 借刀与青龙固定原窗口、source、target、weapon、choice 与不同 source-quota 口径；青龙 CompleteAttack→Suspend 的次序与成熟原 producer 一致。其最终 OLD AI 动作识别与入口 gate 尚为计划接线，不能写作已经运行。
- `GameEngine.TieredRoundZeroResponses.cs:5–179` 真零无懈/闪/桃酒响应使用、实体与零实体 response receipt、原特殊 counterspell return 与 Dying token；`ZeroReturns.cs:5–67` 完整精确 incoming parent 与 actual rescue suffix，再处理原 Processing 例外。没有任意 CardUse presence 的通用放行。
- `GameEngine.CappedConversionBenefits.cs:8–108` 原 AfterDamage candidate/instance/hash/occurrence、X0 无 draw/movement、X>0 实际 draw ledger，排空原 gain/HP 等孩子后再修改；封顶2仍实际 Draw2。`CappedConversionBenefitObservers.cs:5–49` 先证明原 paid root 与每条 incoming edge，再允许完整 Dying rescue suffix；新 CurrentDamageAttempt 许可只锁此 root 与准确 paused Damage 参数。DrawCards 成熟逐 DrawOne 独立单 batch，所以首孩子 singleton movement 不是漏 Draw2。
- 新公共 facts 和 pending receipts 只有标量/纯标量来源元组，不引入私牌实体列表。Actions 的 Targets/Designated/Physical/Conversion 仍由成熟 `CardActionContext` 构造深冻；提示集合以 Array.AsReadOnly 克隆。新 LegalAction policy 是两标量，不引入嵌套可变集合。最终 OLD 的 optional fields/Projection 接线仍需另核。
- 内容 Wei3/Female、完整两技能与全部三阶54 viewAs/18个输出、schema62/presentation3/triggerChoices 已只读检查；历史 FAQ 与身份补证边界按原合同分列。

## 四个真实命令草稿

| 方法 | 静态读到的真实行为 |
|---|---|
| `TrueRoundNamesPhysicalConversionAndPrivateCold` | 一实体转换付款/Phase 限额、Duel 内普通 Slash 打出不占名、new Round、原生实体 Dodge 完成窗冷续行；首个 routine。 |
| `CappedDrawBeforeModificationOwnsChildrenAndCold` | X0→1、Draw1 gain→Recover→HP 孩子暂停、恢复实例续行→2、封顶真实 Draw2。 |
| `SharedRoundQuotaSurvivesTierRegrantAndExtraTurn` | 共享 Round 额度跨升级/真实 remove+grant/extraTurn/new Round；实际零 Slash 与零 DrawTwo 完成，无假实体 movement。 |
| `ZeroCounterspellDodgeAndDyingTypedReturns` | 零无懈原 counterspell 完成窗、零闪完成窗、真实 LoseHp→Dying→零自救酒→HP 子窗；原父与 receipt 一次。 |

`BoundaryGuoHuangHouChecks.cs:191` Cold 真正序列化 checkpoint/反序列化/Restore，返回恢复实例，调用方赋回并继续真实命令；没有只比较后继续旧实例。private 检查用四视角 CreateSnapshot，真实坏 choice 断言不推进。fixture JSON 仅源码静态读，未调用 parser/loader。

未覆盖或未运行：原报告发现的普通锦囊子窗口、零借刀→实体杀 Completed 冷恢复两个回归目前只有静态补丁证据；四方法没有这些实际暂停子例。18个 output 的所有效果/其他机制组合、强制零 Slash、source-loss 后殚心、native score、source/provider/声明所有排列及最终 OLD 默认兼容均未运行。未扩大四方法或新增 runner/定义快照。除上述确定引用问题和最终 generated-material 接线要求，此次限定 NEW 审查未再确认其他 P1/P2；这不是编译、loader 或行为验收结论。
