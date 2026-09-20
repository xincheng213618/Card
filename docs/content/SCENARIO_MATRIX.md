# 内容场景矩阵（C0 / C0-K1 / C0-K2 / C0-K3 / C0-K4 / C0-K5 / C0-K6 / C0-K7 / C0-K8 / M3）

场景 ID 作为内容测试契约；正式 Content/Scenario 测试项目在对应核心入口开放后承载这些场景。当前已实现的基础牌、命令和 Registry 场景由 `tests/CardGame.Core.Tests/Program.cs` 的 Console 自测覆盖。K1 的完整移动契约见 [`CARD_MOVEMENT_CONTRACT.md`](./CARD_MOVEMENT_CONTRACT.md)。

当前新增场景：`k5.events.lethal-damage-trigger-window` 验证规则版本 5 在目标降至 0 点体力时仍先完成类型化伤害后触发窗口、保持私有 Prompt 边界并继续进入濒死结算。

M1 新增场景：`mode.team_2v2.public_teams` 验证公开阵营分配、普通视图脱敏、AI 固定 seed 终局、队伍胜负和 Checkpoint/Replay 一致性；WPF 离屏场景同时验证 2v2 新局设置隐藏身份选择并显示阵营说明。

M3 新增场景：`mode.national_ambitious_6` 验证六人魏 3、蜀 2、野心家 1 的独立势力分配、双将隐私/明置、AI 视角、终局三方标签和 Checkpoint/Replay 一致性；`mode.national_public_attack_evidence` 另验证战术 v2/v3 只用杀、决斗、群体攻击、火攻等公开攻击和公开势力更新隐藏座位敌对置信度；`mode.national_evidence_rescue` 验证相同证据只改变隐藏目标的救援倾向，且 v1 不消费、已公开势力优先；`mode.identity_fire_attack_evidence` 验证 v3 把身份局火攻接入统一的公开攻击观察入口而 v1/v2 保持旧行为；`mode.national_evidence_checkpoint` 验证仍有暗势力时的公开攻击暂停点可序列化，并恢复相同玩家视图、事件流、AI 思考和 Prompt；当前只覆盖可玩实验，不代表完整野心家规则。

