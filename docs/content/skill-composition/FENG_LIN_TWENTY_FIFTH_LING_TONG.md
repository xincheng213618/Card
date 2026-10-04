# 界凌统588交付合同

当前普通OL官网完整正文已冻结于 `docs/content/sources/fenglin-twenty-fifth-588-source-2026-10-04.json`。身份为界凌统、吴、4体力，initial_hp=0；当前页面只列旋风，没有未展开授技。性别 Male 复用同人物经典身份（Fame2011Content），当前官方API没有独立gender字段。技能正文原样保留在presentation，以下工程口径不是已核官方FAQ。

## 实际批次与资格

新增通用trigger value `MovedEquipmentCardCount=5000`。它读取当前 CardsMoved / PerOwnerBatch 的原始物理movement，按原From=该owner装备区的不同实体计数，设备离区即使移入本人手牌也算离开装备区。不得从当前装备数差、相邻命令、Processing清理或嵌套batch外层推断原始离区。它是一个默认0、WhenWritingDefault不输出0的scalar；仅条件实际使用新值的candidate/context捕获新值，旧绑定完全保留旧表示。没有新增event、collection、effect handler、旁路pending或state/map。

旋风使用一个binding的条件：装备离区数>0 OR本owner本次失牌数>=2。既有owner+skill+binding+instance去重只产生一次机会。matchingIndexes限定同一已提交batch、该owner、声明来源区、From!=To；同批其它owner牌不贡献失牌数，两次各失一张不合并。装备与手牌混合真实原子成本同时满足两项时不多发一次。Processing→Discard的cleanup没有owner，不能重算失牌。

工程默认：当前“失去至少两张牌”以本人手牌、装备区、判定区的实际离区为个人区域失牌，三者可在同一实际批次合计；不是牌堆、公共Processing、弃牌堆或武将上的特殊牌堆数量。特殊牌堆（木牛流马粮、伤、权、醇、私密寄存/扣押及命名牌堆）不纳入该触发的sourceZones。卡牌从一项个人区域移到另一项仍按原区域离区计算，不采用净手牌/装备数量。此区域解释缺当前官方FAQ，正式验收需明确保留该默认而非宣称来源已规定。

## 实际弃牌、隐私与失效

连续两次既有ChooseOtherOwnedCardDiscard：每次可拒绝，选择合法另一角色的一张HE牌，至多2张、至多2名，允许同一人两张。工程默认采用与已注册经典旋风一致的成熟HE弃牌区域；不扩到他人判定区。两步各自独立可选，拒绝第一步后仍可选第二步。当前原文没有更细FAQ，保留0/1/2张选择，不把拒绝解释为支付。

每次手牌选择只公布当前不透明牌位，装备身份公开；所有合法性、装备保护、真实ledger、Silver Lion/HP及CardsMoved子帧均由成熟操作处理。第一张支付后原Program cursor前进，真实child回返才能选第二张；不在新scalar helper重做移动。当前非锁定来源失效时，既有executor取消尚未支付步骤，已弃实体不返还、不重复支付。初始机会拒绝只拒绝收益，原始失牌已提交。

## 经典兼容、AI与资源

`classic:xuanfeng`规则和AI不变，其>=2牌分支仍限定弃牌阶段。新技能独立 `boundary:xuanfeng-current`，没有纯配置快照检查、人物host分支、版本递增或人物runner。AI复用已有公开阵营关系、公开装备价值和不透明手牌牌位评分，不读取目标隐藏牌身份；本稿不新增评分或RNG使用。

NEW映射：Core通用helper、Standard内容模块与嵌入rules/presentation、一个Core行为checks文件。Standard csproj已通配嵌入SkillPrograms/*.json，无需资源项目文件接线。OLD增量仅SkillPrograms enum/facts/resolve/strict context gate、CardMovementPrograms两捕获点、模块登记、现runner4名称登记、routine一个代表prefix。原图及WPF gallery/art安装由root集中处理，本稿不下载或安装媒体。

## 行为检查及验收限制

4项方法使用固定seed31、小4人正式fixture及已有命令、物理实体、真实区域/转换/装备子链：

1. OwnerBatchEquipmentOrTwoCardsCreatesOneOpportunity：双手牌、单装备、混合HE、真实给两牌；准确原batch/scalar/单candidate，两个不同目标、原成本一次和四视角cold。
2. SeparateLossesAndActualConvertedUseKeepPaymentAndCleanup：两个单牌命令不凑两牌；现已注册经典父魂的真实双材料杀成本触发一次，两次弃牌都拒绝后原伤害和实体cleanup各一次。
3. PrivateSequentialChoicesRecoveryChildrenDeclinesAndSourceLoss：初始拒绝、先拒绝后弃1、两张同目标、真实Silver Lion回复/移牌child、公开装备/暗手牌牌位、未知choice原子拒绝、真实movement observer抑制来源取消第二次付费、各暂停点cold。来源失效子例是可控非native参与者的真实命令；不冒充native选择。
4. NativeOwnerBatchLossAndStrictValueContextPreserveClassic：经典旧Play失2牌无新机会，原生AI源不带Driver、只Start/Advance并实际弃牌；strict parser拒绝错误window、PerBatch、缺装备和synthetic discard-origin。对Start后立即读取完整历史，并尊重Completed。

没有改变或新增公开collection-bearing event；新fact只是scalar，旧projection继续冻结既有batch、context、choices和nested列表。cold检查通过CreateSnapshot(viewerSeat)对四视角比较，并保留原command/event/实体ledger。预期检查也未执行：用户要求醒前不构建、不测试、benchmark。本稿仅低负载静态读写，尚未编译、未初始化加载器、未运行任何行为或native检查；不声称这些路径已验收。静态设计中的HEJ默认未另造重复定义或snapshot断言。

## 主区静态整合补记

root按冻结 OLD 字节验证后串行应用窄 patch，五项旧文件与完整预览逐项匹配，五项 NEW 文件及当前官方原图已登记。后续 fixture-only 增量修正了 activation 的给两牌操作：使用既有非 awaited 的 selectedTargetHand 合同，避免借用仅 DrawPhaseEnded 合法的 awaited 约束。真实原子给牌依旧进入既有 post-event owner-loss 窗口，新增断言核两张 Hand(0)→Hand(2) 的同一 batch 及无已退休 Program 回返；原四项方法、付款和冷恢复断言全部保留。该增量没有放宽生产加载器，也没有执行加载器或测试。
