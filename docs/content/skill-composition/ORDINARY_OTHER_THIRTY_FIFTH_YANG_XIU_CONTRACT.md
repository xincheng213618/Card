# 当前普通 OL 杨修完整合同

来源是当前 `/hero/104` 服务端完整技能正文，info API 提供同名、魏、3体力及原图。API 没有 skills/gender 字段；`initial_hp:0` 为占位，不设初始体力覆盖。Male 是历史人物元数据工程判断，不是 API 原字段。啖酪、鸡肋全文无未展开授予/字体技能缺口。只在本 stage 新增文件及旧文件预览，主区未由作者修改，全部构建、loader、Core/WPF/游戏/native/benchmark 执行均 false。

## 7300：真实多目标锦囊的本人摸牌后无效

`drawThenNullifyOwnMultiTargetTrick` 只允许单节点、optional Owner、OtherActualUseTargeted，参数仅 op/target/condition，固定 Draw1。沿真实 CardUseFrame/Action/ActualUseTargetIdentity，使用已存在目标窗口，支持本人使用并指定自己。所有当前合法多目标即时锦囊沿真实 owning Use；当前延时锦囊入口只有单实际目标，不能因选择参数而捏造多目标。借刀偶数位置的武器持有者才是锦囊实际目标，杀受害者不计数；三个资格/receipt入口共同使用 ActualTrickTargetCount。

ProgramSkillFrame.OwnTrickDraw 保存原 Use、精确技能实例/绑定/hash、真实 Draw ledger 和已应用标量。先实际 Draw，再等待精确 CardsGained/movement 子树，之后只将同一 use 对本人标为无效并直接 typed Finish。第一 movement 必须同 root ParentFrameId、AwaitingProgramFrameId null 或 exact root、技能来源以及范围内 DrawPile→Hand ledger；后继复用 mature HalfHandPaidDamageObserverEdge 和精确实体桃/酒、零实体酒、round-priced 酒的 whole-tail 证明。其 gain 产生的真实 damage/HP/Dying/救援保持原窗口和用牌，不能重摸或提前无效。付后失技/抑制/死亡按已签发真实 Draw 收据完成，未付资格仍走原来源门；无新 global guard 豁免、并行 pending 或 use-ID sidecar。

## 7301：当前实际回合手牌类型禁制

`restrictDamageSourceHandCategory` 只允许 optional Owner AfterDamageApplied 的确切四节点：ChooseOption(category, basic/trick/equipment)，再三个 ChoiceIs 命中发行节点，各为唯一同名类别。保留旧 AfterDamage choice-group 合同，不扩其它窗口。发行复验准确原 damage window/candidate/source，显式伤害来源须存活；无来源无发行，自伤可以对本人声明。

TurnCardUseEffectStore 保存 owner/source-instance/绑定与原 damage frame、受限人、实际 turn/turn owner、类别标量。多次声明按并集查询、真实 turn end 清除，不因技能来源失效撤回已 issued 限制。公共 snapshot 只含政策，不含受限人的手牌 ID、类别清单或数量；nullable 字段无政策时省略，init/with/JSON 克隆为只读 backing，prepared-view 再冻结。

材料资格只针对该实体当下真正 Hand(owner)，类别采用既有 AdvancedEffectiveHandKind（原有 intrinsic identity），不把任意主动 viewAs 输出当本实体身份。一般 use/response、整手/多材料转换及 RequestSlashByTarget 的 offer/submit均逐材料复验。已装备或私有木牛流马牌不会被手牌范围误拦，正常付款后的 Processing cleanup 不变。

自行弃置仅当 Discard intent 的真实 actor 与手牌 owner 相同。成熟 owned-selection 的 prompt chooser cardOwnerSeat 显式传入预过滤，并由 SelectionActorSeat 冻结到 MoveBound；foreign SelectSource/SelectAndMove 不借 location.OwnerSeat 猜 actor。预过滤只考虑 Always 或已选择 ChoiceIs 的实际 MoveBound discard，未选 conditional Give/transfer 不误拦；最终执行仍复验实际 actor。activation 同样仅预排除 unconditional DiscardSelected，执行时只对真正 toDiscard 付款复验。

上限弃牌分离两集合：计入上限的手牌仍包含鸡肋牌，可自弃集合再排除；必弃数是 min(max(countedHand-limit,0),discardableCount)。不足则弃尽所有合法牌，剩余保护牌仍可超限；零合法不发布不可能 prompt。普通 forced discard 用实际全部 hand 的可弃集合，不误用手牌上限豁免。完整 mandatory color/整手/多张费用不能以不足牌付换取后续收益；未知未来分支仅在实际弃牌处依法过滤。

## 字节与运行边界

descriptor/handler 沿现反射 registry discover，只有声明式 7300/7301 新 cap 与新增政策使 shared 分支生效。旧内容无此政策时筛选恒真、必弃公式等于原值；旧 owning guard/事件排序/暂停点保持。effect 枚举只在尾部新增显式 7300/7301，不重编号旧值。中央 rules/schema/package 常量不改，shared 文件本身因新增能力和登记会改变 hash。

四方法固定 seed31、小 roster、真实命令；关键 paused owning draw/gain/Dying/HP/cost 都将 Cold 返回引擎赋回后续行，对比四视角、frames、movement、facts和 accepted commands。注册/原图/root源记录建议列 manifest。所有方法是待统一编译执行的草稿，不是视觉验收或测试通过证据；未分别实测每个成熟费用消费者、全部多材料/装备/声明组合、source死亡/winner、Silver Lion支付子链，静态共用入口不等于它们已运行。