| 场景 ID | 内容 | 正向断言 | 拒绝/边界断言 | 视图与确定性 |
| --- | --- | --- | --- | --- |
| `basic.slash.target` | `standard:slash` | 合法目标受到 1 点伤害或进入闪响应 | 自身、死亡角色、空城角色不可选 | 相同 seed/命令结果一致 |
| `basic.elemental-slash.nature` | `standard:fire_slash` / `standard:thunder_slash` | 共用杀的目标与闪响应；未被闪避时分别产生 `DamageNature.Fire` / `DamageNature.Thunder`，伤害帧和三类伤害事件保持一致 | 闪避后不得产生伤害；自身、死亡角色、空城角色或伪造属性类型被拒绝；属性抗性尚未实现 | AI 只消费自己的手牌和公开目标信息；相同 seed/命令结果一致 |
| `basic.alcohol.slash_boost` | `standard:alcohol` | 出牌阶段无目标使用，公开设置一次性酒效；下一张直接杀声明时消费并造成 2 点伤害，未消费时回合结束失效；规则 v20 起每回合限使用一次，下一回合重置 | 酒效仍存在时重复使用始终拒绝；v20 即使酒效已被杀消费也拒绝同回合第二张酒及伪造命令，v1–v19 保留消费后可再次使用的历史语义；群体牌/决斗不能消费酒效，酒牌与杀牌均完整经过 `Processing` | AI 和 WPF 只消费本座合法动作；规则分支、固定 seed/命令流、Checkpoint、事件、金额和牌区一致 |
| `basic.alcohol.dying_rescue` | `standard:alcohol` | 规则 v12 起，濒死窗口只有 victim 自己的 `RescueDying` prompt 可包含酒；酒令自己恢复 1 点体力并经过 `Hand → Processing → DiscardPile`，`DyingResponseEvent.UsedAlcohol` 为真并完成濒死帧；v3–v11 仍按历史跨座位语义回放 | 同一响应不能同时使用桃和酒；伪造 Choice、错误牌区来源、非 victim 在 v12 使用酒或把酒用于群体牌/决斗必须拒绝 | 其他 viewer 看不到 prompt 和酒牌 ID；AI 只读取自己的快照/手牌；固定 seed/命令流可复现 |
| `skill.jijiu.red_card_dying_rescue` | `standard:jijiu` | 急救者在私有 `RescueDying` prompt 中将一张红色非桃实体牌当作桃使用；有效牌型按 Peach 进入恢复事件，物理牌仍经过 `Hand → Processing → DiscardPile`，濒死目标恢复 1 点体力 | 黑色牌、桃本身、非濒死窗口、错误 responder、旧 Choice 或伪造牌 ID 必须拒绝；同一实体牌不得复制，普通桃路径的事件形状保持兼容 | 只有当前 responder 看到红牌候选；普通 viewer 不看到牌 ID；AI 只读取自己的过滤快照；固定 seed/命令流、物理/有效牌型事件和 checkpoint 可复现 |
| `basic.dodge.response` | `standard:dodge` | 闪抵消杀并进入弃牌区 | 非响应窗口不能打闪；无闪不能伪造响应 | 他人看不到手牌牌 ID |
| `basic.peach.recovery` | `standard:peach` | 受伤角色回复 1 点体力 | 满血不能使用；当前 Demo 只允许自救 | AI 只能看到自身手牌 |
| `trick.draw_two.immediate` | `standard:draw_two` | 无目标使用后摸两张牌，使用牌进入弃牌区 | 伪造目标、非出牌阶段或错误牌区来源被拒绝 | 其他玩家只看到手牌数量变化 |
| `trick.barbarian_assault.group_response` | `standard:barbarian_assault` | 所有其他存活角色按座次逐一收到私有 `RespondSlash`；未响应者各自进入 1 点伤害/濒死链；父牌全程保持在 `Processing` | 伪造目标、错误响应者、未发布牌 ID 或跳过当前目标被拒绝 | 目标只能看到自己的 Prompt/手牌；固定事件顺序和 `TargetIndex` 可回放 |
| `trick.arrow_barrage.group_response` | `standard:arrow_barrage` | 所有其他存活角色按座次逐一收到私有 `RespondDodge`；未响应者各自进入 1 点伤害/濒死链；父牌全程保持在 `Processing` | 伪造目标、错误响应者、未发布牌 ID 或跳过当前目标被拒绝 | 目标只能看到自己的 Prompt/手牌；固定事件顺序和 `TargetIndex` 可回放 |
| `trick.peach_garden.group_recovery` | `standard:peach_garden` | 使用时所有存活角色（含使用者）按座次逐一处理；受伤者回复 1 点，满血者跳过；父牌全程保持在 `Processing` | 伪造目标、非出牌阶段、错误牌区来源或在结算中插入目标被拒绝 | 普通玩家只看到公开恢复结果；`TargetIndex`、`RecoveryFrame` 和同 seed 事件顺序可回放 |
| `trick.five_grains.public_draft` | `standard:five_grains` | 公开展示等同于存活人数的牌，所有存活角色按座次各选一张并进入自己的手牌；父牌和未选展示牌保持在 `Processing` 直到 draft 完成 | 伪造卡牌、错误 picker、旧 prompt、非展示区牌或跳过当前选牌者被拒绝 | 所有人看到公共展示牌，只有当前 picker 看到私有 `SelectHarvestCard`；固定事件/移动顺序可回放 |
| `trick.fire_attack.formal_reveal` | `standard:fire_attack` | 规则 v19 起可选择包括自己在内的一名有手牌角色；目标私有选择一张牌公开展示但实体仍在手牌，来源弃置同花色手牌后造成 1 点火焰伤害；自选时展示牌本身可支付弃牌成本 | 目标死亡/空手、自选时除正在使用的火攻外没有其他手牌、伪造展示牌或不同花色弃牌必须拒绝；v1–v18 保留只能选择其他角色及展示牌进入处理区后弃置的历史语义 | `PublicRevealedCards` 只公开当前展示牌，其他手牌和两段 Prompt 仍按玩家视角脱敏；当前/历史分支的移动账本、牌区守恒与 Checkpoint/Replay 均可复现 |
| `trick.borrowed_sword.force_slash_or_transfer` | `classic:borrowed-sword` | rules v42 精确选择“持武器者、其攻击范围内另一角色”的有序目标对；持武器者可用实体杀、武圣或激将压入真实子杀，完整结算响应/伤害/濒死后恢复父锦囊，否则把当前武器交给使用者 | 自身、无武器者、越出持武器者攻击范围、死亡/重复目标、伪造有效牌型、错误 responder、旧 Choice 必须原子拒绝；以装备武器本身发动武圣时不能同时用该武器扩大范围；强制杀不消耗当前回合普通出杀次数，rules v41 不发布动作 | 只有持武器者看到杀/武圣/激将/交武器候选；公开事件与移动账本区分父锦囊、子杀和武器转移；交武器与子杀两分支、激将提供者、在途/完成 Checkpoint/Replay 及 v41 兼容均可复现 |
| `basic.deal.round_robin` | `identity_8_basic_demo` | 每人初始 4 张，总牌数守恒 | 不足牌堆不能半程开局 | 其他玩家只看到数量 |
| `basic.deck.repeatability` | `identity_8_basic_demo` | 同一配方和 seed 得到相同开局 | 不同 seed 不应依赖固定历史快照 | 普通快照不含 seed |
| `k8.checkpoint.paused-prompt` | `GameCheckpoint` / `GameCheckpointJson` | 纯 `Submit` 命令驱动的状态可保存选项、已接受命令前缀、Revision、模式、内容包签名、规范化内容指纹和规则行为版本；恢复后私有 Prompt 与后续同一命令保持一致 | SchemaVersion、规则版本、命令数、Revision、模式、内容包签名或内容指纹不匹配必须拒绝；同版本内容定义漂移和旧兼容 API 直接推进的混合状态不得静默存档；缺少规则版本的旧 JSON 必须按 v1 保持历史事件形状 | Checkpoint 是可信宿主文件，可能包含私有命令参数，不得发送给玩家；普通快照仍不含 seed、他人手牌或他人私有 Prompt |
| `mode.team_2v2.public_teams` | `team:standard-2v2` | 四人公开青/赤阵营；阵营按注册计数分配，首位先手和队伍胜负可确定性回放 | 团队计数不符、身份字段混入 team mode、普通视图泄漏 seed/他人手牌或固定 seed 结果不一致必须拒绝 | 所有玩家看到 `TeamId`/队伍角色投影，手牌仍只对本人可见；全 AI、事件签名、Checkpoint/Replay 必须一致 |
| `mode.national_ambitious_6` | `standard-national-war-ambitious@1.0.0` / `national:ambitious-6` | 六席按魏 3、蜀 2、野心家 1 分配；每席选择两名同势力武将；最后存活势力获胜 | 势力计数不符、独立势力不存在或不是一席、跨势力/重复武将、普通视图泄漏他人暗将/势力/手牌或赢家已确定却未收口必须拒绝 | 自己可见私有双将和势力，其他席位未明置时显示未明；AI 只读本座视图；三方终局公开且固定命令流可回放 |
| `mode.national_zhang_jiao_4` | `standard-national-zhang-jiao@1.0.0` / `national:zhang-jiao-4` | 四席按魏 1、蜀 2、群 1 分配；群席只能组成国战张角／华佗；明置张角同时启用雷击／鬼道，黑桃雷击造成 2 点雷电伤害，鬼道可用黑色手／装备牌换判并取得旧牌 | 暗置张角、只明置华佗或 rules v88 不得启用两项程序；不得出现黄天、梅花雷击分支或旁观者暗将／技能泄漏；不含新包的旧组合存档不得被静默升级 | 自己暗置时可见完整双将但技能禁用；只明置张角后旁观者仅见该槽及雷击／鬼道，华佗继续隐藏；半明置 Checkpoint、旧组合内容哈希和完整 Replay 可复现 |
| `skill.jianxiong.after_damage` | `standard:jianxiong` | 伤害完成且牌仍在 processing 时获得杀 | 闪避、非杀伤害或牌已离开 processing 不触发 | 触发原因不泄露其他暗牌 |
| `skill.feedback.claim_damage` | `standard:feedback` | 角色存活并受到伤害，伤害牌仍在 `Processing` 时收到私有 `Feedback` Prompt；发动后获得该牌，不发动则进入弃牌堆，随后攻击帧完成 | 目标死亡、伤害牌已离开 `Processing`、未发布 Choice 或同一张牌重复取牌不能发生 | Prompt 只投影给反馈者；伤害牌 ID 只出现在可信宿主事件和反馈者私有快照；普通视图不泄漏；固定 seed 可复现 |
| `skill.yiji.gift_card` | `standard:yiji` | 郭嘉受到伤害后私有摸两张牌，从精确牌/其他存活目标组合中选择一张交给目标，或保留两张；伤害帧完成后继续 | 目标死亡、自身、未摸到的牌、旧 Prompt、伪造牌/目标或重复移动必须拒绝；本切片每次只分配一张 | 摸牌与赠牌只在可信宿主事件/移动账本中带实体 ID；只有遗计拥有者看到候选牌，目标收到后才在其私有手牌视图出现；固定 seed/命令流可复现 |
| `skill.jieming.draw_to_max_hand` | `standard:jieming` | 荀彧受到正伤害后获得私有目标 Choice；从存活且公开手牌数低于体力上限的角色中选择一名，按缺口从牌堆摸至目标上限，伤害帧完成后继续 | 死亡/满手目标、旧 Prompt、伪造目标、负伤害或重复摸牌必须拒绝；跳过时不发生补牌 | 目标合法性只使用公开手牌数量、体力上限和存活状态；摸牌 ID 只在可信宿主事件/移动账本中出现，普通视图不泄漏；固定 seed/命令流可复现 |
| `skill.yuanhu.cross_seat_recovery` | `standard:yuanhu` | 其他角色受到正伤害且仍存活、未满体力时，援护者获得私有弃牌 Choice；发动后弃置一张自己的手牌并令固定受伤目标回复 1 点体力，伤害帧完成后继续 | 伤害目标与技能拥有者相同、目标死亡/满血、拥有者无手牌、旧 Prompt、伪造牌 ID/目标或非正伤害必须拒绝；跳过时不发生弃牌或恢复 | 候选拥有者和目标座位来自可信触发帧；弃牌 ID 只在可信事件/移动账本出现，普通视图不泄漏；AI 只读取自己的手牌和目标公开体力；固定 seed/命令流可复现 |
| `skill.ganglie.judgment_and_punishment` | `standard:ganglie` | 角色受到正伤害后获得私有发动/跳过 Choice；发动后公开判定，红色时向伤害来源发布私有精确两牌弃置或承受 1 点伤害 Choice，伤害帧完成后继续 | 非受伤者触发、零/负伤害、旧 Prompt、伪造判定/牌 ID、重复弃牌、非来源提交或不完整两牌组合必须拒绝；黑色判定不发布反制 Choice | 判定牌和红/黑结果公开；刚烈发动 Prompt 只给受伤者，反制 Prompt 和两牌组合只给伤害来源；AI 只读取各自私有快照，固定 seed/命令流可复现，濒死救援后恢复原帧 |
| `skill.wuhun.nightmare-markers` | D4a 合成 `SkillKind.Wuhun` 消费者 | rules v90 在实际伤害落地后按每点分别令伤害来源获得1枚公开梦魇；2点伤害发布计数1、2两个事件，致死伤害的标记事件位于濒死事件前 | 自伤、零伤害及 rules v89 不得产生标记；本块不得发布死亡目标选择、死亡判定或直接死亡，也不得把合成消费者加入正式武将池 | 所有 viewer 看到相同正数标记而不获得额外手牌／身份信息；命令前缀 Checkpoint/Replay 重建相同事件、计数和顺序 |
| `skill.wuhun.marker-source-candidates` | D4b1 通用来源账本与候选规则 | rules v91 把梦魇内部计数按技能拥有者座位归属，公开快照保持合计；候选只含存活且该来源计数为正的最大值全部并列者 | 全零、只有死亡角色持有正数、低于最大值者不得入选；不同武魂拥有者的同名标记不得互相覆盖或清理 | 来源归属不进入普通快照，避免未来暗将技能来源泄漏；相同命令前缀重建同一归属账本，纯候选函数按座位稳定排序 |
| `skill.guicai.replace_judgment` | `standard:guicai` | 判定牌生效前，鬼才拥有者从自己的私有手牌 Choice 中选择一张替换，或跳过；旧牌结束后新牌进入同一 `Judgment(target)` 并决定最终红/黑结果 | 非鬼才、死亡/空手拥有者、旧 Prompt、伪造牌 ID、非当前候选提交、替换牌不在拥有者手牌或重复移动必须拒绝；判定完成后不得再次改判 | 替换机会只投影给当前鬼才拥有者；当前判定牌和最终结果公开，替换手牌 ID 只在拥有者私有视图和可信宿主账本中出现；候选游标和同 seed 事件顺序可复现 |
| `trick.indulgence.delayed_judgment` | `standard:indulgence` | 对一名其他存活角色使用，使用牌进入目标公开判定区；目标下回合摸牌前进入 `JudgmentFrame`，规则 v11 起非红桃跳过出牌阶段、红桃正常出牌 | 自身/死亡目标、重复乐不思蜀、旧 Prompt、伪造目标或牌 ID、判定帧外响应必须拒绝；判定牌耗尽时按失败分支收尾，延时牌不得残留；v1–v10 保留历史红黑语义 | 延时牌、判定牌和四花色结果对所有观察者公开；鬼才替换和无懈 Prompt 只投影给对应 responder；`DelayedCardPlacedEvent`/`DelayedCardResolvedEvent`、移动 reason、固定 seed/命令流可回放 |
| `trick.supply_shortage.delayed_judgment` | `standard:supply_shortage` | 规则 v18 起对战斗距离为 1 的其他存活角色使用，奇才可忽略距离；目标是否有手牌不影响使用或无懈链后的置入；使用牌进入目标公开判定区，下回合摸牌前进入 `JudgmentFrame`，规则 v11 起非梅花跳过摸牌阶段、梅花正常摸牌 | 自身/死亡/距离大于 1（无奇才）目标、重复兵粮寸断、旧 Prompt、伪造目标或牌 ID、判定帧外响应必须拒绝；远距伪造命令原子失败，判定牌耗尽时按失败分支收尾，延时牌不得残留；v1–v17 保留“目标有手牌”及无懈后空手跳过效果的历史语义，v1–v10 另保留历史红黑判定 | 延时牌、判定牌和四花色结果对所有观察者公开；鬼才替换和无懈 Prompt 只投影给对应 responder；合法动作、AI 和 WPF 共用 Core 距离结果；`DelayedCardPlacedEvent`/`DelayedCardResolvedEvent`、移动 reason、固定 seed/命令流可回放 |
| `skill.duanliang.supply-shortage-conversion` | `classic:duanliang` | 规则 v33 中徐晃可把自己的黑色基本牌或黑色装备牌从手牌/装备区当兵粮寸断使用；原生和转化兵粮寸断均可选择距离 2 的其他存活角色 | 红色牌、非基本/装备牌、原生兵粮寸断的伪转换、错误来源区、距离大于 2、重复兵粮寸断、伪造有效牌型或旧 Prompt 必须原子拒绝；v32 不发布转换或距离修正 | 判定区公开视图、同名去重、无懈、观星和结算使用有效 SupplyShortage；可信牌区诊断、移动账本与最终弃置保留原实体牌，映射随 Checkpoint 重放且离开判定区即清理；AI/WPF 只消费已发布动作 |
| `skill.luoshen-qingguo.judgment-response` | `classic:luoshen` + `classic:qingguo` | 规则 v34 中甄姬可在准备阶段发动洛神，黑色判定牌进入手牌后再次选择继续/停止，红色判定牌结束链；黑色手牌可作为闪响应杀或万箭齐发 | 非本人、非准备阶段、旧 Prompt、红色牌倾国、装备区牌倾国、错误实体 ID 或错误有效牌型必须拒绝；红色洛神后不得继续，v33 不发布选择或转化 | 初次/重复 Choice 私有，判定与鬼才替换公开，黑色取得/红色弃置唯一；Checkpoint 保留重复边界，完成命令流精确重放；倾国响应保留物理牌身份，AI/WPF 只消费已发布候选 |
| `skill.jizhi.ordinary-trick-resume` | `classic:jizhi` + `standard:qicai` | 规则 v35 中黄月英声明普通锦囊后可发动集智摸一张，再进入原无懈询问或效果；使用无懈可击时同样触发，奇才继续解除锦囊距离限制 | 非拥有者、延时锦囊、旧 Prompt、伪造 Choice、重复回答和 rules v34 不得触发或摸牌；集智不得改变锦囊目标、实体牌终点或无懈游标 | Choice 与摸到的牌仅本人可见，公开事件只记录发动与数量；普通锦囊和无懈反制两类暂停点均可 Checkpoint/Replay，父帧恰好续接一次；AI/WPF 只消费已发布选择 |
| `skill.tieqi.slash-response-prohibition` | `classic:tieqi` + `standard:mashu` | 规则 v36 中马超用杀指定目标后可发动铁骑并进行公开判定；红色结果禁止该目标用闪响应此杀，马术继续减少马超到其他角色的距离 | 非拥有者、非杀、旧 Prompt、伪造 Choice、重复回答和 rules v35 不得触发；黑色结果或跳过不得改变实体闪、倾国、八卦阵或护驾的普通响应，仁王盾仍按既有防具规则生效 | Choice 仅本人可见，判定牌与结果公开且可被鬼才替换；红色结果覆盖全部闪响应入口但不读取目标暗牌；暂停/完成 Checkpoint/Replay 恰好续接原杀一次，AI/WPF 只消费已发布选择 |
| `skill.liegong.conditional-slash-response-prohibition` | `classic:liegong` | 规则 v37 中黄忠于出牌阶段用杀指定目标后，若目标手牌数不小于黄忠当前体力值或不大于黄忠攻击范围，可发动烈弓禁止其用闪响应 | 非拥有者、非出牌阶段杀、双条件均不满足、旧 Prompt、伪造 Choice、重复回答和 rules v36 不得触发；跳过不得改变实体闪、倾国、八卦阵或护驾的普通响应，仁王盾仍按既有防具规则生效 | 条件只读取公开手牌数量、体力和攻击范围，不读取暗牌身份；Choice 仅本人可见，公开结果覆盖全部闪响应入口；两条条件独立、暂停/完成 Checkpoint/Replay、AI/WPF 均有回归 |
| `skill.kuanggu.distance-one-damage-recovery` | `classic:kuanggu` | 规则 v38 中魏延作为实际伤害来源，对结算时战斗距离不大于 1 的角色造成正伤害后，锁定按伤害点数回复体力 | 非来源拥有者、距离大于 1、零伤害、满体力和 rules v37 不得生成狂骨恢复；实际回复不得超过已损失体力，且不得在伤害应用前发生 | 只读取公开来源/目标、伤害量、体力和战斗距离；高优先级候选先于同窗普通伤害后技能，`RecoveryAppliedEvent`/`KuangguRecoveredEvent` 顺序及完成 Checkpoint/Replay 有回归，无私有 Choice 或暗牌读取 |
| `skill.wushuang.sequential-responses` | `classic:wushuang` | 规则 v39 中吕布使用杀时目标依次完成两次闪响应；吕布使用或成为目标的决斗中，另一方每轮依次完成两次杀响应 | 第一张响应不得单独抵消杀或切换决斗 responder；缺少/放弃第二张时已支付的第一张不退还并继续伤害；rules v38 仍为一次响应 | 两次响应分别使用原私有 Prompt、实体牌移动、转换牌与八卦阵/护驾/激将续接；公开进度事件只含次数。双闪完成 Checkpoint/Replay、决斗第一张不足和 v38 分支有回归 |
| `trick.lightning.delayed_judgment` | `standard:lightning` | 只能对自己使用，使用牌进入自己的公开判定区；下个回合判定为黑桃 2 至 9 时造成 3 点雷电伤害，否则转移到下一名存活角色的判定区 | 他人/死亡目标、重复闪电、旧 Prompt、伪造目标或牌 ID、判定帧外响应必须拒绝；判定牌耗尽按未命中处理，闪电不得复制或残留 | 闪电、判定牌、红/黑结果、命中/转移与雷电伤害对所有观察者公开；鬼才替换和无懈 Prompt 只投影给对应 responder；`LightningResolvedEvent`、`DamageRequestedEvent`/`DamageAppliedEvent`、移动 reason 和固定 seed/命令流可回放 |
| `skill.wusheng.red_card_slash` | `standard:wusheng` / `classic:wusheng` | 红色非杀实体牌可作为对合法目标使用或响应的 `Slash`；rules v40 经典身份把自己的装备区加入候选，`LegalAction.PlayedCardKind`、响应 Choice 与命令明确记录有效牌型 | 黑色牌、原生杀/火杀/雷杀、其他角色装备或非法目标不能伪造转化；rules v39 不发布装备候选；同一物理牌不能复制 | `CardUseDeclared` / `CardRespondedEvent` 使用有效 `Slash`；移动账本保留原牌面、实体 ID 与 Hand/Equipment 来源；装备主动完成、响应在途、v39 与包 1.21 兼容均可回放 |
| `skill.jijiu.red_card_peach` | `standard:jijiu` | 回合外可将红色非桃实体牌作为濒死救援 `Peach`；rules v41 经典华佗把自己的装备区加入私有候选 | 自己回合、黑色牌、原生桃的重复映射、其他角色装备、非濒死窗口和伪造 Choice 必须拒绝；rules v40 与演示模式不发布装备候选 | `CardUseDeclared` / `DyingResponseEvent` 使用有效 `Peach` 并保留原物理牌型；移动账本保留实体 ID 与 Hand/Equipment 实际来源；装备候选在途/完成、v40 排除均可回放 |
| `skill.longdan.slash_dodge_conversion` | `standard:longdan` / `classic:longdan` | 物理闪可在出牌阶段作为 `Slash`，物理杀/火杀/雷杀可在需要闪的响应窗口作为 `Dodge`；响应 Choice 明确有效牌型；经典包 1.21.0 将其归入正式赵云身份 | 桃、锦囊和无关阶段不能伪造转化；错误 responder、旧 Prompt、未发布牌 ID 或错误有效牌型必须拒绝；同一物理牌不能复制；包 1.20.0 仍发布 Standard 赵云/龙胆身份 | `PlayedCardKind`、`response-card-kind` 和 `CardRespondedEvent.EffectiveCardKind` 记录有效牌型；移动账本保留原牌面和同一实体 ID；普通视图不泄漏他人手牌；主动完成与响应在途 Checkpoint/Replay 均精确恢复 |
| `skill.mashu.outgoing_distance` | `standard:mashu` | 拥有马术的角色通过 `GetCombatDistance` 将其到其他角色的公开距离减少 1；距离至少保持为 1，顺手牵羊和杀的合法动作消费同一查询 | 自身距离不得被改成负数或用于越权目标；无马术角色、非公开技能映射或 UI 直接改距离均不得生效 | AI 只读取自己的技能、公开座位/装备和合法动作；WPF 座位卡显示调整后的公开距离；固定 seed/命令流可复现 |
| `skill.qicai.trick_distance` | `standard:qicai` | 拥有奇才的角色使用距离型锦囊时跳过距离限制；顺手牵羊及规则 v18 的兵粮寸断均可合法选择距离大于 1 的其他存活角色 | 无奇才、非锦囊基本牌、目标死亡、顺手牵羊目标无可用牌、重复延时牌、伪造技能映射或 UI 直接扩大候选均不得生效；Core 仍校验目标区域和实体牌来源 | AI 只读取自己的技能、公开目标/区域和合法动作；WPF 通过同一目标选择投影；普通视图不泄漏手牌 ID；固定 seed/命令流可复现
| `skill.paoxiao.slash_limit` | `standard:paoxiao` / `classic:paoxiao` | 出牌阶段杀次数不受 1 次限制；经典包 1.20.0 的张飞在无诸葛连弩时可于同一阶段依次使用两张不同实体杀 | 其他阶段不改变合法动作；包 1.19.0 仍发布 `standard:zhang-fei` / `standard:paoxiao`；同一张实体牌不得重复支付 | AI 复用自己的已发布合法动作与持牌视图；第二张杀在途 Checkpoint/Replay 精确恢复 |
| `skill.yingzi.draw` | `standard:yingzi` | 摸牌阶段额外摸 1 张 | 非摸牌阶段不改变摸牌 | 发牌顺序可复现 |
| `skill.kongcheng.target_lock` | `standard:kongcheng` | 规则 v15 的经典身份局中，空手牌时不能成为普通/火/雷杀或决斗目标 | 有手牌后恢复可选；伪造决斗目标原子拒绝；v1–v14 和演示模式保留只禁杀 | AI/WPF 只消费 Core 合法动作；只公开技能与手牌数，不公开暗牌；固定 seed 的新旧版本目标集合可复现 |
| `skill.jianxiong.claim_damage_card` | `standard:jianxiong` | 规则 v16 的经典身份局中，受到正伤害后可选择取得仍在 `Processing` 的伤害牌；同一实体牌进入曹操手牌 | 零/负伤害、无来源牌、来源牌已离开处理区、旧/伪造 Choice 必须拒绝；v1–v15 和演示模式保留自动取得杀类伤害牌 | Prompt 只投影给曹操；跳过与取得可从同一 Checkpoint 分叉，类型化事件、移动账本和固定 seed/命令流可回放 |
| `skill.zhiheng.hand_or_equipment` | `standard:zhiheng` | 规则 v17 的经典身份局中，每个出牌阶段限一次，从自己的手牌或公开装备区选择任意张牌弃置并摸等量牌 | 空选、重复 ID、他人牌、非候选牌、第二次发动及旧 Prompt 原子拒绝；v1–v16 和演示模式保留手牌限定与可重复发动 | 暗手牌候选只投影给拥有者，装备候选公开；混合来源移动、类型化事件、Checkpoint 和固定命令流可回放；WPF 提供装备选择按钮 |
| `trick.dismantlement.target_card_discard` | `standard:dismantlement` | 选择一名有手牌、公开装备或公开判定区牌的其他存活角色；手牌由来源玩家通过私有不透明牌位选择，公开装备/判定区牌通过精确 `TargetCardId` 选择后弃置 | 目标无牌、死亡、自身、伪造目标、过期牌位 Prompt 或不属于目标公开装备/判定区的 `TargetCardId` 被拒绝 | 手牌分支的普通快照和事件不含牌面；公开装备/判定区分支可带已公开的 ID/牌型；可信账本可回放 |
| `trick.snatch.distance_one_target_card_take` | `standard:snatch` | 选择一名战斗距离为 1 且有手牌、公开装备或公开判定区牌的其他存活角色；手牌由来源玩家通过私有不透明牌位选择，公开装备/判定区牌通过精确 `TargetCardId` 选择后转入使用者手牌 | 距离大于 1 且无奇才、目标无牌、死亡、自身、伪造目标、过期牌位 Prompt 或非法装备/判定区 ID 被拒绝；坐骑 modifier 和奇才锦囊距离豁免必须由 Core 查询决定 | 手牌分支的普通快照和事件不含牌面；公开装备/判定区分支仅公开已知 ID/牌型，取得后牌面进入使用者私有快照；可信账本可回放 |
| `trick.duel.response_chain` | `standard:duel` | 多轮杀响应可暂停、恢复并结束 | 旧 prompt、错误 responder 被拒绝 | 固定事件流可回放 |
| `trick.nullification.chain` | `standard:nullification` | 锦囊效果前按固定座次进入有限多层无懈窗口；每张响应牌独立经过 `Processing` 并按链结果抵消或恢复原效果 | 链外打牌、重复回答、伪造响应牌或错误 responder 被拒绝；结束后不得残留 nullification frame | 当前 responder 才能看到私有牌 ID/Choice，其他 viewer 不得看到 prompt；`NullificationWindowFrame`、事件顺序和固定 seed 可回放 |
| `trick.iron_chain.toggle_and_propagate` | `standard:iron_chain` | 精确选择一名或两名其他存活角色并公开设置/清除连环标记；火杀或雷杀命中连环目标时按固定顺序传导同额、同属性伤害，普通杀不传导 | 自身、死亡角色、重复目标、重复座位、超过两名目标、链外牌或伪造状态不得通过；无懈结束后不得残留处理帧，伤害完成后连环标记必须清除 | 目标选择只来自合法公开目标，`IsChained`/状态事件对所有观察者一致；私有无懈 Prompt 只向当前 responder 投影，固定 seed/命令流可回放 |
| `equipment.replace` | `standard:crossbow` / `standard:bagua` / `standard:renwang_shield` / 五类槽位 | 装备牌按 `Hand → Processing → Equipment` 进入对应槽位，同槽旧牌先进入弃牌堆并发布 `EquipmentChangedEvent` | 错误槽位、重复槽位、死亡后装备残留或伪造装备动作被拒绝 | 装备区与新旧公开实体 ID 可由可信事件审计；普通快照只公开装备状态 |
| `equipment.bagua.judgment` | `standard:bagua` | 规则 v14 起，在普通/火/雷杀或万箭齐发需要闪时可选择八卦阵判定；判定牌从 `DrawPile → Judgment(target) → DiscardPile`，红色判定视为闪 | 无防具、非闪响应窗口、旧/伪造 choice 或非法牌区来源不得触发；牌堆耗尽时判定失败且不造牌；v1–v13 的群体响应不得出现八卦选项 | 判定结果在 `JudgmentResolvedEvent` 中公开；万箭结果另写入无物理牌 ID 的 `GroupResponseEvent`；Prompt 仅向当前 responder 投影；固定 seed/命令流下事件/移动顺序一致 |
| `equipment.qinggang.bypass_armor` | `standard:qinggang_sword` | 青釭剑按 `Hand → Processing → Equipment(owner, Weapon)` 装备；拥有者使用普通/火/雷杀时设置 `CardUseFrame.IgnoresArmor`，目标不生成八卦阵选项 | 无青釭剑、非直接杀、群体牌/决斗或伪造 modifier 不得绕过防具；同一实体牌不能复制 | 装备区公开；声明/使用事件和结算帧记录无视防具；AI 只读取自己的装备和目标公开状态；固定 seed/命令流可复现 |
| `equipment.renwang.black_slash` | `standard:renwang_shield` | 规则 v14 起，黑色普通/火/雷杀仍可指定装备者为目标，声明和目标确定后由仁王盾令其无效；青釭剑无视防具 | 无仁王盾、红色杀、非直接杀、群体牌/决斗或伪造 modifier 不得错误阻挡；同槽替换/死亡清理后 modifier 必须失效；v1–v13 仍在合法目标层过滤 | 装备状态、`ArmorEffectAppliedEvent` 和战场提示公开；AI 只读取自己的装备和目标公开状态，固定 seed/命令流可复现 |
| `skill.guicai.judgment-window` | `standard:guicai` | `JudgmentFrame` 在翻牌后、判定生效前冻结 `ReplacementCandidateSeats`/`ReplacementCandidateIndex`；每次替换按 `Hand → Processing → Judgment(target)`，最后按 `Judgment → DiscardPile` 收尾 | 判定帧外不得创建鬼才 Prompt；候选游标、父帧关系、当前牌区和有效 Choice 必须一致；非法命令原子拒绝 | 普通 viewer 不能看到私有 Prompt 或拥有者手牌；`JudgmentReplacementRequestedEvent`/`JudgmentReplacementResolvedEvent` 和 `skill.guicai.replace` 移动可供可信宿主回放 |
| `equipment.distance` | `standard:offensive_horse` / `standard:defensive_horse` | `GetCombatDistance` 在存活座位环距离上应用赤兔/绝影，影响顺手牵羊与其他距离型合法性；`GetAttackRange` 应用诸葛连弩范围 | 不能由 UI 直接改距离；同槽替换后旧 modifier 必须失效 | AI 只读公开装备/合法动作结果；相同 seed/命令流可复现 |
| `equipment.treasure.draw` | `standard:jade_seal` | 玉玺装备后摸牌阶段额外摸一张，装备离开后 modifier 消失 | 非装备状态不得增加摸牌数，死亡清理不得遗留 modifier | 摸牌数量和装备状态公开，牌堆顺序与他人手牌仍隐藏 |

