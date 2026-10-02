# 界刘备、界大乔、界华佗工程契约

2026-10-02 实施前冻结。本批精确基线为第七批已验收本地提交94d61f80de6615c58e9d8ba91c6c0412adc5df48；Full Core431/431、WPF47/47含fresh构建198.975秒，日常Core126/126、WPF13/13含增量构建63.176秒。本文是实施前合同，不是第八批完成证明。保留用户授权的三名GPT6.1 Sol隔离实现与父本地提交，不推送、发布或清理。

## 正式来源与范围

采用普通www.sanguosha.com当前OL界版正文299/309/318，三份raw HTML于2026-10-01T23:44:50Z获取；新列表于2026-10-02T00:11:46Z HTTP200复核，index SHA df68e4bb68731cf79e2ab27a2c970b251c75e084c321769d140bb3847e73bb26。三份页面SHA为6dcb5870547c7f38141b1a07fc06f2106c152fd20c6cad6bb303f89d2b38eccf、7de515168f56e819d1cb0ff7de140459ab41a2cb473fb81b63d0a90a985018c0、83a7ab535c1f696e80c7c5bc298a063a1e0ffcf54cfc617a4dd2c0f4d6dfd359。早期fallback误取title的v1 skills解析已拒绝；本合同只使用原HTML精确character-tabs/skill-text span生成的v2档案。原始错误档案保留，绝不以其作为技能证据。

正式ID/元数据：boundary:liu-bei，蜀、男、4HP、PortraitKey boundary_liu_bei、resource29900；boundary:da-qiao，吴、女、3HP、boundary_da_qiao、30900；boundary:hua-tuo，群、男、3HP、boundary_hua_tuo、31800。编译showcase精确三ID缺失，并非仅显示名未命中。列表initial_hp为0表示没有独立初始HP限制，保留既有默认行为。

补充普通OL新闻为2016-11-01界华佗攻略、2016-11-10界大乔攻略、2022-01-12界刘备主公技调整。原HTTP/URL/UTC/SHA归档source-supplementary；旧攻略只解释现行相同部分，当前hero正文优先，不混入十周年、国战、移动版或backend产品的旧结果。

新增内容模块各自独立文件+paired rules/presentation；主GeneralModules登记仅父合并。查明现有孤立definition再复用或在唯一源校正，不能重复注册同名skill或引入同bundle不相关技能。不逐人物增加RulesVersion/schema/package CurrentVersion/minimumRulesVersion；新可选节点default null并省略，旧canonical算法/无cap事件顺序保留。确需新op才登记完整descriptor/handler/resources/AI：刘备1740–1779，大乔1780–1819，华佗1820–1859；同enum不得碰撞，不添加空占位。

## 界刘备：阶段每recipient一次、第二张后的真实基本牌、提供者决定激将奖励

仁德按当前正文：本人实际Play phase，每名其他角色限一次，给任意正数Hand实体。只用真实Hand→recipient Hand movement，不以selection count伪造已给出数量。每recipient与本phase累计数量必须使用通用named owner+skill+actualPhase的次数/计数状态，来源失去/重授、多instance不得重置；新增extra Play开始重新计算，actualturn间也重置。菜单/AI/提交同一合法性；接受点消费recipient quota一次，非法输入原子拒绝。全Hand绑定、真实付款/赠牌children在所属Program frame，已付cost暂停后不重付。

本phase累计从<2跨到>=2时触发一次可选基本牌真实Use；不是旧普通仁德回血，也不是收到每第二张重复触发。单批给3同样只一次。先完成gift和movement children，再构造可用基本牌种类/合法目标菜单；可decline，无合法基本牌时有限跳过，不制造伪card entity或伪action。杀包含现有属性杀，保留次数与距离/当前规则，不能偷设unlimited；桃满血、酒次数/当前phase、禁用政策按现有真实Use入口。若杀次数耗尽，只去掉杀而不是跳过所有合法基本牌。使用UseVirtualCard等已验证typed子流程及原Program精确返回，包含response/nullification/dying/afterUse children后才完成仁德parent。源失去或owner死亡遵守既有issued program continuation边界，不放宽任意parent/Processing。

激将第一段复用真实faction card request供杀，既支持Use也支持Played/response/借刀/青龙等现有用途；当前正文没有主动请求每回合一次的限制，旧孤立boundary:jijiang的active Turn1不能直接沿用。请求可多次而真实杀仍遵守正常使用次数/用途，空供牌失败/decline不能伪造成功或占用奖励次数。主公标签和身份资格必须走现有qualification，不用人物ID分支。

