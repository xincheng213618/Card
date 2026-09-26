## 2026-09-26 三路公共能力与新武将（rules 164 / schema 54 / 经典包 1.139.0）

朱桓2014、界曹操2014选择版、界张辽2018已进入正式五／八人身份池与图鉴。公共能力为精确实体牌型条件、当前伤害可领取牌选项、正常摸牌计划上限与所选人数扣数；条件、目标集合、上下文资源分别校验，不使用人物配方白名单。护驾保留通用SkillKind。公共AI修正也改变拒战及经典突袭的选择，因此升rules164；包1.139和Checkpoint3保持。见 [schema54契约](content/skill-composition/RUNTIME_V54.md)。

冻结整合副本Release零警告／错误，Core **503/503**、WPF **113/113**，首次Full **135.449秒**。45文件于15:51:33按哈希与备份保护写回；保留并行立绘任务的新GeneralArt实现，只追加三人映射。该对话随后完成新皮肤服务结合验证：Release零警告／错误，14项相关WPF定向检查通过，不把旧副本Full当成新UI全量验收。worker分别17分15秒／28分11秒／21分45秒，派发到写回49分34秒，详见[第三批实测](benchmarks/2026-09-26-capability-generals.md)。

## 2026-09-26 三路 Sol 内容扩展（rules 163 / schema 53 / 经典包 1.139.0 不变）

新增界司马懿、2019 即时分牌版界郭嘉和 2019 界貂蝉，均进入正式五／八人身份池、界限突破图鉴与独立官方立绘。反馈逐点触发、鬼才手牌／装备改判、遗计从当前全手牌中可选交出至多两张、闭月按结束阶段手牌数量摸一／二张，均组合现有程序节点；天妒和离间仍复用通用 `SkillKind`，不称为全 JSON 迁移。界张角专用试验模式的名单保持原范围。

父审将注册门槛固定为首次接入版本 1.139.0，避免未来 `CurrentVersion` 升级时破坏显式旧包；全部新增 bundle 复用公共加载器。没有新增 Core 能力、规则或 schema。并行测试工具把 WPF 渲染目录放到各自 ArtifactsPath 下，避免三路写入同一个临时目录。

最终 Release 全解构建 0 警告／0 错误，Core **486/486**、WPF **111/111**；首次整批检查通过，用时 **139.126 秒**。验证含定向命令、隐私、无效输入、牌区移动、Checkpoint/Replay 和离屏渲染，不等同于实机、多 DPI 试玩。三路独立开发约 12～14 分钟，准备与父审整合单列，见[三路实测](benchmarks/2026-09-26-three-generals.md)。

## 2026-09-26 顾雍、李典与公共伤害时序（rules 163 / schema 53 / 经典包 1.139.0）

新增一将成名四原版顾雍及 2014 身份版李典。慎行复用主动支付，秉壹使用按手牌数限制的存活目标集合、公开整手牌和同色收益；恂恂私密观看四张、选择两张及有序置底，忘隙通过双向伤害参与者和逐点机会执行。WPF 势力标签直接读取注册表，避免新增人物继续维护手工名单。完整定义与边界见 [schema 53 契约](content/skill-composition/RUNTIME_V53.md)。

父审发现旧引擎在濒死前执行伤害后收益，可能使忘隙提前摸到救援牌。本批区分仅允许强制归属标记的 `DamageAppliedBeforeDying` 与救援／死亡后才开放的 `AfterDamageApplied`；武魂采用独立 v53 资源，旧资源保持原文。狂骨冻结伤害时距离。刚烈嵌套伤害、内部技能／判定及公共程序失去体力导致的濒死均维持精确帧父子关系，不能仅放宽不变量绕过续接。

最终 Release 全解构建 0 警告／0 错误，Core **478/478**、WPF **109/109**。完整检查用时 **126.656 秒**；前两次全量失败及相应修复保留在实测记录。群体牌测试保留存活领取→程序失血→死亡清牌→后续目标→一次收束及回放，武魂 WPF 夹具对齐当前前置标记时点。结论覆盖自动化行为和离屏渲染，不等同于实机、多 DPI 或人工完整试玩。本批未提交或推送；[两名 Sol 的时间分解](benchmarks/2026-09-26-new-generals.md)区分独立实现与父审整合。

## 2026-09-26 疠火公共目标数／连续转化迁移与程普专属分区退役（rules 162 / schema 52 / 经典包 1.138.0）

schema 52 增加按有效牌型过滤的 `cardTargetCount` 规则修正与显式 `allowChainedInput` 连续转化。当前【疠火】用前者为所有【火杀】增加一个目标，用后者把武圣等既有转化后的普通【杀】继续转为【火杀】；动作、命令、卡牌事实和回放均保留有序的完整转化来源链。目标数扩展发布 `ProgramCardTargetCountAppliedEvent`，不再使用 `SkillKind.Lihuo` 动作标记。醇醪公开牌区的投影与死亡清理由公共持久牌区能力负责；`GameEngine.ChengPu.cs` 已删除，历史包只保留定义与指纹，不复活专用执行路线。

本批已验证：Release 全解 0 警告／0 错误；程普疠火 Core 定向 8/8；包含 schema 52 定义边界的 Core 全量 466/466；程普 WPF 定向 1/1。当前代码未在最终选择器修复后重跑 WPF 全量或实机／多 DPI 验收。

## 2026-09-26 贞烈公共目标无效迁移与王异专属分区退役（rules 161 / schema 51 / 经典包 1.137.0，历史验收）

schema 51 增加 `cardActionActorIsOwner` 冻结事实与 `nullifyCurrentCardEffect` 公共操作。后者只允许在带 CardAction 能力的目标结算前窗口中作用于技能拥有者，并以类型化事件记录技能、绑定、来源、父用牌帧和有效牌型。普通锦囊现在与杀共用 `CardUseBeforeTargetEffects` 适配：父帧冻结效果实体牌和后续集智／无懈继续点；单目标无效只写入当前 `CardUseFrame.IneffectiveTargetSeats`，群体锦囊的其他目标继续结算。

当前 `classic:zhenlie` 由“来源不是自己 → 当前牌效仅对本人无效 → 失去 1 点体力 → 若存活则从来源手牌／装备区弃一张牌”组合执行。失去体力仍复用公共濒死救援并在拥有者死亡时取消剩余指令；暗手牌 Choice 只公开不透明槽位。专属 `DecisionKind.Zhenlie` 提交、AI、WPF 指南、濒死续接、杀／普通锦囊分支以及 `GameEngine.WangYi.cs` 均已退出当前路径；历史枚举、事件和移动原因只保留数据身份，1.136.0 及更早定义不会重新绑定退役执行器。当前仅余 `ChengPu`、`MouLuMeng` 两个命名专属引擎分区。

定向 Core 验证为 **3 passed / 0 failed / 0 skipped**，覆盖包边界、旧包隔离、杀目标无效、失去体力、暗牌支付、暂停／完成 Replay、群体锦囊仅跳过王异及当前秘计分配；定向 WPF 验证为 **1 passed**，离屏图确认通用发动／支付标题、两项发动 Choice 和不透明来源手牌槽位，无裁切。完整 Solution Release 构建为 0 warning / 0 error，完整 Core **466 passed / 0 failed / 0 skipped**，完整 WPF **108 passed**，`git diff --check` 通过。该结果不表述为实机、多 DPI 或远程客户端验收；本轮没有提交或推送。

## 2026-09-26 解烦公共响应链迁移与韩当专属分区退役（rules 160 / schema 50 / 经典包 1.136.0）

schema 50 为主动程序增加 `usesPerGame` 整局额度，并增加 `requestAttackRangeAid` 公共操作。该操作在发动时按当前公开距离和攻击范围冻结所有能攻击到受益者的存活角色（排除受益者），按技能拥有者相对座位顺序逐人发布私有 `ProgramTrigger`：响应者可以弃置一张当前装备的武器，否则令受益者摸一张牌。冻结目标、响应者列表、游标及每步精确 Choice 均保存在公共程序帧中；通用 AI 只读取公开阵营关系、目标和装备成本。

当前 `classic:jiefan` 以一次整局额度和上述响应节点执行；专属 `UseSkillCommand` 发动、`DecisionKind.Jiefan` Prompt、可变续接对象、AI 分派、WPF 决策分支与 `GameEngine.HanDang.cs` 已删除。历史 `SkillKind.Jiefan`、`DecisionKind.Jiefan`、旧事件和移动原因继续保留数据身份；1.135.0 及更早定义不会重新绑定退役执行器，负例锁定该边界。韩当分区至此整体退出，当前剩余专属分区为 `ChengPu`、`MouLuMeng`、`WangYi` 三个；王异分区仍承担【贞烈】，疠火连续转化编排和额外目标仍部分走专用规则。

定向 Core 验证为 **5 passed / 0 failed / 0 skipped**，覆盖包边界、历史负例、弓骑联动后的冻结响应者、整局限次、武器支付、无武器摸牌和暂停／完成 Replay；定向 WPF 验证为 **1 passed**，覆盖通用技能标题、Choice 和事件栈投影。完整 Solution Release 构建为 0 warning / 0 error，完整 Core **466 passed / 0 failed / 0 skipped**，完整 WPF **108 passed**，`git diff --check` 通过。离屏图 `218-classic-jiefan-response.png` 已复核标题、目标、两项响应和技能状态，无裁切；该结果不表述为实机、多 DPI 或远程客户端验收。本轮没有提交或推送。

## 2026-09-26 秘计公共分配迁移与旧执行器退役（rules 159 / schema 49 / 经典包 1.135.0）

schema 49 增加 `distributeOwnedCards` 公共操作。分配数量只能读取一个更早的实体牌绑定，本轮秘计以实际摸到的 `drawn` 牌数为准，而不是在分配时重新读取已损失体力；候选来自拥有者当前手牌，因此既可交出刚摸到的牌，也可交出原有手牌。私有程序帧保存冻结数量、已交牌和接收目标：第一张移动前可整体放弃，交出第一张后不再发布放弃选项，必须逐张完成冻结数量。每张牌经 Processing 移入目标手牌并发布通用类型化事件，暂停选择与完成结果均由命令前缀精确重放。

当前 `classic:miji` 在公共 `TurnEnding` 窗口先以冻结阶段事实执行 `ownerLostHp` 摸牌并绑定结果，再执行上述分配节点；专属发动／分配 Prompt、续接状态、提交分派、AI 路由、WPF 决策分支和回合结束前钩子均已删除。历史 `SkillKind.Miji`、`DecisionKind.Miji`、`MijiResolvedEvent` 和移动原因继续保留数据身份；1.134.0 及更早定义不会重新绑定已退役执行器，负例锁定该边界。王异分区仍承担【贞烈】，因此当前四个剩余专属分区仍为 `ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`，不能表述为王异整个人物迁移完成。

## 2026-09-26 弓骑公共能力迁移与旧执行器退役（rules 158 / schema 48 / 经典包 1.134.0）

schema 48 增加按绑定牌类别判断的 `boundCardsMatchCategories`，以及可选的 `chooseOtherOwnedCardDiscard` 公共操作；后者只公布其他角色手牌的不透明牌位，装备区和判定区仍按公开实体牌选择。回合规则修正器同时开放 `AttackRange + Unlimited`，由统一攻击范围查询消费。当前【弓骑】以“捕获一张手牌／装备区牌 → 弃置 → 本回合攻击范围无限 → 若该牌为装备牌则可弃置其他角色一张牌”的程序组合执行，阶段额度保证每个出牌阶段限一次。

本轮删除弓骑专属发动、目标牌提示、续接状态、AI、攻击范围钩子和 WPF 决策路由；历史 `SkillKind`、`DecisionKind`、事件和移动原因继续保留数据身份。1.133.0 及更早的旧定义不会重新绑定已退役执行器，新增负例锁定该边界。韩当分区仍承担限定技【解烦】，因此当前四个剩余专属分区 `ChengPu`、`HanDang`、`MouLuMeng`、`WangYi` 的总数不变，不能把本轮表述为韩当整个人物迁移完成。

本轮 Core/WPF Release 构建均为 0 warning / 0 error；完整 Core **465 passed / 0 failed / 0 skipped**，完整 WPF **108 passed**，`git diff --check` 通过。弓骑的暗手牌不透明选择、公开装备选择、可跳过分支、阶段限次、攻击范围查询以及暂停点 Checkpoint/Replay 均由公共执行面验证；没有新增实机、多 DPI 或远程客户端验收，也没有提交或推送。

## 2026-09-26 醇醪旧执行器退役（rules 157 / schema 47 / 经典包 1.133.0）

当前【醇醪】的结束阶段存牌与跨角色濒死救援已分别由 schema 40/41 的公共程序执行，本轮删除仍残留的专属结束阶段选择、濒死牌分支、续接状态、AI 决策和 WPF `DecisionKind.Chunlao` 路由。通用程序继续发布私密候选、移动公开“醇”牌区、执行虚拟【酒】并由命令前缀确定性重建；公开牌区快照与拥有者死亡弃置仍是共享区域职责，不随旧执行器删除。

历史 `SkillKind`、`DecisionKind`、事件和移动原因保留数值／数据身份，旧包内容定义及指纹不改写，但不再自动绑定已退役执行器。新增负例验证 1.123.0 的历史定义不会重新打开专属提示、移动“醇”或产生旧专属事件。程普分区仍保留疠火连续转化、额外目标以及更早包的结算后失血兼容；因此当前四个剩余专属分区 `ChengPu`、`HanDang`、`MouLuMeng`、`WangYi` 的总数不变，不能把本轮表述为程普整个人物迁移完成。

本轮 Core/WPF Release 构建均为 0 warning / 0 error；完整 Core **464 passed / 0 failed / 0 skipped**，完整 WPF **108 passed**，`git diff --check` 通过。WPF 的当前醇醪存牌和救援场景继续通过通用 `ProgramTrigger` 表面完成；本轮没有新增实机、多 DPI 或远程客户端验收，也没有提交或推送。

## 2026-09-26 阶段限次与自有牌主动技能（rules 157 / schema 47 / 经典包 1.133.0）

本批继续按公共能力迁移：`usesPerPhase` 为主动程序增加独立的出牌阶段额度，与 `usesPerTurn` 可同时约束；每次真实进入出牌阶段只重置阶段额度，额外阶段不重置回合额度。`maxCards: null` 表示可选到声明来源区的全部现有牌，合法动作公布实际候选数量，不向 UI 暴露内部上限；此前 schema 的固定 0–64 数值约束保持不变。

孙权制衡切换为 `captureSelectedCards → moveBoundCards(discardPile) → draw(boundCardCount)`，支持混选手牌／装备、空选及伪造选牌原子拒绝、70 张完整换牌、当先额外阶段和装备移动触发。华佗青囊复用阶段额度，修正 1.132.0 程序把“出牌阶段限一次”实现成“每回合限一次”的差异；旧 1.132.0 资源未改。两项当前定义共用 `phase-owned-card-actions` bundle，没有新增人物引擎或 WPF 分支；孙权救援、华佗急救仍走原路径，不计为整个人物迁完。

主动程序 AI 对已由选牌评分计价的输入牌不再在节点估值里重复扣费，保留实际摸牌收益；自动选牌仍使用现有最小合法数量策略，本批不声称已有最优多牌换牌策略。规则 epoch 提升到 157，当前 Checkpoint schema 仍是 3，旧开发期存档按精确匹配政策拒绝。独立输出目录为 `%TEMP%\Card-Iteration-0926`，功能和暂停回放定向验证已通过，整体验证结果随本批收口记录。

## 2026-09-26 十轮续迭代：五位既有武将的技能（rules 156 / 经典包 1.132.0）