## C0-K1 强制移动场景

以下场景是 K1 通知要求的最小覆盖集。每项正式实现时都必须同时验证实体牌守恒、非法来源/目标不产生部分提交、固定 seed 可重放，以及普通玩家视图不泄漏隐藏信息；详细 Given/When/Then 见 `CARD_MOVEMENT_CONTRACT.md`。

| 场景 ID | 对应内容/规则 | 移动重点 |
| --- | --- | --- |
| `k1.slash.hit` | `standard:slash` 命中 | `Hand → Processing → DiscardPile` |
| `k1.slash.dodge` | `standard:dodge` 闪避 | 响应牌独立经过 `Processing` |
| `k1.jianxiong.claim` | `standard:jianxiong` 奸雄选择取得伤害牌 | `Processing → Hand`，不复制实体牌；规则 v16 经典身份为可选取得 |
| `k1.peach` | `standard:peach` | 使用牌经过 `Processing` 后弃置 |
| `k1.deal.draw` | 发牌与摸牌 | `DrawPile → Hand`，顺序可复现 |
| `k1.hand-limit-discard` | 手牌上限弃置 | `Hand → DiscardPile`，不得部分提交 |
| `k1.death-cleanup` | 阵亡清区 | K1 清理手牌；K6 同一死亡提交清理装备区；K7 已覆盖八卦阵、乐不思蜀、兵粮寸断和闪电判定区移动，复杂改判等其他延时清理仍待后续 |
| `k1.lord-penalty` | 主公误杀忠臣惩罚 | 使用 `mode.identity.lord-killed-loyalist` |
| `k1.reshuffle` | 弃牌区重洗 | `DiscardPile → DrawPile`，牌数守恒 |

