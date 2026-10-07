# 2026-10-07 — current OL ordinary Ding Feng, Pan Feng and Ma Liang

本组补丁奉（短兵、奋迅）、潘凤（狂斧）、马良（自书、应援），包括普通版内容注册、五份技能正文、原生共享规则流程、图鉴与三张官网原画。按用户要求，本组提交后停止；本会话没有运行编译、构建、内容 loader、任何范围的测试、对局、UI、AI、种子搜索或性能测量。

## Primary source and metadata

当前来源为 [丁奉](https://www.sanguosha.com/hero/126)、[潘凤](https://www.sanguosha.com/hero/127)、[马良](https://www.sanguosha.com/hero/128) 各自完整正文及其页面公布的 info API。共 7 次正文/目录/API GET、3 次原画 GET，均 200，未重试或跟随重定向。原始响应、headers、请求台账及 metadata/art 解释位于 `docs/content/sources/evidence/ordinary-other-forty-first-*` 和 `docs/content/sources/ordinary-other-forty-first-metadata-and-art.json`。

原 source-only candidate report SHA-256 为 `763000c6e203cd1d098720b888bf990202ce8fc436f240ae149025db65349d7c`，抓取基线为 `f1833922cec066f9f4737971956f2de97d74b350`，三路作者基线为 `3829541ac962270648378e487281b29a6e7d1c19`。原报告保留抓取时的 `implementationComplete=false/runtimeAcceptance=false`，没有把静态来源状态改写成执行验收。

结构化 API 没有性别字段；三个 Male 均为明确标注的历史身份编辑判断。阵营/体力为 wu/4、qun/4、shu/3；`initial_hp=0` 沿用官网 frontend 的满体力 sentinel，未添加初始体力 override。重复使用的已存 `ordinary-other-thirty-ninth-current-heroDetail.raw.js` SHA-256 为 `55617a7de749ff8bf19ecd2b17682b59f35554090d5c1a8cc2101121ea248a59`；三页仍公布同一脚本 URL，没有新增 JS 请求。

三张 PNG 均为官网 advertised cover 的原字节，750×950，已目视核对；原有 249 条 art catalog 内容及顺序保留。

| Asset | SHA-256 |
| --- | --- |
| official-ol-ding-feng.png | c307850a397a5049e849ed0aeb81a9d838234535e989423cae255c836c526a91 |
| official-ol-pan-feng.png | b34719f44cf8b67dc19a6941952b252711327de3b3b30fd498059665b44acae1 |
| official-ol-ma-liang.png | 04572086857e460cdda7d207f0e31b6b27fa4516611ae3a168a1d09c2fe4f598 |

## Rule interpretation and owning state

以下是未被官网完全指定的工程口径，执行验收仍待统一测试。

- 短兵沿真实 Use 追加至多一个当前合法、实际定向距离为 1 的目标。各目标开始原生闪响应窗口时冻结两闪要求；不提高普通选牌时的全局目标上限。整张杀的使用身份、原目标前缀、追加与改向事实相互验证。
- 奋迅无前置弃牌，按每个实际出牌阶段限一次；最终定向距离为 1 包含原生规则距离查询。是否免结束债看整个实际回合对原目标的正数实际伤害，包含发行前的本回合伤害。多次额外出牌阶段发行独立债；目标死亡或技能失去不撤销已发行债，拥有者死亡/真实终局取消未发行尾部。原生 HE 弃牌的银狮、木牛与嵌套观察孩子按原付款继续，恢复不重复支付。
- 狂斧只授予本张杀距离豁免，沿普通原生杀计次、commit/completed、酒和已证明的无限次数授权。费用前查询排除拟弃的自身装备实体，已耗尽次数时不能靠即将弃掉的连弩显示合法。冻结原装备拥有者与实体，真实离区孩子完结后发行唯一杀。仅该准确 Use 的正数实际伤害计入结果，包括原生改向、追加、传导及来源改写；防止伤害/旁支技能伤害不计。外人装备且零伤害弃 min(2, 剩余手牌)，自身装备且有伤害摸二，另两种组合无尾部。
- 自书按每个真实取得手牌批次补摸一张，以精确来源、实例、instruction、付款事实和物理区间排除自身摸牌，避免按 reason 字符串误判。真正结束前的 own-turn gain 在原批次发行时冻结资格；结束后的 due 新取得不沿旧 Draw timing 误触发。其他角色实际回合的取得实体可正常使用，真正 TurnEnded 及成熟 due 链后只清理仍在自身 Hand 的原取得实体；结束者死亡不免除仍存活拥有者的清理。
- 应援只由实际使用者的 true Use 完成触发，不把 Response 或 provider 当使用者。基本/锦囊（含延时）/装备各类每个实际回合一次；放弃不耗额度，接受但牌堆无匹配牌仍耗该类别。赠牌取实际牌堆顺序中的首张同类实体，这是未指定随机性的工程默认。私人目标选择不包含牌堆实体、顺序或可得性，AI 只读 viewer snapshot。旧无 Action 虚拟杀仅经精确生产者证明补齐完成事实；完整目标证明和原目标回传仍由原拥有者保持。

新增待决数据保存在 owning typed frame，已付款孩子严格匹配原父、来源实例/hash、Use/Action 和物理区间。新规则事实经 `AdvanceEventRulesAndQueueFact`，新状态推进经 `AdvanceRulesAndPublishState`。带集合的新事件进入 `CommittedEventProjection`；record 的嵌套集合也冻结。保留旧枚举数值、规则 epoch、JSON schema 和 package version；不改 `GameEngine.PrepDiscardReceipts.cs`。

## Static review and unexecuted checks

三路修改在独立 before/preview 中封存，根以同一基线三方合并共享 hunk；独立交叉审查及根审查修复了连弩费用前额度、结束者死亡清理、自摸精确 producer、结束后旧 timing、短兵完整目标尾、旧虚拟杀完成回传、隐式枚举旧值漂移、付款濒死子树调度等边界。最终静态证据见本组静态审查 JSON；它不代表运行结果。

新增 11 项聚焦行为检查，沿既有 Core runner 注册，三个常规名前缀如下；没有新 runner 开关、框架或重复武将定义快照。

- `Short range Slash and directed ending debt: FixedOneActualTurnZeroCostEndingPhysicalOnce` → `DirectedDistanceDebtChecks.FixedOneActualTurnZeroCostEndingPhysicalOnce`（未编译/未运行）。
- `Short range Slash and directed ending debt: ShortRangeOneTailActualSlashAndNativeDoubleDodge` → `DirectedDistanceDebtChecks.ShortRangeOneTailActualSlashAndNativeDoubleDodge`（未编译/未运行）。
- `Short range Slash and directed ending debt: WholeActualTurnPriorDamageAndIssuedSourceLossHostBoundary` → `DirectedDistanceDebtChecks.WholeActualTurnPriorDamageAndIssuedSourceLossHostBoundary`（未编译/未运行）。
- `Short range Slash and directed ending debt: ShortRangeNativeRedirectCursorAndFireOrdering` → `DirectedDistanceDebtChecks.ShortRangeNativeRedirectCursorAndFireOrdering`（未编译/未运行）。
- `Equipment Slash ownership: KuangfuOwnEquipmentRecoveryAndWholeUseCompletionReturnOnce` → `KuangfuChecks.KuangfuOwnEquipmentRecoveryAndWholeUseCompletionReturnOnce`（未编译/未运行）。
- `Equipment Slash ownership: KuangfuForeignPreventionIgnoresPaidSkillsChangedDyingDamage` → `KuangfuChecks.KuangfuForeignPreventionIgnoresPaidSkillsChangedDyingDamage`（未编译/未运行）。
- `Equipment Slash ownership: KuangfuOwnershipDamageMatrixAndPartialHandPenalty` → `KuangfuChecks.KuangfuOwnershipDamageMatrixAndPartialHandPenalty`（未编译/未运行）。
- `Equipment Slash ownership: KuangfuSpentQuotaRejectsDiscardingItsOwnCrossbow` → `KuangfuChecks.KuangfuSpentQuotaRejectsDiscardingItsOwnCrossbow`（未编译/未运行）。
- `Actual hand gains and category gifts: ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion` → `ActualHandGainAndCategoryGiftChecks.ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion`（未编译/未运行）。
- `Actual hand gains and category gifts: ForeignHandGainsWaitForActualEndAndKeepOnlySurvivingAcquisitions` → `ActualHandGainAndCategoryGiftChecks.ForeignHandGainsWaitForActualEndAndKeepOnlySurvivingAcquisitions`（未编译/未运行）。
- `Actual hand gains and category gifts: CategoryDeckGiftsUseTrueCompletionQuotaAndPrivateTargets` → `ActualHandGainAndCategoryGiftChecks.CategoryDeckGiftsUseTrueCompletionQuotaAndPrivateTargets`（未编译/未运行）。

`tools/VerificationScopes.psd1` 仅添加上述三类 prefix，保留当前测试裁剪和原有全部 prefix。未执行 routine 或 Full，不给出本组耗时/通过数。其他会话的 `2026-10-07-ol-zhou-fang.md` 记录 routine 128.2 秒、Core 266 通过/46 失败、WPF 18/18；这是本组前的外部执行记录。本会话仅静态修复 judged-rank fixture 依赖并提交 `3829541a`，不能据此推断旧失败数下降或本组验收通过。

## Retained task outputs

先前自动审批已拒绝递归清理，理由为 `blocked by policy`；本组没有重试、换路径、搬移或委托绕过。任务预览、静态日志及封存版本仍位于 `C:\Users\17917\Desktop\Card\.artifacts\continuation-20261003-batch17`；另一个先前文件 `C:\Users\17917\Desktop\Card\tools\__pycache__\sync_general_art.cpython-314.pyc` 仍保留。没有生成发布包或构建输出。结束前只读确认遗留路径并停止本组消费者。

## Final combined source review

最后组合修订将短兵的精确追加事实接入转火目标重建，并区分指定目标与实际改向目标。首目标的原生流离窗口在最终目标短兵窗口之前；短兵收据核对该时点完整的原生已接受目标前缀，允许原生改向已证明的重复实际目标，同时保留方天已准备的原目标计划。只有 owning 短兵 receipt 存在时才发行精确原生改向事实；以当前 target cursor 校验重复实际 seat，保留转火已有专用事实的单次计数。应援据同一 Use/Action 的有序原生事实核对真正使用者、种类、完整目标及完成身份，避免追加目标后漏触发。普通及转火虚拟杀的父级返回仍保留最初选择目标。借刀追加复合目标只在本组类别赠牌能力存在时发行完整原前缀和目标事实，新增集合事件深冻结，完成证明不依赖无懈后可能不存在的终端借刀结算事实。转火额外目标的固定检查场景已修正为流离角色攻击范围内的目标，保留重复实际前缀断言。相关 focused 断言仅以源码形式保留，没有编译或执行。

最终静态证据为 [2026-10-07-ordinary-other-forty-first-static-review.json](2026-10-07-ordinary-other-forty-first-static-review.json)；其复核输入 manifest 原字节 SHA-256 为 `cb74f1ff7bfdb81665cd2096b3875165f0b4e102a26de48a59e7ff6d936816df`。提交前逐项复核文件集合、原字节与 Git canonical blob；提交后复核父提交、内容和保护路径。
