# 内容模块与规则引擎的职责边界

人物内容位于 `src/Characters/<character>/<variant>/`，由人物目录注册到现有内容包和运行程序集。完整目录约定与注册入口见 [人物模块与注册](CHARACTER_MODULES.md)。

## 组件职责

| 组件 | 当前职责 |
| --- | --- |
| 人物资料 | 人物/版本身份、默认属性、技能元数据、选将池与注册顺序 |
| 技能规则 JSON | 条件、查询修正、转换、主动/触发入口和效果组合 |
| 展示 JSON 与资源目录 | 名称、说明、提示、立绘和音频引用 |
| 技能运行时 | 技能实例、限次、冻结候选、选择、程序游标及子结算等待 |
| 核心能力 | 实体移动、用牌、伤害、回复、判定、拼点、濒死、死亡及阶段推进 |
| 玩家投影与 WPF | 按权限显示快照和完整选项、构造并提交命令 |
| AI | 在已发布的合法动作与选择中决策，按操作策略估值 |

文件物理位置与程序集职责分开。人物 `Runtime/**/*.cs` 链接编译到 Core，资料和规则资源嵌入 Content.Standard，可选 `Wpf/**/*.cs` 链接到 WPF。人物机制可以在自己的目录声明注册方法，仍处于原程序集的私有状态边界内。

## 内容装载

`CharacterContentCatalog` 发现人物资料并校验身份、顺序、规则资源和选将池声明。`EmbeddedSkillProgramCatalog` 按 bundle 加载规则和展示。最终 `ContentRegistry` 校验技能依赖、武将绑定、模式约束和相关能力引用，并冻结注册结果。

规则 JSON 以 `SkillProgram` 表达。`ProgramOperationCatalog` 解析操作，`ProgramCompositionValidator` 验证绑定资源和组合约束，`ProgramEntryCapabilities` 验证当前入口提供的上下文。可执行计划由 `ProgramInstructionResolver` 缓存，`SkillProgramExecutor` 推进主动与触发程序。

只有授予后才获得的技能也必须注册。规则或技能可以由多个角色复用，相关 JSON 放在 `_shared/Skills`。目录及注册顺序影响确定性身份和选将，整理文件不应重排已经声明的顺序。

## 运行状态归属

角色当前属性和技能授予在 `CharacterState` / `CharacterSkillSet`；实体位置在 `CardZoneStore`；选择、支付、触发和原生效果的暂停状态在对应 `ResolutionFrame`。定义、descriptor 和共享注册元数据不保存某局玩家、帧或候选结果。

程序 frame 记录 `SkillId + BindingId + OwnerSeat + SkillInstanceId`、冻结上下文、选择及执行游标。判定、拼点、用牌、伤害等由原生子帧完成，再用精确父帧与结果凭据返回。适配器和内容处理器不直接通知 UI，也不自行调用父阶段恢复。

规则查询和选择查询只读当前事实；动作提交与效果执行由受控宿主完成。`[ProgramCapability]`、`[ProgramSelectionModule]`、`[ProgramContinuation]`、`[ProgramRuntimeHook]` 等注册边界的具体契约见 [人物模块与注册](CHARACTER_MODULES.md) 及 [结算返回](RESOLUTION_PROTOCOLS.md)。

## 新内容的实际路径

1. 明确人物版本、规则来源及现用技能文本，查找可复用操作和正式消费者。
2. 已有能力可表达时，添加人物资料、规则和展示，注册全部依赖与选将池。
3. 用当前加载器检查格式与组合；新增行为或缺陷修复再增加聚焦场景。
4. 能力不足时，定义所需上下文、输入、选择、费用、状态归属和返回协议，补能力及共享机制检查后再接入内容。
5. 新交互同时校验权限、PromptId、revision 和完整选项；帧续接覆盖费用只执行一次、实体守恒和当前命令重放。

UI 读取服务端合法选择，不从技能描述重建规则；AI 使用相同动作和程序能力。私有手牌、私有观看和完整结算栈不能进入普通玩家公开结果。

开发使用 `tools/Test-Changed.ps1` 的相关名称过滤，批次运行常规范围。纯人物配置复用加载和机制检查，完整验证仍需用户明确要求。源码或文档整理不自动授权发布、打包、提交或推送。

## 存档与格式

当前内容指纹和包签名用于检查保存数据是否与本局注册表一致。Checkpoint 记录命令前缀，在当前实现中逐条重放；可序列化的数据帧用于表达暂停状态，不表示直接装载整个引擎栈。

JSON 格式、Checkpoint 格式和内容包版本分别由 `SkillPrograms.cs`、`Replay.cs` 和包的 `CurrentVersion` 管理。普通内容增补由内容 hash 区分；`minimumRulesVersion` 是保留在规则定义及指纹中的元数据。
