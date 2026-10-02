# 第十三批：界黄忠、界魏延

共同基线为已验收主区 a0f0003ffbbdff2e1e2ef943723e816bc5a408b6。baseline-manifest.json 逐路径记录主区真实工作字节及三个独立 worker；共同完整来源为 source-preflight/manifest.json，SHA 63858a1298bec2a25c7988b498b75153000f433515929b412b3005518852c404，30条旧抓取原证据已重验。当前26/456官网完整正文是本批的内容依据。两份只读设计基于上一提交d8，必要代码与新基线的差异已在 design-rebase-01 原样冻结；实现者须读差异再实施，不能把旧设计当已验收实现。

父统一负责模块注册、原图/GeneralArt/catalog/gallery/sync、来源与能力文档、默认范围、融合检查、主区验收和 scoped 本地提交。两实现 worker 只改独立人物模块/规则资源、精确新泛用能力与有意义的行为检查，不改统一注册/原图/版本/台账。三路均不 push，不 reset/stash/clean，不覆盖旧证据。共同共享文件由父按逐基线三方融合，不相互拷贝在制文件。

## 正式内容与 ABI

- 界黄忠：boundary:huang-zhong，character:huang-zhong，variant boundary，ruleset sanguosha-ol，Shu、Male、4/4HP，portrait boundary_huang_zhong，官方2600。模块 BoundaryHuangZhongContent；bundle boundary-huang-zhong；技能 boundary:liegong-current。旧 classic:liegong 不改。
- 界魏延：boundary:wei-yan，character:wei-yan，variant boundary，ruleset sanguosha-ol，Shu、Male、4/4HP，portrait boundary_wei_yan，官方45600。模块 BoundaryWeiYanContent；bundle boundary-wei-yan；新技能 boundary:qimou-current。狂骨只 AddSkill 现 batch9-damage-skills 的 boundary:kuanggu 一次，不复制、不替成旧 classic:kuanggu。
- 官网 API 没有结构化 gender，男性沿已有同角色定义；不能称官网明示。官网initial_hp零值不作为游戏实际初始体力。
- 新 EffectOp 号段：黄忠2100/2101（目标取消防止、目标增伤），魏延2200起（数量付款、实际损失摸牌、两个实际损失修正读者最多2203）。新 CardPolicyKind 如需要用2100显式值。旧 enum 数值与 null 路径保持。名字可选清楚的泛用名，交付列明 ABI。不新增人物专用 engine。

## 烈弓：具体杀点数、冻结目标事实

当前普通 OL 正文：你【杀】的攻击范围为此【杀】点数。当你使用【杀】指定目标后，你可以执行以下效果：1.若其手牌数不大于你，其不能抵消此【杀】；2.若其体力值不小于你，此【杀】伤害值+1。

前半仅新 policy opt-in：具体实际 Use 的杀系，有正点数时以该杀有效点数替代普通杀范围项，不能改玩家通用 GetAttackRange；已有明确 ignore-distance 能力保留。canonical pure legality 对 LegalActions、提交重验、AI与额外目标/真实改角色相关入口一致。未启用 policy 的旧虚拟/实体/转换/武器行为不变。单实体取冻结有效appearance点数，不能随目标选择后成本变化重新算。

补充2016官网托管攻略明确标为自编FAQ，不是2026正式FAQ：丈八杀点数两张成本牌点数之和，上限13；仅新 policy 且真实丈八转换可采用这一有限 appearance 约定，保留旧无policy EffectiveRank 序列。其他明确无点数的纯虚拟/多实体 appearance 保留普通范围作为工程默认，不求和、最大值或伪造13；只有真实丈八来源可用上述特例。当前正文未给无点数规则，此默认在文档明确，不冒称用户确认/当前官方裁决。如果进一步主来源给出相反现行规则，先原样冻结再根决定增补合同。

后半复用真实 CardUseTargetsFinalized（流离等以后）一目标一可选窗口，actual Use+Slash family+actor 严格gate。候选同时冻结双方HP和手牌数量，actor手牌已付杀。条件为目标手牌<=actor手牌或目标HP>=actorHP；一个optional发动，满足哪项就执行哪项，两项满足一起执行，不二选一，不等响应/伤害时重算。精确新节点才放行需要的eventTarget HP/hand字段，不全局改旧条件窗口资格。

新 typed nullable target receipt 属于 CardUseFrame，记录真实actionId、actor、final target seat、producer frame/effect身份与cancel/damage事实，所有nested集合冻结。多目标事实彼此独立，不把目标增伤写 whole-use base导致全体额外+1；PreparedTargetAttacks 下一目标装回仍读取对应receipt。写事实幂等，不能从observer/history猜归属，不能全局use-ID字典。

不能抵消由成熟 IsSlashDodgeCancellationPrevented 读取新目标事实；允许真实普通/转换闪、八卦、护驾等回应支付，再失去抵消。不能设置 ProhibitsDodge/CannotRespond。现免疫/无效、防具和明确忽略防具界限保留。增伤合入成熟 direct-target FinalizeAttackDamageAmount 一次，酒/银狮/藤甲/减伤/转移/链伤沿现处理；链伤不再应用一份烈弓。目标事实已发后source失效不抹掉，target死亡/移除不转给别人；发动前资格沿成熟enabled/alive和真实target约束。