本组十轮按独立功能与验证边界计数，不表示迁移十名武将，也未增加可选武将池人数：①经典赵云 `longdan` 双向程序转化（1.128.0）；②经典黄盖 `kujin` 可重复主动程序与濒死续接（1.129.0）；③甄姬 `qingguo` 黑色手牌当【闪】（1.130.0）；④schema 46 将单牌程序转化的来源牌区限定为手牌／装备区；⑤关羽 `wusheng` 红色手牌或装备区牌当【杀】出牌／响应（1.131.0）；⑥华佗 `qingnang` 弃一手牌、回复受伤目标、每回合限一次（1.132.0）；⑦旧包定义、精确来源和伪造来源的兼容回归；⑧schema 46 的牌区隔离及非法定义拒绝；⑨黄盖真实 WPF 技能入口；⑩完整构建、Core／WPF 回归与文档收口。

这五项当前技能不再使用 `LegacyKind`，各首次绑定版本之前的定义和历史执行保留。龙胆、苦肉、武圣是对应既有武将的本轮完整目标技能；甄姬的洛神、华佗的急救仍沿原执行路径，不能称两个人物整体迁完。程序 `viewAs` 的装备区能力目前只用于单牌来源，不泛化到多牌转换或锦囊牌转换。程普、韩当、谋吕蒙、王异四个专属引擎分区仍在；既有迁移也不代表截图中的整库文件已经清零。

## 2026-09-23 疠火连续转化与历史执行边界（rules 151 / 经典包 1.127.0）

武圣将红色非【杀】牌视为【杀】后，再由疠火改为【火杀】时，当前包以疠火程序 `viewAs` 元数据决定第二段转化是否可用；用牌动作和冻结上下文保留“武圣→疠火”的有序来源链，伤害后只结算一次程序失血。动作上的 `CardKindModifierSkill` 仍是兼容旧命令形状的标记，整条链并未迁成纯通用流水线。`1.125.0` 仍以旧技能路径失血，`1.126.0` 仍以程序失血但用旧转化入口；两个历史包分别通过实际双目标伤害、事件类型和 Checkpoint/Replay 回归。本组十轮收口时完整 Solution Release 构建为 0 警告／0 错误，Core 451/451、WPF 107/107，`git diff --check` 通过；这不是整库技能迁移完成或实机验收。

## 2026-09-23 疠火物理杀转化迁移（经典包 1.127.0）

当前 `classic:lihuo` 在 schema 45 程序中同时声明单张物理【杀】→【火杀】的自愿 `viewAs` 与结算后强制失血；合法动作保留普通【杀】、疠火转化单目标和叠加额外目标。转化来源进入冻结的用牌上下文，正式失血触发可按来源与实际伤害筛选；疠火用牌事件仍报告正确的“普通杀转化”。没有明确转化来源的朱雀羽扇选择不得暗中拾取唯一的程序转化来源，以免错误失血。`1.126.0` 只绑定失血程序，`1.125.0` 保留全专用规则，两份历史内容资源均不修改。

“先把其他牌当【杀】，再由疠火改为【火杀】”的动作编排及【火杀】额外目标，仍部分走专用规则；程普分区尚不能删除。

## 2026-09-23 自愿普通杀改火杀的通用转化入口（schema 45 / rules 150）

程序 `viewAs` 在 schema 45 开放单张实体【杀】于出牌阶段改为【火杀】；保留同一实体牌的普通【杀】选项，完整验证合法动作、精确转化来源、火属性用牌及 Checkpoint/Replay。该来源不会被误记作朱雀羽扇转化。当前只开放物理【杀】的单牌出牌转化，不表示任意转化链或响应窗口均已支持。正式疠火包仍为 `1.126.0`，尚未改用这一通用转化；对已有“先当【杀】再改【火杀】”链路继续由旧规则处理。

## 2026-09-23 疠火结算后失血迁移（经典包 1.126.0）

当前 `classic:lihuo` 使用 schema 44 的强制 `CardUseCompleted` 程序：有效牌名为【火杀】、整张牌实际造成过伤害，且冻结的转化链包含 `classic:lihuo` 时，结算后通过通用 `loseHp` 节点失去 1 点体力。原生【火杀】和朱雀羽扇转化的【火杀】即使借疠火增加目标，也不承担这项失血；完全闪避的疠火转化同样不失血。`1.125.0` 及更早经典包继续走旧结算路线、内容指纹不变。疠火的【杀】改【火杀】与额外目标目前仍由专用状态规则生成和校验，所以程普分区尚不能删除。

## 2026-09-23 使用完成窗口的转化来源（schema 44 / rules 149）

通用触发条件新增 `cardUseConversionSkillIs`，以冻结的 `CardActionContext.ConversionChain` 判断本次【杀】是否经过指定技能转化。只有 schema 44 的 `CardUseCompleted` 可读取；旧 schema 和使用前窗口拒绝。实际转化【闪】为【杀】与原生【杀】分别验证了命中和不命中，转化后的响应流程结束再打开完成窗口，并支持提示处 Checkpoint/Replay。该条件使后续疠火失血能够同时限定“实际造成伤害”和“确由疠火转化”，但当前经典内容仍未绑定；疠火的转化与额外目标仍在旧执行路径。

## 2026-09-23 使用完成窗口的实际伤害结果（schema 43 / rules 148）

`CardUseCompleted` 新增冻结的 `cardUseCausedDamage` 条件，取整张【杀】使用过程中是否实际造成过伤害；多目标【杀】汇总各目标结果，完全被【闪】等阻止的使用为假。只允许 schema 43 的使用完成窗口读取，schema 42 或较早卡牌窗口会在加载时拒绝。条件在候选发布时冻结，提示中 Checkpoint/Replay 不重新推断伤害。旧疠火失血仍由专用路径处理，经典包继续为 `standard-classic-generals@1.125.0`／schema 41；还未把疠火绑定到通用窗口。

## 2026-09-23 卡牌使用完成窗口基础（schema 42 / rules 147）

通用程序新增 `CardUseCompleted` 窗口，当前只接在实体或转化【杀】的整张牌结算尾部。实体牌先离开处理区、发出 `CardUseFinishedEvent`，然后才按冻结的卡牌使用上下文发布触发候选；在程序提示处可 Checkpoint/Replay。执行中保留已完成的 CardUse 父帧，程序结束后再清理父帧并继续既有结算后流程，避免把“造成伤害时”误当成“整张牌结算后”。schema 41 拒绝新窗口，schema 42 当前只接受三种【杀】牌名。

这是迁移疠火“转化火杀造成过伤害，结算结束后失去体力”的前置能力；目前尚未加入伤害结果条件，也未把疠火绑定到新窗口。当前经典内容仍为 `standard-classic-generals@1.125.0`／schema 41，程普分区和剩余 4 个命名分区不变。本轮完整 Release 构建 0 警告／0 错误，Core **444 passed / 0 failed / 0 skipped**，WPF **107 passed**，`git diff --check` 通过。

## 2026-09-23 跨角色濒死响应与醇醪救援迁移（schema 41 / rules 146）

当前经典包为 `standard-classic-generals@1.125.0`。schema 41 增加面向当前濒死响应者与濒死目标的通用 `DyingResponse` 窗口；`selectSourceCard` 能从拥有者公开牌区绑定一张确切实体牌，`useBoundCardAsDyingAlcohol` 将其作为濒死目标自用的虚拟【酒】结算，并经过处理区进入弃牌堆。发动者可以救其他角色，程序暂停在选牌提示时可按同一实体牌恢复。每名响应者在该次濒死响应序列中仅有一次行动机会，AI 按公开关系决定是否发动。

正式 `classic:chunlao` 在新 `owned-zone-dying-rescue-skills` 中组合存牌与救援两个触发器。`1.124.0` 的存牌专用规则包保持原样；当前包只走程序救援，旧专用救援保留供历史内容兼容。WPF 使用通用技能提示及程序救援事件显示“醇醪 · 酒救援”。疠火及程普其他历史执行路线仍在 `GameEngine.ChengPu.cs`；余下命名分区仍是 `ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`，不能据此宣称整个人物已迁完。

本轮完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107；最后的提示文案微调后再次通过醇醪定向 Core 4/4、WPF 1/1，`git diff --check` 通过。覆盖 schema 40 拒绝新窗口、旧包边界、跨角色救援、公开实体牌的精确扣除、两次提示间的 Checkpoint/Replay 以及虚拟酒事件。`215-classic-chunlao-dying-rescue.png` 已离屏目检，救援选项无遮挡；未做实机或多 DPI 验收。

## 2026-09-23 可变数量私有选牌与醇醪存牌迁移（schema 40 / rules 145）

当前开发规则 epoch 为 145，经典包为 `standard-classic-generals@1.124.0`。schema 40 扩展共享 `selectOwnedCards`：可对指定牌名集合设置至少／最多选牌数，在私有草稿中逐张选择并于达到下限后明确完成；完成前不移动实体牌，暂停／恢复保留已选集合。可复用的发动资格检查只在首个无条件选牌节点执行前检查真实候选，避免没有合法费用牌却发布空技能入口。

正式 `classic:chunlao` 的结束阶段存牌已改由 `owned-zone-storage-skills` 程序组合：要求本人公开“醇”牌区为空，从手牌中选择至少一张三种【杀】，一次性移入公开“醇”牌区。旧包 `1.123.0` 保留历史技能身份与指纹，不绑定新程序。程普的疠火转化／额外目标／伤害后失血，以及“醇”濒死当【酒】救援，仍在专用引擎路线；本轮只完成醇醪存牌，`GameEngine.ChengPu.cs` 不能删除。剩余角色／专属技能引擎分区仍为 4 个：`ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`。

本轮完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107；程普定向 Core 8/8、WPF 1/1，`git diff --check` 通过。验证覆盖旧包边界、schema 39 拒绝新选牌契约、两张杀的私有选牌／完成、确认前零移动、暂停 Checkpoint/Replay、公开牌区、AI 只存一张和既有濒死救援。`213-classic-chunlao-select.png` 与 `214-classic-chunlao-public-pile.png` 已离屏目检，无裁切；未做实机或多 DPI 验收。

## 2026-09-23 有序双目标、暗手牌转移与安恤迁移（schema 39 / rules 144）

当前开发规则 epoch 为 144，经典包为 `standard-classic-generals@1.123.0`。schema 39 为共享程序加入“手牌数不同的两名其他存活角色”的目标集合，选择时按手牌少者／多者冻结为第一／第二参与者；后续移动不会重新解释两人的顺序。`selectAndMoveOwnedCard` 可由第一参与者私密选择第二参与者的不透明手牌位，经处理区移入第一参与者手牌；展示后 `filterBoundCards` 可按指定参与者的有效花色筛选，因而红颜等改色仍由统一规则查询决定。

正式 `classic:anxu` 使用 `unequal-hand-transfer-skills` 的五个通用节点：选双目标、暗牌转移、公开展示、非黑桃过滤和按绑定张数摸牌。AI 对合法有序组合只按公开关系与手牌数评分，不读取牌面。原 `GameEngine.BuLianShi.cs`、安恤／追忆的专属决策及事件、AI／WPF 按技能名路由已删除；追忆继续使用 schema 37 的通用拥有者死亡窗口。`1.122.0` 保留安恤历史内容身份与指纹，但不绑定当前程序。

本轮完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107；步练师定向 Core 3/3、WPF 1/1。覆盖 schema 38 拒绝新节点、错误目标排序定义拒绝、有序双目标、接收者私有不透明牌位、红颜有效花色、一次限用、拥有者死亡追忆和两个暂停点的 Checkpoint/Replay。`210-classic-anxu-target-pair.png` 已离屏目检，无裁切；未做实机或多 DPI 验收。

当前剩余角色／专属技能引擎分区为 4 个：`ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`。以下各批次数字为当时验收记录，不代表当前清单。

## 2026-09-23 来源标记、直接死亡与武魂迁移（schema 38 / rules 143）

当前开发规则 epoch 为 143，经典包为 `standard-classic-generals@1.122.0`。schema 38 在共享程序目录加入 `changeAttributedMarker`、`maximumAttributedMarker` 和 `causeDeathUnlessBoundCardKind`：伤害后程序可以把公开标记按技能拥有者来源记账；死亡程序只从存活且来源计数为正的最大值角色中选择；判定结果以公开绑定牌继续，排除【桃】／【桃园结义】后进入不经过 Damage／濒死／killer／奖惩的共享死亡流程。

正式 `classic:wuhun` 由 `nightmare-death-skills.rules.json` 的两个强制绑定组成：每点实际伤害后给事件来源增加一枚归属梦魇；拥有者死亡且胜负未定时选择最大梦魇持有者，复用普通判定／改判链，再按最终牌名决定是否直接死亡。嵌套直接死亡会保存外层程序与判定窗口，内层死亡完成后只恢复一次；每层死亡结束只清理该死亡拥有者归属的标记。`standard-classic-generals@1.121.0` 仍保留 `SkillKind.Wuhun` 定义与指纹，但当前运行时不会进入历史路线。

本轮实际删除 `GameEngine.Wuhun.cs`、`DecisionKind.WuhunTarget`、武魂专属直接死亡事件、主引擎路由和 WPF 指南／事件栈分支；Core、AI 与 WPF 只消费普通 `ProgramTrigger`、程序绑定事件、共享判定和死亡帧。完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107；武魂／`causeDeath` 定向 Core 11/11、WPF 1/1。离屏产物 `162-wuhun-death-target.png` 与 `163-wuhun-death-guide.png` 已目检，无裁切；不表述为实机或多 DPI 验收。

剩余角色／专属技能引擎分区为 5 个：`BuLianShi`、`ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`。这些分区仍需按实际职责迁移；本轮完成武魂，不代表全库迁移完成。

## 2026-09-23 拥有者死亡窗口与追忆迁移（schema 37 / rules 142）

当前开发规则 epoch 为 142，经典包为 `standard-classic-generals@1.121.0`。schema 37 新增 `OwnerDied` 触发窗口和 `OtherLivingExceptSource` 目标类型：死亡父流程冻结拥有者、实际杀死者、候选绑定、触发事实和游标；已死亡的技能拥有者只在该窗口获准继续执行程序，其他程序仍要求拥有者存活。窗口可序列化，并与已有死亡技能按稳定候选顺序续接，不把人物或技能 ID 写入共享宿主。

正式追忆由 `death-benefit-skills.rules.json` 组合为死亡时可选发动、从除实际杀死者外的其他存活角色中选择一人、令其摸三张牌并回复 1 点体力。当前 Core、AI 与 WPF 均只消费普通 `ProgramTrigger`、共享目标选择和 `ProgramBindingResolvedEvent`；暂停在目标选择时可精确 Checkpoint/Replay。`standard-classic-generals@1.120.0` 及更早仍保留旧 `SkillKind.Zhuiyi` 执行边界，不自动绑定 schema 37 程序。

本轮验收：完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107，追忆定向 Core 3/3、WPF 1/1。离屏产物 `211-classic-zhuiyi-target-choice.png` 已目检，死亡拥有者、排除杀死者后的目标、技能标题和选择提示均无裁切；离屏渲染不表述为实机或多 DPI 验收。

剩余角色／专属技能引擎分区仍为 6 个：`BuLianShi`、`ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`、`Wuhun`。本轮只迁移当前追忆；`BuLianShi` 仍承载安恤和历史追忆兼容，不能据此删除。通用死亡窗口已为后续武魂迁移提供基础，但不冒充武魂已完成。

## 2026-09-23 全手牌锦囊与荀攸迁移（schema 36 / rules 141）

