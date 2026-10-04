# 有界静态审阅范围

本稿只读取 manifest 具名主区文件/已冻结74来源以及 owned 新稿；没有执行任何 C#、production loader、runner、native、UI 或 benchmark。`freeze-delivery.py` 是纯数据字符串/JSON/字节核对，不执行 SkillProgramCatalog.Load，亦不向主区写入。

- 7100新 descriptor/host/handler沿当前反射发现，amount=-1仅新 descriptor 接受；组合严格匹配方向、BeforeDamage与单instruction。新增 reference 校验沿现 registry ValidateReferences 的已存在 Program 迭代，资格技能存在但不要求其 Program，支持完整 Locked/State 焚心。
- 逐名核对当前 Card/Program attack state、OriginalAttack、BeforeDamage、Movement、HP/RecoveryReplacement、当前 candidate/hash/instance、mature paid suffix 与 pending AttackAttempt completion API；未使用“只要有某帧”即放行。first batch 保留 null-or-exact AwaitingProgramFrameId，同时强制 exact Parent/Origin/单牌 ledger。
- 已付成本 source-loss resume 仅新op+本receipt；无新cap时 nullable fields省略、Capture/Restore提前返回，旧 Increase和全局嵌套guard不取消。后续连环当前amount来自每recipient base，aggregate保留。
- 新公开events为scalar，无列表/政策嵌套；只有定义policy带集合，三个init显式克隆。private候选沿现commit/snapshot冻结，真实Discard后才公开paid ID。
- 四稿已经修正父审确认的schema硬编码、Renegade错误HP门、native错误HP门、chain自伤死亡终止风险；Renegade另有真实incoming拒绝断言。测试仍未运行，其它缺口明确见CHECK-COVERAGE.md。
- shared patch 11OLD与registration patch 4OLD使用单Update/bare@@。纯数据独立重建必须逐份精确等preview并匹配rawBefore、BOM-preserving LF-before、BOM-free LF-before和LF-after。没有捕获/覆盖他人PrepDiscardReceipts dirty。
- 版本常量仍由Replay/SkillPrograms/Standard CurrentVersion权威定义，本批不改其值；增成员与登记的整文件hash当然改变。旧enum body仅删除新增显式7100后与旧body逐字相同，且紧随原显式6500，原隐式数值继承不变。
- JSON仅语法解析与当前schema常量对齐，不声称严格production加载成功。两个技能完整正文分别在program presentation和正式Fenxin State定义，没有用空program或半人物登记。
- PNG为当前source精确advertised URL单GET200、headers/UTC/rawSHA/bytes齐全，PNG IHDR静态确认750×950；无视觉运行验收、全媒体哈希或重复获取。portrait `ol-ling-ju`沿既有ol normalization与catalog读入，gallery显式other。

最终 main 应用、编译、严格内容加载、四项行为与measured routine/full验证由root在用户授权执行窗口串行完成；本交付不把静态依据写成这些运行结果。
