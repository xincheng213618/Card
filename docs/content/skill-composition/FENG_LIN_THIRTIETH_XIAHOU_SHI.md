# 当前普通 OL 界夏侯氏 752 暂存合同

来源：`docs/content/sources/fenglin-thirtieth-752-source-2026-10-04.json`，原始 SHA256 `27a56606cc569f098e3726a203268761566b02200d50553f799ddd2b259672d6`。当前 API 蜀、3HP，没有性别字段；Female 仅由既有 Fame2015Content 的经典同人身份补证。未联网、未操作原图。

一手原文：

- 樵拾：每个结束阶段，你可以与当前回合角色各摸一张牌。然后若其与你手牌数不相等，此技能本轮失效。
- 燕语：出牌阶段，你可以重铸【杀】。出牌阶段结束时，若你本阶段失去过至少两张【杀】，你可以令一名男性角色摸两张牌。

## 最小公共能力

- EffectOp 6700 `drawEndingPairThenBlockRoundIfUnequal`：一条 optional own/foreign actual Ending 程序。发行即冻结真正当前回合角色、Ending frame、Round、actual turn、原 source instance/definition/hash。先 owner Draw1 并排空全部原孩子，再当前角色 Draw1 并排空全部孩子，之后查询最终公开手牌数。owner 就是当前角色时仍两份真实顺序 Draw1；不去重、不假造实体或 Draw2。
- Round 失效沿 SkillRuntimeState 的 owner+skill+state Round usage，仅在最终不等时一次消费；真 Round 重置，额外回合不推进轮。ProgramDependencies 显式识别 6700，独立未注册 classic package 的小 fixture 也必须真实启动 Round。不同 source instance、失技/重获不解除本轮失效。
- EffectOp 6701 `recastSelectedPhysicalSlash`：仅单张 owner Hand 的 printed Slash/FireSlash/ThunderSlash、实际本人 Play 主动程序。复用成熟 RecastDiscard/RecastDraw/CardRecastEvent，原成本→完整孩子→实际奖励 Draw1→完整孩子，owning scalar receipt/ledger 排空后再返回。3100 的 DrawPhaseEnded 约束和经典 RecastSelectedCards 保持。
- TriggerValueKind 6700 `currentActualPlayPhysicalSlashLossCount` 是另一 enum 的命名，与 EffectOp 6700 不冲突。仅新值存在时，在可信 CardMovementRecord 追加 nullable scalar actual Play stamp（actual turn owner/number、phase actor/instance、真实 producer frame）。本人的 Hand/E/J 到非本人区域且 printed Slash 才计一次；不公开逐失实体/牌名，没有 use-ID sidecar。新 nullable 字段缺省省略，旧内容不改变事件/JSON形状。PlayEnded 直接复用 SelectTarget AnyLivingMale→Draw2。

## 拥有帧与 prepared 边界

新 pending receipts/events 均无集合字段，source/location/invoice 全为不可变标量 record。私牌 ID 不进入公开失去计数事实。重铸 ID 只有实际到公开 DiscardPile 后才能进入支付 event。新 opt-in stamp 只在可信移动账单，不进入公开逐牌 marker。

首次子窗口绑定原 invoice 真实 sequence/reason/From/To、Batch.ParentFrameId、Awaiting null 或原 frame、来源实例；更深 HP/movement/Damage/Dying 仅在锁定此真实 root 后局部复用已提交精确逐边 helper。Dying 先证明入边再判断完整 alcohol/rescue suffix。回返不重付，不宽放旧 observer/executor，不使用任意 frame presence。

四项小型真实命令/四视角 JSON 恢复续行草稿覆盖：own/extra Ending 双份真实摸牌与独立 Round opt-in；最终不等封锁、失技重获和真正新 Round；物理 Slash Use 加一次真重铸/Processing清理不重复及 male Draw2；真实 Hand→他人Hand 给牌加重铸与下一 actual Play reset。同实体真实重获再失去只做 ledger 静态默认，未声称已有行为覆盖。第一项 routine 候选。所有草稿未加载、编译、运行。最终 OLD 基于 root 确认的 da931c4c97600741ac521bd4c1be4d83d9ed9cce 冻结；历史 NEW 就绪台账保持原字节。
