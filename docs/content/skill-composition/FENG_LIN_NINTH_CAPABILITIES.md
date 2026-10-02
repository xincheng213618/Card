# 界关羽、界张飞、界马超工程契约

2026-10-02 实施前冻结。共同基线为第八批已验收本地提交72bb2062ebb77a5cf4b58c26842c8419d4325e5a；主区Full Core463/463、WPF48/48含fresh构建201.178秒，日常Core132/132、WPF14/14含增量构建61.162秒。本文不表示第九批已经实现。沿用用户授权的三名GPT6.1 Sol隔离实现和父本地提交，不推送、发布或清理。

## 来源与边界

采用普通www.sanguosha.com当前hero/300、301、303正文。2026-10-02T05:56Z重取四页HTTP200；index原文SHA df68e4bb68731cf79e2ab27a2c970b251c75e084c321769d140bb3847e73bb26与第八批一致；三份hero原文和前期预检逐字节一致。确切URL、UTC、SHA、index字段和character-tabs/skill-text span配对正文冻结在source-preflight。旧攻略中的拼点义绝、替身回复上回合末血量、咆哮只无限次数、2020咆哮“下一次造成伤害”均不是当前正文，不能替代本合同的现行分支。十周年、手机版、backend与x产品线不作为普通OL现行来源。

正式ID：boundary:guan-yu / boundary:zhang-fei / boundary:ma-chao；均蜀、男、4HP，resource30000/30100/30300，PortraitKey boundary_guan_yu/boundary_zhang_fei/boundary_ma_chao。新内容模块独立partial和paired rules/presentation，GeneralModules仅父合并。现有孤立boundary:paoxiao只有无限杀次数，须在唯一源补全并由张飞内容注册；不能重注册第二份同ID。马术优先复用完整classic:mashu，无需重复定义快照或新op。新通用op范围关羽1860–1899、张飞1900–1939、马超1940–1979，不插空占位。

新可选字段缺省null并Json忽略，保留无新能力旧canonical/事件顺序。人物配置与新能力只依赖内容hash，不逐人物提升规则epoch、schema、包版本或minimumRulesVersion。同fingerprint共享语义改变由父审查且只在真实唯一版本源决定。

## 共享实际回合非锁定技失效

关羽负责共享能力及早期ABI交付，马超消费同一能力，不再实现另一套失效账本。冻结调用ABI：GameEngine私有partial方法IssueCurrentTurnNonLockedSkillSuppression(ProgramSkillFrame frame, int targetSeat)。持有真实ProgramSkillFrame并验证实际所属/owner/source后发布已生效回合事实；这不是待决流程，不用并行pending对象或action/use-ID sidecar。共享状态绑定当前实际turn与其owner，在本实际回合结束后失效，包括回合外借刀/明策杀产生的铁骑。不是到施加者下回合或被施加者下回合。

依据ContentSkillDefinition.Tags.Locked判断非锁定，不把限定技自动当锁定；觉醒沿现有明确metadata归一化。一般技能、动态grant、主公投射、化身来源必须走同一资格查询；不可永久DisableGrant，再以保存grantIds强行恢复，因为后来新增/重授的非锁定技同样应被封至当前实际回合结束，原有local disable不能被错误解封。锁定技保留；失效生效/expiry必须使cached binding shard和公共snapshot资格一致，纯视图/AI读不改变规则、grant或事件。无新cap旧流程字节/顺序保持。

施加后的事实独立于源失去、owner死亡或再授，不能因此提前解除；重复施加不同来源不得提前覆盖到期。并存旧缠怨等失效时不以一个来源结束解除另一来源。新增collection-bearing事件深freeze进CommittedEventProjection。关羽先冻结可审阅的共享partial及最小绑定/过期修改和定向结果，父按SHA将同一真实实现导入马超工作区；马超不得以stub/模拟失效检查声称正式通过。

## 界关羽

武圣：一张本人的有效红色HE牌真实作为Slash使用或打出，含当前合法响应/借刀/武器供杀；本人使用的有效Diamond Slash不受距离限制。保留次数、目标、禁用和实际实体成本；只Diamond，不扩大所有红牌、只使用不是所有打出。复用现有viewAs/query或通用可选政策，不人物ID硬编码。

