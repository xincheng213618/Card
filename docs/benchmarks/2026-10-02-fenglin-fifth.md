# 袁术、周妃及张绣从谏推进记录

本批以第四批已验收提交25e4d368f2986470602c1f3d977d050ffb071a1d的精确字节为共同基线，加入普通OL袁术100、周妃411，并补现有张绣415缺失的从谏。正式技能、选将、雷包图鉴与三张750×950官网立绘已整合，张绣保持原有雄乱配置。无逐将全局规则epoch、JSON schema或内容包版本变更；未推送、发布。

袁术庸肆修改真正正常摸牌计划，弃牌阶段重新计算存活角色有效势力数，真实弃置合法HE的min(X,可弃数量)，完整movement children后进入通常弃牌。伪帝保留actual Lord与能力来源的精确grant/instance关系；来源暂禁用与派生本地禁用分别处理，来源失去、重授、死亡及模板变化即时失效或刷新。真实身份、势力、胜负和AI阵营保持原样，菜单、主公技provider/response、被动、host及contribution按具体派生实例获资格。新可选能力bool进入canonical gameplay fingerprint；无该能力时不运行同步。

周妃良姻按每实际回合全桌真实越界占首次，两方向独立，放弃及当时无技能均不顺延；显式游戏外域是工程契约，不冒称官网FAQ。批次完成时冻结scalar turn/batch/direction/ordinal，先记账再开启children。双方真摸/各自私有HE弃置完成后读取当前手牌与同来源箜声数X。箜声先获得非装备，完整子链后由使用者逐张真正Use装备；确切来源直接支付Processing并走目标、装备替换及typed完成链。用户明确选择：只有实际使用过至少一件装备才扣1体力；空堆、仅非装、全非法均不扣，剩余非法装备留原堆，真正扣体力进入完整濒死/死亡链且恢复不重复。

从谏读取真实重定向完成后的最终锦囊目标，owner属于distinct多目标集合，真实给不同最终目标一张本人HE，实际receipt及全部movement children完成后摸牌。按支付实体Equipment给2，其他给1；不是虚拟输出牌名。owner死亡无奖励，多grant同名一次，来源/动作/receipt均在所属typed frame，不用旁路pending或useID账本。

## 整合和验证

三路独立冻结manifest逐路径校验；父三方合并保留各自typed draft/continuation、descriptor注册与行为检查。针对性整合结果见source档案及validation-parent-targeted-01原始summary/log；具体审查修复和实际限度随后附在本记录。

58个变化路径SHA保护写回，验证后源码漂移为空。一轮fresh Full：Core390/390、WPF46/46，含构建140.110秒；无过滤日常：Core106/106、WPF12/12，含增量构建45.777秒。Full原始summary/build/Core/WPF日志在main-full-evidence冻结，日常在main-routine-evidence冻结。既有GaoDaYiHaoChecks两条nullable构建警告保留，无错误。Full原始证据将在日常复用同构建产物之前单独冻结；所有新增机制的必要代表检查纳入日常，实际是否仍约一分钟以测量为准。离线Core/WPF与官网HTTP不作为OL客户端实战或桌面人工试玩验收。

来源见[官方档案](../content/sources/fenglin-fifth-2026-10-02.json)，解释边界见[工程契约](../content/skill-composition/FENG_LIN_FIFTH_CAPABILITIES.md)。原始交付、人工冲突候选及验证日志位于%TEMP%/Card-FengLinFifth-20261002。

后续风、山缺口是普通于吉211、左慈56。旧两个工作副本已查到且不在当前main祖先链；先核对当前普通文本和typed运行时、隐私、版本及已裁剪验证约束，不能直接合并旧整批或恢复历史suite。

## 父审查修复与实测边界

合并后的针对性并集Core39/39、现有WPF控件6/6通过，含fresh构建25.970秒。范围包含本批三路、孙亮8项相关Lord/费用回归、旧雄乱、真实流离最终目标、binding/catalog/contribution、nested movement、prepared snapshot，以及public pile/private response/artwork UI。没有增加人物定义重复快照或人物专用UI开关。

袁术修复了上游无效的稳定派生关系被觉醒计入以及觉醒失去后同步再授：只对projection额外过滤资格，失去派生保留本地disable；普通原生ownership语义保持。真实觉醒/source suppression及后续实际规则推进检查通过。贡献在付款前固定实际instance，只派生receipt写null省略的新增字段，旧payload保持。

周妃同SourceId多个独立pile改成启动时冻结引用技能/instance/已存在location，优先exactinstance、其次同SourceId稳定顺序；未建pile仍可冻结grant，child结束只读该key当前count，不能被新更早instance/其他pile/总数替代。新直接grant/source/zone机制fixture覆盖关联并真实heal；这不是外部布局修改的命令replay验收。原7项均按真实命令与原多viewer/checkpoint检查通过。一个batch双向、first后新获技能、X0治疗及第一Equip付款前被移走未另做组合动态验收。其余未测专门组合见冻结README。

三张官方750×950原图已逐一看过；编译WPF g.resources内三张PNG各自SHA与冻结官网字节完全相同，compiled-art-proof.json保留assembly SHA和resource字节证据。46项已注册WPF Full与现有控件通过，不冒称逐张新立绘游戏窗口渲染或现场对局验收。
