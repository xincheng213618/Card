当前普通 OL 官网 550 的正文独立包含摸牌结束付款和结束阶段领取，完整保存在指定 source ledger。没有查得针对下列交叉项的官方 FAQ；以下均为工程默认，不声称官方裁定。经典双雄的判定、替代摸牌与原回合转换保持原合同。

- 付款范围为自己当前真实手牌、装备区，排除判定区/粮/其他特殊牌堆。正常区域选择及装备离区副作用沿成熟路径；原生生成武器弃置实到 OutsideGame，冻结该实体与真实去向，绝不伪称进入弃牌堆。支付者该张牌的 EffectiveSuit 在离区前冻结，None 无红黑转换收益。
- HE 异色 Duel 限当前实际回合、原技能实例与 gameplay hash；来源失效、抑制或死亡不可使用。与当前引擎生成武器离区销毁规则一致，新来源不发布 IsGeneralWeapon 装备的 Duel 转换。普通 HE 保留，同名 Duel 可以明确选择该来源，身份转换规则优先。普通 Slash/Dodge 回应不能借此当 Duel。
- 只有实际转换链含当前实际回合该 PaidColorOrigin 原来源签发的 Duel 转换，才额外 opt-in 成熟的 EffectiveSuit/颜色捕获；不依赖完整 Standard 目录恰好同时登记别的外观跟踪能力，旧 classic 和普通无此来源 Action 不增加字段。
- 真实完成的每次摸牌阶段只有该次原窗口候选机会；付过的原 program 不会因子链回返或失技重获再付。若额外实际摸牌阶段再次真实付款，同来源实例采用最近一次颜色，不并存两色；不同实例各有准确原来源，旧付款不替换为新实例。跳过摸牌不会制造 completed-draw 机会。
- 原弃牌成本不可回滚。装备离区的白银狮子、九援队列、木牛粮等真实孩子全部返回后，原来源仍有效才签发颜色许可。公开付款事实只暴露已实际移走的牌，不提前泄露尚未支付的手牌。
- Ending 领取是独立的强制节点，不依赖本回合是否发动过颜色付款，也不要求受伤时已持有该颜色实例。只有当原实际回合 own Ending 的技能来源仍有资格时开始领取。技能在 Ending 前失效不会预约未来领取；已实际领取的实体不会因随后失源、死亡或胜负已定而回滚。
- 领取只选本实际 turnNumber/turnOwner 对自己已实际造成正伤害的 causal card 原实体中，目前仍在 DiscardPile 的部分。原材料已到 Hand/装备/粮/OutsideGame/DrawPile 或未完成清理的 Processing 时不跨区抢回。该区域限制是正文未明示的工程默认；部分材料仍弃牌则仅领那部分。
- 同一实体多次伤害、同一 Duel 多轮回应、多目标及连环传导不叠加实体；按原伤害 frame 与 material ordinal 的稳定顺序首次领取。Duel 反伤取原 Duel 的实体成本，绝不把打出的 Slash 或外层借刀实体当来源。纯 HP loss、无牌技能伤害不造牌。
- 普通真实 CardUse 保留 Actor/Provider 原材料；action-null 的既有真实虚拟 Slash 与 program-owned 零实体 Duel 可记录实际无实体因果，但不能领取牌。没有制造 AcceptedAction/ActionId。
- 既有真实使用者替换不改初始 CardUseDeclared。当前 Actor 与初始声明来源经同一原 use 的连续实际 ProgramCardUseActorReplacedEvent 及原 BindingStarted 事实相接，Provider 和原实体始终保留；不把初始来源重新当当前伤害来源，也不补写旧声明。
- Lightning 按 exact JudgmentFrame.DelayedCardId + 原 attack.PhysicalCardIds + 真 hit fact 确认原延时牌；独立排除该次判定牌。复制成熟 DamageApplied 的实际 SourceLess 值，不在本角色修订旧 Lightning producer 的来源语义。伤害转移/链伤读取实际当前受伤角色，原材料身份保持原 causal frame。
- 领取在原 Ending program 内原子移动候选实体，并先冻结准确材料/ledger 后才进入 gain 孩子。所有仍弃牌的原材料一次取得；子链再次暂停不重付、不重取、不更换原窗口。被其他技能提前取得的实体不再属于本次领取候选。
- 主公额外体力、插入阶段与额外回合沿成熟实际回合边界：插入阶段不改 turnNumber；下一真实回合旧许可及旧伤害资格失效。
- 新 AI 只为含 6100 的真实单牌成本程序增加公开转换收益估值 14，配合成熟选择者保留价值；这是有限工程先验，不是实测胜率。结束领取为强制，无手动替 bot 选择。

此交付没有编译、C# 载包、运行 fixture、冷恢复执行或性能测量。源码静态链路与四项真实命令草稿分别列在 manifest，特殊装备/抑制/借刀/链伤/部分材料仍需父在用户允许验证后执行。
