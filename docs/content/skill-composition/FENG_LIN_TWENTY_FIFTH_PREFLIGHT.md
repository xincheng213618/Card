# 下一批三候选当前来源预检

只读预检。当前 main 的登记表、已登记模块及第24批排程优先于旧 preflight 结论。没有开始实现、登记、修改主区、下载媒体、构建或测试。所有候选 gid 来自已冻结官网普通 OL `Listdata[界限突破]` 87 行索引，不靠记忆选择。

官网 reader 对三页各返回 timeout；其后只对已证实的三个 hero URL 和 heroDetail.js 使用的同站 info route 各读一次。六份响应均 HTTP 200，完整原字节、时间、URL、字节数、SHA 和去标签正文已落盘。没有抓取网页广告指向的移动/十周年/X页面或资源包。

当前静态模块检查有84个模块。三候选姓名和拟用的 boundary ID 在已登记内容中均未发现。600简雍、641廖化、604高顺已经在 main 登记，全部排除；当前 `BoundarySunCeContent` 已注册，452历史 deferred 结论不得继续当作其当前未登记证据。451刘禅未核思蜀的来源边界继续排除。本预检不初始化可执行 registry，也不据此声称24批验收通过。

## 建议三路

| 建议顺序 | 当前官方身份 | 完整技能 | 相对工作量 |
| --- | --- | --- | --- |
| 1 | [588界凌统](https://www.sanguosha.com/hero/588)，吴4，initial_hp=0 | 旋风 | 最小；付款与私密弃牌已有能力，主要补同批次失牌的精确OR资格 |
| 2 | [597界陈宫](https://www.sanguosha.com/hero/597)，群3，initial_hp=0 | 智迟、明策 | 中等；经典智迟可复用，明策需要真实虚拟杀的完成收益回执 |
| 3 | [626界刘表](https://www.sanguosha.com/hero/626)，群3，initial_hp=0 | 自守、宗室 | 较多；变额摸牌义务/实际伤害牌使用账与按势力一次的防伤替代 |

三页均无fontRefs，无正文授予未展开技能；技能名与正文一一对应。官网 API 核身份与体力，不含技能正文或独立性别字段。已有同名 classic 身份可提供 gender 补充时应单列来源，不声称当前 API 明示。

## 588 界凌统

当前旋风原文：「当你失去装备区里的牌后，或一次性失去至少两张牌后，你可以依次弃置至多两名其他角色共计至多两张牌。」

已有 `classic:xuanfeng` 在 `classic-ling-tong.rules.json:11–19/24–38` 分别处理 Equipment perBatch 与弃牌阶段至少弃2手牌；后者不能替代当前不限阶段、不限弃置原因的一次性失牌。`GameEngine.CardMovementPrograms.cs:183–196/330–337` 和 loader `SkillPrograms.cs:2879–2895` 已有多来源区的 `PerOwnerBatch` 与实际 movement ledger，适合真实同一批次合并。

`ChooseOtherOwnedCardDiscard` 已支持单牌的真实暗置手牌牌位、公开装备、可拒绝、装备保护及移动孩子返回（`ProgramCardActionOperationDefinitions.cs:157–185`；`GameEngine.ProgramOtherOwnedCardDiscards.cs:7–45/52–98`）。连续两次该 op 已满足共至多2张、至多2名，包括对同一人弃两张；不必新造人物弃牌 runner 或假实体快照。

最小缺口是单一 actual owner-batch 的「含装备离区 OR 总失牌数≥2」资格和去重。仅分成两个独立 trigger 会让含装备且失≥2的同一批次重复发动，不能用逐牌移动或两个相邻命令凑“一次性”。可新增公开标量 equipment-left count / exact combined movement condition，继续采用现有 owner-batch ledger；动作时点、来源实例与有资格的整批冻结在 owning window。若采取其它结构，需要证明同一实际批次只一项机会。

未核官方FAQ：区域字样未进一步限定；弃牌区域采用成熟HE口径或扩含判定、特殊公开牌堆失牌是否计“一次性”、以及拒绝第一张后是否仍能选择第二张，应正式分列工程默认。现有两个独立可拒绝 op 支持0/1/2张；不得把手牌暗置牌位透露为隐藏牌ID给AI。代表性未来检查应覆盖单装备/双手牌/混合HE同批仅一次、给牌/用牌/弃牌原因、两命令各一牌不凑数、0/1/2张、同一或两对象、装备保护、来源失效、真实孩子与四视角冷恢复。

## 597 界陈宫

当前智迟原文：「锁定技，当你于回合外受到伤害后，本回合【杀】和普通锦囊牌对你无效。」当前明策原文：「出牌阶段限一次，你可以交给一名其他角色一张【杀】或装备牌，然后其选择一项：1.视为对你选择的另一名角色使用一张【杀】，若造成伤害，执行另一项；2.你与其各摸一张牌。」

经典智迟的 perDamage / notOwnTurn / grantTurnCardEffectImmunity 已有同义合同（`classic-chen-gong.rules.json:86–131`；`ProgramVirtualSlashOfferOperations.cs:29–52`），可引用 `classic:zhichi` 而不改旧定义。现经典明策 selectOwnedCards→moveBoundCards→OfferVirtualSlashOrDraw（同文件11–81）可复用真实礼物及候选选择能力，但旧 helper 的 draw 分支只让受赠者摸1、Slash目标限该 actor 攻击范围、造成伤害后没有新的双方收益，不能直接登记新当前明策。

`GameEngine.ProgramVirtualSlashOffers.cs:83–110` 已有真正无实体 Action.Type=Use / CardUse / CardAttack / 原 Program parent return；`CardUseCausedDamage` 是真实 action 完成事实，而非查最后一条 DamageApplied 或对指定目标HP作差。新增 opt-in 受赠者选择与 owner 指定另一对象的 owning draft，原实际 use 成功造成伤害后仅发一次双方Draw1，并在use完成、反应/伤害/死亡/摸牌gain孩子回返后精确返回。选择draw则双方真实各摸1；无合法Slash对象时该分支仍可执行。原赠牌实体只交一次，不能把空实体杀当作重付赠牌，也不能改 classic Offer 的单人摸牌语义。

未核FAQ：正文没有旧“其攻击范围内”措辞；是否仍受普通Slash距离、赠牌时如何冻结有效杀身份/可赠装备区、链伤/改源是否算该实际杀“造成伤害”、任一参与者死亡时余下收益与当前source失效策略，应分列工程默认并以精确use receipt贯通，不靠粗略Damage事件匹配。代表性未来检查应覆盖双方draw、真零实体Slash+Dodge/护甲/实际damage与prevented区别、gain/救援/Completed孩子、source loss、unknownchoice原子拒绝、四视角冷恢复及native正常选择。

## 626 界刘表

当前自守原文：「摸牌阶段，你可以多摸X张牌，你以此法摸牌的结束阶段，若你本回合使用过伤害牌，你弃置X张牌（X为全场势力数）。」当前宗室原文：「锁定技，全场每有一个势力，你的手牌上限便+1。其他角色对你造成伤害时，防止此伤害改为令其获得你区域内的一张牌，每个势力限一次。」

既有 `classic:zishou` 在 drawPhaseStarting 按 livingFactionCount 摸牌后签发 selfOnly（`draw-policy-skills.rules.json:68–106`），当前定义没有这个目标禁令，不能改旧或直接复用完整classic技能。livingFactionCount变额Draw、真实Ending与选择自有区域付款可以复用。新接受额外摸牌的 owning receipt 要同时记请求X、实际取得数量、实际turn/source-instance及结束付款阶段；是否仅实际额外摸到≥1张产生义务、X冻结在摸牌时还是结束时重算，应给明确工程默认，不能从任意Draw/Gain事件推断发动。

宗室手牌上限条款可复用经典 livingFactionCount modifier（`rule-query-skills.rules.json:139–153`），新增防伤替代需要 BeforeDamage 的 current victim/source/candidate 与按势力一次的公开游戏账。防止当前完整damage attempt后，让真实来源获得受伤者区域的一张原实体，按 source 实际势力签发一次消费，获得牌/HP/救援/死亡孩子不能重防止、重取或重复消费。`GameEngine.DamagePreventionPrograms.cs:192–213` 已有精确父window与公开 prevented fact；成熟 SelectAndMoveOwnedCard 可复用区域与实体移动，但必须保留“来源自己选择 victim 区域牌”的语义与隐藏手牌牌位，不让owner或AI看未公开手牌身份。

实际伤害牌Use账不能借classic精策数量或本回合DamageApplied统计。454/658的真实CardUseDeclared、legacy Action=null typed producer、普通Response排除原则可复用；无懈虽是真使用但不是伤害牌。现 `GameEngine.MouLuMeng.cs:44–48` 的 IsDamageCardKind 只列三杀/决斗/南蛮/万箭/火攻，未含闪电，不能不说明就把旧谋英博列表当完整伤害牌定义。对官网域一次有界搜索未找到正式current分类/该技能FAQ；闪电是否计入须官方规则或独立新opt-in明确默认，不扩旧谓词。

未核FAQ：每势力限一次的游戏scope/来源当前势力变更、重获技能是否保留次数、空区域时防伤是否仍消费、无来源或自己伤害、牌区域是否HEJ、一次多点/链伤/非牌damage的精确attempt边界，均要明记默认。建议第三路最后集成，新增共享能力工作量高于另外两项，但完整正文已有，无来源阻塞。代表性未来检查覆盖正常/额外实际turn、零entity和转换damage-card use但普通SlashResponse不算、X/空牌堆/源失效、同势力第二次伤害与另一势力首伤、真实原牌领取/私有信息/救援死亡尾部、cold/native。

## 协作与限制

三候选可按588→597→626独立stage，普通新content复用既有能力不增加global版本；任何shared更改若改变旧同fingerprint重放必须按AGENTS独立评估，而不是按人物提版本。当前只能交source与静态缺口，所有实现和检查设计都未实施、未验收。root应待第24批静态收口后分配新的能力段，仍保持三路stage→root串行整合；用户醒后再统一执行其授权的测试。
