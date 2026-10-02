# 第十二批：界诸葛亮、界孙尚香

共同生产基线 d8acdc5c4321042cd2721ddcf4d6b8443ec4d472，已在主区一次 Full 与无过滤日常验收。baseline-manifest.json SHA 5db45324afc32c55c1875ee3b2e19eab2c9f54ba96d6b5afb487af02fcbcf645，5087 条实际工作字节分别复制至两个实现 worker 和 root worker；主区与索引保持干净，不写回在制实现。原冻结文件不覆盖，必要补充单独编号。

当前普通 OL 官方来源 source-preflight/manifest.json SHA 371ee3a8bd472779f8f40f4a30b207a3da2d4ca35e90beb9bea72d53396c0a18，51 条原证据已逐 SHA 核对。两份只读设计的必要代码输入已与新验收主区字节核对；它们本身仍是设计，不是运行通过。界诸葛亮设计 manifest 9c3fac2aeafb649057099972c38e24308dd4917ea7428e48119b3f8d83187998；孙权/孙尚香设计 manifest 8fc055f61ef78462d0437707ef2ed0e881f1cb9ef038c54265efe447fa913823。界孙权本批不实施，先明确回复前替代的真实 producer 续接，不把当前救援改为旧桃 bonus。

## 正式内容与父负责范围

- 界诸葛亮：general boundary:zhuge-liang，蜀、Male、3/3 HP，character:zhuge-liang，variant boundary，ruleset sanguosha-ol，portrait boundary_zhuge_liang，官方44000。新技能 boundary:guanxing-current，bundle boundary-zhuge-liang，独立 BoundaryZhugeLiangContent；空城直接复用 classic:kongcheng，保留其 Locked/State 元数据与完整杀系、决斗目标禁止。
- 界孙尚香：general boundary:sun-shangxiang，吴、Female、3/3 HP，character:sun-shangxiang，variant boundary，ruleset sanguosha-ol，portrait boundary_sun_shangxiang，官方44300。新技能 boundary:jieyin-current，bundle boundary-sun-shangxiang，独立 BoundarySunShangxiangContent；枭姬复用 classic:xiaoji，不复制人物同义定义。
- 官网结构化 gender 均缺失，Male/Female 沿已有同人物定义，不能称为官方 API 明示。父统一负责 GeneralModules、gallery、GeneralArt、原图/catalog/sync、来源/能力/口径文档、默认范围、主区验收和 scoped 本地提交。
- 实现 worker 不改统一登记、旧人物与旧技能正文、原图、全局版本或台账；测试 fixture 可在最小 registry 正式调用独立 module，禁止 duplicate 定义替代正式内容接受。

## 观星：数量、真实完成与私有观看

保留 ReorderTopCards 成熟拥有帧与真实 CardZones 物理提交。新增 nullable 明确 opt-in 的 population threshold count 语义：启动每次真实观看时 >=4 存活看最多5，<4看最多3，短堆 min(该数量, 实际可用)。与旧 numberExpression/exactTopCount 混用拒绝；旧 null 自由排序和李典 exact 路径的 host、序列形状、取牌/RNG/提示与 AI 保留。新字段名称及内部类型由实现按泛用语义确定并报告，不引入人物专用 executor。

每次观看只固定一次真实 ViewedCardIds；之后持续展示同一组完整牌面不算重新取看。准备、结束两 binding 都通过新 count opt-in 启用 owner-only 完整 CardSnapshot，包括花色/点数。复用成熟私看投射/冻结，别人及旁观者不得得到 ID、顺序、花色、点数。不得以旧 free DisplayName 当完整看牌已实现。继续使用原 top/bottom/finish-top 选择结构与真实排序方向，产品提示解释底顺序，不暴露 opcode。

仅准备 binding 在拥有的 TopReorder nullable completion policy 冻结 instruction、实际 turn number/owner、declared BooleanState ID。完成先原 exact slice/分区验算，成功 ReorderDrawPileTop 后，才根据实际 ViewedCount>0、TopCount==0、BottomCount==ViewedCount 写 private declared BooleanState；然后 clear draft/advance。失败、中止、空堆和未提交的 finish-top 不授资格。不可用公共牌堆猜、选择意图或独立 result sidecar代替实际完成。新 scalar/state 用成熟规则推进入口；collection-bearing 新事件必须深冻结。

private BooleanState initial false、ResetScope.Turn，沿旧 instance-key、preserve/reacquire 约定。准备与真实 TurnEnding 各自独立 binding/actual-turn quota1；结束只读取资格，不再写准备全底 state，跳过或完成均有限结束，无自我递归。真实 Normal/Extra turn 均经过真实 TurnEnding；Schedule 只有 Draw/Play，无可达单独额外 Ending，禁止用 host 换 phase冒称该场景。准备前 Scheduled Draw/Play 的相邻父恢复顺序应有真实小 witness；不因假设第三方优先级扩全局 phase。

未实际提交时来源取消无奖励、原实体仍在 DrawPile；提交后不回滚真实排序。已付 private scalar不因来源失去立即撤销，结束实际资格仍要求 alive/enabled/相同 instance。新实例不继承，是既有 key 工程解释。Turn raw state在下一实际 BeginTurn 重置，与 actual-end 无可用动作区别记录，不改所有旧 state reset 时点。

