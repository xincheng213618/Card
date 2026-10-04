# 726 矫诏／殚心独立合同与 producer 静态审查

审查时点：2026-10-04；作者稿正在实现，所列 SHA 是本轮读取输入，不代表作者最终冻结。只写本报告目录，没有修改主区或作者稿。没有编译、loader、游戏、检查、native AI、benchmark 或 HTTP。

## 已报告并静态修正的确定问题

**P2：全场 Round 牌名目前遗漏其他角色真正使用的闪。**

`src/CardGame.Core/ProgramTieredRoundConversion.cs:106–118` 的 `TryRecordTieredRoundDodgeUse` 已精确限制同一 incoming Slash、本人 target、actor/provider/responder、parent action 和 opponent，但112–113行又要求恰好一个本次新增 `TieredRoundConversion` 转换源。于是其他角色原生实体闪、其他已注册转换闪、八卦真正零实体闪的同类 Use 均不记为本轮用过的 Dodge，初始矫诏会仍可选择该已使用牌名。

这不是把所有 Response 当 Use：主区 `GameEngine.AlternativeViewAsCosts.cs:28–31` 的成熟 `IsProgramResponseCardUse` 正是按真实 Slash 防御区分；`GameEngine.CardActions.cs:130–135` 对原生实体闪也先记录真实 Use 再发布 Accepted；`GameEngine.ResponseUseCompletion.cs:45–51` 明确语法 Response 中有真正 Use，且没有本新 policy 要求。普通群体闪／供牌响应／Slash 打出仍应排除。

最小建议：全场 Round tracker 沿成熟真正 Dodge Use producer 判别，保留现 exact incoming/parent/opponent/actor/provider 门；新零材料转换的发行 receipt 只验证其自身来源，不能成为所有角色 Use 的唯一入口。物理或八卦等零实体来源应分别使用其真实 producer 证明，不宽放任意空成本 Response。已直接同步作者和 root；本轮读取 SHA `e25d1aa0881d9f4c2c3f25de4682d8198a7222404b785d50a2daf39d7d73a253` 中该门仍存在。

随后作者修改并通知，已再次只读确认：新 `TryRecordTrueRoundDodgeUse` 保留原 same-Slash／actor／provider／parent／opponent 严格定位并使用成熟 `IsProgramResponseCardUse`；只有本新零材料转换另验自己的发行凭据，群体／供牌排除。复读 SHA `9480ac023ada0faa094ec9a3f6cbeeff68e10d43185fbdf9ec99774ca9bf2005` 已收口此遗漏。以上三个确定问题均已由作者局部修正；本报告范围没有尚未处理的确定 P1/P2，但不代表未冻 OLD 或运行行为已验收。

## 已由作者收口的两处问题

- 殚心真实摸牌的 Gain observer 可以实际 Damage，再进入 `DyingContinuationKind.Damage`／`AttackHpLoss`。原自动 Dying 资格只认 `ResumesProgramSkill` 会阻挡已被完整 paid-root/连续边证明的路径。现 `GameEngine.CappedConversionBenefitObservers.cs:39` 已采用任意 ActiveDying 加本 root 完整证明，保留局部边界，没有放宽旧通用门。
- 已接受矫诏的实际 actor 可被成熟 `ProgramCardUseActorReplaced` 更换，原 provider 与额度归属仍冻结。原凭据要求当前 actor 等于原 actor 会拒绝该合法动作。现 `GameEngine.TieredRoundZeroUses.cs:151–162` 固定原 receipt provider 与唯一原 Declared，再使用成熟 `MatchesDeclaredActualDamageUse` 的 Previous→Actor 链；没有改原 receipt 或重新查询当前来源。

两项均仅静态复读确认，不能作为运行通过证据。

## 已查范围内未发现另外的确定缺陷

- 初始级使用实际 Play／Phase 额度；修改一级和二级共同使用 owner+skill+state／usage 的 Round key，没有 instance 新份额。成熟 `SkillRuntimeState.cs:55–96` 的 Turn/Phase reset 不重置 Round，`GameEngine.MouLuMeng.cs:27–41` 的 Extra turn 不推进 Round。最终 parser 和规则仍须保证各级使用相同 policy StateId／UsageId，并登记 UsesRoundTracking；这些 OLD 接线正在实现，未当作冻稿遗漏。
- 殚心发行冻结 X 与原 AfterDamage candidate／source／hash／occurrence，真实 DrawX 先完成全部孩子，再复验未发行修改。`GameEngine.CappedConversionBenefits.cs:33–41,59–68` 中 X0 不调用 Draw；满级 X2 仍调用真实 Draw2，`current < 2` 阻止第三次修改；失源只取消未发修改，已摸牌账不撤回。
- 全场其他普通 Use 在真实 CardUseDeclared 入口按 effectiveKind 去重。真实 Nullification 有 exact 当前 counterspell→原 CardUse、parent Action、opponent、actor/provider/responder 门；Action=null 只借成熟精确 legacy virtual producer，不从裸 CardUsed 或材料移动猜 Use。原生／其他转换 Dodge 的独立遗漏已按上述局部修正。
- 零材料实际 Play Slash 保留普通距离／次数／真实宣告／Committed／目标／Completed；新 Slash 已捕获调整前原伤害。普通锦囊沿 `BeginJizhiOrNullificationWindow`，该成熟入口自身打开真实 Committed use，因此没有另报重复缺钩子。
- `GameEngine.TieredRoundZeroReturns.cs:5–21` 新零实体 Peach／Alcohol rescue 锁已签发 Use receipt、原 Dying token、responder、CardId0、空物理成本和目标，再验证整个成熟救援后缀。作者随后修正 token API 为实际 `PeachCardId`／`AlcoholCardId`，复读 SHA `7fa9285fdfd6ca2c0880e356dc97c222d31d48456b995605b7791d76c8bfbd12` 已反映正确字段。这里 `original != dying` 的双方实际取同一栈元素，未发现前批重复 Facts 集合在 JSON 后引用不等的同类问题。
- 本轮 8 NEW 中新公开事件及 owning receipts 都由标量组成，不携私手实体集合；共享 Action 的深冻结沿既有类型。新 choices/actions 的结果列表使用只读克隆。最终新增 owning-frame 字段及 Projection/parser/JSON 接线尚未冻结，本报告不声称已覆盖其最终实现。
- 新 AI 无实体无隐藏牌 ID；counterspell 仅按传入玩家视角、公开关系和实际锦囊目标评分。没有执行 native AI。

没有扩展测试或人物配置快照。最终内容／presentation／四个行为方法／完整 OLD 还未纳入本轮审查；作者明确其正在实现。当前 source 主文、工程默认和代码静态合同不等于运行验证。