当前开发规则 epoch 为 141，经典包为 `standard-classic-generals@1.120.0`。schema 36 新增完整拥有区域牌数量表达式 `allOwnedZoneCards`、绑定牌同色条件 `boundCardsSameColor`、空来源跳过策略 `skipIfNoCards`，以及主动入口 `useAllHandCardsAsOrdinaryTrick`。这些能力均以冻结绑定、确切技能实例和普通程序帧执行，不读取人物名或专属决策类型。

正式奇策由 `all-hand-trick-skills.rules.json` 组合为“当前全部手牌当一种合法普通锦囊使用”：共享主动草稿动态要求精确全部手牌，第二段普通 `ProgramTrigger` 选择冻结牌型、目标及目标牌，并复用既有集智、无懈、响应、伤害和结算清理。正式智愚由同一 bundle 组合为伤害后可选摸一张、自动捕获并公开全部手牌、同色时让冻结伤害来源弃置一张手牌；来源无手牌时只跳过该操作并继续完成技能。专属 Core／AI／WPF 命令、事件、移动 reason 和指南分支已移除，`GameEngine.XunYou.cs` 已删除；`SkillKind.Qice`／`SkillKind.Zhiyu` 只保留 1.119.0 及更早历史定义的身份。

专项验证覆盖 schema 35 对四项新契约的加载期拒绝、全手牌伪造防护、全部普通锦囊候选、同色与异色展示、来源空手、私密提示、Checkpoint/Replay、通用 AI 和 WPF 共享草稿／事件投影。完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107；离屏产物 `200-classic-xun-you-card.png`、`201-classic-xun-you-qice-choice.png`、`202-classic-xun-you-zhiyu-choice.png` 已目检，无裁切。离屏渲染不表述为实机或多 DPI 验收。

剩余角色／专属技能引擎分区为 6 个：`BuLianShi`、`ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`、`Wuhun`。这些文件及主引擎中的专属分支继续按公共能力组迁移；本轮完成荀攸，不代表全库迁移完成。

## 2026-09-23 多牌转化与父魂迁移（schema 35 / rules 140）

当前开发规则 epoch 为 140，经典包为 `standard-classic-generals@1.119.0`。schema 35 为既有 `viewAs` 增加精确 `inputCount`，并新增 `useSelectedCardsAs`、伤害后 `source` 主体及转化来源过滤、`grantTurnSkills`。多张实体牌继续进入同一 `CardActionContext`，使用与响应均保留技能、绑定和确切技能实例来源；伤害后触发只读取冻结的转换链与出牌阶段事实，不按人物名或旧攻击标志判断。

正式父魂由 `multi-card-conversion-skills.rules.json` 组合为“两张手牌当杀使用或打出”；主动入口使用普通 `UseProgramSkill` 草稿，决斗、南蛮、借刀和激将复用通用多牌转化候选。仅父魂转化杀在出牌阶段造成伤害后，以强制的来源主体触发器授予本回合武圣与咆哮。专属 Core／AI／WPF 命令和事件已移除，`GameEngine.GuanXingZhangBao.cs` 已删除；`SkillKind.Fuhun` 只保留 1.118.0 及更早历史定义的身份。

专项验证覆盖 schema 34 对 `inputCount` 和新操作的加载期拒绝、主动精确两牌、响应不授予、转换来源审计、回合技能授予与过期、Checkpoint/Replay、通用 AI 估值和 WPF 共享草稿／事件投影。完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107，`git diff --check` 通过；离屏产物 `207-classic-fuhun-active-draft.png` 与 `208-classic-fuhun-parent-skills.png` 已目检，无裁切。离屏渲染不表述为实机或多 DPI 验收。

剩余角色／专属技能引擎分区为 7 个：`BuLianShi`、`ChengPu`、`HanDang`、`MouLuMeng`、`WangYi`、`Wuhun`、`XunYou`。这些文件及主引擎中的专属分支继续按公共能力组迁移；本轮完成父魂，不代表全库迁移完成。

## 2026-09-23 类别挑战与满宠迁移（schema 34 / rules 139）

当前开发规则 epoch 为 139，经典包为 `standard-classic-generals@1.118.0`。schema 34 新增主动入口精确牌集合捕获、绑定牌公开、不同类别弃牌选择、伤害事件来源参与者和按绑定牌数量摸牌。主动入口允许在声明上下界内选择可变数量牌，但必须由 `captureSelectedCards` 立即冻结为资源图中的集合；`eventSource` 只允许用于 `AfterDamageApplied`，类别响应仅公开合法选项，不泄漏响应者其他手牌。

正式峻刑由 `category-challenge-skills.rules.json` 组合为：每个出牌阶段限一次，捕获至少一张手牌并移入弃牌堆，选定目标可弃置一张与全部费用牌类别均不同的手牌；若无合法牌或选择不弃置，则翻面并按费用牌数摸牌。正式御策在每次受到伤害后可选发动，使用公共区域牌选择器冻结并公开一张仍位于手牌区的实体牌，由冻结的伤害来源作不同类别响应；不弃置时拥有者回复 1 点体力。两项技能的专属 Core／AI／WPF 路由、移动 reason 和结果事件均已移除，`GameEngine.ManChong.cs` 已删除；`SkillKind`／`DecisionKind` 数值身份只保留历史序列化边界。

专项验证覆盖 schema 33 拒绝新节点、可变主动成本资源图、类别过滤、无合法牌自动分支、御策发动／选牌／来源响应三段暂停、公开牌投影、AI 选择和完整 Checkpoint/Replay。完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107，`git diff --check` 通过；WPF 产物位于 `%TEMP%\CardManChong139-WpfFull`，已目检 `225-classic-junxing-selection.png` 与 `226-classic-yuce-reveal-choice.png`，未见裁切。离屏渲染不表述为实机或多 DPI 验收。

剩余角色／专属技能引擎分区为 8 个：`BuLianShi`、`ChengPu`、`GuanXingZhangBao`、`HanDang`、`MouLuMeng`、`WangYi`、`Wuhun`、`XunYou`。这些文件及主引擎中的专属分支继续按公共能力组迁移；本轮完成满宠，不代表全库迁移完成。

## 2026-09-23 伤害前防止窗口与仁心迁移（schema 33 / rules 138）

当前开发规则 epoch 为 138，经典包为 `standard-classic-generals@1.117.0`。schema 33 新增公共 `BeforeDamageApplied` 生命周期窗口、冻结的 `eventTargetHp` 条件事实和 `preventCurrentDamage` 操作。普通杀伤害与刚烈反伤在真正扣减体力前统一进入可序列化候选窗口；候选按优先级、从受伤目标起算的相对座次、技能／实例／绑定稳定排序，并在每次回答前复验拥有关系、目标存活和程序定义。防止成功只终止当前这一次待结算伤害，再按类型化续接恢复攻击或刚烈父帧。

正式仁心由 `damage-prevention-skills.rules.json` 组合为：其他角色即将受到伤害且冻结体力为 1 时可选发动，从自己的手牌／装备区精确弃置一张装备类别牌，翻面并防止本次伤害。支付继续使用公共区域牌集合与移动原因，界面只消费普通 `ProgramTrigger` 和共享支付面板；已删除 `GameEngine.CaoChong.cs`、专属仁心决策消费、AI／不变量／伤害入口、移动 reason、结果事件及 WPF 指南／表现分支。`SkillKind.Renxin` 与 `DecisionKind.Renxin` 的数值身份只为历史内容／序列化保留，不再有当前产品消费者。`1.116.0` 及更早包保留历史元数据和指纹，但不会自动绑定 schema 33 程序。

专项验证覆盖共享发动／跳过、精确装备支付、伪造回答原子拒绝、发动与支付暂停点 Replay、普通攻击与刚烈续接、目标体力事实、翻面和通用防止事件。完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107，`git diff --check` 通过。WPF 产物位于 `%TEMP%\CardCaoChong138-WpfFull`，已目检 `221-classic-renxin-prevention.png` 的技能标题、支付候选与受保护目标上下文，无裁切；这是离屏自动化，不表述为实机手动验收。产品、Core 测试与 WPF 测试加载的 DLL 哈希一致：Core `478EC07C0987845EB67F7F6BB093DC5FCF1540A95DC3F18444B68620FE4A2FE7`，Content `D80ACCA1157C9D1AAB3F78B53FFD5C3D48FF44F51C8AC7C715EB2D1A91588AE9`。不提交、不推送。

剩余角色／专属技能引擎分区为 9 个：`BuLianShi`、`ChengPu`、`GuanXingZhangBao`、`HanDang`、`ManChong`、`MouLuMeng`、`WangYi`、`Wuhun`、`XunYou`。这些文件及主引擎中的专属分支继续按公共能力组迁移；本轮完成仁心，不代表全库迁移完成。

## 2026-09-23 手牌颜色限制与潜袭迁移（schema 32 / rules 137）

当前开发规则 epoch 为 137，经典包为 `standard-classic-generals@1.116.0`。schema 32 新增公共 `otherLivingAtDistanceOne` 目标和 `grantTurnHandColorRestriction`：前者按当前共享距离查询选择一名距离恰为 1 的其他存活角色；后者读取一张冻结位置仍有效的牌集合绑定，以该牌在技能拥有者侧的有效颜色，为选定角色写入 `TurnHandCardColorRestriction`。限制只检查该角色手牌区的有效红／黑颜色，装备区、其他区域及异色牌不受影响；所有授予与其他公共回合用牌效果使用同一幂等键、到期事件和回合清理。

正式潜袭由 `hand-color-restriction-skills.rules.json` 组合为准备阶段可选摸一张、从自己手牌／装备区选择并弃置一张、选择实时距离 1 目标、授予同色手牌限制四步。已删除 `GameEngine.MaDai.cs`、专属 `DecisionKind.Qianxi` 消费、AI／不变量／准备阶段桥接、运行时用途字符串、专属移动 reason、结果事件和 WPF 指南／状态分支；枚举数值身份只作为历史序列化标识保留。所有杀／闪／桃／无懈、转换牌和丈八候选统一读取角色无关的 `IsTurnHandCardRestricted` 查询。`1.115.0` 及更早包保留历史定义和指纹，但不会复活已删除的潜袭提示或自动绑定当前程序。

专项验证覆盖通用触发／支付／目标提示、伪造回答原子拒绝、三段暂停、红黑两支响应过滤、Duel 响应暂停回放、回合到期和旧包无退役路线。完整 Solution Release 构建 0 警告／0 错误，Core 443/443、WPF 107/107，`git diff --check` 通过。WPF 产物位于 `%TEMP%\CardQianxi137-WpfFull`，已目检 `184`–`186` 的武将卡、公共支付面板和结算后界面，无裁切；这是离屏自动化，不表述为实机手动验收。Core 与 WPF 测试加载的产品 DLL 哈希一致：Core `424AFC63BD3848CA9E6CE08369800FC87F394A8D71144849CA2751F75B7AAD01`，Content `3616FFA0C9A2ABE1165B977CA5709875503C78206082450050EF79C1DDA44C0A`。不提交、不推送。

剩余角色／专属技能引擎分区为 10 个：`BuLianShi`、`CaoChong`、`ChengPu`、`GuanXingZhangBao`、`HanDang`、`ManChong`、`MouLuMeng`、`WangYi`、`Wuhun`、`XunYou`。这些文件及主引擎中的专属分支继续按公共能力组迁移；本轮完成潜袭，不代表全库迁移完成。

## 2026-09-22 私密区域牌集合与英魂迁移（schema 31 / rules 136）

当前开发规则 epoch 为 136，经典包为 `standard-classic-generals@1.115.0`。schema 31 新增公共 `selectOwnedCards`：由 Owner / SelectedTarget / Actor 私下从自己手牌、装备区或判定区选择固定数量或拥有者已损失体力值数量的牌，结果写入私密牌集合绑定，再由既有 `moveBoundCards` 消费。候选实体和原始牌位在草稿创建时冻结；逐张选择阶段不移动牌，数量不足时只选择全部现有牌，空来源直接生成空绑定。确切技能实例、参与者存活或任一来源牌位失效时整段取消，不产生部分支付。暂停草稿、已选 ID 和原始牌位均为类型化可序列化状态。

正式英魂已改为 `owned-card-exchange-skills.rules.json` 的两个准备阶段互斥分支：目标摸 X 后弃 1，或目标摸 1 后弃 X，其中 X 为发动窗口冻结的孙坚已损失体力值。目标用私有公共选牌面板选择自己的手牌/装备，完成后才一次性进入普通移动批次，因此失去装备仍能触发枭姬等共享后续窗口。已删除 `GameEngine.Yinghun.cs`、主引擎专属准备阶段桥接、AI/不变量分派、专属移动 reason 和结果事件；旧文件可从 Git 历史恢复。`1.114.0` 及更早包仍保留历史定义和指纹，但运行时负例证明不会复活旧英魂提示，也不会自动绑定当前程序。

公共 AI 只按可见手牌数和公开区域牌估计集合规模，并把支付损失归给实际牌主；不会读取暗牌身份。WPF 复用既有 `SkillChoices` 和 `AnswerPromptCommand`，离屏检查分别渲染首次和第二次私密选择，确认首选不移动、已选牌不再出现、第二选后精确两牌只提交一次，不新增英魂人物界面分支。

本轮验收：完整 Solution Release 构建 0 警告/0 错误，Core 443/443、WPF 107/107，`git diff --check` 通过。WPF 产物位于 `%TEMP%\CardYinghun136-WpfFull`，已目检 `231-program-owned-card-set-first.png` 与 `232-program-owned-card-set-second.png` 的候选、剩余数量和布局；这是离屏自动化，不表述为实机手动验收。Core 与 WPF 测试加载的产品 DLL 哈希一致：Core `F789FE64D07A8E734A60AEA105ECFBC99104B12903DB1E9D3BE4767853745653`，Content `889FB4B36072FA06C54749652B8A78A4BC4BB734B42C5F914BA968C984F98813`。不提交、不推送。

剩余角色/专属技能引擎分区为 11 个：`BuLianShi`、`CaoChong`、`ChengPu`、`GuanXingZhangBao`、`HanDang`、`MaDai`、`ManChong`、`MouLuMeng`、`WangYi`、`Wuhun`、`XunYou`。这些文件及主引擎中的专属分支继续按公共能力组迁移；本轮完成英魂，不代表全库迁移完成。

## 2026-09-22 持久牌区与觉醒旧执行链清理（rules 135）

正式权计、自立、排异与单骑已经分别使用公共牌区、生命周期、技能授予和主动子伤害能力。本轮删除 `GameEngine.ZhongHui.cs`（521 行）及散落于主引擎、查询、AI、WPF 的专属提示、支付、伤害续接、手牌上限和结果事件分派；同时删除生命周期宿主中的旧单骑觉醒。`GetAuthority` 只保留为共享牌区访问器，公开快照中的空 Authority 区统一投影为空集合，不再由人物包开关控制。怒斩尚未迁移，其转换规则仍留在卡牌转换分区，未冒充完成。

历史包内容定义和指纹不改写，历史 SkillKind、DecisionKind 和效果枚举保留数值身份；这些身份不再启动被删除的执行器。旧定义不自动绑定当前程序。专项保留当前组合的权计精确存牌与上限、自立双分支及暂停回放、排异支付与致死子伤害后恢复，并增加旧包负例：连续三次伤害不走旧权计；测试准备三张 Authority 和已获得旧排异后，也不会恢复旧上限、主动动作或自立觉醒。单骑旧包不再扣上限或授予技能。测试准备的反射状态不表述为玩家命令或可回放操作。本文后面的“历史专属执行边界”是早期实施记录，以本段的退役边界为准。