用户已明确：激将新增摸牌奖励按每名主公每actual turn共享一次，由实际用杀/打出/代供杀的蜀将决定是否奖励；不是每供方各一次，decline不消费，后续合法行动仍可奖励。记录同owner+named skill本actual turn quota，来源重授不刷新，下个actual turn重新计次。正文中的实际供方选择必须准确反映到prompt actor/AI；技能owner仍是主公，不能把可选权交给主公或让人类回复AI的成本私牌。

资格使用冻结真实CardAction：普通外来actor于其回合外Use/Played杀，或主公Use/Played杀时真实非主公Shu provider于其回合外供杀。effective faction和actualTurn owner在实际行动点明确捕获（only compiled cap）；反例包含供方在自己的回合、非蜀、actor/provider误归因、失败声明/无实际行动。同一实际供杀用途不能因内部响应+最终用牌重复提示/奖励；复用action/typed用途归因或拥有帧游标，禁用ID sidecar。可通过通用optional chooser policy/actor routing扩展，ABI先告知父，nullable旧字段不变。仅真正accepted奖励后Draw1原实体、完整gain children，再返回原实际用途；被拒绝/源失效期间不消费quota。

必要固定命令小检查：同recipient二次禁止/不同recipient各一次、分批1+1与单批3门槛、extraPlay重置及source重授；gift child暂停+非法输入+四viewer replay；真实基本杀次数阴性、桃/酒合法支路及decline；真正原生外来response与主公provider奖励、控制方正确、两名蜀方共享一次/decline不消费、本人回合/非蜀阴性、重复真实请求可继续。host lifecycle审计与command replay分开，不靠seed扫描。

## 界大乔：国色两项共享阶段额度、真用牌或双成本弃置后摸牌

流离复用现有真实HE弃牌→按本人攻击范围转移当前杀的机制，保留不能转给使用者等已有合法边界、最终目标与response/dying typed续接。核对新boundary skill定义/注册完整，不仅放入技能文案。无需复制既有流离定义快照/所有历史矩阵。

国色当前正文每实际Play一次，两项共用该阶段额度，不按每option或每source分别计算，extra Play刷新。可把两项放同一配置activation/options，复用SelectOwnedCards/UseSelectedCardsAs/真实judgment选择等能力，避免为人物另设pending。方片成本允许本人Hand/Equipment的有效Suit，复用已有转换query和禁用政策；接受后冻结实体、确切来源及kind一次。

项1把一张方片当Indulgence真实Use，不能只移动到Judgment模拟接受；保留合法目标、已有同延时卡限制、无懈、conversion chain、equipment离槽children等，原动作最终完成后Draw1，无懈并不取消已完成国色的补牌。牌张成本仅付一次，phase quota同时与项2共享。

项2弃置自己的方片及场上一张Indulgence。场上指真实Judgment位置，允许本人或其他角色的该牌；须使用现有effective delayed identity识别转换形成的乐，不能只查physical printed kind，也不能选择Processing/外界暂存。冻结public judgment实体和自己的HE成本；可采用一个真实原子多来源movement batch或现有精确paid两步能力，选择适当通用入口并说明裁决。所有实体在接受点验证；其后成本失效/owner死亡/来源失效取消分支不能重复付或退款重开，不能凭选择文本声称两个弃牌都已实际完成。支付完成并完成所有movement/gain/loss children后Draw1。沿既有Judgment清理effective kind状态边界，不留下延时kind side账本。

必须覆盖共用phase限次/extraPlay、HE方片使用成真实乐与被无懈后补牌、项2真实解除自己的/他人的乐及转换形成乐、非法Judgment实体/非方片原子拒绝、支付→movement child→补牌暂停/replay一次。若实现选择两步而非原子双成本，请发父确认其具体失败/取消后是否Draw的工程裁决，不冒官方FAQ。新旧source/key不冲突，使用通用AI/公开Judgment和opaque其他Hand边界。

## 界华佗：急救复用、有效势力互异参与者、按弃牌有效花色冻结奖励

急救复用现有boundary:jijiu的HE红牌、本人回合外真实Peach rescue；普通本人turn内濒死不触发，outside own turn自救可用，旧shared conversion provider/replay保留，不只是Hand成本。

