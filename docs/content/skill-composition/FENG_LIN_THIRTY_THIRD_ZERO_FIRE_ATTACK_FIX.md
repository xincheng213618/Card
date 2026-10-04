# 726：零材料火攻的物理实体边界修复

此稿只修改当前已签发的二级零材料 FireAttack。依据父代理已冻结 `batch33/726-ordinary-route-audit/finding-fire-attack.json` 的静态 producer/consumer 矛盾；没有运行时复现、编译、加载器或测试结果。

## 实施范围

普通 `ResolveFireAttackDiscard` 与成熟颜色政策 `ContinueColorFireAttackPayment` 保留原真实揭示、可选弃置、成本及全部孩子的处理。只有 `IsTieredRoundZeroFireAttackUse` 证明了同一 owning CardUse 的 CardId0、FireAttack、空实体列表、准确已签发 tier2 receipt/原转换 tuple/hash/配额/签发事实时，攻击的物理 `card` 参数才为 null。其他实体路径仍使用原 effect card。逻辑 Card0 只作为成熟效果表示，绝不进入实体区域库或伪造卡牌移动。

新增局部一致性证据进一步锁定同 use/source/CardUser、FireAttack、空物理材料、没有 ProgramSkill/ProgramJudgment/延时伤害父，以及该 use 最近一次真实 `FireAttackResolvedEvent` 的成功弃牌事实与原目标。它不查询后来当前来源 shard；已接受牌的源失效不回收原支付。真实连环/重定向可能改变当前攻击对象，顺序锦囊结束可能把 target cursor 移至尾后，所以不把当前 Attack.Target 或尾后 index误当初次揭示目标。事实原目标仍须属于同一 owning use 的目标列表。

一致性只在现零材料一致性能力内增加 FireAttack 分支。完成只在准确零材料火攻证据成立时进入原 `FinishTieredRoundZeroAttack`，用 owning use 的真实 Action 发出一次 CardUseFinished，再沿原 CompletedSlash 窗口/PopFinishedCardUse/CompleteFinishedAttackCardUse 返回。原 CompleteAttack 在此之前的 sequential target、winner 和其他 typed completion 顺序不改。没有新事件、集合、框架、receipt sidecar 或 runner，也没有版本修改。

## 原第三方法的两条必要行为草稿

现 `BoundaryGuoHuangHouChecks.SharedRoundQuotaSurvivesTierRegrantAndExtraTurn` 保留原所有断言，追加同一固定 Seed31/四人夹具的普通同花色付款与成熟颜色政策付款两个分支。全牌组真实 Spade Slash 可供目标实际展示及玩家付一牌，不修改人物定义快照或直接 state。

草稿经真实两次伤害/殚心选择建立 tier2，提交公开零 FireAttack 动作，实际选择正数实体成本；在付款选择、真实 Damage→source observer、Completed 子窗分别执行四视角 journal/checkpoint 冷恢复并接续恢复出的实例。断言真实弃1、火伤1、准确 null attack card/空物理材料、原 Damage/窗口/候选 owning chain、签发/true-use/Round成本/完成事实各一次、Completed 已清理、无实体0移动、无残余 Processing。新增 fixture 只是成熟 DamageSource ChooseOption 与颜色政策，保留原生命周期和 presentation mandatory 字段；代码草稿没有实际执行。

## 静态范围及待统一运行

- 已做具名 API/字段与 loader 源码允许字段的文本核对，并以文本重建核对 patch/完整 preview。未调用 SkillProgramCatalog.Load、C#编译或游戏。
- 新实际分支意图覆盖普通/颜色 Hand 成本、after-damage 与 completed 冷暂停。真实运行是否到达这些暂停点仍未验证。
- 银狮装备颜色支付/HP或Recovery孩子、真实连环/目标重定向、多目标顺序 FireAttack、伤害被替代/防止、source-loss/winner/Dying 组合在本轮只沿成熟 producer/return 静态核对，未新增运行覆盖。
- 原其他零材料18输出矩阵、借刀/青龙、UI验收的既有未运行限制没有被本稿改写为已通过。

主区只读。无关 `GameEngine.PrepDiscardReceipts.cs` dirty 既不覆盖也不纳 whitelist；原 batch31/32/33 冻结稿全部保留不改。
