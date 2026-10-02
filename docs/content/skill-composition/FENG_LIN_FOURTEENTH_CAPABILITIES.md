# 第十四批共同能力合同

当前普通 OL：界公孙瓒320/32000，Qun Male4HP；界华雄446/44600，Qun Male6HP。性别沿现仓库同人物/默认Male惯例，API没有结构化性别字段。原文和元数据见source-preflight；原图由父接入。general/bundle：boundary:gongsun-zan / boundary-gongsun-zan、boundary:hua-xiong / boundary-hua-xiong；模块BoundaryGongsunZanContent、BoundaryHuaXiongContent。技能采用 boundary:yicong-current、boundary:qiaomeng-current、boundary:yaowu-current、boundary:shizhan-current，不修改旧classic定义。

共同基线为本批 baseline-manifest.json 的实际捕获字节，含已提交第十三批和明确列出的并行dirty UI；所有worker初始字节相同。旧设计依据a0f，design-rebase逐文件差异必须先阅读，不宣称旧设计SHA等于新文件。父负责统一模块/图库/素材/版本台账/合并/主区Full及routine/本地范围提交。worker只改Core/Content及必要的新增机制检查；不改注册入口、图库、原图、scopes、版本来源、主区或其他worker。无提交/推送/发布。

## ABI及旧路径

EffectOp显式2300 DiscardDamageTargetAndClaimMount；2400 DrawByDamageCardColor、2401 UseSelectedActorDuel。每项为明确新node，descriptor/parser/资源/host/公开AI契约完整。可用有限新nullable拥有帧状态，旧null/default必须保持序列省略和原行为。不得改旧DamageCardIsRed、StartVirtualDuel、普通MoveBound、source有效性或普通虚拟用牌成本来套新行为。不提升规则epoch/schema/package版本；若发现同指纹旧命令真实行为改变，先报告具体可达证据。

新事实用AdvanceEventRulesAndQueueFact，新状态用AdvanceRulesAndPublishState。所有pending归拥有帧，runtime push/replace/complete及typed child return；已付成本不能再付。新collection-bearing事件/快照须freeze，标量receipt不可当作玩家视图。只用CreateSnapshot(viewerSeat)验证隐私，trusted栈只诊断。

## 界公孙瓒

义从纯现DSL：锁定，outgoing始终-1；HP不大于2时incoming+1（not hpAtLeast3）。保留距离夹限和其他规则叠加；不能改旧classic:yicong或重复增加逐人物配置snapshot。

趫猛是AfterDamageApplied的damageSource可选单次绑定：必须正实际伤害，owner确为damage source，且有真正Use的Slash/FireSlash/ThunderSlash provenance，实际card action actor为owner。虚拟零实体杀和真实转换杀也可；Played/Respond和cardless伤害不可。链伤与转移若仍保留这一真实Use/source事实则可，不能只借用排除chain的DirectCardUseDamage。此资格为当前工程解释，未称官网FAQ。

只对该次damage最终victim的HEJ真实选择一张，复用成熟opaque手牌槽与公开装备/判定槽；目标死亡后已清空区域不提供空选择。实际弃牌付款完成时、movement fact/child之前冻结exact card/from/to discard/movement sequence、source/binding/producer index和印刷EquipmentSlot坐骑匹配事实。单node有限尾段，不把已付discard收据伪装成ownerHeld卡集或开放任意MoveBound消费者。

child返回后：只有该同一卡仍在已声明弃牌位置才获得；若已被任何child移走则有限完成、不抢回或报错。印刷OffensiveHorse/DefensiveHorse均属坐骑，HE中的坐骑牌也算；其他牌仅弃不获。获得走真实移牌/gain child typed返回，限一次。未付source失效取消；已付exact尾段owner仍alive则source失效也完成，owner死亡终止剩余获得。已付target后来死亡不撤销事实，可用性仍只查弃牌位置。optional skip不付款不发receipt。公开native估值可读公开槽/关系/未知手牌数量，禁止读隐藏目标手牌种类或用RNG。

## 界华雄

耀武为DamageAppliedBeforeDying、damageTarget的锁定mandatory能力，正实际伤害HP apply后、该新节点自己的Draw/gain子窗前捕获owned immutable card appearance事实。这个before-dying时点是工程解释；不能全局改旧红色条件窗口。新DrawByDamageCardColor只适用这一窗口/owner身份/Always有限Draw1，不接主动或其他任意上下文。

