# 当前普通 OL 界李儒642暂存合同

状态：仅静态实现和真实命令检查草稿，未编译、未执行 loader、测试或 benchmark，未写主区。吴国太640稿与旧共享源均保持不动。根代理负责串行整合、登记、原画和验证。

输入为 `docs/content/sources/fenglin-twenty-seventh-642-source-2026-10-04.json`，SHA256 `64f750ebff02bc7d4e2eed71913883cda07df1f26759b9de7c3342b696fa78eb`。官网页和同站API均为当前HTTP200完整正文，无字体resolver缺口；群3。API没有性别字段，男性只沿经典同人物身份作辅助口径。

- 灭计：出牌阶段限一次，你可以将一张锦囊牌置于牌堆顶并令一名有手牌的其他角色选择一项：1.弃置一张锦囊牌；2.依次弃置两张牌。
- 焚城：限定技，出牌阶段，你可以选择一名其他角色开始，令所有其他角色依次选择一项：1.弃置任意张牌（须比上家弃置的牌多）；2.受到你造成的2点火焰伤害。
- 绝策：结束阶段，你可以对一名手牌数小于等于你的其他角色造成一点伤害。

灭计复用 CaptureSelectedCards→MoveBoundCards(drawPileTop, awaitMovementTriggers)，保持原真实锦囊实体和一次出牌阶段使用。5700 ChooseCategoryOrSequentialDiscard 显式选定分支，锦囊一次或任意牌逐次弃置；每次真实HE移动及所有孩子返回后再选下一张。新阶段、付款和公开标量事实保留于原 ProgramSkillFrame，不使用pending sidecar。

5701 EscalatingDiscardOrDamageFromSelected 冻结任意合法起始角色和座次环，起始门槛1，按上一参与者实际完成弃牌数量更新下一门槛。候选牌先私有选择，确认后整批原子移动；拒弃的真实2火焰Damage全部返回后才进入下一参与者，门槛归零。死亡跳过与源失效见工程默认。

5701使用成熟Owner占位目标并显式ReadSelectedTarget资源，只从原activation的唯一SelectedTargetSeats读取起始角色。不会让已死原起始席经旧executor的SelectedTarget-alive门跳过后续整环，不改变旧executor或经典同类op。5700仍是真正SelectedTarget单人操作；受迫者死亡停止其未付余项。

绝策只新增 TargetKind OtherLivingHandAtMostOwner=5700，复用成熟 SelectTarget→Damage。EffectOp 的5700与TargetKind的5700属于两个独立enum，并非同一能力值冲突。当前目标公布与命令提交均读公开真实手牌数，包含零手牌和与自身相等者，不沿classic空手目标口径。

所有新事件均为标量，不公开私有选择中的牌ID；原有真实movement事件、批次集合及prepare冻结沿成熟能力。owning receipts内五个集合采用只读backing字段和显式init克隆，构造、with与JSON反序列化均独立冻结；第1草稿在真实付款冷恢复实例检查不可改写。精确原指令、source/instance/hash、实际turn、实际ledger、连续typed父边及真实Damage父返回须由NEW断言核实。旧classic两非锦囊/固定下一席焚城和旧目标谓词不改变。没有每人物版本提升。

OLD接线尚未冻结；仅生成当前主线的窄预览补丁及逐文件before/afterSHA时交付，绝不覆盖其他并行稿。

## 四项必要行为草稿

`BoundaryLiRuChecks.SequentialAnyDiscardKeepsTopPaymentAndChildReturns`：保证真实红锦囊，不继承经典黑色成本；置顶movement孩子结束才选目标分支。显式两张分支先真弃锦囊，成本observer实际摸牌产生gain孩子，第二张真实SilverLion弃置产生HP再movement孩子，全部关键暂停用四视角JSON冷恢复的新实例继续。原实体/ledger/父frame、成本一次及phase usage保留。建议唯一routine代表。

`SequentialShortfallAndOriginalSourceLossKeepRealCosts`：真实命令建立只有一张HE的受迫者，选两次分支后只付一次并记录Remaining1；另子例在置顶孩子里真实撤销原技能实例，保留原top付款且不进入后续挑战。不会假grant或反射改state。

`ChosenStartEscalationUsesActualDiscardAndFireReturn`：选席2开始，固定ring[2,3,1]；两次私密选牌前无移动，确认后一次原子真弃2，下一人选择真实2火伤并进入Damage/HP孩子，门槛重置后末席真弃1。另小分支在已付movement observer里真实LoseHp8，准确DyingEntering暂停冷恢复，无桃死亡后回到同root下一席。冷恢复、实际payment count、限定game一次和exact ProgramAttackReturn均有草稿断言。

`EndingPublicHandComparisonAndNativeForcedChoices`：真实命令建立2/2/3/0公开手牌量，Ending只提供相等和空手目标，提交后实际damage和HP孩子返回到原Ending；独立子例只用真实Advance让非人类受迫者按自己的published实体选择并完成原弃牌分支。

上述四项未编译、未加载、未运行。Dying自救/酒救援、来源死亡或胜负、特殊装备来源、国战、复活和延时锦囊置顶仍只静态证明或工程默认，不冒充实际覆盖。

## 主线静态整合记录

2026-10-04：在共享 Core a794646f 上接入 8 个 NEW 文件、10 个 OLD 文件窄接线，源和交接 support 原字节核对，OLD 的统一 LF SHA 与冻结预览一致。冻结 delivery-manifest SHA-256 为 6875ab4250d1d07163e8d5f34969fb6729a0e428476773636a26ab72170923fb。正式登记完整 boundary:li-ru、四项行为草稿和唯一 routine prefix。五个新集合在构造、init、with 与 JSON 恢复入口都复制为只读集合；5701 的 Owner 仅为指令分发占位，实际目标锁定原选择起始及冻结环。官网原图随主线登记；未构建、未执行生产 loader、测试或基准，尚无运行验收。全局规则、schema 与内容包版本继续沿权威路径，不逐人物提升。
