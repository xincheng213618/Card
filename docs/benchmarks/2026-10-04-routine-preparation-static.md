# 无懈救援夹具的静态前置精简

此修改尚未构建或运行。用户要求休息期间停止 CPU 重验证；当前只有源码和 JSON 静态检查，不声称性能收益。实测待运行原 `Unrespondable counterspell equipment payment completed cold`，随后纳入用户统一验收。

在既有 `BoundaryWolongZhugeLiangChecks.UnrespondableCounterspellEquipmentPaymentAndCompletedCold` 中，涅槃、酒诗、醇醪三个独立真实场景都需先把选定角色从 HP8降至1，再由原青弦链实际造成1→0、支付救援并处理子结算。原前置各执行七次 `hurt-other`、每次真正失去1体力；现在各用一次独立 fixture `prepare-one-hp`、真正失去7体力。复用原 LoseHp操作，不直接写状态或改变人物初始体力。

旧 `hurt-other` 的1体力合同保留，原生装备/恢复场景仍用它。三个前置的HP1、无手牌救援、涅槃未提前消费等断言保留；酒救援前另加HP8断言。实际最后1→0、Dying、原生选择、装备/酒成本、HP/gain/Completed子帧、exact父链、Processing清理和全部四视角冷恢复均未删除。

静态可确定减少18条 live准备命令及16个后续冷恢复前缀合计96条重放准备命令；Advance数量、CPU和秒数尚未测量。此文件仍有36处Cold调用，源码核对前后数量相同；方法的重复执行与分支场景仍按原流程进行。各方案的其他重复快照/提示精简未实施。

原历史95.776秒日常来自第23批冻结输入，不是修改后的结果。[静态台账](2026-10-04-routine-preparation-static.json)记录准确旧/新文件SHA和未运行边界，[原只读方案](2026-10-04-routine-static-review.md)保留审阅证据。检查数量、注册范围及所有生产版本未改变。
