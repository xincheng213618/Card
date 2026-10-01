# 风林火山第二批公共能力契约

当前普通OL诸葛瞻410（蜀3HP）、陈到409（蜀4HP）、孙亮403（吴3HP）已于2026-10-02完成根目录整合，正式选将、官方立绘与共享交互已接入。完整官网正文、字节SHA、唯一索引匹配和明确工程推断见[来源](../sources/fenglin-second-2026-10-01.json)。一次Full为Core315/316、WPF41/41；唯一失败是既有反射夹具未传新可选参数，修复后定向Core13/13、WPF2/2，默认日常Core92/92、WPF11/11，含增量构建49.611秒。未重跑Full，详见[验收记录](../../benchmarks/2026-10-01-fenglin-second.md)。

| 分工 | 优先复用 | 本批操作预留 |
| --- | --- | --- |
| 诸葛瞻 | 私有牌顶、真实排序/获取、目标局部无效、真实失血/濒死 | 1320–1339：全回合条件计数、私有配额取牌及剩余顶序、先登记首次指定 |
| 陈到 | actual Play phase/use ledger、预付款距离合法性、typed响应流程 | 1340–1359：首张无距离、精确整牌不可响应、实际阶段使用禁令 |
| 孙亮 | 真弃置源、动态目标集、伤害防止、已使用实体转移、recipient规则 | 1360–1379：全弃牌阶段occurrence计数/HP合计目标集、范围谓词、完成杀成本赠予与双级选择 |

仅新增配置启用新公共descriptor/resource/host/AI/JSON；默认字段省略，旧内容顺序、RNG、响应/死亡/Processing和物理移动约束保留。严禁人物ID执行器/UI分支、逐人物schema/规则epoch/包版本上涨。独立worker从共同当前dirty源码与保留测试快照开始；原915文件快照保留。完成runtime迁移后，以独立930文件有效快照继续port，不覆盖原快照或恢复历史suite。父按SHA守卫三方整合并运行一次Full。

诸葛瞻在真实peek执行时冻结X：whole-turn造成伤害、真正owned HEJ弃置为零、活人手牌最少允许并列。精确取牌后剩余仅顶序，获牌子链不能重复领实体。父荫首次Slash家族/决斗目标先登记，手数不够或来源当时无效也不允许下一次误作首次；同一真实use不重记。X0先选他人，真实HP loss和濒死续接完成预定参与者序列；新已承诺效果子帧不能以通用死亡源放宽处理。

陈到使用本actual own Play-phase实例首张use（装备也计），无距离须在人/AI合法性及付款前生效；不能响应覆盖真正回应协议而不禁普通效果互动。接受后本阶段不能再Use，打出不禁；既发出的禁令不因来源失去/压制提前恢复，离开阶段到期。禁止以EndPlay跳过阶段代替。不能混用其他产品前两张/仅对他人限制正文。

孙亮X为整个弃牌阶段真实owned HEJ弃置occurrence，不能只用hand ID集合或仍在弃牌堆的牌。目标选择时冻结HP和预算，允许本人，X0不构造非空零HP伤害集合。掣政以target攻击范围是否包含source与actual Play-phase真实Use数判断，覆盖链伤而不防LoseHP。立军必须真实已用杀成本、provider/Lord分别选择并等待移动子链，给provider的SlashLimit+1用真实recipient modifier；多来源不能复制cost或刷新provider同phase一次限制；当前classic身份局不能配置多个真正Lord，未以非法配置扩展模式。

来源档案列明弃置归属、响应类型、首遇/issued-effect寿命、X0与多Lord等实现推断；未取得对应官网FAQ，不标为官方裁定。正式夹具ModeId必须identity:classic-前缀，固定小牌池，不seed sweep。已验证代表场景含真人/正常AI、真实私有暂停、viewer checkpoint与command JSON恢复、非法输入原子拒绝、实体守恒及父链死亡/失效；配置能复用的部分不增加人物定义快照。未穷举所有失去后重授、空牌堆组合或多个真正Lord。离线WPF渲染与命令恢复不等于桌面真人完整对局验收。

共用actual Play-phase计数契约已冻结：复用真实`_cardUseDebitPhaseInstanceId`，`GetActualPlayPhaseUseCount(actorSeat)`仅当前角色真实Play返回本实例Use次数，ActionId去重，装备/虚拟多成本各计一次，typed Use响应计而真正打出不计。陈到提供单一ledger实现，孙亮只声明其范围外弃牌操作需要该opt-in计数；额外Play另计。往烈无懈的不可响应只作用该counterspell节点，不传播到被响应的根锦囊。以上仍是明确工程解释。

借刀分类已用官网2016历史FAQ及现行真实调用点进一步核对：持武器者实际子杀走`BeginCardUse`，本人与current ownPlay相符时计一次Use；仅供牌者的响应和前置请求不计为该供牌者Use。立军赠予由真实`action.ActorSeat`控制，精确成本可来自另一个physical provider。当前三人规则仍以当日www hero正文为准，历史FAQ仅支持卡牌协议分类，不冒称当代人物FAQ。

新pending数据随QuotaTop、DiscardBudget和CompletedFactionGift的ProgramSkillFrame保存，CardUseFrame承载issued效果，typed return接续已承诺付款；不恢复旧pending/use-ID侧表。新集合事件在CommittedEventProjection冻结。真实闪accepted事实恰一次，虚拟闪与已支持extended双实体无懈走相同用牌禁令，准确反制节点不向根锦囊传播。

共享WPF显示公开阶段用牌禁令、私有罪论取牌和剩余排序、立军供牌者与主公双级选择；普通手牌使用与打出保持各自合法性，技能选择使用通用“正在选择”标签。三项新增UI行为均在全量41项中通过，并检查真实暂停、checkpoint恢复、非法命令与控件渲染。