该批未增加规则节点或更改当时正式配方，使用 schema 30 / rules 135 / classic 1.114.0。定向权计/自立/排异 3/3、单骑 1/1 通过；完整 Solution Release 0 警告/0 错误，Core 440/440，WPF 106/106，`git diff --check` 通过。最终日志位于 `%TEMP%\CardLegacyRoutes135-20260922` 的 `build.log`、`core.log`、`wpf.log`；两组测试产物哈希一致：Core `0C608162B62B4CE3D200AE27C4B9BDA67E52977755653FD5CDFB1B805CA1F526`，Content `1F497E5B564128E25B7A530AD13DE9F156EE2FB938935B9913C102B311CD3114`。已检查 `ui\198-classic-zhong-hui-quanji-choice.png` 的公共选项区域，WPF 测试还通过既有命令验证选牌、Authority 展示、自立与获得排异；不声称实机手动验收。不提交、不推送。

该批结束时剩余角色/专属技能引擎分区为 12 个：`BuLianShi`、`CaoChong`、`ChengPu`、`GuanXingZhangBao`、`HanDang`、`MaDai`、`ManChong`、`MouLuMeng`、`WangYi`、`Wuhun`、`XunYou`、`Yinghun`。当前清单以本文顶部为准。

## 2026-09-22 公共命名选项与区域牌类别支付（schema 30 / rules 135）

当前开发规则 epoch 为 135，经典包为 1.114.0。本轮新增 `chooseOption`：指定 Owner / SelectedTarget / Actor 作为响应者，将选择写入命名结果，后续普通节点用 `choiceIs(sourceBind, optionId)` 条件分支。选项可用性只读取响应者的公开体力、手牌数、翻面和横置状态；同一程序内结果名字不可重复、不得提前引用或引用未声明选项。规则与展示分离，presentation 3 的 `optionLabels` 不进入 gameplay hash。不引入人物收益枚举或专属执行器。

`selectAndMoveOwnedCard` 支持可选的 Basic / Trick / Equipment 类别过滤；只有选择者与牌主为同一参与者时才允许过滤，避免通过他人的暗手牌候选泄露牌类别。源牌位与实体身份仍复验，技能确切实例或支付参与者失效时不支付。选项回答前重验确切技能实例、响应者存活和选项条件；失效则取消剩余步骤，父窗口只恢复一次。

正式举荐由 `support-choice-skills.rules.json` 中的选人、非基本手牌/装备支付、目标选项及普通摸牌/回复/状态设置组合定义。已删除 `GameEngine.Jujian.cs`、结束阶段 LegacyJujian 桥接、专属 AI/界面分发和旧结果事件。历史 SkillKind / DecisionKind 数值标识保留，当前执行不再产生旧 Jujian 决策；没有新增历史执行器。旧文件可从 Git 历史恢复。

AI 共用公开上下文估值：预测每个命名选项的一条分支，选项局部前瞻止于下一个选择，不枚举指数级选择树；翻面/解链收益归所选目标，由敌友评分计算，不算作拥有者收益。这个有界启发式不声称完整最优策略。

### 仍未清空的迁移清单

截至该批，仍有 13 个带武将或专属技能名的引擎分区：`BuLianShi`、`CaoChong`、`ChengPu`、`GuanXingZhangBao`、`HanDang`、`MaDai`、`ManChong`、`MouLuMeng`、`WangYi`、`Wuhun`、`XunYou`、`Yinghun`、`ZhongHui`。其中已有正式程序定义的旧分支也必须继续清理；`GameEngine.cs` 和 AI/UI 内散落专用路由同样计入欠账。文件清单只是入口清点，不代表每个文件都能整块删除；当前清单以本文顶部为准。

后续按公共能力组继续：先清理已迁程序的残留，再处理私密支付/强制弃牌与信息公开、用牌转化及父结算恢复、伤害/死亡参与者与标记等缺口。同类机制成批补定义和差异回归，不按每名角色再造框架。`CardActions`、`Pindian`、`RuleQueries` 等真实公共分区应保留；移动文件或改名不算迁移完成。

本轮验收：完整 Solution Release 0 警告/0 错误，Core 440/440，WPF 106/106，`git diff --check` 通过。最终日志为 `%TEMP%\CardNamedChoice135-20260922-final\build.log`、`core-final.log`、`wpf-final.log`；公共选项界面渲染位于 `ui-final\230-program-named-choice.png`，已检查离屏布局与既有 WPF 命令提交，不声称实机手动验收。Core 与 WPF 测试加载的产品 DLL 哈希一致：Core `3F7CFD20D221A5C9E78EFBA0BEE630DC7FD4A52930BFBB14065BA77C1BE4974A`，Content `8B97413D9C5C0F1DF0C53FD3B13123ADC3C045CF5042171147330DA0DA236FC2`。最终 AI 估值还验证了已提交选项不得被后续预测重选。全库迁移仍未完成，持续迭代 goal 保持进行中。

## 2026-09-22 工作树整合（上一检查点）

主目录的 schema 23/24（组合内核、公共交互与目标策略）保留原语义。a148 工作树从旧基线并行扩展时占用了相同编号，整合后其状态授予、准备阶段强制分支、持久牌区、主动子伤害和主动拼点依次改用 schema 25–29／最低 rules 130–134；当前开发规则 epoch 为 134，经典包为 1.113.0。旧开发存档仍只接受当前 epoch，不新增历史执行器。

正式陷阵继续使用主目录已验收的 `ClassicCardActionSkillPrograms`，不以工作树内重复实现替换它；龙吟、拒战、统一目标授权以及 AI 自动推进提速均保留。工作树中的同类实现通过冲突合并保留其独立公共能力，旧副本完整保存在整合备份中。下文并行开发时期的编号和测试数字是历史记录，不代表整合后的验收；整合结果由本节记录。

整合版本已完成完整 Solution Release 构建（0 警告、0 错误）、Core 436/436、WPF 105/105。日志位于 `%TEMP%\CardWorktreeMerge-20260922-213617`：`build-tenth.log`、`build-core-final.log`、`core-final.log`、`wpf-full.log`。Core 与 WPF 测试实际引用的产品 DLL 哈希一致：Core `C2F0CA6427B5A58C136AA87C37CF28CAE651EE28871EFD88CCB934810DB4EFA8`，Content `FD77D9BA5674997C524A51667C6416D3F892BAF5BD7584709C5119EAACCB89D8`。初次整合失败日志保留，修复涉及新旧拼点结果校验、准备阶段分支分组、觉醒节点映射和真实牌区支付接口；未跳过用例或放宽牌区、回放断言。

用户本次要求合并并移除 `a148` 后恢复持续迭代。以本节为最新写权约定：原任务 `01a0c83d-1ad0-7d70-bd6c-2d6f8495b53c` 已经应用 handoff 迁回 `C:\Users\17917\Desktop\Card`，迁回后的“持续迭代”任务 ID 为 `01a0c97a-5138-77b0-ace5-119412ab465f`；恢复后只在主目录工作，不在已移除的目录构建或写入。主目录和工作树的原始文件、哈希与二进制 diff 已完整备份在同一 TEMP 根的 `main`、`a148` 子目录；安全 stash 另行保留。不提交、不推送。

工作树末尾的 `ChooseTargetBenefit` 尚未形成可运行的宿主和恢复链，本次不注册该未完成操作；草稿在 `a148/files` 备份内。恢复时继续按公共能力完成这一缺口，再组合迁移举荐，不复制人物专属路径。已有能力只维护组合定义、展示及差异场景；同一能力组集中构建验收，保留主目录的 AI 自动推进批处理与速度设置。

# 存量技能按公共机制迁移

2026-09-22。用户已授权安排迁移，并明确由现有“持续迭代”任务实施，架构任务负责设计、独立审查和验收。本文落实 [组合式规则引擎契约](COMPOSABLE_RULE_ENGINE.md)。第 0、1A、1B 批已正式验收，可按文末能力范围恢复自主内容开发；后续存量机制仍逐批迁移，不代表全库迁移完成。

## 分工与实施约束

- 本批主目录整合曾由架构任务独占写入；129 最终验收交接后由持续迭代接回产品写权。原“持续迭代”此前通过 app handoff 隔离到 `C:\Users\17917\.codex\worktrees\a148\Card`，目标任务 `01a0c83d-1ad0-7d70-bd6c-2d6f8495b53c`；此前隔离期间不得写原主目录的限制，由本次明确写权交接替代。交接时 17 个未提交文件均已在主目录逐文件按 hash 恢复。Sol 子代理只持有明确分配的文件，完成即交回。
- 用户已明确允许通过 `gpt-5.6-sol` 子智能体并行加速，不使用其他模型子智能体。当前按公共机制分配解析/宿主/测试文件；架构负责契约、三方合并和最终共享构建，不再按人物连续派一个完整改造循环。
- 产品按公共能力批次整合，独立纯模块和已交接的 UI 可并行准备。同类人物定义与差异场景成批维护，不能每个人物重新走一次框架、版本、UI 与全量检查循环。共享项目构建统一安排，避免竞争和混用产物。
- 沿用现有 `SkillProgram`、基础 handler、使用作用域仓和窄宿主接口。人物只提供默认值；新基础节点必须表达公共操作，不能按技能名路由。
- 保留所有现有未提交修改，不自动提交或推送。开发存档只接受当前规则 epoch，不为本次迁移增加历史执行器。已支持能力范围内的新武将开发现已恢复；CardUse B–D 按文末公共机制切片边界重新评估后推进。

## 上一验收：公共事件、支付、结果与状态（schema 24 / rules 129）

2026-09-22，已达到本批六项公共机制的验收终点。完整 Solution Release 0 警告/0 错误，Core 433/433，WPF 104/104；日志位于 `%TEMP%\CardPublicMechanisms129`，分别为 `solution-build4.log`、`core-full2.log`、`wpf-full2.log`。原始失败保留在同目录 first-run 日志中，不抹去整合问题。

不可变 DLL 快照 `%TEMP%\CardMechanisms129-Independent-d1d5ea91b65a4035a72b6a54a254f019`：Core SHA256 `4F3FB4B1BFD0917F60AA9D8F5F5BC13E2E823EEB50BE3B72028E8C6757CFB11A`，Content SHA256 `B2881C0056709F3D4A9D0B60A46A27B9281EB1492015B98878D25FBE69DF8B87`。`CardPublicMechanismsAudit129` 和 `CardDirectedPolicyAudit129` 使用该快照复测通过；`CardDirectedArmorAudit129/dll-129-final` 的源/副本运行前后 hash 与之相同，两组群体牌/火攻真实提交检查通过。装备和手牌的反射操作仅用于准备独立夹具，实际授予、用牌、响应均经公开命令；不把准备操作表述为玩家操作覆盖。

整合修复包括 Actor 普通效果目标、正式 Program 去掉 legacy 实现标识、响应/用牌物理牌边界识别、定向防具授权的群体与火攻消费，以及公共主动技能指南名称。序列化事实测试改为比较字典内容，未放松 JSON 往返、命令回放、物理牌归属或父帧身份断言。旧被动适配保留已有模板顺序，避免改变激将动作顺序与旧空城路径。

交接范围：已有能力只增加或迁移 rules/presentation、人物默认绑定和差异场景；公共能力不足则先明确缺失的上下文/节点/资源约束，集中补可复用节点，不新增 `GameEngine.<人物>.cs` 或同名 Core/AI/UI 分支。先完成同一能力组的定义再统一注册和运行必要检查，不要求清空全库旧实现才开发内容。不承诺未经实测的每人耗时。

本轮最终交接后，持续迭代可接回主目录产品写权；架构与两个 Sol 审查代理停止并写。`a148` 工作树保留未合入觉醒修改，不覆盖、不自动迁入本批。执行时明确主目录为 `C:\Users\17917\Desktop\Card`，不要从旧工作树构建混合产物。

## 上一整合检查点：统一节点契约（128）

schema 23 / rules 128 已实际合入主目录，保留全部交接修改。28 个既有操作统一 descriptor，主动和七个共享生命周期入口按能力与资源检查组合；主动判定、主动状态变更、跨入口选牌/赠牌与本回合授予均已进入真实引擎验证。12 组正式规则、36 条定义已批量接入，11 份重复资源 loader 收敛为公共目录；工具可直接查看能力与真实节点样例并加载校验组合。最终完整 Release 0 警告/0 错误，Core 433/433、WPF 104/104，日志、`summary.json` 和 `product-hashes-final.json` 在 `%TEMP%\CardUnifiedKernel128`。两组真实 AI 对局已补齐选定目标收益及已生效目标限制的组合回归。这已超出先前 TEMP 候选的三个入口，但仍不是全库迁移完成声明。

后续不能继续把单骑、自立等人物名字当作下一阶段工作单。应先按缺失机制归组：旧用牌/响应/判定的父流程适配、事件参与者与支付、拼点结果绑定、目标相关授权以及可持久化状态。已具备节点的部分交 Sol 成批维护组合数据与差异场景；缺口必须先形成公共上下文/节点契约，不得给每个技能包一个新接口类。

本批前存在的 `GameEngine.Juzhan.cs`、`GameEngine.GuanPing.cs`、`GameEngine.GaoShun.cs` 专属路径已删除并由公共机制实际替代。`GameEngine.LiuBiao.cs` 的通用势力计数此前已移至公共数值查询文件并删除空人物分区，自守此前已迁程序。

本批实施历史：两组 Sol 曾与主目录隔离，A 在 `C:\Users\17917\.codex\worktrees\card-event-programs\Card` 处理事件/参与者/支付/额度；B 在 `C:\Users\17917\.codex\worktrees\card-program-results\Card` 处理拼点/有向授权/持久状态。共同原始基线在 `%TEMP%\CardPublicMechanismsBase128-18d6269f778d451eabbde35605ac9742`，含 2838 个文本文件和 hash 清单；两个目录都已同步主目录的 68 个未提交路径。当时只在各自目录写入，根任务已审查三方差异并整合；两组共用 schema24/rules129，不逐节点升级。原 `a148` 工作树仍保留其被中断的觉醒修改，不覆盖、不合入本批。

### 剩余公共机制的统一完成契约

schema24 / rules129 已完成独立验收，证据见上文。正式龙吟、拒战、陷阵共用 rules/presentation bundle、当前实例身份、冻结事件事实、公共执行器、拼点子帧和状态展示；以下是实现与后续维护必须遵循的契约。

以下六项合为同一个公共机制目标；人物名称只用于行为对照，不作为宿主方法、执行器或新状态类型。先补机制再批量换定义，不能把每个角色再拆成一次 schema、AI、UI、全量检查循环。

