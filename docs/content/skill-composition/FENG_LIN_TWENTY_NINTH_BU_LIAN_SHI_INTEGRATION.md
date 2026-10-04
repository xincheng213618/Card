# 775 独立冻结接线说明

最终 Core 基线是 `7e0814da88e25d896e29285dffeb53936bbd33ea`（563 已提交，保留 457/485/550）。以 `delivery-manifest.json` 的每个 OLD `rawBeforeSha256` 为应用依据，不整份覆盖旧源码。`shared-wiring.patch` 每文件只有一个 Update，`preview/` 是该旧文件窄补丁的准确结果，并提供原始及规范 LF SHA。

10 个 NEW 映射保持同一相对路径。9 个 OLD 仅 Core：SkillPrograms、ProgramCompositionValidator、Resolution、GameEngine.SkillPrograms、GameEngine.ProgramLifecycle、GameEngine.SkillProgramHost、SkillProgramExecutor、GameEngine.Runtime、GameEngine。描述符由成熟 ProgramOperationCatalog 自动发现，无需另加人物专属机制注册。

root 统一处理 `BoundaryBuLianShiContent.Register(builder)` 的一次模块注册、原图、四个现有 runner 条目和一个 routine 前缀：

- `BoundaryBuLianShiChecks.OrderedOpaquePairPaysBeforePublicGiftAndReward` — `boundary bu lian shi ordered opaque pair gift and reward`
- `BoundaryBuLianShiChecks.SelfHandSelectionIsNoMoveAndEquipmentObtainOwnsRecovery` — `boundary bu lian shi self hand equipment recovery and source loss`
- `BoundaryBuLianShiChecks.EndingIssuanceAndOwnerDeathRepeatOnlyOriginalRecipient` — `boundary bu lian shi ending issued recipient death repeat and cold`
- `BoundaryBuLianShiChecks.UnissuedOrDeadRecipientNeverPublishesReplacementDeathTarget` — `boundary bu lian shi unissued or dead recipient cancels death repeat`

routine 候选仅第一个，前缀 `boundary bu lian shi ordered opaque pair gift`。旧经典步练师已经是 Female，本次无需改其身份定义。没有 per-character epoch/schema/package 变更。

所有运行状态均 false。四方法是固定小型真实命令/JSON 恢复续行草稿；未编译、加载、运行或测量。死亡追忆收益中观察者另发 Damage/新 Dying 是当前共享机制明确尚未支持的组合，不是“只未测试”；详见 CONTRACT 与 ENGINEERING-DEFAULTS。其余深层 gain/HP/救援边仅静态审查。
