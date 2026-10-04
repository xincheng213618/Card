# 当前普通 OL 界步练师 775 暂存合同

一手来源是 `docs/content/sources/fenglin-twenty-ninth-775-source-2026-10-04.json`，SHA256 `76212903e4e86c42fe8f9f14b2e9108b706514164b81a8e6698d562cb179bbab`。当前 API 为吴、3 体力；性别沿既有步练师元数据补证为女，API 的 null 不声称是性别证据。经典安恤、经典追忆、其他产品文本保持原样。

官方正文：

- 安恤：出牌阶段限一次，你可以获得两名角色各一张牌。然后你展示一张牌并交给其中手牌较少的角色，若不为黑桃，你摸一张牌。
- 追忆：限定技，结束阶段，你可令一名其他角色摸三张牌并回复1点体力。然后你死亡时，可以对其再次发动本技能。

## 6300–6303 公共能力

`ObtainOneFromEachSelectedTarget`（6300）仅供原两名互异存活角色的零牌出牌阶段主动程序，选定 pair 可以包含本人。原顺序、真实 actual turn/Play phase、来源实例、定义 hash 在 owning ProgramSkillFrame 冻结。逐人从合法 HEJ 选择一牌；他人手牌使用成熟不透明牌位。每次真实获得的银狮恢复、RecoveryReplacement、HP、movement/gain 子链先全部回返，游标才进入下一名。本人 Hand→同一 Hand 沿成熟同区域语义不移动、不发获得、不伪造 Processing；本人 Equipment/Judgment→Hand 正常移动。

`GiveShownCardToLeastOriginalTarget`（6301）仅接上述完整两次选择、本人手牌选一张及无条件公开展示。公开展示已使用既有 ProgramCardsRevealedEvent 的完整集合冻结。新 receipt 冻结展示实体的有效花色、原 pair 的此时手牌数及选择出的较少者；赠牌后所有真实孩子结清，再按冻结非黑桃奖励摸一张。来源在已付孩子中失效，不撤回成本、已获得牌或赠牌；尚未执行的奖励按默认取消。新获得与赠牌事件只公开人数/来源/实际账单范围，不公开尚未展示的暗手实体。

`IssueFixedRecipientBenefit`（6302）只允许原本人实际 Ending 可选候选，前接选择一名 other、后接 Draw3/Recover1。原 owner+skill+named game usage 在成熟 SkillRuntimeState 中一次扣除，发行事件存第一次原受益者及 source instance/state/hash。它不是新的 use-ID 侧表或人物布尔标志。

`SelectIssuedFixedRecipient`（6303）只允许同实例真实 OwnerDied 候选的第一节点，后接 Draw3/Recover1；精确 Death→ProgramDeathTriggerWindow→Program parent/current candidate/发行事实保持。仅选择原受益者；没有第二个选角色窗口、没有第二次限定额度扣除。新节点的 dead-owner 放行只发生在这个受严格 composition 限定的 OwnerDied 程序，不扩大旧 Executor 默认。

## 拥有帧与隐私

所有新增 pending record 与 public event 字段均为标量、不可变来源 record、位置 record，不新增集合。实体 ID 只进入 owning receipt；未展示手牌身份不进入公开新事件。prepare 的旧选择和公开 reveal 集合仍由已有投影完整冻结。每段成本都有真实 From/To/reason/sequence 账单及唯一 scalar fact；返回不重付。卡牌移动或恢复产生更深 HP/Dying/救援时，只在已证明新 root、首孩子和连续精确 typed 边后复用成熟 observer helper。

Draw3 仍采用成熟真实 DrawCards 的逐牌 atomic ledger，不假设三张牌是一个 batch。新增捕获 hook 在实际 Draw 完成后、子窗口开始前冻结真实数量与账单范围；Recover1 hook 在真实 producer 前保存 HP/请求值，满血不伪造恢复。新 Damage/Dying allowlist 只接受精确新 owning root，不接任意 observer presence；AfterDamage 深层游标有同一局部 opt-in 证明。

## 四项未运行检查草稿

1. `OrderedOpaquePairPaysBeforePublicGiftAndReward`：真人原 pair 顺序、暗手牌位、首 gain 先返、公开展示/同数选一、黑桃无奖励及非黑桃实际奖励、原 phase 一次成本，四视角 JSON 恢复后继续。
2. `SelfHandSelectionIsNoMoveAndEquipmentObtainOwnsRecovery`：本人 Hand 无假移动；本人白银狮子 Equipment→Hand 的真实 HP/movement/gain 先返，恢复实例继续原 pair；首真实 gain 子程序令来源失技后保留该成本，取消尚未支付的第二人及展示/赠牌。
3. `EndingIssuanceAndOwnerDeathRepeatOnlyOriginalRecipient`：真实 own Ending→原受益者 Draw3/Recover1→真实 LoseHp/无桃死亡→同受益者再次 Draw3/Recover1，原 native AI 子选择与确切 Death 父链。
4. `UnissuedOrDeadRecipientNeverPublishesReplacementDeathTarget`：未发行、第二 actual Ending、原受益者真实死亡时不改选新对象。

routine 建议第 1 方法所对应名称前缀，root 统一登记。检查不重复将定义快照，不新增 runner。

所有执行状态均 false：未构建、未加载、未测试、未 benchmark。活着的安恤或初次 Ending 收益下更深 gain→Damage/Dying、queued RecoveryReplacement/虚拟酒/醇醪仅静态 typed 边审查，不能声称已实跑。最终 OLD 基线为 root 确认 550/563 已合入的 `7e0814da88e25d896e29285dffeb53936bbd33ea`，逐文件原始 SHA 决定精确应用。

已知共享机制限制：OwnerDied 的追忆收益观察者若再次调用 Damage 或进入新 Dying，成熟 BeginProgramSkillDamage、BeginProgramSkillDying、CompleteProgramAttack 仍拒绝原 ActiveDying 残留。此组合尚未支持，需要另行实现准确原 Death/Dying 的 suspension/return 协议；本稿没有宽化入口、静默吞触发或制造假 HP/movement。这与仅未运行的深层边界不同。当前死亡重复完整保留真实 Draw3、Recover1、原受益者及普通 gain/HP 子选择的 typed return。
