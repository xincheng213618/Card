# 726 零材料 FireAttack 修复：独立静态复核

输入作者冻结 manifest `f3283a1d5b7c1ec7dc674b15b243e9d18a7558db2b1a15a1fe0ab30ad509265f`，patch `1a383d34f3b066734bf8c2420d8589bf54858582c9b00ab4bdd9a01daa012205`。本报告不改作者原稿或主区。审查已结束；本限定范围没有发现另一个可确证 P1/P2。这个结论只代表静态源级审查，不代表编译、加载或行为通过。

## 原问题与隔离边界

原独立 finding 的火攻 Card0 被当作真实实体访问问题由两处真正付款 producer 修正：preview `GameEngine.cs:5815–5852` 的普通同花色手牌，及 `GameEngine.ColorFireAttacks.cs:64–108` 的成熟颜色 HE 付款。两者先保留原真实选择、实体移动和 FireAttackResolved，再仅在 `IsTieredRoundZeroFireAttackUse` 成立时传 `card:null`。原实体火攻仍传原 effect 对象；没有把整个 FireAttack 或所有 Card0 放宽。

`TieredRoundZeroReturns.cs:71–91` 的资格锁 CardUse.CardId0、FireAttack、空 physical 与 `IsIssuedTieredRoundUse(use,true)`；后者 `TieredRoundZeroUses.cs:218–238` 验证原来源 tuple、instance/binding/hash、FrozenTier2、零材料、唯一签发事实及原额度，不重新向当前 shard 要求来源。具体攻击又锁 source/current CardUser、非 skill/judgment/delayed 父和同 use 最近真实成功 FireAttackResolved 的正数实际付款实体。事实原目标属于 owning target 集合，未把后续合法连环/重定向/current target 或 sequential 尾后 cursor 假作初次揭示目标。

新增成员均在当前具名类型内存在：FireAttackResolvedEvent 的 MatchingDiscardCardId/CausedDamage、CardAttackHandle 的 CardUserSeat/ProgramSkillFrameId/ProgramSkillCardUseFrameId/ProgramJudgmentFrameId/IsDelayedJudgmentDamage、DamageFrame 的 Nature，以及 DamageTriggerWindowFrame 的 SourceCardId/SourceCard。实体参数 null 使 CardAttackState.CardId=null、PhysicalCardIds=[]，不再进入 GetAttackCard(0)；没有造 fake movement/Action/paid fact。

## 成本与返回

普通费用依旧 Hand→Processing→Discard（FireAttackDiscard / FireAttackDiscardFinished）一次；颜色路径依旧先 paid receipt、实际 HE→Discard、fresh owning frame，再处理 HP/movement 孩子，之后真实成功 fact 才建 attack。新 helpers 不支付任何费用或创建替代实体。

`TieredRoundZeroReturns.cs:39–68` 只在原 zero consistency 路径增加 FireAttack；processing 仍必须为空或符合原精确 borrowed/Qinglong 支付父范围，未放宽任意 Processing。`TieredRoundZeroUses.cs:290–303` 完成新增 FireAttack exact 分支，保留 Slash/Duel 旧条件。它在主 `CompleteAttack` 的 sequential target / chain / winner / typed tail 处理之后运行；`GameEngine.cs:13138–13149` 与 `12886` 先后次序没被 patch 改动。原 use 的真实 Action 发一次 CardUseFinished，CompletedSlash 子窗后沿 `CardActions.cs:492–497` 捕获 completion→Pop→原精确完成尾部，没有 second payment 或额外父回返。

没有新增事件、nested collection、旁路 pending 或 use-ID map；私有付款成本仍走成熟 prompt 视图。修改没有涉及 PrepDiscardReceipts 的无关 dirty 行、旧内容/版本、其他角色或 runner。

## 四个现有方法与新增两分支

四个原方法名保持，原第三方法既有断言仍在。追加分支位于 preview checks `162–224`，两个固定 Seed31 小实例各用实际规则命令建立 tier2，发布零 FireAttack action，再选择正数 hand entity；没有反射/direct HP 写入。普通和 randomRevealColorFireAttack 使用当前真实政策/事件字段。新 DamageSource chooseOption 和 Completed actor/includeResponseUses 配置，逐项对照当前 parser 及枚举文本，未运行 loader。

草稿在真实 FireAttackDiscard、damage-source 子程序、CardUseCompleted 子窗分别执行 `g = Cold(g,r)`，之后操作返回的新 engine。Cold helper `checks:330–333` 序列化 accepted-journal checkpoint，Restore 重执行真实 producer，再比较四个 CreateSnapshot 视图/事实/实体账/命令。不能把这种机制说成把嵌套 pending Frame fields 独立 JSON 反序列化，也没有声称别名问题被运行复现。断言真实弃1、伤1、签发/额度/finish 各一次，Completed 清理和无实体0 Processing；新分支没有执行。

## 字节与文本验证

具名 11 owned + 14 dependencies 的 raw SHA 与作者清单全匹配。5 个完整 preview 的 raw、LF-with-BOM、BOM-free-LF 与 BOM flags 均匹配。patch 每文件唯一 Update，并进行纯文本反向重建：根此时已把这五个主区文件应用为有效 after，逐文件逆 patch 后均精确得到作者 before BOM-free-LF SHA。没有将已应用主区错误当作 raw-before，也没有覆写主区。证据见 `named-text-verification.json`。

未运行编译器、构建、SkillProgramCatalog.Load、检查、游戏、native、benchmark 或网络。颜色装备付款/SL恢复、连环/redirect、多目标顺序、防伤/伤害替代、source-loss/winner/Dying 与 WPF 都仍只有静态延用成熟路径证据，本修复草稿不覆盖其运行验收。