## C0-K2/K3 强制控制与内容场景

| 场景 ID | 对应能力 | 必须成立 |
| --- | --- | --- |
| `k2.revision.stale` | `GameCommand.ExpectedRevision` | 过期命令返回 `StaleRevision`，快照、日志、牌区和随机数不变 |
| `k2.prompt.exact-choice` | `PromptId` / `PromptChoice` | 卡牌和目标绑定为一个完整 Choice，不接受伪造组合 |
| `k2.prompt.private` | 玩家视图 | 非 responder 看不到 PromptId、Choice、他人手牌或 AI 候选 |
| `k2.prompt.answer-once` | `AnswerPromptCommand` | 错误 responder、旧 Prompt、未发布 Choice 和重复回答均拒绝 |
| `k3.registry.isolated` | `ContentRegistry` | 两个 Registry 不共享可变注册状态，公开集合为只读投影 |
| `k3.registry.references` | 包依赖和引用校验 | 重复 ID、未知引用、版本不足和依赖环在 Build 时失败 |
| `k2.command.active-skill` | `UseSkillCommand` / `LegalActionKind.UseSkill` | 出牌阶段从合法动作提交无牌、无目标的 `苦肉`，提交带当前拥有者私有手牌/公开装备集合的正式 `制衡`（旧规则仅手牌），或提交带私有手牌集合和其他存活目标的 `仁德`，或提交带私有手牌集合和受伤存活目标的 `青囊`，或提交带两张私有手牌和两至三名受伤存活目标的 `回春`，或为 `强袭` 提交 0–1 张手牌/装备区武器与一个攻击范围内目标；Core 校验技能、数量、所有权、牌型、重复项、目标白名单、PromptId 和 Revision 后才闭合主动技能帧 |
| `k3.registry.active-skills` | `standard-active-skills@1.0.0` | 扩展包依赖 `standard@1.11.0`，注册五项主动技能、两个被动技能、七个演示武将和 5/8 人模式；基础 Standard Registry 的内容指纹不变 |

