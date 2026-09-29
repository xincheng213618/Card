# RUNTIME V65 —— 规则 188 / 经典包 1.159.0

本批为单武将交付：神曹操（归心 / 飞影，神话再临·林包 2010，神 3 体力，最低规则 188）。经典包 1.158.0 → 1.159.0；规则 epoch 仍为 188，因为新增操作只被新内容消费，同一内容指纹下的既有命令结算、事件顺序、随机数消费与暂停恢复语义均未改变（Checkpoint schema 仍为 3，规则 JSON schemaVersion 仍为 62，`minimumRulesVersion 188` 表示本内容不需要更高 epoch）。

## 神曹操批次：归心 / 飞影

### 新增公共能力

1. **操作 `takeRandomCardFromEveryOtherCharacter`**：`SkillProgramEffectOp` 加值；效果描述符要求 `target=owner` 与显式 `zones`（`hand`/`equipment`/`judgment` 的子集，必须非空且不重复），不接受条件节点（`RequireAlways`）。结算按**当前回合顺序**（以拥有者为原点）遍历每名其他存活角色：按声明区域汇总候选（手牌、装备区、判定区），按牌 Id 稳定排序后以 `_random.Next` 抽取一张，经 `From → Processing → Hand(owner)` 两段移动进入拥有者手牌，移动原因 `skill-program.<skillId>.TakeRandomCardFromEveryOtherCharacter`；区域中没有牌的角色自然跳过。
2. **公开脱敏事件 `ProgramRandomCardsTakenFromCharactersEvent`**：只发布拥有者、参与座位、声明区域与总数；手牌身份不进入事件流（与突袭 `ProgramRandomHandCardsTakenEvent` 同一脱敏口径），装备区/判定区牌的移动由移动账本与公开牌区自身体现。
3. **AI 估值 `ProgramCompositionAi.TakeRandomCardFromEveryOtherCharacter`**：按“每名其他存活角色各得一张”计入 `ownerDraw`，公开区域（装备/判定）额外计入中性调整；无从公开上下文获得合格人数时以 1 为保守下限。

### 消费方

- 归心：`afterDamageApplied` 触发，`subject=owner`、`damageOccurrence=perDamagePoint`、可选；effects = `takeRandomCardFromEveryOtherCharacter(zones: hand+equipment+judgment)` + `turnOver(owner)`。每点伤害各发布一次私有发动/跳过 Choice（与神司马懿忍戒、曹丕放逐同型）。
- 飞影：纯规则查询 `{"query":"incomingDistance","operation":"add","value":1}`，零新增 Core 代码（与义从的入距分支同型）；不影响神曹操计算与他人的距离。

### 复用与零增量

- 触发窗口、每点伤害游标、可选 Choice、绑定复验与暂停回放全部复用 `afterDamageApplied` 通行链。
- 翻面复用 `turnOver`（曹丕放逐 / 蔡文姬悲歌先例），暗置时翻回正面。
- 距离复用 `SkillRuleQuery.IncomingDistance` 与既有 `RuleQueryService.EvaluateDirectionalDistance`，不新增距离特例。
- 全部内容由 `classic-shen-cao-cao.rules.json` / `.presentation.json` 承载，注册只增加资源常量、惰性目录、两条 `AddSkill`、一条 `AddGeneral` 与武将池条目。

## 验证口径

定向检查 `ShenCaoCaoChecks`（3 项）：

1. 定义与 schema：神 3 体力、双技能与身份池；归心触发形态（窗口/主体/每点/可选/两效果/三区域）、飞影 `incomingDistance +1`；四条解析拒收样例（空区域、非法区域、重复区域、非拥有者目标、带条件的随机取牌）。
2. 归心随机取得并回放：人类神曹操用【决斗】主动承受伤害后，归心窗口按每点伤害发布一次；发动后公共事件列出的座位恰为“区域里仍有牌”的其他角色，每名被取牌角色恰有一张牌经 Processing 进入神曹操手牌、卡牌总数守恒、神曹操翻面；暂停检查点与结算完成后的状态/事件流重放全等。
3. 飞影入距：其余四名角色计算与神曹操的距离 = 座位环距离 + 1，而神曹操计算与他人的距离保持环距离不变，自身距离仍为 0。

门禁：定向 3/3；Release 全解构建 0 警告 0 错误；全量 Core 646/646 全绿（基线 643 + 本批 3 项）；WPF 图鉴过滤器通过（god 组 4 名成员，新增 147-gallery-god.png 渲染），缺立绘名单不含 classic:shen-cao-cao。入池位移另暴露并根治两处既有夹具脆面（鲁布无双闪夹具的 GeneralId 白名单 → 可观测量谓词 + 完整断言体深探针；救援夹具按引擎生效势力分类 + 合成桃注入探针），均未放宽断言。

批次记录见 [2026-09-30-shen-cao-cao-guixin-feiying](../../benchmarks/2026-09-30-shen-cao-cao-guixin-feiying.md)。
