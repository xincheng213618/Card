# 界吴国太 640 暂存实现合同

状态：stage-only，未编译、未运行 loader、未运行测试。根代理负责 OLD 整合、模块注册、runner/routine、原画和后续验收。没有修改主目录、规则版本、JSON schema 或内容包版本。

来源：`docs/content/sources/fenglin-twenty-sixth-640-source-2026-10-04.json`，SHA256 `63ebbd951a00144754cc5fc515f8554d96e8dd597a67eec43ac286e31d14875a`。普通 OL 当前官方 hero/640 与 gid=640 API 均 HTTP200；页面捕获 UTC `2026-10-03T23:09:44.173515+00:00`，API 捕获 UTC `2026-10-03T23:09:44.429245+00:00`。页面原字节 SHA `ab147141719944d2208ecc154b12fd989a1c4bc062872efaeec1ec2e64e7303b`，API 原字节 SHA `55cafd311e72b609693fb67b1152d9132486d0475750fc68cee7281753ef9abe`。身份吴、体力3；API 未给性别，Female 复用现有经典吴国太身份资料，单列为辅助字段，未冒称 API 明文。

甘露全文：出牌阶段限一次，你可以令两名角色交换装备区里的牌。若X大于你已损失的体力值，你须在选择角色时弃置X张牌（X为其装备区里的牌数之差）。

补益全文：当一名角色进入濒死状态时，你可以选择其一张牌，若此牌不为基本牌，则其弃置此牌，然后回复1点体力。

## 能力

5500 `selectEquipmentPairAndPayment` 只用于零输入、零初始目标、每出牌阶段一次的主动入口。角色对选择是真实 published choice；选定时在 owning `ProgramSkillFrame` 冻结原 pair、装备数、本人 lostHP、所需全额 X、actualTurn 和移动账序号，发行无实体编号的标量事件。合法 HE 数量及随后私密成本选择均排除当前 exact source equipment；自弃不会把他人的防弃装备策略当作本人的禁止。选择足额 X 张前不移动任何实体，真实 atomic discard 后先完成 SilverLion/HP/RecoveryReplacement/movement 子结算，再使用成熟原子装备交换。该四节点组合强制 payment → await → exchange，不添加 pending/use-ID sidecar。

5501 `selectDyingOwnedCard` 只用于 `DyingEntering/subject:any`。选牌保持同一个真实 Dying token、其入口帧、准确 current candidate/source instance/gameplay hash 和 victim。自己的手牌可见，他人的手牌使用无编号、无牌名的 opaque slot；公开装备/判定仍使用真实公开身份。Basic 结果不建立实体或来源位置 receipt、不放入 cardset、不发公开 reveal；只发行 NonBasic=false 标量事实。NonBasic 结果以 victim 为 `SelectionActorSeat` 建立 Private cardset，然后通过实际 discard 将该牌公开。完整成本孩子结束后，最后的真实 Recover 精确作用于已冻结 victim，回复一点而非恢复至一点。

两种 receipt 都只含标量或不可变 `CardLocation`，新事件没有列表或私有 card ID；现有 prepared-view 的 outer/nested collections 冻结仍用于所有 published choices。trusted `ResolutionStack` 可以保留 paid NonBasic ID，玩家投影不会从私有 cardset 构造新可见实体。Basic 的 PrivateRevealedCards 投影有明确草稿断言。

## 拥有帧与 OLD 增量

只对包含新 op 的准确四节点 composition 接线。经典甘露的 `equipmentExchangePair` 合法性与经典补益均保持原样。核心增量包含 enum/descriptor、严格 active/trigger composition、两个 owning-frame scalar properties、新 selector choice/AI 路由、甘露 owned-card mapper 与合法成本过滤、source-qualified activation gate、existing equipment-exchange 的新 paid receipt 分支、exact Dying continuation admission，以及 nested paid program Dying 的 runtime top-program 分发。新 op 存在时启用既有 selection-actor provenance gate，避免只有这两项新能力的自定义 registry 因未登记装备保护 policy 而丢失 victim 的私密 binding 归属；新 op 与既有 policy 全部缺席时，该旧 gate 维持原 false 行为。

新 helper 首个 HP 或 movement 必须从原 receipt 与真实账匹配；SilverLion `RecoveryReplacement` 还需 `Return.AwaitedProgramMovement`、真实 removal reason、source/target/amount 和 exact typed parent。沿成熟逐边 helper 逐帧证明连续祖先后才调用既有 Peach/provider、bound alcohol 或 virtual alcohol whole-tail 证明，不能跳过进入 Dying 的入边。原 source 死亡或失效允许已经支付的孩子按真实父链结束，取消尚未执行的后继。目标死亡/已出胜负同理，真实成本不回滚，不再收费。

OLD 以根代理确认的含 626/603/620 主目录字节重新生成；每文件仅一个 `Update File`，保留 before SHA、logical-after SHA 与 preview。本合同没有授权改旧共享行为或泛化任意 observer presence。

## 四项行为草稿

`BoundaryWuGuoTaiChecks.PaidEquipmentPairFreezesCostAndSwapsAfterChildren`：真实 owner SilverLion+手牌混合成本，X2/lostHP1，逐张私密选择先冻结，原子付款、回复到 lostHP0、HP/movement 暂停、同 pair 交换、成本一次与 phase 限额。建议唯一 routine 代表。

`FreeAndChangedEquipmentPairsNeverRepriceCommittedPayment`：真实不足 HE 拒绝激活、X等于lostHP零成本，以及付款 observer 实际放入新武器后交换同 pair 的当前装备，原 X 不重新计价。

`DyingOwnedSelectionStaysOpaqueAndBasicHasNoPayment`：真实 LoseHp 进入本人和他人 Dying；他人暗牌 slot 不泄露编号或牌名，Basic 无实体绑定、无支付、无回复、无私有 reveal 投影。

`NonBasicDiscardOwnsSilverLionAndRecoveryBeforeReturn`：他人 Hand/Equipment 两种真实非基本成本，装备 removal 的 HP 暂停、atomic movement 暂停、最后一点 Recover 暂停、exact 原 Dying 返回，native victim observer 用真实 Advance 命令，不手动回答 AI。三类关键 paid 暂停均先 JSON checkpoint 冷恢复，再在恢复实例继续实际命令；所有四视角、typed frames、fact/ledger/history 都逐一等价比较。

所有检查仍为未执行草稿。没有声称 AI 主动甘露择优、复杂救援/Replacement 获技组合、来源死亡/胜负取消或国战环境已经实测；这些本次仅静态证明。没有反射写 state、人物定义快照、seed 搜索或完整局模拟。

根整合记录：基于71407cae按完整冻结清单复制9个生产NEW与1个四方法检查文件；9 OLD应用前原始SHA、应用后原始与归一LF SHA全部匹配。冻结清单SHA d59a005e1a2c4f5b58e765ca97d6bcf7e7941803eb58fa638b116041b611d35b，接线SHA 8bc21c3875636ec8abf7cf59993cb811f50e406913a83978e0efb705f340a48c。两处检查保留精确ParentFrameId/atomic IDs/Resume门并允许成熟batch的null AwaitingProgramFrameId。主区已注册完整640模块、4检查草稿、1 routine前缀，原始PNG SHA25b70f67046779a63fb2fafe0197852608d8e76b0bfc271ff8ee14c9696e242a匹配安装。626/603/620证明保持；只解析JSON与scope数据，未编译、加载、执行、测量或发布。
