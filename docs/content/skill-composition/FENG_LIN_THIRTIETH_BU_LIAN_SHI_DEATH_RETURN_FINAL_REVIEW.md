# 6800 FINAL OLD 独立静态窄审

结论：所列最终交付范围内没有发现新增确定 P1/P2。此结论只涉及静态合同、真实 producer 字段和精确接线，不能替代编译、production loader、游戏或测试验收。

输入：作者 `delivery-manifest.json` SHA `17cc10ce391cc18117add00ad966de1914f5a13ac3aeacde9b9e2efd514c1f7d`；`shared-wiring.patch` SHA `d932d32b35f29cd23627c075aa5ad5bcb0e88d1dcd0cfea8124fb63a26ea05cd`；授权 baseline HEAD `cb0418e5e161b06159fa9b66df526ca7e95b9c6a`。审查以作者 baseline/preview 为准。root 在本次读取中完成主区应用，没有将已应用文件误判为 raw-before。

## 已核实的交付一致性

- 3 NEW、8 support、patch 共12个具名 payload 的 raw SHA 均与最终 manifest 一致；官方775 source SHA 一致。
- 10个具名 OLD baseline 与 preview 的 raw SHA、UTF-8 BOM 标记、保留 BOM 的规范 LF 和去 BOM 的规范 LF SHA 均一致。root 应用前，10个主区 raw-before 也全部匹配；这条只是读取时点证据。
- 逐文件按物理顺序对独立文本执行精确 hunk 替换，10个结果全部等于对应完整 preview；每文件恰好一个 Update，合计22个 hunk。没有执行作者生成器或项目代码。
- 原第三方法去掉仅新追加的 `NestedOriginalDeathBenefit("damage")`／`("hp-loss")` 两调用后，4192字符逐字等于 baseline。四个公开方法、已有 runner 和 routine 前缀均保留。
- 前轮 `review.md` SHA `6d96cca5ff1cd0d4ecfbd355affa127f9a5673863370b4b63bc409ce2c42e9ea`、`manifest.json` SHA `fceec4fba701154915d6df45a0367c417d442c111c49d92fbe1eaf4a53517ade` 均未改变。

## 关键生产边界

1. **原已死 Dying 的精确过滤。** NEW `GameEngine.OwnedDeathBenefitReturns.cs:38–102` 要求6800原三指令、原固定受益者 issuance、同实例/hash、原 OwnerDied window/current candidate/cursor、原 Death 与直属 Dying 的结构和序列值匹配。`IsOriginalDyingSuspendedByOwnedDeathBenefit` 先要求受害者已死，再找这份精确 receipt；整个结构验证不调用 ActiveDying。实际新的活人 Dying 不满足过滤条件，仍为 ActiveDying。原帧没有被移走、改写或临时删除。

2. **旧6303未获得新协议。** OLD `GameEngine.FixedRecipientBenefits.cs` 只在选择／AI／recipe识别处加入6800；6800收据再要求 `ExactOwnedDeathBenefitReturn`，原6303比较保留。NEW严格 composition只允许可选 OwnerDied/owner 的 select→Draw3→Recover1，不接受额外输入、目标或额度。enum新增值显式6800，紧随和后续项均有原显式赋值，不改变旧隐式数值。

3. **DeadOwner 和伤害许可局部。** preview `SkillProgramExecutor.cs:1509–1513` 只在已有 dead-OwnerDied 操作名单添加6800；不新增任意死者效应许可。`GameEngine.SkillPrograms.cs` 的 CurrentDamageAttempt guard 只增加 `AllowsOwnedDeathBenefitNestedDamage`。NEW `GameEngine.OwnedDeathBenefitObservers.cs:26–40` 仍要求当前明确 Damage 指令、目标／amount／nature／actor reference一致、无活人 Dying、本6800原 attack owner，以及真实已付第一子帧与完整连续 observer 子树；没有以“存在 receipt”替代原账单证明。

4. **真实孩子和嵌套死亡。** NEW observer prefix 先验原 receipt 与成熟 `PairBenefitFirstChild` 的 Draw movement／Recovery HP或replacement原账；逐边复用严格 HalfHand paid Damage 边，局部处理已死、已有精确6800 return 的 Dying边。物理／多材料 Peach、bound／zero Alcohol只通过其整段真实救援证明返回，不用任意 CardUse／Dying presence。多个已死原 Dying 可分别由各自 exact root 过滤；两个同时活人 Dying仍为明确不支持范围。

5. **完成 pending attack 的成熟入口。** NEW `GameEngine.OwnedDeathBenefitObservers.cs:60–87` 在 top Program仍有 AttackAttempt 时，锁其 typed AttackReturn/current attempt且要求没有剩余 Damage／BeforeDamage／Dying，再调用 `CompleteDamageAttack`；不会直接 executor 跳到下一指令。preview Runtime 的 RecoveryReplacement分派在254行，新分派在340行，恢复替代优先级保留。

6. **完整性与返回时序。** preview `GameEngine.cs:16907` 在原 Death早退前调用 `AssertOwnedDeathBenefitReturns`。Runtime481行在 Pop原 Program之前调用 completion hook。NEW返回要求root在top、无pending attack/movement/recovery/cost、当前原attack相符且没有重复 returned fact；随后由原调用者真实 Pop。三指令 executor结束时 cursor=3（preview `SkillProgramExecutor.cs:1441–1453`），匹配新receipt的1..3范围，没有第四指令 cursor矛盾。winner／deadbeneficiary仍取消未付尾部，已付子链与实体不回滚，不造替代 damage或新受益者。

7. **冻结与隐私。** NEW `ProgramOwnedDeathBenefitOperations.cs:3–27` 的 cleanup IDs、responders和attempted binding列表在 ctor/init中只读克隆，with及JSON init也使用同setter。结构比较采用标量与 SequenceEqual，未使用带列表的 whole-record引用相等。hash只序列化这份确定有序的 frozen cursor；新公开事件只有标量与来源标识，不携私牌集合。新增 owning field为 nullable／WhenWritingNull；旧存档缺该字段仍保持旧shape。

## 新增草稿的实际 producer 字段

既有第三方法尾部两固定seed子例使用真实 kill-damage激活建立原 Program→Damage→Dying→Death；第一子例为真实 Draw3的 CardsGained→Damage1→DyingEntering→RecoverTo，第二为真实 Recover1→AfterHpRecovered→LoseHp2→DyingEntering→真实两材料Peach→Committed→HP observer。fixture保留classic identity前缀，CardsGained采用合法 perBatch；二材料viewAs包含 inputKinds/inputSuits/inputCount/sameSuit/sourceZones/extendedUse；恢复后的后续命令实际写回 `g = RestoreAfterCold(...)`，没有只比较checkpoint后继续旧实例。只读核对这些字段和成熟入口，**没有运行证明任一子例到达所写断言**。

support中的 OLD-HOOKS／CONTRACT保留前期“最终窗口待定”的设计状态文字；最终授权、完整字节和执行状态以 FINAL-DELIVERY／delivery-manifest 为准。这是历史设计说明，不是本轮生产缺陷。

最终执行边界：编译、build、production loader、游戏、测试、native AI、benchmark、HTTP全部未执行。只读取具名文件与必要小文本/SHA处理；只写本次两个审查交付文件，不改主区、作者稿或前轮报告。
