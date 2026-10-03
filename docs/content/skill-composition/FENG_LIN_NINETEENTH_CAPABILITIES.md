# 第十九批共享能力合同

当前普通 OL 界庞德462、界法正610、界韩当676以独立内容模块、规则与展示JSON注册。沿既有马术、移牌选择、杀请求、攻击范围援助和回合规则修正能力；本批13项新增Core行为检查、全注册Core611/611与WPF57/57通过，实际运行与原失败保存在[验证记录](../../benchmarks/2026-10-03-fenglin-nineteenth-validation.json)。

## 当前杀的实体与抵消凭据

`ClaimCurrentUsePhysicalCards`只在使用者的杀指定目标窗口执行。原CardUseFrame保留动作ID、生成程序帧、指令索引、技能实例/hash、目标和实体ID回执。领取范围为原动作实际材料中仍在Processing的实体；先发回执再原子移动，移牌子窗口返回原程序帧。无实体或先前已领取时不造牌。原杀继续响应及伤害流程，正常结束不再次弃置已领取的材料。

`PreventCurrentTargetSlashCancellationByRule`沿既有ProgramTargetSlashReceipt发行当前目标的抵消禁令。它无需黄忠的手牌/体力比较，保留原动作和目标身份校验；不扩展为全体目标禁令，也不改变其他防具使杀无效的规则。

## 私密手牌与实际偿还

`ViewAndTakeSelectedTargetHand`只在摸牌结束的选定其他角色上执行。程序帧保留当时可看的手牌和私密选牌进度；只有技能拥有者的CreateSnapshot视角得到牌面及提示，公开事实只记录查看人数。选齐实际可取得的牌后一次移动，并等待其获牌子结算，冷恢复不重新观看或支付。

`RequestHandBySuitsOrLoseHp`只在伤害后的选定来源上执行。实际来源在自己的私密提示中选择符合其有效花色的手牌，或失去1体力；程序帧保存付款结果。真实移牌、体力变化、濒死与救援沿typed父子路径返回，不重新付款。恩怨以perDamagePoint沿已有伤害游标逐点运行。

失血付款后的救援续点仅识别该已付款程序根的精确父链。直接桃及注册ViewAs桃保留声明时实际材料、来源区和技能实例，回复与用牌子窗口都返回原濒死帧；程序救援沿原DyingResponse程序、绑牌酒与实际牌序列恢复。真实检查覆盖单实体桃、两张手牌转换桃、春醪绑牌酒、回复/完成用牌观察窗口暂停和四个玩家视角的冷恢复。转换桃的HE输入合同另有静态校验，本批未新增装备材料转换桃的实际用例。

`MoveBoundCards`新增显式awaitMovementTriggers的SelectedTargetHand目的地。此模式通过现有多来源原子移牌方法，将拥有者混合HE付款保留为一个批次；与手牌取得共同保证perSourceOwner的至少两张判断。原默认不等待的路径不改变。

## 真实弃牌与实际回合额度

`DiscardBoundCardForTurnSlashBenefits`在零输入出牌激活内读取一张拥有者HE绑定牌。弃牌前冻结有效花色，原ProgramSkillFrame保留真实移动序号；等待装备回复、体力变化及移牌子窗口后，发行无限攻击范围和该花色的回合杀额度豁免。豁免同时接入候选合法性与实际次数扣款，不靠增加一个大常数模拟无限。

`ScheduleFirstRoundGameUsageRefund`只允许usesPerGame:1激活的第一条指令。首轮发动时把原使用键、来源实例与实际回合绑定到回合效果仓；原回合结束清该使用键一次并过期。非首轮没有返还义务。额外回合不自行推进轮数，失效来源及已结束游戏的取消口径见[工程默认](FENG_LIN_NINETEENTH_RULINGS.md)。

新公开事件的实体列表在CommittedEventProjection中冻结；新公开回合策略列表在PlayerViewProjector中冻结，标量记录无需复制内部列表。规则事实使用AdvanceEventRulesAndQueueFact；指令、付款及子窗口归属继续由runtime push/replace/complete承担。

## 范围与验收

只添加可选节点、独立内容及新共享能力，沿当前RulesVersion、RulesSchemaVersion和内容包CurrentVersion，不逐武将递增。发展验证使用现有名称过滤和Test-Changed.ps1；真实检查、全注册Full、无过滤日常耗时及原失败见[本批运行台账](../../benchmarks/2026-10-03-fenglin-nineteenth-validation.json)。

实体领取检查覆盖原杀继续、双材料、真实借刀父子用牌、无实体与多目标、付款移牌子窗口及发放前的来源抑制；闪和八卦沿实际响应链验证，藤甲独立无效仍生效。弓骑覆盖冻结有效花色、银狮与移牌子窗口、实际次数扣款、来源抑制后已发行效果持续及回合过期；解烦覆盖精确原使用键、首轮返还、第二轮不返还、额外回合不推进轮及最小注册表显式轮数能力。解烦死亡、来源失效及已胜利尾部是静态审查和工程默认，未声称新增这些实际命令用例。