1. **事件上下文与参与者绑定。** 用牌事件冻结 `CardActionContext`、父结算 ID、已付次数身份、有效牌型和公开颜色事实；候选来自本局当前技能索引，明确 Owner / Actor / Target / Observer 关系。候选保留 action/owner/skill/instance/binding：同一实例多授予来源去重，不同实例不能按 SkillId 折叠。事件参与者与动态选择结果均存为类型化、可序列化绑定；一个用牌事件只收集一次候选，方天等多目标共享事件身份。旧 CardUse/Response 窗口仅提供事实与父继续点，效果通过同一 ProgramSkillFrame 执行。新增通用 `CardUseCommitted` 边界保留真实支付和额度消费之后、流离改目标之前的时机；不能把它与 `CardUseTargetsFinalized` 合并。先忠实覆盖现有可达边界，不能把仅杀与直接闪電有入口的现状宣称覆盖全部牌型。
2. **选择与支付。** 一个公共“选择指定参与者区域牌并移动”节点描述 chooser、card owner、允许区域、张数和目的地；人类、AI、回放均使用同一合法候选。非己方暗手牌使用 opaque slot，禁止在候选里暴露真实牌 ID；提交前重验来源与可用性，先提交游标再移动。成功支付才执行依赖其结果的效果，不能用清空整区节点冒充支付一张。
3. **额度账本。** 消费键必须包含真实 CardActionId、消费角色、query 和作用域实例；撤销只接受该事件已经提交的 debit，且同一 debit 最多撤销一次。未计次的用牌不能凭空增加余额，多技能拥有者共享撤销状态。新节点不能直接减 `_slashCountThisTurn` 或把退款伪装成提高次数上限；人类、虚拟杀、激将与 AI 共用消费入口。
4. **有向临时规则。** 公共 grant 明确来源四元组（owner/skill/instance/binding）、actor、target、作用域与实例、牌型过滤，以及禁选、距离豁免、次数豁免或忽略防具的效果。目标限制优先于豁免，权限仅作用于匹配的 actor-target；不能把目标授权变成全局 Unlimited。`BypassSlashLimit` 允许向匹配目标在额度耗尽后继续使用，但实际用牌仍消费次数，不能因此给其他目标留下免费额度；不计次或退款使用第 3 项账本。全牌规则省略 cardKinds 表示 Any，显式列表表示子集，不在人物定义中枚举全部牌型。来源失效检查确切实例，不能被另一实例的同名技能保活。统一合法动作、提交复验、额度消费及护甲查询后再删旧专属查询。
5. **公共子结算结果。** `StartPindian` 以 ProgramSkillFrame 为父，复用已有拼点私密选牌、公开比较和收尾；结果绑定公开 source/opponent/双方点数/win，不让人物分支决定恢复路径。条件读取结果绑定，节点继续可发放规则或执行其他基础能力。游标在等待前提交，子帧只收尾和恢复一次。不可为接入而无条件放宽原有“Processing 必须为空”或父帧栈约束；需要验证父帧所属牌与子帧所属牌的明确边界。
6. **程序持久状态。** 状态 key 包含 owner、skill id、skill instance 和声明的 state id，显式初值、类型、可见性、重置作用域及失去/重新获得政策。本批先支持布尔状态和设置/切换，resetScope=game、reacquire=preserveUntilGameEnd；停用、移除来源和重新授予同一实例不会偷偷重置。状态条件必须控制触发候选和提示，不能只在效果节点里空跑；候选使用首次冻结事实，同一窗口翻转后不增补另一分支。技能条件与提示读取同一状态，不以名字保存阴阳分支，不把用途字符串编码成角色/目标数据库。先保持旧拒战的初值、切换成功点与动态拥有语义，再机械转换配方。

两组整合还须统一显式参与者引用 `ProgramParticipantReference(kind, resultBind)`。结果 source/opponent 必须指明已产生的命名结果，其他引用不得携带 resultBind；Actor/Observer 的多目标事件须先通过 `SelectTarget(eventTarget)` 选择，再消费 SelectedTarget。不能把“最后一次结果”或数组第一个目标当隐式参数。公开牌色是冻结事件事实，节点条件可据此分支；未知或混色不猜作红色。旧拒战目前位于贞烈之后，迁移时要保留这个父流程顺序或形成明确规则修正，不能静默前移到现有 TargetsFinalized 窗口。

验收终点（本批已达到）：相同基础能力能够表达拒战、龙吟、陷阵三个不同机制组合；这三者的专属候选/提示/AI/恢复/目标查询退出正式路径，且不使用这些技能 ID 的自定义组合通过实际提交、暂停回放、私密投影和父继续点验证。已有能力的新人物只增加组合、默认绑定、展示与差异场景；缺新能力时只增加可复用节点及其机制测试。未声称全库迁移完成。

## 第 0 批：三个组合样例收口（已验收）

当先、伏枥、称象分别验证插入阶段、自救和公开牌集选择，必须成为同一程序执行器的组合定义。

交付内容：删除 `GameEngine.LiaoHua.cs` 的专属流程；删除 `GameEngine.CaoChong.cs` 中称象专属实现、保留尚未迁移的仁心；清理对应专属决策、AI、UI、快照及恢复分支。不能保留旧实现作为新内容的隐含回退路径。

验收重点：动态授予/移除/禁用及多来源；候选身份和发动前重检；额外阶段结束后恢复正常回合；主动失去体力进入濒死后能恢复父程序；公开牌区与私人提示分离；空牌堆和无合法子集；取消只清理本帧持有的临时牌；AI、人类与命令重放一致。跨窗口组合场景证明节点不只支持三个固定配方。

架构任务在匹配产物 `%TEMP%\CardLifecycle116-lifecycle` 上独立验证：

- `%TEMP%\CardDecoupleAudit0922` 三项通过：任意授予来源生效、动态涅槃不依赖模板、国战双将暗置不抑制独立来源被动技能。
- `%TEMP%\CardComposedLifecycleAudit0922` 四项通过：亮牌后插阶段在加载期明确拒绝；成功结束清理本程序未消费临时牌；条件跳过牌集生产后依赖该绑定可控取消；AI 可选触发通过公共程序入口恢复执行。
- `%TEMP%\CardGrantIdentitySolAudit0922` 四项通过：模板单来源、同一技能实例多来源、移除模板保留其他来源、禁用模板保留其他来源均只产生一个候选并执行一次。该脚本现已改为正确行为断言，退出 0 表示通过。

完整整合产物 `%TEMP%\CardLifecycle116-full-c89ad9f141fc4062958f1fc24da7a3d8` 的 Solution Release 构建零警告、零错误，Core 377/377、WPF 104/104 通过。架构任务审看了廖化自救与称象公开牌集选择的渲染，并确认专属处理器、事件和决策枚举已退出。

最终契约检查复现 Event 限次缺少事件身份，已按有界修复在 schema 11 生命周期加载期拒绝；Game/Round/Turn/Phase 仍可加载，旧 Juzhan 独立 Event 路径保持不变。最终产物 `%TEMP%\CardLifecycle116-event-scope-c211ee5ee02b4666ae08b2285827b162` 完整构建零警告、零错误，生命周期专项 7/7 独立复跑通过。`%TEMP%\CardEventScopeSolAudit0922\Acceptance.csproj` 独立验证三个生命周期窗口：12 个已支持范围组合可加载、3 个 Event 组合全部拒绝。旧 `Audit.csproj` 保留修复前缺陷证明，不作为验收脚本。此加载约束小修后未重复全量 Core/WPF。

`git diff --check` 通过。仓库级 `dotnet format --verify-no-changes` 仍报告大量空白格式问题，不能宣称该项通过，也不为通过它格式化整个脏工作区。第 0 批验收允许进入第 1A，尚未恢复新增武将。

## 第 1A 批：统一数值规则查询（已验收）

目标是让距离、攻击范围、出杀次数等从当前状态和修正来源计算，命令执行、合法动作、AI、UI 使用同一结果。持续效果不伪装成触发器。

### 具体实施范围

1. 在现有 `SkillRuleQuery`、`SkillProgramModifier`、`SkillProgramRules` 上演进独立的只读查询服务，补齐 `AttackRange`，统一距离方向修正、杀次数上限、摸牌数与手牌上限的计算入口。查询不修改使用记录，不持有整个引擎。
2. 模式提供基础值，装备/当前有效技能/临时效果提供带来源的修正。攻击范围和角色间距离分开；次数上限与已用账本分开；距离豁免不能绕过目标禁令。
3. 明确覆盖、加减、下界和无上限的组合政策。覆盖冲突使用显式优先级，不能按技能 ID 或反射顺序取最后一个。同优先级冲突必须有确定的拒绝或裁决机制。无上限用显式结果表示，旧 `int.MaxValue` 仅可留在明确标出的外部适配边界，不能参与加减。
4. 将马术、义从、咆哮、宗室迁为正式组合定义，移除正式注册的 LegacyKind 路由。复用当前体力条件；宗室所需现存势力数采用公共只读数值表达式，不能新增 `ZongshiHandler`。用于无内容注册表演示的旧适配若仍保留，必须与正式路径明确区分。
5. 对横野、弓骑、将驰、血裔、不屈、权计等尚未迁移的状态生产者，列明过渡来源适配。它们先向统一查询提交贡献，后续在相应机制批次迁移生产者；不能继续让正式查询结果在返回后又叠加人物专属修正。保留既有规则行为，避免同一来源被计算两次。
6. 收口普通杀、虚拟杀、方天目标集、伏魂在 `GetSlashLimit` 之后追加天义的路径，将天义次数贡献纳入统一查询。主动激将的发动、目标选择及其他角色供牌成功后的复验也必须消费相同额度与目标授权。不得为本批顺带重写整个用牌流程。技能是否可发动、目标是否合法和是否计次数仍保持各自明确契约。

陷阵的越限与距离授权和指定目标相关，不是全局无限次数或攻击范围。将驰、程序转化来源等距离豁免也保留自己的用牌上下文；查询攻击范围的借刀、烈弓、解烦和 WPF 展示不能受其他用牌路径的豁免污染。人类提交、普通杀结算和 AI 已共享 `BuildLegalActions`，应保留这一消费边界。手牌提示已有合法动作优先；无动作时的失败解释应消费相同额度和目标检查，避免另算一次而把无目标误报为次数用尽。

接入前需列出旧计算的基础值与修正阶段。例如血裔当前在手牌上限的程序 Set 之前，横野、宗室、权计在之后；摸牌装备加值也在程序 Set 之前。统一入口不能无说明把全部旧来源改成 Set 后的 Add。冲突覆盖应在内容或状态接纳边界明确拒绝/裁决，不能直到对局查询时才意外崩溃。

正式英姿目前有可选发动语义，**不在本批直接改成常驻摸牌 +1**，留给摸牌计划批次。横野等的完整触发程序和持久牌区也不在本批一次改完。

### 可删除入口与验收

- `GetCombatDistance`、`GetAttackRange`、`GetTurnDrawCount`、`GetSlashLimit`、`GetHandLimit` 成为薄查询入口；专属数值拼接退出这些入口。`GetZongshiHandLimitBonus` 随宗室组合迁移删除；旧马术/义从/咆哮正式被动路径停止使用。
- 普通人物动态获得四个代表技能后即时生效；义从跨体力 2/3 边界、势力角色死亡导致宗室变化、装备变化、来源移除/禁用恢复均不读取模板旧值。
- 距离有方向性且自距为 0；武器范围与距离修正不混算；无上限加有限修正不溢出；多个 Set 的结果与注册顺序无关。
- 同一次用杀在合法动作、实际提交、AI 与公开投影中结果一致；当先额外阶段仍正确重置次数账本；临时修正按既有期限失效。
- 用一个测试内容定义组合范围、距离和次数修正，证明无需引擎按名字新增分支；不新增正式武将。

### 已裁决的接入契约

- 独立只读 `RuleQueryService` 不持有引擎；窄上下文提供当前体力、上限、手牌数、阶段、是否本人回合及现存势力数。结果区分有限/无限，冻结基项和修正贡献明细。`RuleQueryBaseTerm` 表达有来源的 Set 前基础项；旧整数 API 的无限值适配明确留在边界。
- 距离按存活座次和双方坐骑基础值 → 来源 Outgoing 修正 → 目标 Incoming 修正 → 最终至少 1 的两段策略计算，中间不先夹到 1；自距直接为 0。摸牌装备基础项在程序 Set 之前。攻击范围以武器打印值为基础，手牌上限以当前 HP/不屈/血裔为基础，其余贡献按明确阶段进入服务，不在返回后续加。
- schema 12 的新修正具有稳定 `id` 和显式优先级，Add/Unlimited 非零优先级拒绝；Set 暂限常量，现存势力数表达式用于 Add。Unlimited 只允许 SlashLimit、AttackRange 和已有带卡牌来源身份的 SlashDistanceLimit。新增语义进入 gameplay hash，沿用旧 schema 定义时不无故改变其 hash；规则 epoch 随语义变更更新，只接受当前开发存档。
- 贡献身份区分拥有者、SkillId、SkillInstanceId 和 modifierId；同一技能实例的多授予来源先合并，不同实例保留。装备使用物理牌 ID 与属性，临时状态包含拥有者、技能及 usage ID。默认模板不作为当前拥有关系来源的替代。
- 在注册表冻结前保守校验跨程序 Set 冲突：同 query/方向、同 priority 的不同值若不能证明条件互斥就拒绝，要求作者明确优先级；同值可合并。检查潜在较高优先级条件失效后出现的冲突，不能仅检查初始状态命中的最高项。技能授予/恢复必须在接纳前校验，不能先污染角色再报错；查询保留不变量防线。
- 当前注册的 `standard:mashu`、`standard:paoxiao` 与 classic/sp 同类 ID 一起转为正式 Program 定义，避免默认/国战仍走旧正式路径；无注册表 demo 的旧适配可以单独保留。组合配方可复用，不复制引擎规则。
- `CanSpendSlashUse` 统一额度和按目标的授权；忽略次数参数只接受经过校验的动作来源。禁止用牌、禁止目标和是否计数各自保持语义，激将同时验证天义/陷阵失败等禁杀约束。原账本及当先额外阶段的重置保持不变。

三个实施切片：先落服务、基项、上下文、schema 和定向机制检查；再接五个薄查询入口与全部额度消费点；最后迁正式组合定义、删除旧正式路由并整合回归。上述内部设计检查已通过，不再等待用户审批。

第一切片曾在 `%TEMP%\CardRuleSlice1b-a64956cf6e1f4b3dbe0c1f6322deb9ac` 通过 10 项检查，但独立 `%TEMP%\CardRuleServiceAudit0922` 检出距离中间转 int 与 long 加值回绕问题。现已由单一 `ReduceWide` 核心修正，保留双阶段输入/输出与来源快照；最终匹配产物 `%TEMP%\CardRuleSlice1d-5ac810d004844b92a00c70b9d1ff4203` 的 10 项专项独立复跑通过，独立三项数值审查全部通过：跨方向极值抵消还原 2、Incoming Set 得到 5、long 溢出明确拒绝。第一切片的数值阻断已关闭，可进入第二切片。

Sol 对 schema/hash/静态冲突的限定复核通过；其发现的标识拼接碰撞也已修正，来源现在包含 owner seat 和采用长度前缀的 SkillId、InstanceId、modifierId。`%TEMP%\CardRuleIdentityAudit0922` 支持 `AuditDllRoot`，现为正确行为断言；针对最终 1d 产物独立重跑全部通过：两个贡献均保留、合并值为 7、不同拥有者身份不同。旧 schema 哈希路径、新字段进入新 schema 哈希、各优先级 Set 冲突与有限互斥证明未发现其他问题。

第二切片固定产物 `%TEMP%\CardRuleSlice2-build-81000c129bd3479792571e4bbbcc44c1` 完整 Solution Release 构建零警告、零错误，实施任务 Core 388/388 通过。五个入口已接公共服务，天义追加集中成来源贡献，陷阵额度保持逐目标授权。Sol 对普通/虚拟/方天/伏魂/丈八/激将供牌前后、禁令、怒斩可信不计次来源和提示路径只读审查未见可达绕过。架构独立数值审查四项通过，其中 Unlimited 阶段现在显式 `IsUnlimited=true` 且 `OutputValue=null`，旧解释占位问题已关闭。

允许直接进入第三切片，不为批次边界另建半完成 epoch。版本 117 与 schema12 正式内容定义同时整合；第二切片的 epoch116 结果只验证已有内容迁移，不能替代新版定义实际进入引擎。最终仍需一次完整 Core/WPF 整批验收。Sol 已交付独立 `%TEMP%\CardRuleEngineCompositionAudit0922`，只引用匹配 DLL，不构建仓库；架构任务在首版 117 产物 `%TEMP%\CardRuleSlice3-build-9e8af54e33f8486b969ec5d6061e1852` 独立运行四组实际引擎断言全部通过：任意授予来源、同实例多来源去重和移除/禁用恢复、不同实例叠加、HP 条件下双向距离变化、现存势力变化与模板保持不变。该首版仍有实施任务报告的 10 项 Core 差异待修，不能作为最终整批验收。