## C0-K4 模式开局与选将

| 场景 ID | 对应能力 | 必须成立 |
| --- | --- | --- |
| `k4.mode.registry` | `ContentModeDefinition` | 模式选择角色分布、牌堆、候选数量和武将池，不复制另一套状态机 |
| `k4.general.private-offer` | `SelectGeneralCommand` | responder 只能看到自己的候选，其他 viewer 看不到 Prompt、候选 ID 或未公开武将 |
| `k4.general.shared-pool` | 共享池去重 | 每次选中后从池移除，任何已选武将不能再次选择 |
| `k4.general.invalid-choice` | Revision/Choice 校验 | 未发布武将返回 `InvalidGeneral`，状态、日志、牌区和随机数不变 |
| `k4.setup.pause-resume` | 可暂停开局 | `Start`/`AdvanceOneStep` 可停在真人选将，回答后继续 AI 选将、公开、洗牌和逐轮发牌 |
| `k4.setup.repeatability` | 固定 seed + 命令流 | 相同模式、seed 和选将命令产生相同公开快照、事件顺序和武将分配 |
| `k4.setup.five-player` | 5 人身份模式 | 1/1/2/1 分布、全 AI 选将和整局终止均成立 |

## C0-K5 结算帧与类型化事件切片

| 场景 ID | 对应能力 | 必须成立 |
| --- | --- | --- |
| `k5.resolution.stack` | `ResolutionStack` | 杀/属性杀的卡牌帧、响应窗口帧和伤害/死亡子帧保持父子关系，完成后栈清空 |
| `k5.resolution.serializable` | 数据型结算帧 | 可信宿主可将当前帧栈序列化为 JSON；帧中不包含委托、WPF 对象或玩家视图数据 |
| `k5.events.card-damage` | 类型化事件 | 提交事件包含出牌声明、目标确认、携带 `DamageNature` 的伤害请求/应用/AfterDamage 和结算完成信息 |
| `k5.events.active-skill` | 主动技能帧与类型化事件 | `ActiveSkillRequestedEvent`、`SkillHpLostEvent`、`SkillCardsDiscardedEvent`、`SkillCardsGivenEvent`、`SkillCardsDrawnEvent`、`ActiveSkillResolvedEvent` 共用 `ResolutionId`；苦肉的 `skill.kujin.draw`、制衡的 `skill.zhiheng.discard`/`skill.zhiheng.draw`、仁德的 `skill.rende.give-card` 和强袭的 `skill.qiangxi.discard` 移动可审计；青囊的 `skill.qingnang.discard` 移动及回春的 `skill.huichun.discard`、逐目标 `RecoveryAppliedEvent` 可审计；苦肉或强袭体力成本降至 0 时保留 `ActiveSkillFrame`，救援完成后才继续各自效果，精确手牌/目标 ID 不进入普通快照 |
| `skill.qiangxi.cardless-damage` | 强袭可选成本与无牌来源伤害 | 规则 v32 每阶段限一次；空牌成本失去 1 点体力，手牌/装备区武器成本经 `Processing` 弃置，目标必须是当前攻击范围内一名其他存活角色；`AttackResolution.SourceSkill=Qiangxi` 且来源牌/有效牌型为空，仍进入共享伤害后技能与濒死链；自损先濒死时获救后才继续目标伤害，v31 不发布动作 |
| `k5.events.damage-skill` | 伤害后技能触发 | 存活目标收到私有 `Feedback`、`Yiji` 或 `Jieming` Choice；非受伤者的援护者收到私有 `Yuanhu` 弃牌 Choice；刚烈受伤者收到私有 `Ganglie` Choice，红色判定后伤害来源收到私有 `GangliePunish` Choice；`DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 记录请求与发动/跳过，反馈发动时 `DamageCardClaimedEvent` 记录取得伤害牌，遗计发动时 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent` 记录私有摸牌与跨座位赠牌，节命发动时 `DamageSkillCardsDrawnEvent.TargetSeat` 记录补牌目标，援护发动时 `DamageSkillCardDiscardedEvent`/`RecoveryAppliedEvent` 记录弃牌和恢复，刚烈发动时 `JudgmentRequestedEvent`/`JudgmentResolvedEvent`/`GangliePunishmentResolvedEvent` 记录判定与来源反制，并与对应移动 reason 一致 |
| `k5.events.damage-trigger-order` | 伤害触发候选排序 | `DamageTriggerCandidate` 按优先级、相对当前行动者座次、技能序号和 `CandidateId` 稳定排序；`DamageTriggerWindowFrame.CandidateIndex` 冻结并推进当前候选，收集逐个检查存活拥有者的 `CanTriggerAfterDamage`，候选身份写入触发/技能帧和请求/结果事件；`DamageTriggerScope` 统一表达受伤者、其他存活角色和任意存活角色的座位关系，援护通过 `OtherLivingPlayer` 复用该契约，刚烈的跨座位部分发生在红色判定后的显式来源反制效果，遗计的效果可把牌交给其他座位，节命的效果可把牌补给公开合法目标 |
| `k5.events.alcohol` | 酒的一次性状态、实际伤害金额与濒死自救 | `AlcoholAppliedEvent` 公开酒效设置，直接杀声明时消费；未消费时发布 `AlcoholExpiredEvent`；加伤后的实际金额在 `DamageFrame`、伤害请求/应用/AfterDamage 和技能上下文中保持为 2；规则 v12 的濒死者使用自己的酒生成 `DyingResponseEvent.UsedAlcohol` 与以自己为目标的 1 点 `RecoveryAppliedEvent`，群体牌/决斗不消费 |
| `skill.yingzi.optional-draw` | 经典周瑜摸牌阶段的英姿可选性 | 规则 v21 发布私有发动/跳过 Choice；发动摸三张、跳过摸两张并发布 `DrawSkillResolvedEvent`，AI 发动；非法 Choice 原子拒绝，暂停 Checkpoint 可确定性恢复；v1–v20 与演示模式自动多摸一张 |
| `skill.tiandu.claim-judgment` | 经典郭嘉判定结果后的天妒选择 | 规则 v22 在自己的判定牌生效后、牌仍位于公开判定区时发布私有发动/跳过 Choice；发动后同一实体牌进入手牌并发布 `JudgmentCardClaimedEvent`，跳过则正常弃置；非法 Choice 原子拒绝，暂停 Checkpoint 与 WPF 可恢复，答复后原判定父结算继续；v1–v21 与经典包 1.0.0 保留自动弃置路径 |
| `k5.trick.group-response` | 群体逐目标结算 | `GroupCardUsedEvent` 声明完整目标列表，`GroupResponseEvent` 携带 `RequiredCardKind` 并按座次逐个提交；南蛮入侵要求杀，万箭齐发要求闪；桃园结义按同一 `TargetIndex` 逐目标恢复并使用 `RecoveryFrame`；每个目标的伤害/濒死/恢复结束后才推进父帧 |
| `k5.trick.public-draft` | 公共展示与私有逐人选牌 | `CardsRevealedEvent` 只包含显式公开牌；当前 picker 获得自己的 `SelectHarvestCard` prompt，`HarvestCardSelectedEvent` 推进 `TargetIndex`，选中牌移动到 picker 手牌 |
| `k5.events.dying-winner` | 濒死/胜负事件 | 现有 Demo 的基础濒死响应、苦肉主动技能濒死续接、死亡、身份公开和胜负判定产生稳定事件；多伤害嵌套和更复杂技能濒死询问仍属于后续切片 |