除疠每实际Play一次：任意名有效势力各不相同的其他存活有HE牌角色；其他角色集合的势力互异，不把自己的势力加入排重（官网FAQ明确不包括自己，可同势力一名他人）。按当前神/化身等真实effective faction查询，而非printed faction或Team；菜单/AI/accept同合法性，输入重复seat/同势力/死亡/无可弃牌非法原子拒绝。任意名按可选零名其他人处理，这是显式工程口径、非旧FAQ新结论；本人仍实际弃一牌，不能零支付。额度同owner+named skill phase，source重授不重置，extraPlay刷新。

旧普通攻略明确先弃自己的，再按游戏行动方向（逆时针）依次弃选择的他人，最后相应角色摸牌。本引擎实际座位推进方向为行动方向，应复用相同seat-order helper而非依点击target排序；不要凭UI几何顺序猜。先在所属ProgramSkillFrame冻结participant计划/cursor，本人真实HE选择、他人HE使用既有公开equipment/opaque Hand槽选择，逐participant真实弃一牌并完成当次movement children，然后下一位；不得把已支付cost再移动。participant后续死亡/失去全部HE时按已验证selector缺牌行为有限跳过，已完成receipt保留；owner死亡/终局不继续新成本或摸牌。

黑桃奖励按实际被弃牌在其原owner区域、弃置点的有效Suit冻结，不是进入Discard后physical Suit。普通官网2016攻略明确弃小乔牌不会因红颜摸牌，因此不能沿用第七博图的印刷Suit裁决。不得从live Discard倒推奖励：嵌套child可能拿走或再转移牌。typed receipt包含来源参与者、真实实体/付款完成和effective Suit，深冻结在拥有frame；最后按完成receipt的Spade角色各Draw1（含自己），每角色一次，等待真gain/dying children，缺牌跳过者没有receipt/reward。触发后的来源变化不得重复奖励；来源生命周期机制测试和真正指令夹具分开表述。

必要固定小检查：同势力其他target排除但与自己同势力可选、effective faction/神/化身规则用已有通用查询或小source fixture；本人Hand/Equipment弃置、other opaque Hand/公开装备、真实seat顺序与nested child、全部弃完才奖励；印刷Spade小乔红颜阴性、自己黑桃阳性/卡被child取得仍按receipt；零其他参与者口径、后续缺牌/owner死亡停止、phase reset/重授与非法输入原子性、四viewer和checkpoint/实体守恒。

## 分工、共享入口及验收

三名已授权Sol使用新精确基线的独立liu-bei、da-qiao、hua-tuo副本；只改自己的Core+content+focused checks，不写main、parent、别的worker、冻delivery，不联网/WPF/Full/Git。父掌握来源/立绘/UI、最终版本、registration/Program/loader共享块三方合并、一次fresh Full、实测unfiltered routine、本地提交。Freeze source-manifest baseline/final SHA、完整变化source及原始失败成功日志，后续修补新delta，不改旧交付。

刘备拥有recipient phase/门槛basic Use及optional实际actor主公奖励；大乔拥有国色转换/解除/统一quota与流离registration；华佗拥有participant distinct effective faction/真实弃牌receipt次序。共有SkillPrograms/ProgramLifecycle/Resolution/Runtime/ProgramEntryCapabilities/ProgramCompositionAi/GameEngine入口提前给父发ABI；不要覆盖整文件他人改动。优先独立partial文件，通用type与可选参数明确，不用人物名字/ID硬编码规则。无cap旧canonical/payload/event顺序保留，任何同fingerprint shared语义修复由父在Replay唯一源判断epoch。

遵循AGENTS：PowerShell、recursive路径LiteralPath检查；command commit/recovery、prepared player views CreateSnapshot(viewerSeat)与nested冻结、collection-bearing committed events深freeze、typed owning帧/精确父返回、paid一次、card movement与隐私边界。新规则用AdvanceEventRulesAndQueueFact，新推进用AdvanceRulesAndPublishState。无新test框架/人物开关/duplicate definition snapshot，不恢复旧suite。固定seed/小deck/有界真实指令，不扫seed/完整对局找样本；适当现有filters开发，父完成batch后Full及无过滤routine按实际timings报告约一分钟目标。新增文本EOF单换行/无尾随空白，PNG只复制精确官方bytes，文本脚本不得处理PNG。WPF fixture离屏渲染、真实Core命令、trusted API机制测试与客户端实战限度分开。