第三切片收口审查指出的两项已在源码复核并由最终产物验证：宗室专属 `GetZongshiHandLimitBonus` 与查询中的对应 Add 已删除；历史依赖选择已采用固定 `1.98.0` 迁移边界，避免下一次包升级改写显式旧版本工厂的依赖。现有历史包描述与签名检查不构成增加历史规则执行器的理由。测试也已区分固定能力引入版本与当前 epoch，不要求所有旧程序的 `MinimumRulesVersion` 随每次 epoch 升级。

独立 `%TEMP%\CardRuleBaseStageAudit0922` 已由 Sol 在首版 117 产物运行两项实际引擎检查：当前 HP 基项在程序 Set 之前，HP 从 5 改为 2 后 Set5 + Add2 仍为 7，禁用后恢复 2；武器打印范围 4 在程序 Set 之前，Set5 + Add2 为 7，禁用后恢复 4。未覆盖血裔或玉玺的实际内容场景，不据此扩大验收声明；最终匹配产物仍需复跑。

架构任务随后从修正版 `%TEMP%\CardRuleSlice3-8c0657c3d631458590c441a7b7e1cb86` 复制 Core/Content DLL 至独立 `%TEMP%\CardRule117-Independent-fcfab569b69c475f98009f90117b3795`，复制前后源文件及副本 SHA256 均一致。五套 TEMP 验收全部退出 0：数值服务四项、来源身份、schema12 实际引擎组合四组、HP/武器基项两项，以及 `CardQueryConsumptionAudit0922 -- --expect-fixed` 的天义与激将合法动作和真实提交。Core SHA256 为 `F2E1833A3D079BB0D146BFA67AD5B308787E54DB2E89AE8BEAC28352AC7A1AC8`，Content 为 `AE737CE9EC26D833D4F4DCAEEDC1A9182A424DBCD4EF7317216393E6F265A0A7`。最终产物若相同无需重复上述检查；实施任务已报告 Core 391/391，尚待完整 WPF 与最终产物一致性确认，整批仍未正式放行。

完整 WPF 随后检出确定性阻断：`--filter=playback` 独立复跑同样失败，`PlaybackChecks.FindSpectator` 长局触发 `A resolved BarbarianAssault left Processing through an unsupported destination: DrawPile.`。实施任务负责复现与产品修复；Sol 查询审查代理只读核对组攻收尾和牌区所有权链，不能并写或重复启动 WPF。源码线索是处理中不变量已有 `group.DamageCardClaimed` 的 Hand/DiscardPile/DrawPile 续接许可，组攻最终收尾却更严格；这只是待证实的根因线索，必须用具体物理牌的获得、再次离手、重洗事件链确认，不能仅放宽当前位置判断或跳过长局验收。修复后需该真实复现与牌区/单次收尾检查、完整 WPF，并按改动补必要 Core 验证，再决定 1A 放行。

Sol 只读核查已完成：源码存在伤害牌被奸雄/反馈合法领取、持牌者死亡弃置、后续摸牌触发重洗的可达链；已有处理中校验正是为此以 `DamageCardClaimed` 约束放行。修复应保持合法领取身份条件，未领取牌出现在 DrawPile 仍须拒绝，五谷/桃园的收尾不随之放宽。该审查不是本次 WPF 故障的运行时事件证据；实施任务仍需从固定复现确认同一实体牌的链路、目标不重复和单次收尾。

实施任务已取得实际故障链：南蛮 #53 从 Processing 经反馈进入 Hand[6]，角色死亡后弃置，再经 `deck.reshuffle` 进入 DrawPile；后续目标仍在原群体结算中。首版修复改为逐物理牌的 `DamageClaimedPhysicalCardIds`，但架构审查发现登记点仍以 `group.Card.Id == damageCard.Id` 限制，漏掉乱击等多牌转化的其余物理牌，已要求按同 ResolutionId 与群体物理牌成员身份逐张记录。新增回归的领取/重洗/完成证据必须限定到同一次用牌的时间区间与身份，不以整局同 CardId 的最后事件或累计次数替代；负例需验证真实收尾入口仍拒绝未领取牌回 DrawPile。产品修复和测试仍由实施任务持有，等待有界修正及匹配构建验证。

Sol 已完成激将/天义的真实消费差异复现：原 epoch116 基线 seed2、真实天义胜利且杀账本为1时，普通杀有10个合法动作、主动激将为0。工程 `%TEMP%\CardQueryConsumptionAudit0922\Audit.csproj` 支持 `AuditDllRoot`；默认模式是缺陷复现断言，退出0不代表验收。附加 `-- --expect-fixed` 为修后断言，现已在上述第二切片匹配产物独立通过：普通杀10个、激将1个，选定合法目标提交接受。注意 UseSkill 的候选在 `SelectableTargetSeats`，不是空的 `TargetSeats`；最初新增的验收命令误用后者导致拒绝，已修正夹具，未放宽产品校验。此检查不替代供牌后结算、陷阵目标授权或完整回放。所有 Sol 产品写权均已交回。

### 第 1A 最终验收结论

固定产物 `%TEMP%\CardRuleSlice3-8c0657c3d631458590c441a7b7e1cb86` 完整 Solution Release 零警告、零错误，Core **392/392**、WPF **104/104**，架构任务已审看对应实际构建/测试输出。Core SHA256 为 `F61EB73B1345BA3098CEAD8922CAC8D9AC3A4092238F73A3E056DC0540C14311`，Content 为 `DEDF46216596438722B08585F790955A8DD179E14A7EF071A8DC13BF01874E23`。

组攻收尾阻断已关闭：领取登记按同一次结算及物理牌成员身份逐张记录，DrawPile 仅允许对应已合法领取的牌；五谷和桃园收尾未放宽。WPF 原旁观场景通过，并在同一 CardUse 的声明至完成区间核对领取、死亡弃置、重洗、每个目标一次、完成一次及无重复弃置/巨象领取。架构额外建立 `%TEMP%\CardGroupFinishAudit0922`，直接调用真实 `FinishGroupAttack` 验证未领取牌在 DrawPile 会被拒绝，拒绝前后没有牌移动或完成事件，不只验证提取的布尔 helper。

架构保存的最终 DLL 快照为 `%TEMP%\CardRule117-Final-0dddb017b5574769af701aa7f8f39d7b`，SHA256 与上述交付一致。该快照上的真实收尾负例、schema12 实际引擎组合四组、HP/武器基项两项、天义与激将真实提交全部独立复跑通过。纯数值极值和来源身份独立检查已在本批前序匹配产物通过，此次有界组攻修复未改变相应代码，未重复这两套独立检查。`git diff --check` 通过；仓库 format 既有空白问题仍不声明通过。

第 1A 正式验收通过，允许持续迭代任务立即按下述契约实施第 1B 第一切片。当前数值能力已经可由内容组合，但恢复新增武将仍待 1B 验收与明确能力交接；本结论不表示全部旧技能已迁移。

## 第 1B 批：阶段窗口（已验收）

1A 已验收，实施任务持有全部产品写权，可直接按有界切片推进，不再等待批准。第一切片先补公共阶段窗口、触发级结构化条件和精策组合；随后收口据守、闭月与结束阶段协调。新增触发语义和状态设置原语应进入 schema/runtime/hash 与下一规则 epoch，可按 1B 整批统一演进，不要求每个内部切片独立升级，也不要求旧开发存档兼容。

- 精策当前是每个出牌阶段一次机会，本回合用牌数按 `TurnStartedEvent` 之后的 `CardUseDeclaredEvent` 累计。`EnterPlayPhase` 每次重置 Phase 使用仓，因此当先的额外 Play 和普通 Play 可各一次，第二次阈值仍含额外阶段用牌。普通 Play 被跳过则没有第二个 PlayEnding。不要误改成每回合一次。
- 触发级条件要在发布可选提示前判断，不能只给 Draw 加条件。公共上下文提供本回合用牌数、当前 HP 和模式资格，不增加 `JingceCondition` 或技能名分支。窗口帧保存触发时的事实与稳定边界身份，提示/继续步骤保存所用事实；重新执行前重验当前技能拥有关系。候选游标保证跳过和完成都不重复询问。
- 数值比较使用有界公共 `Compare(left, operator, right)` 条件：操作数先支持整数常量、`CardsUsedThisTurn` 与 `CurrentHp`，比较符采用明确枚举；精策声明前者大于等于后者。不得把整条比较固化成 `CardsUsedThisTurnAtLeastCurrentHp` 一类配方枚举。操作数与比较符进入 schema 校验和 gameplay hash，同一节点通过字段对常量及不同比较方向的场景验证复用；不扩张成任意脚本表达式。
- 本批显式触发条件只开放给已冻结事实的阶段/回合边界：`TurnStartBeforeNormalFlow`、`PlayEnding` 及随后实现的 `TurnEnding`。`SelfDyingResponse`、`AfterDamageApplied` 尚未提供相同的事实快照，加载期拒绝显式 condition，原省略条件的程序保持行为；不为本批扩大伤害/濒死上下文改造。尤其不能把非当前行动者找不到本回合开始事件时的整局用牌计数当作本回合事实。
- PlayEnding 的父窗口耗尽后依次恢复已插入阶段、否则进入弃牌。精策可选提示和条件不满足的路径都必须到同一继续点；不依靠递归重入重新收集。
- 保留旧 `IPhaseSkillModule` 消费者时，它们的 PlayEnding 完成回调必须进入公共 Program 窗口之后的继续点，不能重新调用收集入口。验证可选 Program 跳过后与旧阶段模块共存也只询问一次，并继续处理其余旧模块。
- 据守当前是 Draw3 后 `IsFaceDown=true`，现有 `TurnOver` 是 toggle。本批保留已有行为，补公共“设置正反面状态”原语；不新增据守 handler，不把先因伏枥翻成背面的角色再翻回正面。此组合场景必须有行为验收。
- 当前结束顺序为醇醪、秘计、据守、举荐、闭月。据守和闭月进入同一个 TurnEnding 语义窗口时，按下述边界队列保留未迁移流程的相对次序，独立程序执行器不认识这些技能。不用两个按技能拆出来的伪窗口绕过，也不为本批扩大举荐迁移。
- 结束窗口耗尽后进入明确的回合最终收尾：临时效果过期、一次 `TurnEndedEvent`、切下一角色。旧异步流程回来时恢复边界游标，不重开已处理窗口。翻面跳过整个回合时沿现有跳过路径，不额外触发阶段技能。
- 删除据守标志/选择/提交/AI/UI 分支，以及闭月、精策内容模块和专属决策路由；仍被其他阶段拼点/用牌夹具使用的公共 `IPhaseSkillModule` 不整套删除。简雍的“纵适”是独立拼点结果模块，不能与刘表“宗室”或阶段模块混淆。

定向验收覆盖同一角色同时持有三项、当先额外与普通阶段、跳过阶段、动态失效/多来源、可选提示命令重放、先翻面再据守、与未迁移举荐共存、回合只最终结束一次。展示沿通用私密程序提示与公开状态投影，不新增三套 WPF 实现。

第一切片早审已纠正固定比较配方与旧 PhaseSkill 回调重开 Program 窗口的问题，并收紧显式条件可用窗口。Sol 对 Compare 的字段/枚举/重复属性校验、求值映射、schema/runtime/minimumRulesVersion 及 gameplay hash 完成限定只读审查，未发现其他问题。

第一切片匹配产物 `%TEMP%\Card1B-PlayEnding-build6\bin\CardGame.Core.Tests\release` 的 Core.Tests Release 构建零警告、零错误，生命周期 **8/8**、精策 **5/5**，架构已审看实际输出。当前正式精策注册为 schema13 Program，`JingceModule` 与专属决策、事件及 AI/UI 路由已删除；旧 SkillKind 的名称元数据和显式旧包描述保留，不提供历史执行器。定向结果覆盖本回合计数与当前体力、条件不成立无提示、来源去重/不同实例、提示后失效，以及当先额外和普通出牌阶段两次机会。

架构复制前后核对 SHA256 一致，固定 DLL 快照 `%TEMP%\Card1B118-Independent-1372d28098504fdc9682a25cb23f9915`：Core `C1CC968CF07A388D652F7449EE726C11B068CC8D6CB9C7CEC0E5E54816D4EC25`；Content `F2872CE4D66F8F1F2030E07DDF56A36DA274F65D26F436E0CCCDDEC24524399C`。独立 `%TEMP%\CardPlayEndingAudit0922` 通过 `AuditDllRoot` 引用该快照，两组实际引擎审查全部通过：前项失去体力后后项仍使用冻结窗口事实、可选 Program 跳过后两个旧阶段模块不会重开窗口、假条件不提示，并验证私密投影、帧 JSON 和暂停命令重放。TEMP 夹具最初的 C# 原始字符串括号错误已修，未作为产品问题；该独立工程不构建仓库。

第一切片复核通过时放行据守/闭月后续切片，当时整批尚未验收，并要求补足普通 Play 被跳过时不产生第二次 PlayEnding/精策机会的负例，再集中完整 Solution/Core/WPF。该负例和最终整体验收现已通过，结果记录如下。

### 结束边界协调的具体裁决

采用引擎协调层的可序列化有序边界帧，保存 `Id/OwnerSeat/TurnNumber/Facts/Items/ItemIndex/Step`。`Facts` 是首次收集时冻结的公共触发条件事实；各 Program 项的提示、回答与继续均使用该快照，不能在恢复时重新 Capture。当前技能拥有关系仍实时重验，旧举荐的支付牌和目标仍在抵达桥项时计算。工作项只有公共程序候选与明确受限的旧流程过渡项；本批过渡项仅用于仍未迁移的举荐。该内部枚举/分派不进入 SkillProgram JSON、条件或基础 handler，也不成为新增技能扩展方式。

醇醪、秘计保持现有前置流程。其后首次创建边界帧并冻结一次程序候选，将举荐过渡项置于据守与闭月之间；排序由内容显式 priority 加过渡项固定排序位置决定，不能依赖模板技能列表或反射顺序。程序子帧继续消费同一个 `TurnEnding` 窗口，执行前重验拥有关系、存活、定义身份和使用限制。

抵达举荐项才计算其可支付牌和目标，据守刚摸到的牌必须可用。调用旧入口前先记录等待该过渡项；旧举荐跳过或完成回到 `EndTurn` 时，顶部优先恢复已有边界帧并推进一次游标，不重复醇醪、秘计或候选收集。现有举荐提示不要求空解析栈，因此父边界帧可留栈；新增 invariant 验证父帧身份、回合、对应的待处理举荐和子帧关系。若实现中发现其他全局栈约束冲突，先给出具体证据再调整，不能直接取消这些 invariant。

所有工作项耗尽后先移除父帧，再调用独立 `FinalizeEndTurn` 一次。角色死亡或对局结束按已有终止政策停止剩余项，不伪造额外回合结束。帧不得持有委托、引擎引用或 UI 对象；当前 Checkpoint 仍是配置加命令前缀，通过重放重建这些暂停状态，不声称已直接持久化 `_pendingJujian`。新增帧可序列化与命令重放一致分别验证。

TurnEnding 首版早审曾发现候选初筛用首次事实、恢复时却重新 Capture；源码现已将 `Facts` 保存至父帧，创建、提示与回答使用同一快照，该项已关闭。匹配 `%TEMP%\Card1B-TurnEnding-build4\bin\CardGame.Core.Tests\release` 的 Core.Tests Release 构建零警告、零错误，现有据守/闭月定向 2/2，架构已审看输出；此时实施任务仍在补新边界专项，不能据此验收整批。