## C0-K7 判定与延时锦囊

| 场景 ID | 对应能力 | 必须成立 |
| --- | --- | --- |
| `k7.delayed-card.lightning` | `standard:lightning` 延时判定 | 闪电只可自用；判定牌为黑桃 2 至 9 时发布 3 点 `DamageNature.Thunder` 伤害并在伤害/濒死链完成后续接原回合，否则按固定座次转移到下一名存活角色；公开事件、判定区移动、牌数守恒、普通快照脱敏且同 seed/命令流可重放 |

## 当前执行顺序

1. 先用现有 Core 契约维护基础 `basic.*`、三十二个被动 `skill.*`（含 `skill.mashu.outgoing_distance`、`skill.qicai.trick_distance`、`skill.jijiu.dying_rescue`、`skill.tiandu.claim-judgment`、`skill.qixi.as-dismantlement`、`skill.keji.skip-discard`、`skill.tuxi.gain-card`、`skill.luoyi.damage-boost`、`skill.duanliang.supply-shortage-conversion`、`skill.luoshen-qingguo.judgment-response`、`skill.jizhi.ordinary-trick-resume`、`skill.tieqi.slash-response-prohibition`、`skill.liegong.conditional-slash-response-prohibition`、`skill.kuanggu.distance-one-damage-recovery` 和 `skill.wushuang.sequential-responses`）、八个主动技能（含 `skill.qiangxi.cardless-damage`）及伤害后技能事件的回归。
2. K1 已开放：`k1.*` 移动契约已完成并审阅；运行时覆盖保留在 Core Console 自测。
3. K2 已开放：`k2.*` 命令/Prompt 场景由同一 Console 自测覆盖。
4. K3 已开放：`k3.*` Registry 场景已覆盖，`standard:*` ID 在 Standard 包中冻结。
5. K4 已开放：`k4.*` 私有选将和模式开局场景由同一 Console 自测覆盖；更大规模 seed 矩阵仍是扩展验证。
6. K5 已开放结算帧/事件切片：`k5.resolution.*`、`k5.events.*`、`basic.elemental-slash.nature`、`basic.alcohol.slash_boost`、`basic.alcohol.dying_rescue`、`skill.kujin.active_command`、`skill.zhiheng.active_command`、`skill.rende.active_command`、`skill.qingnang.active_command`、`skill.huichun.active_command`、`skill.qiangxi.cardless-damage`、`skill.feedback.claim_damage`、`skill.yiji.gift_card`、`skill.jieming.draw_to_max_hand`、`skill.yuanhu.cross_seat_recovery`、`skill.ganglie.judgment_and_punishment`、`skill.longdan.slash_dodge_conversion`、`skill.mashu.outgoing_distance`、`skill.qicai.trick_distance`、`k5.events.damage-trigger-order`、`trick.barbarian_assault.group_response`、`trick.arrow_barrage.group_response`、`trick.peach_garden.group_recovery`、`trick.five_grains.public_draft`、`trick.dismantlement.target_card_discard`、`trick.snatch.distance_one_target_card_take`、`trick.dismantlement.target_card_opaque_slot` 和 `trick.snatch.target_card_opaque_slot` 由 Console 自测覆盖；K7 的 `skill.guicai.replace_judgment`、`skill.guicai.judgment-window`、`trick.indulgence.delayed_judgment`、`trick.supply_shortage.delayed_judgment`、`trick.lightning.delayed_judgment`、`k7.delayed-card.accumulation` 与 `k7.delayed-card.lightning` 也已由同一自测覆盖；普通/火/雷杀响应和类型化伤害、苦肉无牌主动技能、苦肉 1 点体力濒死后共享救援续接、强袭体力/武器成本与无牌来源伤害及自损濒死续接、制衡私有多选弃牌与等量摸牌、仁德私有多选交牌/其他存活目标/Processing 跨手牌移动/按数量回复、青囊私有一张手牌弃置/受伤目标选择/恢复、回春私有两张手牌和两至三名受伤目标选择/逐目标恢复、反馈存活伤害取牌、遗计跨座位分配、节命目标补牌至上限、援护跨座位弃牌恢复、刚烈公开判定/来源私有反制与濒死续接、鬼才判定替换、龙胆闪/杀互转、伤害触发候选稳定排序与游标暂停/恢复、酒的一次性直接杀 +1 伤害与回合结束失效、v12 酒仅自救与 v3–v11 跨座位兼容回放、决斗多轮杀响应、无中生有无目标摸牌、南蛮入侵逐目标杀响应、万箭齐发逐目标闪响应、桃园结义逐目标恢复、五谷丰登逐人选牌、过河拆桥手牌不透明牌位选择弃置与公开装备/判定区精确选择、顺手牵羊距离一手牌不透明牌位选择取得与公开装备/判定区精确选择、奇才距离型锦囊合法动作、闪电命中/转移/雷电伤害和基础单次伤害濒死求桃已运行；`DamageTriggerScope` 通用座位关系已开放，其他多目标复杂选择、属性抗性、多伤害嵌套和复杂牌型仍未开放。青釭剑无视防具场景属于 K6/K7 装备回归，见 `equipment.qinggang.bypass_armor` 和 `equipment.renwang.black_slash`。
7. `DamageTriggerScope` 已开放通用座位关系；后续仍需为更多技能补充具体合法条件、类型化效果和复杂跨座位结算。
8. 后续 K5 完整结算、K6 其他装备效果和 K7 剩余判定/响应链按清单顺序实现，不把多个阶段合并成一次大重写。
8. K8 可信命令 Checkpoint 场景已覆盖私有 Prompt 暂停、规则行为版本兼容、内容包签名与内容指纹校验、同版本定义漂移拒绝、JSON 往返和恢复后继续提交；完整内部状态存档仍待后续扩展。
