# Zcode 并行提交与累计脏改动整合修复

已将外部提交的真实共享能力与前十轮累计改动整合到当前工作区。最终 HEAD 为 `60fe122539152333d33f99cf142dc2d5ad12bcbc`；本任务没有提交、推送、改动暂存区或运行发布/打包。未移除任何武将、旧兼容边界、已有测试注册或有意测试删减。完整机读结果、失败名称与错误前缀见 [验证证据](2026-10-08-zcode-integration-repair.json)。

## 整合和实际修复

- 第一轮以 `aed24e70` 为共同基线，将工作区和 `e935fab7` 三方合并，逐项处理 32 个重叠文件。保留新增武将/肖像/图鉴注册、Op/Window/MovementOccurrence、操作 AI 语义、loader 校验、被动伤害上限、轮账本、生命周期、事件观察和恢复接线。
- 合并扩展 API `BeginProgramTopReorder(requireCurrentSeat)`、`BeginProgramSkillDamage(explicitSourceSeat)` 和 `InsertPhase(beneficiarySeat)`；PlayEnding 同时保留 Pindian 与 phase insertion，历史追踪同时保留实际使用序号与同花色用牌条件。保留按效果归属校验 PaidTargetEnding 的累计修正。
- 验证期间外部 HEAD 到 `60fe1225`，新增滕芳兰，再以 `e935fab7` 为基线合并 5 个重叠文件。实际捕获到旧脏共享文件缺失 `LuochongResolve`/`AichenRemoveOption` 的 6 个编译错误；追加整合后真实工作区构建恢复。
- 外部批次 7 类集合事件遗漏提交冻结：解阵转换/恢复、盗书、长姬、追还、战意、落宠。将其集合全部接入 `CommittedEventProjection`，扩展现有共享观察者检查，验证生产者数组后续修改及观察者写入均不能改写冻结载荷。修复前该共享检查实跑失败，修复后通过。
- 收尾审计另确认既有闭境惩罚事件可达路径直接发布原生 `ToArray`：无可弃手牌分支发送 lost，实缴完成分支发送 lost/discarded。最小追加 `ProgramBijingPunishEvent.LostCardIds` 与 `DiscardedCardIds` 冻结，总计修复 8 类事件。仍扩展同一共享检查，实跑红 0/1、绿 1/0，并复用已有闭境惩罚的真实命令/移动/重放检查。BijingMarked、BijingRecast、DuanfaRecycled 的现行生产者已提供冻结副本，未改它们。
- 实际轮开始场景先复现 `This lifecycle window cannot own a clean phase boundary`，补齐 RoundStarting 的生命周期边界及 typed continuation/正常轮次校验；随后复现外部座位直接错过轮开始、到达 PlayCard 的失败，移除丰积拥有者必须等于当前回合角色的错误门控，仍校验座位和存活。短夹具使用实际命令、4 个参与者和冷恢复，无直接改阶段或种子搜索。
- 抚蛮两个已封存生产文件未修改；累积的 intrinsic 手牌身份、发行前钩子、伤害观察、完成候选、拥有者桥接、AI、nullable 帧字段和 native child return 均保留。主线程自行修正抚蛮夹具，本任务未写它的夹具或 round10 文档/快照目录。

## 验证

所有构建都在真实工作区源文件上执行，使用本任务独占 `--artifacts-path`。首个 Core+测试构建 0 错误、19 警告、9.95 秒；收尾新隔离目录全量 Rebuild 0 错误、19 警告、30.51 秒，确认有效方法头并运行相关检查后，最终全解决方案增量构建 0 错误，约 1.370 秒。

| 最终范围 | Core | WPF | 总耗时 |
| --- | --- | --- | --- |
| 相关名称过滤 | 20 通过 / 0 失败 / 20 总计 | 未选择 | 逐检查时间见 JSON |
| routine（无过滤参数） | 328 通过 / 28 失败 / 356 总计 | 18 通过 / 0 失败 | 166.226 秒 |
| Full | 830 通过 / 109 失败 / 939 总计 | 58 通过 / 0 失败 | 507.046 秒 |

相关过滤包含潘濬 8 项、实际回合用牌类别 4 项、抚蛮 4 项、集合冻结、外部座位轮开始/冷恢复、孟进和闭境惩罚。执行命令：

```powershell
# 最终相关过滤使用已经构建的测试程序集，并逐检查计时。
$taskArtifacts = 'C:\Users\17917\Desktop\Card\.artifacts\zcode-integration-repair-20261008-9fbe'
dotnet (Join-Path $taskArtifacts 'clean-build\bin\CardGame.Core.Tests\release\CardGame.Core.Tests.dll') '--filter=Ol Pan Jun' '--filter=Actual turn use kinds' '--filter=Gifted Slash' '--filter=committed event collections reject observer mutation' '--filter=round-start programs include foreign owners' '--filter=classic Pang De discards an opaque hand card' '--filter=Ol Lü Kai bijing punishes lost marked card' --verbose
& .\tools\Test-Changed.ps1 -ArtifactsPath (Join-Path $taskArtifacts 'clean-build') -Verbose
& .\tools\Test-Changed.ps1 -Full -ArtifactsPath (Join-Path $taskArtifacts 'clean-build') -Verbose
```