Sol 仅在 `%TEMP%\CardTurnEndingAudit0922` 准备独立 DLL 引用工程，架构在固定快照 `%TEMP%\CardTurnEnding118-Independent-dc4b31d8c283454290bf8129c11afc52` 实际运行两组均通过：同人持有精策/据守/举荐/闭月，按据守 Program → 新摸非基本牌支付旧举荐 → 闭月 Program 执行，暂停命令重放状态/事件一致且 TurnEnded 只增一次；通过真实苦尽、濒死与伏枥命令先翻为背面，再发动据守保持背面。快照复制前后 SHA256 一致，Core `48491DC65519D13AAFCBF4AA18827E99F9B325ADC85CE2E3D4163C49C9410219`，Content `61CF61D5764F78742DB7D19476A033234BCC4A455542B49FCFEE61CAB2DC4572`。独立 TEMP 编译有两条未使用常量警告；运行退出 0，不将该工程描述为零警告。自定义组合人物和固定牌堆用于验证机制，不证明正式将池或牌堆平衡。普通 Play 跳过、动态失效、冻结事实的新增边界专项及整批 Solution/Core/WPF 仍待最终产物收口。

后续 `%TEMP%\Card1B-TurnEnding-build6` 的 Core.Tests Release 构建零警告、零错误，精策 **6/6**、生命周期 **8/8**、结束阶段 **6/6**。新增普通 Play 跳过负例已通过：准备时将原生乐不思蜀移入判定区，之后沿真实判定、跳过阶段与回合结束路径，确认当先额外阶段后只有一次 PlayEnding/精策及一次 TurnEnded；该夹具不证明乐不思蜀的出牌提交路径。结束阶段新增专项覆盖同源去重、不同实例、提示后失效、冻结事实及组合顺序，源码和实际输出均已审看。`%TEMP%\Card1B-TurnEnding-wpf1` WPF.Tests Release 构建零警告、零错误，闭月通用提示专项 **1/1**。上述待补边界现已关闭，实施任务可直接集中完整 Solution/Core/WPF；整批尚待最终匹配产物与完整结果，不重复已通过的无变化检查。

### 1B 完整验收与修复记录（已关闭）

完整 Core 在 `Card1B-TurnEnding-final1` 出现 `Ice Sword AI uses private opaque target-card choices and replays` 失败，单独 `--filter=Ice Sword AI` 可复现。架构独立 `%TEMP%\CardIceSwordAudit0922` 引用该匹配产物，重跑相同旧包 1.56 与有限 seed 场景，确认实际错误在 seed17 的 StartGame 自动推进中：`BeginQiangxiDamage → ApplyAttackDamage → TryBeginTianxiangChoice → PushResponseWindow → SetCardUseStep`。现场只有 ActiveSkillFrame #35（强袭，来源 2、目标 3），PendingAttack 的 Card 为 null、IsActiveSkillDamage=true；天香误用要求 CardUseFrame 的响应入口，不能因测试名归因于冰剑。

实施任务负责有界产品修复，并验证天香发动、跳过及转移后对真实伤害父流程的续接；不跳过 seed、不改宽原冰剑断言、不放松 SetCardUseStep 的类型不变量。独立 TEMP 当前退出 1 是有效缺陷复现，尚未代表修后验收。等待修正后的必要定向与完整 Core/WPF 结果、最终匹配产物，再放行 1B 与自主内容交接；其余已通过机制不要重复派修。

后续 build9 已将响应父帧状态更新改为显式接受 CardUse、ActiveSkill、Judgment、ProgramJudgment 四类，天香回答使用同一入口；原 SetCardUseStep 类型约束保留。原 Ice Sword AI 定向 1/1、TurnEnding 6/6 已通过。架构复制前后校验得到独立快照 `%TEMP%\CardTianxiang118-Independent-acf6e82320204c7cb15ac0c9edf2a4dd`，Core SHA256 `006A03E8470A277E5A351F3BDF5726C0162193501D085EB4F88A827915E4A13D`、Content `790EDF849AFD3A7FA217011658EFA4537E61D82A4DA61508CF2CF65BB3ECEA35`；同一 CardIceSwordAudit0922 实际运行退出 0，seed17 已可完成原自动推进并验证 checkpoint 状态重放一致。新增强袭→天香跳过/转移专项及最终完整结果仍待收口；新夹具须读取目标座位的私有提示，不将公开 State 隐藏 AI 提示误报为产品丢失提示。该次独立复核仅关闭原崩溃复现，不能替代新增差异场景和完整验收。

final2 完整 Solution Release 构建零警告、零错误；新增强袭→天香回归 1/1、闭月通用提示 1/1、包含曹仁精确背面文案的多技能 WPF 检查 1/1 均已审看实际输出。强袭差异场景读取目标座位私有提示，在暂停点重放后验证跳过/转移、接收者伤害与补牌，以及按同一 ResolutionId 仅一次 ActiveSkillResolved。架构最终固定 DLL 快照 `%TEMP%\Card1B118-Final-ac44cd332e9a482dac9a6a1f39537d48`，Core SHA256 `9E5336FFC4560768D7541BFD17E39A9EFDF2CBEEC62EBD9B4BF85E487CD6197D`、Content `D0745C788D52EA8024755F3940D2751692E2F3803C0560D6D6517017AB448722`。该快照实际复跑 CardPlayEndingAudit0922 两组、CardTurnEndingAudit0922 两组均退出 0；后者仍只有 TEMP 未使用常量的两条编译警告。

**第 1B 批正式验收通过。** 同一 `%TEMP%\Card1B-TurnEnding-final2` 产物的完整 Core **400 passed / 0 failed / 0 skipped**，完整 WPF **104 passed**；架构核对了两次实际完成记录及退出码 0，而非仅引用任务口头汇报。Core.Tests 与 Wpf.Tests 输出中的 Core/Content DLL SHA256 均与上述固定快照相同。专属精策/据守/闭月路由已实际删除，内部旧举荐桥仍明确受限，不能作为新内容接口。强袭天香与曹仁 UI 两个全量失败点现已关闭，`git diff --check` 通过；未运行或声称仓库级 format 通过。结合第 0、1A 批结果，已有能力允许恢复组合式自主内容开发；架构任务完成本轮审查并交回两份架构文档维护权，不要求全库重写后才继续。

## 第 2 批：牌移动完成（已正式验收）

rules v119／schema 14 新增 `CardsMoved` 公共窗口。`MoveCard`、`MoveCards`、`MoveAllCards` 在物理操作与同步装备钩子完成后提交不可变原子批次，记录父规则帧、直接父移动批次、来源牌区移动前后数量及有序移动记录；木牛流马粮草转移作为独立子批次关联外层装备移动。窗口只在无待决策、空结算栈且未终局的安全边界串行消费，候选按优先级、技能／绑定／实例及出现序号稳定排序，并在提示回答或强制执行前复验存活、当前技能实例、定义 hash、条件和使用额度。

枭姬当前定义选择 `Equipment + perCard`，同一批失去两张装备会形成两个独立机会；连营选择 `Hand + perBatch`，只在冻结的来源区数量由正数变为零时形成一次机会。两者均使用通用 `ProgramTrigger` 与共享 Draw handler；`_pendingXiaojiTriggers`／`_pendingLianyingTriggers`、两种专属 `DecisionKind`、专属提示／AI／恢复分支、专属移动 reason 和 `EquipmentLossSkillResolvedEvent` 已删除。WPF 继续消费通用程序提示，没有新增技能名分支，也不订阅 `EventCommitted` 审计通知执行规则。1.99.0 保留历史内容定义与指纹，1.100.0 发布 schema 14 程序；移除的历史执行路线不复活。

机制专项验证同一两牌 `MoveCards` 冻结两个逐牌候选和一个逐批候选、来源区 2→0、帧 JSON 往返、首提示 Checkpoint 恢复、三次执行状态／事件一致、提示后技能实例失效安全跳过，以及木牛流马嵌套批次的直接父 ID。该检查发现并修复了只读 `CardLocation` 反序列化为默认牌堆位置的问题，现以显式 JSON 构造契约保留牌区。完整 Release Core **403 passed / 0 failed / 0 skipped**，完整 WPF **104 passed**；Solution Release 构建零警告、零错误，`git diff --check` 通过。原裸衣场景曾以相邻 ResolutionId 推断伤害修正归属，新增移动批次合法消费 ID 后已改为按技能、来源、目标和修改前后数值匹配，不改变实际伤害规则。

**第 2 批正式验收通过。** 当时已放行第 3 批伤害后复杂选择；`CardsMoved` 首片仍只开放一个拥有者来源牌区和拥有者摸牌效果，其他牌区、目标和效果需按新公共能力另行扩展，不能绕过加载器白名单。

## 第 3 批：伤害后的复杂选择（已正式验收）

rules v120／schema 15 把 `AfterDamageApplied` 接入公共程序窗口。候选以稳定的技能／绑定／实例身份冻结，并显式声明 `perDamage` 或 `perDamagePoint`；程序执行前继续复验存活、技能实例、定义 hash 和使用额度。新增公共原语包括取得仍在处理区的全部伤害实体牌、从伤害来源的暗手牌位或公开装备中选一张、绑定摸牌结果、把一张绑定牌交给其他存活角色、选择手牌少于体力上限的存活角色，以及在执行时计算 `targetMaxHpMinusHandCount`。暗手牌只发布不透明槽位，暂停点只保存可重放的数据帧、绑定牌 ID 与来源位置。

第 3 批验收时的 `standard@1.13.0` 发布标准奸雄、反馈、遗计、节命的 schema 15 定义，`standard-classic-generals@1.101.0` 发布对应经典定义；1.12.0／1.100.0 保留原内容定义和指纹，但移除的历史执行路线不复活。标准奸雄／反馈与经典奸雄按每次伤害取得伤害实体牌；经典反馈按每次伤害从来源手牌或装备区选择一张；标准／经典遗计、节命按每点伤害各形成一个有序机会。遗计摸到的两张牌以私有绑定保存，并允许保留全部或交出其中一张；节命的补牌数在目标确认后按实时手牌数计算。

`CreateFeedbackDecision`／`CreateYijiDecision`／`CreateJiemingDecision`、三套提交分派、Human／AI 继续函数、专属 AI 评分入口、WPF 决策分类和 `IPassiveSkill` 伤害效果实现均已退出；四个对应技能类只保留稳定 `SkillKind` 身份与名称。旧公开 Human 方法仅保留为明确抛错的兼容壳，旧 `DecisionKind`／移动 reason／事件记录仍是历史序列化数据契约，当前引擎和 WPF 没有生产或消费这些专属路线。

机制专项覆盖标准伤害牌取得与跳过、经典来源暗手牌／公开装备选择、遗计绑定摸牌与跨座位交牌、节命动态表达式、每次／每点候选、私密投影、提示中 Checkpoint 恢复及事件／状态一致。组攻牌被通用程序取得后又因死亡弃置、洗牌并继续原结算的确定性边界仍由独立 `GroupClaimChecks` 覆盖，不再依赖 WPF 随机长局夹具。最终当前工作区完整 Release Core **395 passed / 0 failed / 0 skipped**，完整 WPF **104 passed**；Solution Release 构建零警告、零错误。Core 总数从第 2 批的 403 调整为 395，是八个旧专属伤害路径测试退出注册后的基线变化，不表示通用场景覆盖回退。

**第 3 批正式验收通过。** 当前可继续第 4 批摸牌计划；伤害窗口只开放已经过 schema 15 校验的上下文、目标、牌集与效果，不把任意事件订阅或脚本执行开放给内容。

## 第 4 批：摸牌计划（前六切片与绑定索引已正式验收）

rules v121／schema 16 新增有界 `DrawPhaseStarting` 生命周期窗口。首片只允许固定数量的 `draw owner` 效果：候选按技能／绑定／实例稳定排序，强制程序直接执行，可选程序复用通用 `ProgramTrigger` 私有选择，全部候选完成后才执行且只执行一次正常摸牌。父帧同时保存延后到摸牌后的跳过出牌阶段标记，Checkpoint／Replay 不依赖旧英姿专属状态。

当前 `standard@1.14.0` 发布强制的 `standard:yingzi` schema 16 定义，`standard-classic-generals@1.102.0` 发布可选的 `classic:yingzi` 定义；1.13.0／1.101.0 继续保留历史内容定义和指纹，但不会复活已删除的专属英姿决策、AI、WPF 分类或被动摸牌 modifier。专项验证覆盖 schema／rules／包边界、标准自动加摸、经典发动／跳过与暂停恢复，以及两个强制加量技能按稳定次序组合后只进行一次正常摸牌。

第一切片有意接在既有再起、自守、将驰、双雄、突袭、裸衣等专属替代／改量流程之后：一旦旧流程接管或替代摸牌，就不再打开本窗口。因此它只验收“普通摸牌前的可组合加量”。该历史切片完整 Release Core **398 passed / 0 failed / 0 skipped**，完整 WPF **104 passed**；Solution Release 构建零警告、零错误。

第二切片以 rules v122／schema 17 为同一窗口加入显式 `drawPhaseMode`：加量候选继续组合，成功完成的替代候选把父帧标为 `NormalDrawReplaced`、终止剩余候选并抑制正常摸牌；可选替代若跳过，则继续后续加量候选和一次正常摸牌。新通用节点 `selectTargets` 只允许一至两名有手牌的其他存活角色，`takeRandomHandCardFromSelectedTargets` 从每名已选目标随机取得一张暗手牌；目标集合在选择和执行时复验，公开事件只含目标座位与数量。

`standard-classic-generals@1.103.0` 把 `classic:tuxi` 迁入该 schema 17 配方，优先级 100，排在英姿等加量候选之前。突袭的专属 Core 状态、提交／AI 路由、`IPassiveSkill.CanReplaceDrawPhase`、WPF 分类和专属事件消费均已退出；`DecisionKind.Tuxi` 与旧事件／移动 reason 仅保留兼容数据。1.102.0 保留历史定义和指纹，但不会复活旧运行器。专项验证覆盖 schema 降级／目标上限／效果序列拒绝、发动与两段私有提示、伪造输入、两个暂停点恢复、随机暗手牌移动、历史包不复活，以及“突袭完成压制英姿与正常摸牌／突袭跳过继续英姿与正常摸牌”的组合政策。当前完整 Core **400 passed / 0 failed / 0 skipped**、完整 WPF **104 passed**；Solution Release 构建零警告、零错误。

第三切片以 rules v123／schema 18 扩展同一替代窗口：`OwnerLostHp` 在发动时决定亮牌数量，`FilterBoundCards` 按花色把冻结牌集投影为命名子集，两个 `MoveBoundCards` 分别将红桃与非红桃送往弃牌堆和拥有者手牌，`BoundCardCount` 再按红桃数量回复。`standard-classic-generals@1.104.0` 将 `classic:zaiqi` 迁入该配方；再起专属 pending 状态、提交／AI／WPF 路由和旧执行器退出，1.103.0 只保留历史定义和指纹。专项覆盖未受伤不触发、跳过后的普通摸牌、按已损失体力亮牌、两集合完整分流、回复、通用事件、暂停 Checkpoint 与 Replay，以及 AI 对普通摸牌的稳定比较。

第四切片以 rules v124／schema 19 加入摸牌换伤害能力：`AdjustNormalDraw` 只修改摸牌父帧的普通摸牌总量，`GrantTurnCardDamageModifier` 将技能来源、有效牌型、数值与稳定帧／指令键写入通用回合卡牌效果存储；伤害消费同时要求实际来源等于原始用牌者、拥有者匹配且不是连环传导。`standard-classic-generals@1.105.0` 将 `classic:luoyi` 迁入该定义；当前 Core、AI 与 WPF 不再按裸衣技能名分派，1.104.0 及更早定义只保留历史内容／指纹与兼容 DTO，不复活专属路径。专项覆盖 schema／rules／包边界、发动／跳过、私有提示、摸牌调整、幂等授予、回合清理、直接杀加伤、决斗反向负例、暂停 Checkpoint／Replay 与通用 AI 策略。