新 opt-in descriptor-only 提供小正公开控制价值 prior（建议4），实际 optional begin/end必须原生 AI 发动并有限完成。可在现组合 AI 增加纯 ValueAdjustment helper；不能 Draw(1)冒充取得牌、不能读未观看的私牌或调用 RNG，也不能改旧 null/exact估值。真实观看后 native 选择可复用已有启发式，报告只证明可达/有限，不冒称最优策略。无需为人物加 runner。

## 结姻：两种真实付款与冻结体力分支

官方“你与其中体力值较大的角色”按两人中较大者摸1、较小者回复1解释；相等无额外摸/回，是本批工程解释，并非额外官方 FAQ 或用户裁决。顺序为先较大者 Draw1（包含其真实子窗），再较小者 Recover1。比较在真实付款及其所有装备失去/替换/技能/HP子窗完成之后冻结，后续 Draw 子窗再改 HP 不重算分支。不让自己无条件多摸1。

两种 active binding 共享成熟 UsageGroup，同实际 Play phase 总限一次：
1. 选男性，discard 一个真实 owner Hand 实体，任意种类。复用 captureSelectedCards + strict MoveBound discard/await movement。
2. 选男性，将一个 owner Hand/Equipment 的真实 Equipment 实体置入其装备区。新最窄泛用 placement operation 或 nullable 模式补成熟能力；复用单卡实际成本/替换/失去装备钩子和 typed movement continuation，不用完整 useBoundCardByTarget 代替纯置入，不改变旧 SelectedTargetEquipment gift 或 corresponding-zone 的 null语义。

男性资格按实际有效 Gender。新增 AnyLivingMale 或等价泛用能力允许实际变为Male的 owner自选；常态Female不因此改。self Hand discard/Hand Equipment真正置入可行；self Equipment原区重新置入无位移不能成为付款，也不触发枭姬。合法目标、activation实际Card/Target组合和最终接受均用同一纯 placement eligibility；区域/slot capacity0、abolished、no-op、不合法实体、来源失效在真实 Move 前排除/拒绝，不先耗成本或隐藏替换牌。已占槽可按真实替换流程置入，真实移动原装备；multiple-slot 等现规则照实核，不能只检查区域。

转移/装备替换使用真实 Transfer/Replacement intent，奇才不拦。不从 reason、目的地或 turn推断意图。候选提示保留 owner私有手牌和实际区域来源；产品用弃一手牌/置入装备说明分支，不泄露内部节点。

付款完成与较大/较小冻结结果放 owning ProgramSkillFrame typed nullable state或绑定；不增加栈外 pending/use-ID sidecar。真实 once-cost与 child parent返回走现方法，恢复不得再付，别人取走弃牌/替换装备不撤销已付事实。若通过组合实现无需新增全局 receipt ABI，先真测再依据实际失败收窄修补。少方死亡等不换新对象，来源/角色取消沿成熟有限结束，未真实 End不能声称过期。

较大者/较小者/实际HP before与stage冻结；相等包括self pair不执行奖励。HP/Draw/装备子窗中的合法死亡救援必须沿实际命令测试，不能为人物泛放宽 observer、parent invariants或 executor。原 native AI候选与估值需要实际非mandatory正式 activation witness，旧路径估值保留；纯公开 HP比较可以在新的泛用 descriptor估计，不读取别人的Hand。

## 验证与交付边界

每路新增约五个 grouped Core行为检查，覆盖未覆盖的新机制/bug，不为每人物写配置快照。使用既有 --filter 和 Test-Changed.ps1，固定小 pool/已知 deck，无 seed大搜索或完整对局模拟。观星新增完整私看投射应有一项通用 WPF行为组，其他 WPF仅必要真实新UI行为，不写注册镜像。

观星：5/3与短堆真实提交；全底/非全底/结束一次及真实 Extra；源生命周期和各暂停点四view冷恢复；非法输入/新parser拒绝及旁观私有；正式 optional AI begin/end与旧 free/exact回归。结姻：Hand弃/HE装备/真实替换与slot/no-op阴性；两binding共享phase quota及实际Extra Play；付款child后 HP变化再冻结；冻结后Draw子窗变化不交叉分支/相等；真实枭姬/来源失效/死亡救援、once成本、四view冷恢复与 old classic Jieyin/Xiaoji/奇才 Transfer回归。

host audit和实际 accepted-command/cold evidence分别标明；trusted diagnostics不是用户私有快照。真实产品异常先冻 accepted prefix、attempted command、faulted typed stack、源码/DLL/log，依据证据批准最窄增量；不能覆盖失败后称一次通过。worker只跑相关 focused，不Full；父整合后一次适当 Full和无过滤测量routine，日常保留原81Core/15WPF过滤范围并只补必要代表项。

新facts用 AdvanceEventRulesAndQueueFact，新state用 AdvanceRulesAndPublishState；QueueGameEvent/PublishState只输出。共享集合/事件/视图深冻结，CreateSnapshot(viewerSeat)、checkpoint/Replay/fingerprint/input/privacy/movement边界不变。不得人物自增 epoch/schema/package；版本源仍仓库唯一代码源。保留原测试剪裁、不restore历史suite、无 commit/push/清理。冻结源码 source-manifest含共同baseline、本合同/适用addendum、每owned路径baseline/final SHA以及各原focused/evidence，交付后不再改同包。
