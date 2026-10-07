# 枪舞与甚贤检查夹具的依赖补齐

`JudgedRankSlashPolicyChecks.Start` 原先只装入基础包、经典包和夹具包。经典包依赖救援技能包，救援技能包又依赖主动技能包；Registry 在注册内容前校验依赖拓扑，因此原四项检查会在创建 Registry 时因缺少依赖被拒绝。

本次只补入 `StandardActiveSkillExpansionPackage(true)` 和 `StandardRescueSkillExpansionPackage`，与同组其他两个夹具的装包方式一致。检查断言、注册入口、牌组、种子、生产规则和版本来源均未改变。

验证范围为当前源码的包依赖声明、Registry 缺包检查和三个夹具入口的静态对照，以及 Git 差异检查。按用户此前安排，本任务没有编译或运行测试；四项行为检查及既有例行失败仍需实际执行确认。周鲂批次文档所记录的 46 项既有失败属于其他任务的执行证据，不能据此宣称本修正已经降低失败数。