早审发现 schema 19 加载器仍把能力拼成固定的两步或五步配方，现已改为 `DrawPhaseProgramValidator` 的能力级线性数据流校验。它允许 `Draw`、`Recover`、`AdjustNormalDraw`、`GrantTurnCardDamageModifier`、`RevealTopCards`、`FilterBoundCards`、`MoveBoundCards`、`SelectTargets` 与 `TakeRandomHandCardFromSelectedTargets` 任意安全换序和重复，同时要求绑定先生产后消费、分流集合来自同一亮牌根、每张亮牌恰好消费一次、目标集合最多取得一次，且生产者／消费者不能被条件性跳过。AI 同样按这些节点和玩家可见事实逐项估值，不识别技能名或整配方；多个调整先合计再钳制，替代只扣一次普通摸牌，同类伤害授予可叠加，亮牌不读取牌堆顺序，目标手牌不读取暗牌身份。

第五切片以 rules v125／schema 20 加入同一技能实例内的互斥可选分支。`choiceGroup` 只接受同窗口、同优先级、无使用额度且均为可选的 `additive` 触发器；展示文件 schema 2 以 `triggerChoices` 提供分支标签，玩法 hash 不混入文案。新节点 `GrantTurnCardActionProhibition`、`GrantTurnRuleModifier` 与 `GrantTurnCardTargetRestriction` 进入类型化回合策略存储并共同到期，`Draw` 允许按公开的 `LivingFactionCount` 求值。`standard-classic-generals@1.106.0` 将 `classic:jiangchi` 与 `classic:zishou` 迁入该窗口：将驰两分支分别表达额外摸牌并禁止使用／打出杀，以及少摸一张并授予杀次数 +1／距离无限；自守按现存势力数摸牌并限制本回合牌只能指定自己。群体牌在进入逐目标结算前使用同一目标策略过滤。当前 Core、AI、WPF 不再按这两个技能名分派，1.105.0 及更早定义仍保持历史行为边界。

第六切片以 rules v126／schema 21 为同一线性图加入 `StartJudgment` 和 `GrantTurnCardConversion`。前者只在拥有者摸牌阶段启动公开判定，以命名绑定接收经过鬼才等改判后的最终实体牌；后者只接受该稳定单牌绑定，按最终有效花色冻结红黑，并在本回合向相反颜色手牌提供【决斗】转换。`MoveBoundCards` 再把判定牌交给拥有者；若天妒已把同一牌移入拥有者手牌，移动节点按同源同目标安全跳过。`standard-classic-generals@1.107.0` 将 `classic:shuangxiong` 迁入该配方，当前 Core、AI、WPF 只消费共享判定、通用绑定、回合转换和普通合法动作，1.106.0 继续保留旧定义与专属历史路径。

主动技能加载入口同时改为效果操作正列表，只允许当前主动执行器真正支持的摸牌、回复、失去体力、交出已选牌与弃置已选牌；测试枚举所有其他操作并要求拒绝，防止生命周期状态节点因枚举扩展而被意外开放。`ProgramExecutionPlan` 再把主动与共享触发指令解析为按不可变定义引用缓存的只读计划；缓存不含对局状态，暂停恢复使用已提交游标定位原指令。

随后完成对局内 `MatchSkillBindingIndex`：每个席位按技能授权 revision 与国战主副将选择／显隐状态惰性生成不可变 shard；生命周期和数值规则保留 `SkillId + SkillInstanceId`，主动技、贡献、view-as、强制牌身份及旧卡牌／判定程序保持按 `SkillId` 去重。索引只缓存定义、节点、来源和绑定身份，HP、手牌、装备、条件、目标、限次和窗口候选仍按原时机求值／冻结。所有当前组合热路径均消费对应能力桶；`EnabledSkillPrograms` 仅作为两个既有诊断夹具的索引只读投影，不恢复运行时全程序扫描。旧技能专属分支仍按迁移队列保留，本次不宣称全库已经组合化。

前三切片与绑定索引的历史验收为完整 Core **405 passed / 0 failed / 0 skipped**、WPF **104 passed**，其独立快照 `%TEMP%\CardMatchIndex123-Final-3149029483a8496a94467f3d47ca4171` 验证了动态拥有／条件／装备／显隐／暂停失效及三个八人固定种子的 3849 个事件。第四切片最终匹配产物为 `%TEMP%\CardComposition124-Independent-50b22b3e9e314935bf3ae61b75c1576b`，Core SHA256 `133E0B158C27B670FE2FBF0513DDED60552D980797BDD0C19243782D2C90B828`，Content SHA256 `C71B9D9D41D61F7E8416276DDBC4E23E2489B3E3E738FBCAA138EB2BAD6CC4A5`；Solution Release 零警告零错误，完整 Core **410 passed / 0 failed / 0 skipped**、WPF **104 passed**。独立审计接受 66 个合法组合并拒绝 37 个非法图；混合真实引擎场景验证亮牌暂停、Checkpoint 精确恢复、技能失效取消清理与普通摸牌恢复；schema19 定义在摸牌与回合结束窗口各完成 5 次，共 1001 个事件，与 Replay 一致。

## 后续队列（候选范围，逐批收紧）

| 批次 | 代表技能与公共能力 | 要退出的专属实现 | 关键验收 |
| --- | --- | --- | --- |
| 4 摸牌计划（前六切片完成） | 英姿、突袭、再起、裸衣、将驰、自守、双雄已进入 `DrawPhaseStarting`；具名公开判定结果、按结果颜色授予回合转换 | 继续清点其他 CompleteTurnStart 专属流程，不再保留双雄当前路径 | 加量与减量稳定组合；互斥分支只执行一项；替代完成短路、跳过后继续；判定改判、实体牌取得、回合转换及重放保持同一来源 |
| 5 当前属性与授予 | 涅槃、单骑、自立已完成；区域清理、设置状态、上限变更、带来源技能授予/移除、持久牌区计数与强制分支 | 已退出当前涅槃、单骑、自立执行路径；历史包保留旧边界 | 模板不变而实例变化、多个来源、上限归一化、共享分支额度、回放 |
| 6 持久技能牌区 | 权计、排异、醇醪；带拥有者/来源/可见性的命名牌区、支付、子伤害 | 专属储牌/取牌/分支恢复流程 | 跨回合、空牌堆、支付后濒死、技能失效时牌区归属与清理政策 |

2026-09-22 初次文件级盘点共有 42 个 `GameEngine*.cs` 文件（含主文件）：21 个共享基础设施、21 个以人物或技能机制命名。rules v130 收口后，原 `GameEngine.LiuBiao.cs` 中唯一剩余的存活势力计数已归位到 `GameEngine.RuleQueries.cs`，该误导性的角色命名分片被删除；随后仅承载共享拼点发起和结果回调的 `GameEngine.Tianyi.cs` 归并到 `GameEngine.Pindian.cs`。rules v131 后又把完全依附共享拼点帧的陷阵兼容续接和烈刃阶段流程归位到同一 `GameEngine.Pindian.cs`，删除 `GameEngine.GaoShun.cs` 与 `GameEngine.Lieren.cs`。rules v132 进一步把当前陷阵迁入 schema 27，专属分派只为 1.112.0 历史执行保留；同时将四个 15–38 行的辅助分片分别归回角色运行时、国战、卡牌动作和模块交互。随后，单骑历史觉醒归入生命周期兼容、怒斩卡牌转换归入卡牌转换机制，并删除 `GameEngine.SpGuanYu.cs`。当前为 33 个文件：主文件、16 个共享机制分片和 16 个角色／技能命名审计面。上述归并只是职责归位，不冒充配置化迁移：怒斩仍是待配置化的当前转换修正，烈刃仍有明确的阶段适配，历史陷阵与单骑仍需兼容，后 16 个也不等于都能直接删除。后续完成度按“当前正式绑定是否仍按技能名分派”统计，不按文件名或文件数量推断。

后续批次复用已有选择、判定、拼点等资产，但必须核实它们是否真正接入共享执行器，不能只因已有枚举或接口就认定可复用。需要的新节点在实施前按公共操作定义，不以技能命名。

第 5 批第一切片以 rules v127／schema 22 为既有 `SelfDyingResponse` 加入三个可复用节点：`DiscardOwnedZoneCards` 只接受拥有者的手牌、装备区和判定区并按声明顺序逐牌移动，`SetChainedState` 显式设置连环状态，`RecoverTo(IntegerConstant)` 在最大体力上限内恢复至固定值。`standard-classic-generals@1.108.0` 将 `classic:niepan` 迁入“清理三区 → 解除连环 → 回复至 3 → 摸三张”的线性定义；当前路径不再依赖 `SkillKind.Niepan`、专属 Choice 或 `NiepanResolvedEvent`，1.107.0 继续保留历史定义和专属执行边界。专项覆盖实例化整局限次、濒死父流程完成、牌区移动原因、暂停／完成回放及旧包隔离。

第二切片以 rules v130／schema 25 为冻结触发事实加入 `CurrentHandCount` 和不绑定具体技能的 `LordGeneralNotIn`，并在干净的 `TurnStartBeforeNormalFlow` 边界开放 `ChangeMaximumHp` 与 `GrantSkills`。`standard-classic-generals@1.109.0` 的 `sp:danji` 以不可跳过、整局一次的程序判断手牌大于体力且主公不在刘备武将集合，随后减 1 点体力上限并按稳定来源授予马术、怒斩。当前路径不再调用单骑专属觉醒函数，1.108.0 及更早包仍保留原逻辑和指纹；下一切片复用同一状态节点处理自立的持久牌区计数与强制分支选择，不把这些能力回写成武将名条件。

第三切片以 rules v131／schema 26 增加 `CurrentOwnedZoneCount(zone)`，仅接受拥有者范围的持久命名牌区，并把 `choiceGroup` 扩展到准备阶段的强制互斥分支。组内分支共享整局额度；只有一个合法分支时直接执行，多个合法分支时发布没有“跳过”的通用 `ProgramTrigger` 选择。`standard-classic-generals@1.110.0` 将 `classic:zili` 迁入“权不少于三张 → 回复一体力或摸两张 → 减一上限 → 授予排异”的定义；满体力时回复分支不进入候选。当前包不再进入自立专属提示，1.109.0 保留历史 `DecisionKind.Zili` 路径和内容指纹。

恢复持续迭代后的实施单位改为“能力批”而不是“人物切片”：若同类内容已经落在受支持节点内，应成组维护组合定义、默认绑定、展示和差异场景；若能力仍缺失，先汇总一组同类需求并补一个可复用公共节点，再迁移多项内容。不得为单个技能新增 Engine／AI／WPF 分支或新的整配方白名单。本次正式验收覆盖到 schema 26 的摸牌节点组合、互斥分支、类型化回合策略、公开判定结果绑定、按颜色回合转换、受控濒死状态变换、当前上限变化、带来源技能授予、持久命名牌区计数与准备阶段强制分支；其他窗口及迁移队列中的旧分支仍按各自边界推进，不能据此宣称全库已组合化。开发速度只在有实际批次耗时、公共代码变动和重复样板减少证据时比较，不以测试数量推断倍数。

## 验收与恢复快速内容迭代

每批交付真实删除清单、剩余适配清单、匹配产物目录和定向行为结果。架构任务独立验证高风险边界；切片完成后集中做一次完整构建和 Core/WPF 验证，不在每个内容修改后重复全量回归。

第 0 批通过后继续第 1A，不直接恢复原新增武将队列。第 1A、1B 通过后评估是否已有足够能力让内容任务独立组合开发；未迁移的复杂机制可以作为明确队列继续推进，不以全库一次重写作为恢复内容的条件。

恢复开发的交接必须明确：已有能力只增组合定义、展示与必要行为场景；缺能力先补一个可复用基础节点，再完成内容。每次新增内容若再次需要在 GameEngine/AI/WPF 增加人物或技能名字分支，应回到机制设计，不沿旧方式继续堆叠。架构任务确认并交接后，再停用相应验收跟进。

正式验收后的自主开发范围：

- 能用已加载和验证的节点表达的内容，直接维护 Program 定义、人物默认技能绑定、展示和差异场景。首选本轮已有范式：数值查询修正、回合开始插入阶段、濒死自救、伤害后亮牌/子集选择、PlayEnding/TurnEnding 条件与摸牌/设置状态、有界 `CardsMoved` 来源区计数和逐牌／逐批摸牌、schema 15 的每次／每点伤害选择、绑定牌移动和动态补牌表达式，schema 16–21 `DrawPhaseStarting` 的固定加量、目标暗手牌替代、动态亮牌／花色分流替代、普通摸牌换回合用牌伤害修正、互斥分支、类型化回合策略、公开判定绑定及回合牌转换，以及 schema 22 `SelfDyingResponse` 的三区清理、连环设置和固定值回复。必须遵守每个窗口的现有节点白名单；不因枚举里有某个节点就假定任意窗口可用。对局内绑定索引由运行时按授权和国战显隐自动维护，内容不另建订阅表。
- 缺少能力时，持续迭代任务可先实现有界公共机制切片，再完成目标内容；其名称、上下文、输入、暂停/继续和测试按机制定义。仍未迁移的其他摸牌替代／减量、当前属性授予与持久牌区按后续队列推进，不复制旧人物 partial。CardUse B–D 作为独立公共机制切片重新评估后可推进，不自动沿旧专属路由续写。
- 内容局部修改运行相关差异场景；基础机制修改增加必要的共享边界验证，批次整合再运行完整 Solution/Core/WPF。不为每个新人物重造运行器、AI 分支、UI 决策类型或全量测试开关。两份架构文档是持续约束，已支持的组合无需逐项等待架构批准。

第四个状态牌区切片以 rules v130／schema 25 增加类型化拥有者牌源、白名单化拥有者持久命名牌区目的地，以及按指定命名区数量求值的规则修正。`standard-classic-generals@1.111.0` 的 `classic:quanji` 组合为“逐点伤害后可选摸一张 → 选择自己一张手牌 → 移入 Authority”，手牌上限由 Authority 数量的通用修正提供；1.110.0 保留历史 `DecisionKind.Quanji` 路径和内容指纹。

第五个状态牌区切片以 rules v131／schema 26 把主动程序的选牌来源从隐式手牌扩展为声明的拥有者牌区，并增加可复用的“选定目标不是拥有者”“选定目标手牌多于拥有者”条件与能暂停进入完整普通伤害链的 `Damage` 节点。`standard-classic-generals@1.112.0` 的 `classic:paiyi` 从 Authority 精确支付一张牌，令任意存活目标摸两张，再按摸牌后的状态决定是否伤害；当前合法动作、AI/WPF 主动技选择和战斗提示只消费通用程序身份，1.111.0 保留 `SkillKind.Paiyi`、专属事件和历史执行边界。

第六个主动拼点切片以 rules v132／schema 27 增加 `Pindian` 节点和 `pindianWon`／`pindianNotWon` 条件。节点只接受一张拥有者手牌及一名有手牌的其他角色；程序帧在子拼点、结果模块和牌清理完成前保持暂停，随后携带冻结结果只继续一次。类型化回合策略新增指定目标的距离无限、杀次数无限与无视防具授权；`standard-classic-generals@1.113.0` 的 `classic:xianzhen` 由这些授权和失败后的杀使用禁令组合，当前 Core、AI、WPF 不再消费 `SkillKind.Xianzhen` 或 `XianzhenResolvedEvent`，1.112.0 保留历史兼容执行。