新增轮开始检查注册在标准名称过滤 runner，加入 routine；集合检查扩展现有共享检查。Core Full 的新增注册数为 13：外部潘濬 8 项、主线程抚蛮 4 项、本次共享轮开始回归 1 项。没有恢复历史整个测试套。

对照是 Round9 累计脏工作区，不是 pristine HEAD：Core Full 817/109/926、routine 323/28/351；WPF Full 58/0、routine 18/0。最终 routine 共同失败 28 项，新增失败：无；消失失败：无。最终 Full 共同失败 109 项，新增失败：无；消失失败：无。名称及 1200 字符错误前缀逐项一致：routine 28/28、Full 109/109。既有失败保留，不能将整个 Core 套件称为全绿。例行耗时仍高于约一分钟目标，本任务没有删除检查来制造达标或通过。

首轮 `e935fab7` 累计 Full 为 828/110/938、WPF 58/0，其中旧孟进检查比 Round9 多出一次 skip 断言失败。独立干净 `e935fab7` 同项 1/0，最新 `60fe1225` 累计过滤同项亦通过；未改该检查或场景。其遍历完整变化中的选将目录，边界会随内容变化，首轮差异保留在 JSON。

一次最终 Full 在同步执行入口 300 秒截止后被终止，退出码 -1、303.896 秒、完成 702 项但无 totals。没有采用该次残留的旧 WPF 日志；另用持久后台 PowerShell 进程重新运行完整范围，上表仅使用其真正完成后的新日志。

闭境追加修复前的完整验证确实完成：Core 830/109/939、WPF 58/0、461.431 秒，109 个失败名称及错误前缀与 Round9 完全一致。该次对应此前的源文件哈希，作为历史记录保留在 JSON；追加修复后重新记录 3407 个文件哈希，再运行上表的相关过滤、无过滤 routine 和 Full。

闭境追加修复后一轮复用旧 build 的真实运行出现产物损坏：routine Core 32/324/356，Full 82/857/939，并伴随 WPF 失败，首个错误为 `BadImageFormatException: Bad IL range`。该轮摘要、日志哈希、首个失败前缀与 DLL 各 copy 哈希保留在 JSON。旧 Core DLL 的 `IsPrepDiscardProgramDying` RVA 1065128 对应 32 字节方法头全零，`PrepDiscardPaidObserverRoot` 的头格式也错误；已通过的 regression DLL 和新 clean-build DLL 在相同 RVA 均有合法 fat IL 头。新目录全量 Rebuild 禁用共享编译器并关闭节点重用，未修改源码语义；关键 20 项再通过后重跑上表 routine/Full。损坏写入者及根因尚未确定，不能将这次运行隐去或归因给某个并行任务。

## 版本和验证边界

重放规则版本从源码更新为 199：修复会使相同 RoundStarting/丰积内容指纹执行相同命令前缀时产生不同的暂停点和结果，因此需要规则 epoch。闭境修复只冻结提交输出，不改变相同指纹的执行规则，没有再次提高 epoch。技能 JSON schema 62、classic 内容包 1.164.0 均未修改。未改写任何已存储 checkpoint/replay 数据。

验证仅覆盖实际 Core 控制台、冷恢复、现有机制与 offscreen WPF；没有逐一实跑全部新增武将/跨技能组合、手动桌面操作、原生音频或官方客户端规则验收。事件冻结的新增 payload 用 detached 生产者/观察者写入夹具覆盖，实际新武将每种事件的产生流程没有逐一重演。轮开始夹具覆盖外部座位、两项“否”选择和中途冷恢复，未专门覆盖旋回或“是”分配分支。

## 最终完整性和清理

最终验证起点记录 3407 个 src/tests/tools 文件哈希；结束变化：无；HEAD 是否保持一致：True。本任务改写文件和保护生产文件哈希见 JSON。主线程 `.artifacts\goal-20261007-01a1156d\round10-source` 及其 build 未读取、修改或清理。

临时目录：`C:\Users\17917\Desktop\Card\.artifacts\zcode-integration-repair-20261008-9fbe`。清理状态：blocked-by-policy。已核对绝对路径和任务所有权，消费者 0；自动审批检查拒绝 `Remove-Item -LiteralPath ... -Recurse -Force`，仅返回 `blocked by policy`，未执行删除。仍保留 7,514 个文件、5,369,059,186 字节（约 5.00 GiB）：独立 HEAD 快照/压缩包、恢复副本、诊断程序、build/test/renders 与原始日志。准确残留路径和拒绝原因也记录在 JSON；主线程临时目录未读取或清理。