义绝：本人实际Play阶段限一次，弃本人一张HE牌，然后令一名有Hand的其他存活角色自行选择展示一张Hand。阶段额度同owner+namedSkill+actualPhase，一次接受点消费，source重授不刷新、extraPlay刷新。两个选择者分清，其他viewer在展示前不可获得私牌身份，展示是公开真实行动。成本及真实movement children先完成，再发target私有展示选择；已付款暂停后不重复付款。支付后目标死亡/空Hand有限终止，不能展示不存在的牌或免费尾随效果。

在展示发生点冻结目标原区域有效color/suit：black发共享当前回合非锁定技失效、目标Hand使用或打出禁令、以及本人对该target使用有效Heart Slash伤害+1。禁令只Hand：真装备转换/成本弃牌仍按各用途合法；覆盖人类/AI/供牌/救援/无懈/多实体转换与公开存储来源，不只Play菜单。禁用不能被fake response或内部声明绕过。有效花色在真实Use点冻结，仅作用于施加者自己本实际回合对指定目标的Heart Slash，保留普通伤害修正/酒/属性/多目标目标限定；不泛化所有红杀或其他人杀，源失去后已发事实仍在本回合有效。

red分支获得被展示的那张真实Hand实体，完成gain/loss children后由发动者可选令目标Recover1，满血/死亡有限处理。展示后牌被其他child取得时不得duplicate/take wrong card；不得以展示文本代替真实移动。回复必须await HP/character/dying child typed return，成本、展示、获取、回复均所属帧cursor/一次标记。

必要小命令回归：HE支付和target自己展示控制、四viewer/存档回放、black抑制非锁定而保留锁定/动态grant/expiry；Hand Use/Played供杀救援阴性而equipment成本合法；Heart增伤仅target与当前actualturn/源失去事实；red真得牌→child→可选回复；非法输入原子拒绝、缺牌/死亡有限续接、每阶段共享/extraPlay/source重授；Diamond Slash远距阳性、Heart远距阴性和真Played实体守恒。host生命周期审计与实际命令夹具分别写明。

## 界张飞

咆哮锁定：无限本人真实Use杀次数；被真实Dodge完整抵消后，为本实际回合下一次实际Use Slash增加伤害。不要加入其他版本的“第二杀以后无限距离”、不可响应、HP代价。不能把Renwang免疫、伤害被防止、只出一张未满足无双的Dodge误认抵消；沿真实SlashFullyDodged或更准确的typed取消边界核实其与贯石斧/青龙的语义。同取消事实多个skill source/instance不可重复录入，储备事实使用named owner+skill+actualTurn，来源失去/重授不刷新或提前删除。

当前hero写下一次杀，不是下一次杀造成伤害时：在下一次真实Use接受/commit点把储备冻结到该CardUseFrame并一次消费，整张杀所有目标的该额外伤害一致；该杀又被抵消仍消耗旧储备，取消产生的新储备只给再下一张杀。只Played响应不消耗；借刀/青龙真实Use会消耗，actor替换归属以最终实际使用者为准，多目标重定向/子帧恢复不再消费。回合结束没用则过期，extra实际turn正确重置。

多目标分别抵消的累计口径作为显式工程选择：父已向用户发可选问题，目前按每次真实取消累计1；同一实体取消事件不重算。若用户选择同Use最多1，父发布补充裁决，必须落实到最终规则与source档案，不能以旧FAQ冒充当前明确规定。

替身：限定、可选、准备阶段回复至当前HP上限，摸实际以此法回复的X张。不是上回合结束HP，也不是出牌阶段末取得未伤害杀。复用成熟recover/draw/count能力优先，新op仅不足时；limited named quota接受点一次，HP/character子窗口完整结束后按真实有效恢复receipt摸牌，不以最初maxHP−HP猜数量；满血可以有限选择或跳过，但不能凭0回复白摸牌。源失去/owner死亡/终局按已发流程边界不新发尾随奖励；不恢复重复limitedsource消耗。

