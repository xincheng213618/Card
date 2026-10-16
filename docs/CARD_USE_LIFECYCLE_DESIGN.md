# 用牌生命周期

一次用牌由原生合法动作、`CardActionContext` 和 `CardUseFrame` 协作完成。实体材料、有效牌身份、目标、实际效果和完成返回保持同一次使用的因果身份；技能程序在规定窗口使用已有能力。

## 接受与目标

`PlayCardCommand` 按当前已发布动作校验操作者、revision、PromptId、牌、目标组合和转换来源。转换保留实际授予的 `SkillId + BindingId + OwnerSeat + SkillInstanceId`；多实体和连续转换保留完整来源链。客户端不能自行拼接另一组目标或借用其他实例的转换资格。

`BeginCardUse` 生成本次使用 ID，归一需要显式自身目标的牌，捕获有效身份、实体原位置及父动作，然后建立 `CardUseFrame`。冻结的动作保留 `TargetSeats` 与 `DesignatedTargetSeats`：实际结算目标和规则要求的真实指定目标分别读取。

下一张牌增减目标由合法动作生成及对应政策实现。以正式简雍巧说为例，`playPhaseStarting` 程序选择对手并拼点，赢时授予下一张使用牌的目标调整，未赢时授予本回合锦囊使用禁令；纵适由拼点领取政策接入。当前实现分别生成保持、增加或减少目标的完整动作，正式使用声明消费该拥有者的资格，回合开始清理未消费资格。

`GameEngine.NextCardTargetAdjustments.cs` 的牌种适配保留这些边界：

- 普通杀及单目标牌扩展合法额外目标，距离豁免仍受目标禁令和牌型条件约束。
- 桃要求实际受伤目标；无中生有、酒等按显式自身目标开始。
- 群体牌以原生实际目标集生成减少选择，不能低于一个目标。
- 借刀按武器持有者及其被杀角色成对增加，子杀使用武器持有者自己的攻击规则。
- 装备与延时牌保留原生放置目标契约；实际使用仍消费下一张牌资格。
- 重铸保留独立动作语义，不进入普通使用的目标调整。

其他下一次真实使用或红色牌目标政策有各自的授予、消费和来源凭据；它们由相关适配接入，不能仅修改帧上的目标数组来获得规则许可。

## 使用与效果推进

普通使用声明后，实体支付、次数和转换事实按原生入口记录。`CardUseFrame` 保存本次材料、动作、目标游标、目标效果状态、已启动窗口和实际伤害结果。`TryMarkProgramUseCommitted` 等标记使同一使用的规则窗口只启动一次。

`cardUseCommitted`、`cardUseTargetsFinalized` 和 `cardUseBeforeTargetEffects` 分别由杀、简单牌及锦囊适配在规定位置打开。程序完成后使用类型化的 `ProgramCardContinuation`、`ProgramTrickContinuation` 或 `ProgramSimpleCardContinuation` 返回。杀响应、无懈、伤害、回复、目标牌选择及濒死继续使用各自原生子帧。

普通锦囊多目标、调整后的桃/酒和借刀保留目标游标：一个目标完成不提前结束整张牌。借刀每次推进一个完整目标对；群体伤害、群体恢复和五谷丰登保留原生目标及展示牌游标。暗手牌选择使用不透明牌位，公开区才提供确切实体选择。

窗口调度按牌种实际实现。杀的重定向、响应前和完全闪避窗口有各自语义；真实指定目标集合与单个效果目标分别冻结。通用操作还受入口上下文及组合校验限制，见 [公共时机](CARD_LIFECYCLE_CONTRACT.md)。

## 完成与材料

`FinishCardUse` 先判断是否还有同次使用的后续目标，再清理本次仍在处理区且未被合法领取的材料。完成事实经 `CardUseFinishedEvent` 记录；若存在 `cardUseCompleted` 候选，保留完成的用牌父帧，完成子程序后才弹出。

材料被合法领取后，使用帧仍可按原身份继续。领取凭据必须匹配本次使用和真实移动事实；其他实体不能凭当前位置获得相同豁免。五谷展示材料、父锦囊材料和借刀子杀材料各有自己的校验，见 [使用中实体牌的续接边界](CARD_USE_MATERIALS.md)。

弹帧前执行原生用牌验证并准备本次返回事实，弹帧后再经 `[NativeUseReturn]` 返回所属程序或请求。攻击返回保留自己的清理、借刀及父程序边界，见 [结算协议](RESOLUTION_PROTOCOLS.md)。

## 限制与恢复

使用禁令按有效牌型及动作语义检查，合法动作、正式提交和转换入口消费相同政策。无懈及其他响应场景中的真实使用，按原生事实判断；材料原牌型不替代最终有效牌型。已签发效果按自身回合、阶段或下一次使用的期限消费和清理。

暂停状态与支付凭据属于拥有帧；返回时不重复声明、付费、消费次数或授予效果。Checkpoint 保存接受命令前缀并由当前实现重放，恢复同时校验格式、内容包签名、内容 hash 和命令。

聚焦检查按实际变化验证目标、材料、父子返回、私密选择和暂停重放；已有机制保障由后续内容复用。常规范围使用 `tools/Test-Changed.ps1`，完整验证需用户明确要求。

## 源码入口

- [动作上下文](../src/CardGame.Core/CardActions.cs)、[原生用牌](../src/CardGame.Core/GameEngine.cs)
- [用牌状态](../src/CardGame.Core/GameEngine.CardUseLifecycle.cs)、[目标调整](../src/CardGame.Core/GameEngine.NextCardTargetAdjustments.cs)
- [下一次实际使用调整](../src/CardGame.Core/GameEngine.NextActualUseTargetAdjustments.cs)、[使用限制](../src/CardGame.Core/GameEngine.CardUseModules.cs)
- [简雍规则](../src/Characters/jian-yong/Skills/classic-jian-yong.rules.json)、[简雍注册](../src/Characters/jian-yong/classic/Content/classic-jian-yong.character.json)
