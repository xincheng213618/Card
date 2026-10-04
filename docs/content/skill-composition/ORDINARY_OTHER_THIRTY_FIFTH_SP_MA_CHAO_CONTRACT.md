# 当前普通 OL SP 马超（107）完整暂存合同

状态：静态冻结交付；没有编译、运行加载器、游戏、Core/WPF 检查、原生 AI 或基准。

当前官网完整正文、API 群/4、0 初始体力占位与原 PNG 由 `source-manifest.json` 的十个具名文件冻结。当前正文没有派生技能链接。性别 male 使用马超既有身份/传记判断，API 无性别字段。经典马超及界马超不修改。

固定身份为 `ol:sp-ma-chao` / `ol:zhuiji` / `ol:shichou`，`OrdinarySpMaChaoContent`、`ordinary-sp-ma-chao`、`ol-sp-ma-chao`、`character:ma-chao`、variant `sp`、ruleset `sanguosha-ol`。

追击的两句均为锁定技。7340 的 cardPolicy 在当前有效来源下，于距离完整计算后对其他体力不高于自己的角色设有向距离为 1；本人距离仍 0。7340 的 EffectOp 与 TriggerWindow 是不同枚举的同编号独立名称。新 mandatory actual-target 窗口先冻结实际目标列表及当次距离，逐目标私有地发布本人 HE 弃一张或真实全部装备重铸。每一 actual use 的同 actor/skill/binding/target 只公告一次，后来追加的实际目标独立公告；先完成全部机会再进入伤害/响应，与 7000、7200 各自的已访问目标记录共存。

付款前冻结选中实体与来源区域、原实例/定义 hash、原 use/nullable action、actor/provider、实际回合、原目标和 typed return。真正移动后发行付款事实；cost HP/RecoveryReplacement/CardsMoved 及后续 draw/gain/HP/Damage/Dying 子树逐边返回原 paid root。移动返回显式继续 drain，不直接丢弃 pending。已付重铸属于目标本人，发起马超失源不取消其收益；未付机会才核查当前源。原公开 CardRecast、额外公开付款列表均须在真实弃置之后发行。公共付款列表接入 CommittedEventProjection；pending 的 Entries/PaidCardIds/PaidFrom 及 CardUse visits 均以 backing/init 复制冻结构造、with 和 JSON 输入。

誓仇复用 CardTargetCount：两个 Add 项合计 lostHP+1，因此没有其他加成时，总最大值为基本 1+lostHP+1。7341 policy 仅为需要扩展模板的成熟真实两材料主动转换与丈八入口启用新独立 issued receipt；普通实体/单材料的成熟模板继续读同一查询。真正选定目标、全部实体、actor/provider、完整转换链、来源实例与 typed parent 保持，不能伪造 600 grant。真实 600 next-use 加成在 BeginCardUse 之前冻结；宣告消费原 token 以后不重新计算已发行的 maximum。620/5403 将原杀变火杀只使用成熟 CurrentSlashFirePolicy/OriginalAction 的确切事实与返回证明，不泛准额外转换。

独立多材料 producer 使用现有 Fangtian 的分目标执行但保留全部物理材料；最后完成及后续追加目标均沿自己的 parent，防止同时执行旧选中杀的第二次返回。legacy Action=null 神速保留原固定 producer/选取语义和真实追加事实，不制造 accepted action 或物理 0；追击与 7000/7200 的公告桥对其单独证明。

19 个 OLD：18 个 Core 窄接线、1 个 StandardClassicGeneralPackage 模块入口。没有修改旧枚举值、旧默认 nullable 形状、版本源或 PrepDiscardReceipts。JSON 自动资源通配不需要 csproj 修改。runner/scopes/gallery/art/source record 给根代理共同注册建议。

四项 fixed seed31 草稿采用真实 Submit/CommandJson 和 accepted-journal checkpoint restore；Cold 返回新的实例继续执行，四 viewer 投影、帧、事实、ledger、zones 逐项比较。没有 definition snapshot 或新 runner。