必要回归：真实取消→下一Use冻结一次→多目标/再次被取消、额外实际turn和未用到期、Played不消耗/真借刀Use、锁定失效阴性及普通距离仍保留；替身limited回复X、HP子窗口/真实Draw/replay、首轮受伤可用（现行没前回合限制）、满血0、重授不刷新、非法输入。复用现有泛型机制，不冗余snapshot。

## 界马超

马术复用完整锁定距离−1。铁骑非锁定，可选，真实使用杀指定目标后，按最终目标且先保留流离/求援等目标指定交互，再可选使目标当前实际回合非锁定技失效，然后发动者进行真实Judgment。用通用operation/typed judgment返回，包含改判、取判定牌、gain/dying子流程；最终有效Judgment suit由实际结果固定，不拿第一次印刷牌花色。不是旧classic:tieqi红色判定只禁止Dodge。

目标自行选择是否弃一张与最终判定花色一致的本人HE牌。必须实际弃置并完成movement children后才解除此当前目标这张杀的Dodge取消限制；没有同suit/拒绝/付款实际失败则不能抵消此杀，不能以选择文本当付费。有效Suit在本人的当前区域查询；真实HE可以包含装备（付掉装备后其能力不再存在），不强限Hand。各target拥有确切typed parent、judgment返回、付款cursor；多目标和恢复只提示/判定/付款一次，不为另一目标解除禁令。

“不能抵消此杀”沿真实Dodge抵消能力实现，保留Renwang免疫等原本不是Dodge抵消的效果；不能把它扩大成任何响应禁令、强行伤害或所有伤害防止失效。成功支付后真实Dodge/八卦判定按余下装备与锁定技资格正常；失败禁止八卦Dodge抵消等同效用途，但不阻止非抵消转移等已有规则。source死亡/失去后已 issued suppression仍到当前回合末，所属真实杀卡返回和支付movement child只结算一次。

必要回归：全4suit真实判定/改判最终结果、Hand/Equipment同suit成本、拒绝/缺牌/非法suit、owner/target私有chooser/四viewer冷回放、成本child后才正常Dodge、锁定八阵/仁王与非锁定失效区别、转移后最终target/多目标每target、source生命周期/target死亡终局、实际回合外借刀与expiry。普通classic铁骑无新增cap保持旧事件/canonical。

## 隔离交付、父整合与验证

三个worker仅写自己的guan-yu/zhang-fei/ma-chao副本Core/content/focused checks，不写main、parent、其他worker或冻delivery，不联网/WPF/Full/Git。父负责共同来源、素材/图鉴、公共UI、共享文件逐祖先三方整合、唯一版本源和最终fresh Full/日常计时/本地提交。

先给父ABI、共享改动清单和必要裁决，再实施。新增partial独立，少碰共享大文件；loader enum/descriptor/handler/resources/entry/AI全部到位。共享失效ABI先由关羽早期交付，父验证后导入马超同一bytes，马超final manifest注明只读dependency，不将别人的完整文件作为自己的变更覆盖。

完整交付source-manifest写baseline/final SHA、改变source和原始成功/失败summary/logs；每次修补新delta注明上一冻结SHA，旧合同、来源、交付和日志不可改。fixed verified seed/小deck/有界真实命令；不扫seed或完整对局找样本。仅新行为/bug的必要Core检查，UI新行为才加WPF，日常增加少量代表，不添加人物runner、重复定义snapshot或恢复被裁剪历史suite。

严格AGENTS：PowerShell、递归路径resolved absolute与LiteralPath；command commit/recovery、prepared snapshot CreateSnapshot(viewerSeat)及nested冻结；collection-bearing committed event深freeze；pending归所属typed runtime帧，push/replace/complete精确parent和一次成本；新事实AdvanceEventRulesAndQueueFact、新推进AdvanceRulesAndPublishState；card movement/input/privacy/content/checkpoint/replay兼容边界保留。文本EOF单换行/无尾随空白，PNG只精确复制官方bytes，文本归一化绝不能覆盖PNG。真实Core命令、host机制审计、实际离屏WPF与客户端人工实战分开记录。