多目标独立增伤、当前finalized窗口对应“指定目标后”、无点数fallback、非Locked suppression沿现Tags为本批明确工程解释，不冒称正文给了完整FAQ。新 optional AI 只依descriptor和公开frozen数量/HP/关系估值，不读私有实体、不伪造Damage/Draw、不调用RNG，不能改变旧估值。

## 奇谋：限定数量付款、实际损失 X 与真实子窗

当前456正文：限定技，出牌阶段，你可以失去任意点体力并摸X张牌（X为你以此法失去的体力值），然后你本回合计算与其他角色的距离-X且使用【杀】的限制次数+X。

使用成熟真正owner Play active入口、Game usage一次，accepted activation先消费一次，不能取消数量后返还/另一次免费尝试。数量choice只提供1..当前体力，非法0/负/超过/任意参数不能提交；范围采用2016官网托管自编FAQ可参考解释，当前2026正文无新的更具体范围，文档标明该区别。owner当前HP<=0不提供入口。quantity提示是产品自然选择，不暴露节点内部。

四段有限组合：选择并付实际HP -> 按实际loss receipt摸牌 -> OutgoingDistance−X -> SlashLimit+X。各修正独立producer effectIndex，不能同key发两不同query。复用成熟 actual turn ledger/end到期，不截断为旧静态20。新的动态reader只承认 exact same producer receipt、owner、binding、actualturn和限定usage；旧固定amount/null、普通 LoseHp/Draw/Grant 改动为零。

ProgramSkillFrame nullable typed HP-loss receipt在同一次真实 HP apply 后、任何 HpChange/rulefact/children之前冻结 requested/before/after/actualLost，X=真实差。后续救援桃/HP技能、摸牌gain子窗不重算X，不读OwnerLostHp/currentHP差/历史事件。真实 payment子窗沿 typed parent return、游标advance-before-handler，恢复不能再次Loss/Draw/grant。付款/摸牌/两修正各自记录exact issuance/completion，任何新集合投射创建即冻结。

付款前来源有效资格照旧。付款后owner活着、同actualturn、精确新receipt且正好在限定三个tail instruction内，可继续已付收益，即使source移除/禁用；仅这新cap的精确gate，不泛放宽老executor/source/death/observer。救援失败owner死亡后终止剩余收益，保留实际paid事实与消耗，不向死者摸牌或授新修正。已发turn修正来源后失效不撤销，真实actual End到期。

同actualturn额外Play仍保留+X与限定消耗，Slash普通phase计数沿成熟重置；真正Extra turn不继承过期modifier，整局usage仍用完。不要host换phase冒称实际调度命令验收。新reader资源合同须强验证数量producer、paid stage、一次Draw、两各一次的有限顺序，禁止重复/错绑定/跨owner/重选/第二producer污染。最终普通optional原生AI须真实选择并有限完成；新估值可以依公开HP/loss/距离，不伪造摸牌或偷读Hand。

## 行为检查、交付与边界

每路约5个grouped Core新行为组，使用已有filter/Test-Changed.ps1和小固定fixture；不seed搜索/完整对局、不重复配置定义快照、不恢复剪裁。WPF只对真正新数量UI或提示行为必要才加共享行为组，成熟路径自然满足可复用旧组。狂骨只一个代表复用触发，优先覆盖新缺口。

黄忠组：specific rank及真实单实体/丈八/无点数legality；actor付款后四格finalized条件和skip；真实闪支付不抵消+一次增伤与旧armor边界；多目标prepared、source/target生命周期和链伤；普通optional nativeAI、输入reject、冷热恢复四views/旁观、parser新nodegate+旧null。

魏延组：狂骨距离/perpoint代表；真实限定quantity付款/X摸/两grant/非法重提；实体桃救活+HP/gain嵌套保持X与once；付款前后source生命周期及救援失败；真实额外Play/Extra turn与各暂停点恢复四views/旁观+旧null。host audit和真正accepted command/cold witness严格分别，静态不当运行证据。

真实产品异常先冻结accepted prefix、attempted command、faulted typed stack、源码/DLL/log，再据证据收窄修补；保留所有失败原日志、分编号不覆盖。worker focused，无Full。父融合后一次适当Full及无过滤routine实测，默认保留原85Core/16WPF过滤顺序，只加必要代表项。

新rulefact AdvanceEventRulesAndQueueFact，新state AdvanceRulesAndPublishState；QueueGameEvent/PublishState输出专用。command commit/recovery、prepared深冻结、CommittedEventProjection、CreateSnapshot(viewerSeat)、checkpoint/replay/fingerprint/input/privacy/movement边界保留。不因人物升epoch/schema/package，不在其他文件复制版本数值。source-manifest交付逐owned baseline/finalSHA、共同baseline/合同/正式来源SHA和各原证据SHA，冻结后停止改包。
