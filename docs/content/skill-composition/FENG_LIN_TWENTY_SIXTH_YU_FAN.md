# 界虞翻 603 实现合同

权威输入为 [当前普通 OL 603 官方来源](https://www.sanguosha.com/hero/603)，本工作读取父任务已冻结的 `docs/content/sources/fenglin-twenty-sixth-603-source-2026-10-04.json`，精确 SHA256 为 `2002c486ce10219cb8dd5fe9f2d5aeddd28831fcbb4f592f357d7311d7970a38`。原 HTML 与 info API 的 HTTP200、URL、时间、原字节长度与 SHA 位于该台账；API 的 info 不提供技能和性别，技能来自同一当前详情页。两项正文无未解析的 font 授技或派生定义。界虞翻为吴3、initial_hp=0；Male 复用既有 `Fame2013Content` 的同人物虞翻身份，不声称 API 新增了性别字段。

官方纵玄正文：当你的牌因弃置而置入弃牌堆后，或你上家的牌于每回合首次因弃置而置入弃牌堆后，你可以将其中任意张牌置于牌堆顶。

官方直言正文：你上家或你的结束阶段，你可以令一名角色摸一张牌并展示之，若为装备牌，其使用之并回复1点体力。若为非装备牌且其体力值不等于你，其失去1点体力。

以下是缺少当前官方 FAQ 时的工程默认，独立于上述原文。没有据此改写官方描述，也没有使用移动、十周年或其他版本补足规则。

- 上家是逆座次最近的存活角色，跳过死亡座位；仅一人存活时不存在独立的上家。纵玄在真实弃置 batch 完成时冻结关系，直言在创建实际 Ending 候选时冻结，等待孩子或选择时不重算当前对象。死亡或全局技能改变后使用已冻结关系，并复用当前来源资格、活着的目标及原实体可用性检查。
- 每回合是实际 turnNumber。自然回合和真实额外回合各有新首次资格，插入的阶段没有新资格。首次按原弃牌持有者的第一次真实弃置 batch 发生时消费，整个机会拒绝或选择零张也不会让第二批成为首次。公开第一弃置事实与来源实例独立，失技、抑制、重新授予不抹掉或补发本回合首次；不可用或暗置来源不会通过公共事实暴露技能、grant、主副将或私密牌。
- 自己的真实弃置不限定第一次。相同批次自己的弃牌和合格上家弃牌取并集，每个 owner/skill/binding/instance 只建立一个候选；不同真实批次不合并。HEJ 均按原真实来源覆盖，调用现有 `GetProgramDiscardSource` 恢复确切 Processing 弃置来源，不改 movement ledger。使用/响应清理、重铸、死亡清理、装备替换和牌堆展示处理不是弃置。
- 原弃牌必须仍在弃牌堆才进入可选集，其他来源或其他 Processing 实体不能进入。另一合法消费者先移走的牌不会被重新取得；已经开放的排序 draft 中若原集合变化，复用成熟取消规则终止未付选择，不重放原弃牌成本。可以选择任意子集，包括空集；依次选择的第一张最先被摸到。置顶仍使用既有 `PutDiscardedCardsOnDrawPileTop` 移动理由及实际 PlaceDrawPileCardsAtTop，不新增另一套排序或实体领取协议。
- 直言按 Draw→原实体公开 Reveal→装备筛选→受赠者实际 Use→回复→最后体力比较执行。装备使用、替换装备、回复及移动孩子均复用经典成熟路径；原 Draw 绑定的公开元数据由该程序实际 Reveal 事件冻结，不在孩子后改用手牌当前实体推断。已合法移动的原展示牌可继续提供元数据，但不能重复使用/移牌。
- 非装备分支仅在最后执行步骤比较本人和受赠者的实时体力（非体力上限），相等不失血；本人作为受赠者必定相等。展示牌以冻结原 CardSnapshot.Kind 判断是否装备，沿用经典同一分类，不以未来区域或转换用途改变类别。牌堆为空的零张摸牌不制造展示实体或体力成本。
- 来源或目标在未付后继失效时沿用当前实例资格和取消清理，已真实摸牌/使用/回复/体力成本不会撤销或重付。非装备失去体力调用当前 owning ProgramSkill 的真实 HP/Dying 路径，instruction cursor 已推进，实体桃、HP观察、濒死/死亡和父返回继续复用通用协议，无 parallel pending 或 use-ID sidecar。
- 原生 AI 对新5300零成本公开排序机会使用0.5的正收益先验，仅有冻结实际 moved count>0时生效。排序使用原公开候选顺序，允许完成空集；既有 classic760 的估值和排序未改变。新5301在选择目标时不窥牌堆顶，用既有摸牌/回复公开估值，未知非装备后果先验为0，不声称最佳策略或实测胜率。两者不消耗 RNG、不读其他暗手牌或隐蔽来源。

5300与5301为新 opt-in descriptors/handlers，数值分别5300、5301；OwnOrPreviousLiving scope 为5300。5300加载合同限定一个 CardsMoved/Owner/PerOwnerBatch/真实弃置/HEJ 指令，拒绝该专属路径未实现的 movement reason/ignore filters。5301与新 Ending scope 必须是完整七节点一张实体摸牌/展示/装备使用/回复/原绑定比较组合，不能挂在 unrelated window、不同绑定或只替换 target cap 上。普通经典定义及 old760 的来源语义、资源合同与AI均保留。

新事件只有公开不可变标量，HP比较包含不可变 `CardConversionSource` 与 gameplay hash，不包含 card-ID 集合、牌堆或暗手牌。新增 Facts 字段 `FrozenPreviousLivingSeat` 为 nullable 且 WhenWritingNull，仅新候选赋值，旧 context JSON 不增加字段。公共 Reveal 的 Cards 列表沿用 `CommittedEventProjection.ProgramCardsRevealedEvent` 冻结；私有排序与原 batch/context 的列表继续由当前 player view/owning frame 投射冻结，不新添重复 PlayerSnapshot 集合。

5301无提示的原生 Self/DyingResponse 程序自动续点只在已有 Runtime 付费来源证明列表追加新的 OR。该证明先锁定原实际 Ending 当前候选及 occurrence/hash/instance、完整七节点最后指令和公开原实体绑定，再锁定依次发生的真实 Reveal→比较事实（目标体力1、非装备且与来源不等）→同父一条实付1点 HP、剩余0→该直属 Dying。原生实体桃/转换救援与醇醪各借用既有完整真实 Use/成本/HP/观察孩子证明；濒死进入与死亡子窗逐边核验。来源在实付之后失效不会否认旧成本，普通 Dying 或其他 op 不由新门放行；626原有 DrawDebt/SourceFaction 证明和普通救援协议完整保留。

四个 focused methods 草稿使用正式 Start、选将、正常区域选择、Play/UseProgram/Answer/Advance、额外回合和真实濒死命令，seed固定31、每副64张。覆盖混合HE一次成本/指定置顶顺序，原判定实体，真实转换过河拆桥的Processing弃置与用牌清理区别，首次拒绝/空集/失技重获/实际额外回合，装备使用及HP孩子，非装备自受益相等与濒死/实体桃/回复观察回返，上家Ending排除无关座位，native Start/Advance原生选择及严格加载拒收。人工夹具可正常提交多个参与者的真实命令；独立 native 例只用 Start/Advance，未人工答 AI。

当前限制：按用户静默开发要求未编译、未运行任何检查、未调用 C# 加载器、未测量 routine 或 Full。公开关系在多人同批、死亡改变相邻关系、短牌堆、同目标装备替换/白银狮子、原牌在排序期间被另一合法消费者移动等边界实现复用或静态审计，未宣称本四项各自新增运行覆盖。根应在允许验证后串行 focused，并按批次安排 routine/Full；本稿不登记主区、提升版本、下载媒体、提交或发布。
根整合记录：基于 3e36e653 的7个 NEW 和8个 OLD已按冻结清单逐文件检查。主区8个应用后原始 SHA与逻辑 preview完全一致；注册4个检查草稿和1个routine名称前缀，官方原始 PNG按冻结 SHA安装。5301的精确濒死返回比较冻结上家、候选、窗口、实际成本和公开原实体字段，不依赖含集合 Facts 的引用相等。代码和草稿均未编译、加载或执行；routine耗时尚未测量。冻结 stage manifest 为 da59e5a4358229e4cd783cf7fbb1fb5a803442dc1ab7c59002bdb684eaf9dc8a，OLD patch为8dd4794d09ae44ec34dfdadb6db62b362170966529f4a6e8fe8616cbdb5c6cfa。

冷恢复检查补充：独立followup清单 db678f41aa98fc5ea92b897aa4b49bcac2ce5fbab07217a71473f360573a338f 只改原第三方法三个暂停点，在装备HP观察、进入濒死和实体桃已接受之后，用真正JSON恢复的引擎实例继续子流程。主区检查源SHA为166e8fe980dcff561a6438ac1f9e23576e0e960f6ac5eff1b2840fed5230a1be；四项方法及八文件OLD补丁数量不变，尚未执行。