有真实card use action时用已经冻结的EffectiveIsRed/EffectiveSuit颜色，不在gain/recovery/source变化后重算红颜/转换。无card-use action的实际实体延时牌伤害用该实际牌的印刷颜色；source-less不自动当cardless。真正无牌program damage无收益；有真实无色虚拟牌/混色丈八属非红，owner摸1。红色牌使真实有效且alive的damage source摸1，无来源/已死来源则不摸；非红牌使alive的owner摸1（HP0但尚未死亡也可，完成后继续真正濒死流程）。链伤按该伤害保留的实际牌颜色逐次执行，不按伤害点重复Draw。不把缺失牌等同非红，不伪造来源seat。颜色/来源/card存在性与一次Draw领取归拥有damage window/program frame的typed scalar事实；不得新建action-ID边车。

势斩：真实出牌阶段两次共享quota，零成本、选一名其他存活角色。UseSelectedActorDuel仅新零卡单other activation/Always，冻结selected-other作为初始actual actor、owner为唯一目标，真实零实体无色Duel Use。不能套旧两个目标direct-effect离间；必须CardUseFrame声明/指定/无懈/Jizhi/Duel响应/damage/dying/完成/typed Program父返回全链。selected actor不能拒绝“令”，但其使用禁止和owner目标禁止/空城/实际资格要在候选与提交同源检查；不可行则不提供目标。

启动前source失效按原取消；真正Use已发后，child正常完成，不因父source后来失去撤销用牌或再消费phase quota。合法角色/目标修正不能被只适用于初始发行的receipt invariant误拒绝。dead owner/actor走成熟用牌生命周期终止，禁止无限waiting。真实额外Play重新获得本phase两次，额外回合不继承旧phase消费。AI只估真实决斗风险、公开手牌数量/关系及owner自身合法知识，不能伪造固定Damage收益、读对方私牌或改旧AI路由。

## 验证及冻结交付

各worker约五组Core共享机制/新整合检查，必要行为保留全部断言；纯配置已有共用覆盖，不另做逐人物快照。先成熟小fixture/verified seed，不能扫描seed或完整对局取巧；普通optional native有限完成应是accepted命令，不强制改成mandatory。新UI行为才加WPF检查，否则沿成熟prompt。新组与相关旧filter并集通过后冻结delivery，worker不跑Full/routine。

实际命令+各viewer cold restore与host-only审计明确分开。保留每次原失败日志/summary/DLL引用，产品runtime fault须原prefix/checkpoint/attempt/source/DLL/真实observed栈，不把恢复后的空栈当fault现场。delivery提供共同baseline/原合同/来源与rebase SHA、精确owned paths/baseline/final SHA、原证据引用及简明REPORT，父验收后不继续改冻结包。

# 第十四批牌伤害存在性补充

先读取CONTRACT.md，再读本补充；原冻结合同不改字节。

耀武的“有牌”按实际typed damage attempt的明确card origin / EffectiveCardKind判断，不能仅用attack.Card非空或实体成本数量判断。实际card-based attack包括正常/转换/零实体virtual、成熟旧direct virtual Duel，以及有真实牌的无来源延时伤害；这些都不能因为没有CardUse action或没有PhysicalCardIds就当作cardless。真正Program cardless damage没有card origin/kind，不产生耀武收益。

有owning真实CardAction时读已经冻结的EffectiveIsRed/EffectiveSuit；无该action但有实际实体牌时按该牌印刷颜色；无action的明确虚拟card kind且没有实体/花色则无色非红。此fallback是工程解释，旧CardAttack与cardless boundaries不改。源码若表明某种typed attempt的kind只是非牌显示提示，必须报告具体producer事实，不能靠名字判定。

damage source seat来自已冻结真实damage事实，source-less必须是null资格，不能用其内部占位seat替代。红色分支的受益者固定为该真实source，实际执行Draw时仍须其alive；非红受益者为owner，HP0但尚未死亡仍alive。colour/source/card存在性及一次领取不能在gain/recovery child后重算；不把alive检查误当需要重新查询live skill/source ownership。
