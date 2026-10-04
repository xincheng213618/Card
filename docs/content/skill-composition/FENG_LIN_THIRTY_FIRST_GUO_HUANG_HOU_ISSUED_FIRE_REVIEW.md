# 726 已发行转换与成熟5403改火杀：独立窄审

此报告独立于已经冻结的 `new-final-review.md` 及原合同/6800审查。只读作者 `followup-02-issued-fire` 与准确成熟 producer；不修改 main/作者/原报告，没有编译、loader、游戏、测试、native AI、benchmark 或 HTTP。

原问题：已发行 tiered Slash/ThunderSlash 经成熟5403 committed offer 变为 FireSlash，并准确追加转换来源。原 NEW `IsIssuedTieredRoundUse` 只承认原 effective kind 与单转换链，合法后继会被误拒。不能由此放行任意 kind/source 变动，也不能重新支付额度或覆盖原 receipt。

冻结输入：followup manifest `1c872bed0dfef3c8a2b2bfc89aeb32d50e4d0e51232e47c5fbc936e36c46e7b6`；patch `63f9b3fce360b6ddbc1e8dfd0e62a69614f6290a32c530af11bdf96332ff7a46`；原 ZeroUses `bad953d8bccc56a2308c2772c8fb14ca69190681a286863975ad5883fea676ee` →有效 `56496044044d8a2ea820f60d5e14194899ecedf0bc23f21e97a71e9b21ab8fe4`；辅助 make-followup.py `4070ed092fd39af0488f694cfc897a82ea6aa045782871e7e19c5dd85a90472f`。逐名实际 SHA 全 match，独立纯文本两 hunk 重建等于完整 preview；无执行作者脚本。

## 精确边界复读

- NEW `TieredRoundIssuedOriginalOutputAction` 仅当确有原 `CurrentSlashFirePolicy`，且 receipt kind 为 Slash family、policy 原 kind/OriginalAction action ID/type/kind 与原 receipt 一致时进入例外。没有政策仍要求当前 CardKind 与原 receipt 相同。
- 它调用成熟 `AssertCurrentSlashFirePolicy(use)`。主线 `GameEngine.CurrentSlashFireOffers.cs:119–152` 真实 producer 保存 immutable 原 Action，仅 Converted 时追加自身准确来源，并发标量 `ProgramCurrentSlashFireChangedEvent`。`:247–271` 的断言锁原/现 actors/providers/type、原 physical costs、Converted/原kind、准确转换链、当前 FireSlash/attack/prepared attacks、原目标/额外目标/redirect 因果、原 Declared 及 change fact。NEW 不绕开该断言，不重新枚举已失去的来源。
- 返回 `fire.OriginalAction` 仅作比较。`IsIssuedTieredRoundUse` 的比较用 `use with { CardKind = r.EffectiveKind, Action = action }` 创建只读比较视图，未 Replace 运行时，也未写原 receipt。原 receipt仍按完整原source/instance/hash/binding/output/material/phase或Round usage与唯一发行fact校验。mature actor替换证明继续以原 accepted kind 与准确 Previous→Actor链为准。
- 原 `ConsumeTieredRoundConversion`、material movement、发行事件、TrueRound tracker 没有在该 patch 改动。真正 Declared 时记录原牌名，没有为了效果改火补写 Accepted/Declared/伪名。Slash family 名称归一仍是已有明确工程默认；当前真正伤害 kind/Fire nature 使用真实 mutable runtime，而非比较视图。
- 该例外只属于 CardUse 的已发行 Use receipt；没有将 incoming Slash 的火政策套在 Dodge/Nullification response receipt，也没有扩展旧5403合法性或任意其他 producer。

此窄范围未再确认新的 P1/P2；原确定组合拒绝已由独立修正稿静态收口。原13NEW/01冻结字节保持。单实体/零实体 Slash、ThunderSlash→FireSlash及same-use冷恢复仍只有源码静态证明，未增加实际命令草稿或声称运行通过。响应receipt全局恢复验证与21OLD的普通锦囊真实返回门是另项审查，不能以本报告代替其闭合。
