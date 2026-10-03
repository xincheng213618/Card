# 界吕布、界袁绍共享能力

采用当前普通OL的两名武将，详见[来源](../sources/fenglin-fifteenth-2026-10-03.json)与[规则口径](FENG_LIN_FIFTEENTH_RULINGS.md)。复用经典无双、成熟转换/标记成本/Draw/真实Duel路径，不修改旧classic内容、规则epoch、JSON schema或内容包版本。

2500 `ObtainDamageTargetCardAndResolveCategory` 在真实本人Use杀对其他最终角色造成正伤害后，从HEJ获得一张。实际移动回调先冻结实得Hand凭据及打印类别；非装备令原受害者摸一张，装备由受害者选择合法另一角色，实际本人发行无色零实体Duel。owning frame与独立Duel origin持有付款和父Use关系，嵌套true/false返回保留外层实体杀。GeneralWeapon规范化至OutsideGame只留真实移动token，不伪造Hand收益；精确observer能有限返回。

2600 `GrantFactionPopulationMarker` 在真实GameStarting窗口建立时冻结逐角色effectiveFaction人数，成熟主公资格下一次授予两倍群人数的裔。新`ownerMarkerCount`只通过MarkerState依赖注入实时aggregate查询；使用新节点的catalog才纳入Role资格stamp，旧默认保持。真实普通/额外Play开始付一枚裔摸一张。

2601 `RemoveSelectedCurrentArrowBarrageTarget` 对所有本人实际Use万箭的finalized窗口提供可选移除，CardUse上的scalar凭据限制整次Use一次。同步真实余集、window候选FrozenContext和live program context，保留初始历史事实；旧CurrentCardUseTargets查询本体不变。新事件只有scalar，owning集合保持只读。

新规则事实通过AdvanceEventRulesAndQueueFact、新状态通过AdvanceRulesAndPublishState；四视角使用CreateSnapshot。源/Role/actor等HOST审计与正式命令/cold检查分开标识。恢复后实际验证见[批次记录](../../benchmarks/2026-10-03-fenglin-fifteenth.md)。
