# 触发窗口与结算返回

`ProgramTriggerWindowDriver` 统一生命周期、出牌阶段开始、牌移动、体力变化和用牌窗口的候选推进。窗口通过类型化 host 提供当前帧、准备、完成、候选读取、跳过和提示入口；游标与待决状态继续保存在原来的结算帧上，host 仅在调用期间存在。

公共驱动器保留资格检查、自动跳过、可选提示和程序启动的顺序。生命周期与出牌阶段开始的选择组复用同一算法，未选分支各记录一次跳过，恢复游标随原上下文返回。冻结事实、窗口专属校验、支付子帧和结束后的续行仍由所属机制负责。结束续行可能同步开启新窗口，驱动器不能继续消费该新窗口。

新增同类窗口时实现 host，避免另写候选循环；不能把窗口的冻结事实改成每次推进重新采样，也不能把窗口关闭、子帧完成、父帧继续推进合并成一次未经校验的返回。

程序触发完成后通过 `[ProgramWindowReturn]` 注册返回所属窗口。`ProgramWindowReturnCatalog` 按窗口唯一分派，处理器接收原程序帧与完成结果，继续校验父帧身份、当前候选、组恢复游标及阶段。公共入口仍先完成已支付代价的收尾、弹出子帧并记录完成事件，再处理延后回合结束和历史收益的优先返回。

注册表共享不可变元数据和开放类型化委托，不捕获某一对局或程序帧；重复窗口、错误签名和未知窗口会失败。程序内部指令恢复与牌移动返回继续使用 `[ProgramContinuation]`，窗口返回协议负责已经完成的触发子帧。

续接入口的现有签名如下；`Movement` 接收所属程序帧，移动凭据仍在该帧上，不接收 `long` 或另造的返回上下文。

| 注册入口 | 方法签名 | 现有实现 |
| --- | --- | --- |
| `ProgramContinuationKind.Resume` | `bool Handler(long frameId)` 或 `bool Handler(ProgramSkillFrame frame)` | [谦逊续接](../src/Characters/lu-xun/mou-mobile/Runtime/GameEngine.MouQianxun.cs) |
| `ProgramContinuationKind.Movement` / `MovementTail` | `bool Handler(ProgramSkillFrame frame)` | 同上；两个阶段可转入同一个内部续接方法 |
| `ProgramWindowReturn` | `void Handler(ProgramSkillFrame frame, bool completed)` | [共享窗口返回](../src/CardGame.Core/ProgramWindowReturnRuntime.cs) |
| `ProgramNativeDrawReturn` | `void Handler(ProgramSkillFrame frame, NativeDrawOutcome outcome)` | [连策摸牌发起与返回](../src/Characters/lu-zhi/mou-ol/Runtime/GameEngine.Liance.cs) |

`ProgramContinuation` 在同一方法上只能声明一次；需要接入 Resume 和 Movement 时分别声明两个上述签名的入口。`bool` 表示是否接管本次分派；顺序在同类注册内唯一，不照搬范例已占用的序号。签名以[续接发现器](../src/CardGame.Core/GameEngine.ProgramContinuations.cs)和[摸牌返回发现器](../src/CardGame.Core/ProgramNativeDrawReturns.cs)为准，无须再向中央列表追加同一处理器。

原生摸牌沿用 `BeginOperationProgramDraw` 和 `NativeDrawOutcome`。实际移动记录的 `CardMovementRecord.Sequence` 为 `int`，摸牌结果的 `SequenceBefore`、`SequenceAfter` 为 `long`，`AttemptFrameId` 为 `long?`；保存摸牌区间的回执沿用结果类型，不收窄成 `int`，本次没有 attempt 时保持 `null`。完整类型见[摸牌结果](../src/CardGame.Core/NativeDrawAttempts.cs)和[移动记录](../src/CardGame.Core/CardZones.cs)。

回复替换子帧的续接种类在 `recovery.Return.Continuation`，继续推进的拥有帧 ID 在 `recovery.Return.ResumeFrameId`，请求来源在 `recovery.Attempt.Completion`，而不是 `recovery.Continuation`。复用[现有恢复协议](../src/CardGame.Core/RecoveryReplacement.cs)及[已支付装备回复的父边证明](../src/Characters/zhang-xiu/mou-ol/Runtime/GameEngine.MouZhangXiuOlObservers.cs)，继续核对本机制自己的父帧、付款和来源，不能只凭返回枚举取得续接资格。

原生用牌在弹帧前按 `[NativeUseValidation]` 验证，再执行全部 `[NativeUsePreparation]` 并捕获返回事实；弹帧后按 `[NativeUseReturn]` 推进。布尔回调表示已独占处理，普通回调按顺序继续。准备结果只在本次调用的返回上下文中存活，不能写成另一套引擎 pending 状态，也不能重复消费该上下文。

准备处理器可声明成对的返回方法及返回顺序；只有本次准备成功，配对返回才参与弹帧后的独占分派。`NativeUseReturnContext` 保存本次用牌、已经捕获的返回事实及各准备结果，并拒绝第二次返回。回调仍需核对其原生拥有者、来源及已完成费用。

攻击返回通过 `[NativeAttackReturn]` 保留清理前、清理后、借刀处理后和恢复父程序前四个边界。人物专属回调在所属人物或机制文件注册，主引擎保留通用清理、借刀处理和父程序恢复。共享元数据按阶段与顺序排序，实际委托绑定到各自对局，校验异常直接传播。

源码入口：[候选驱动](../src/CardGame.Core/ProgramTriggerWindowDriver.cs)、[窗口返回目录](../src/CardGame.Core/ProgramWindowReturns.cs)、[窗口返回处理器](../src/CardGame.Core/GameEngine.ProgramWindowReturns.cs)、[程序续接](../src/CardGame.Core/GameEngine.ProgramContinuations.cs)、[原生返回](../src/CardGame.Core/ProgramNativeReturns.cs)。
