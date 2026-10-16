# 袁术、周妃、张绣共享能力

项目采用的普通 OL 正文及工程解释见[来源档案](../sources/fenglin-fifth-2026-10-02.json)。

庸肆：当前存活角色的有效势力去重。正常摸牌加X遵守既有正常draw计划/替代/跳过，不另开假Draw。弃牌阶段开始重新冻结X，强制弃自己的合法HE实体min(X,可弃数量)，完整children后进入通常弃牌；不调整手牌上限代替。可弃为空则继续；已承诺数量及已付款留owning frame，不在child恢复重算/重付。现有数字表达式可通用扩展，只新增合法资源能力，不改变已有固定amount语义。

伪帝：按实际存活Role.Lord的有效Lord-tag原生和后天真实grants映射，排除本能力的派生来源避免递归；无实际Lord、非identity或owner本身就是实际Lord时不做自映射。真实Role/faction/身份HP/胜负、AI阵营关系保持。派生技能保持真实SkillId与稳定来源关系，来源含actualLord seat+grant/instance和consumer能力grant/instance。来源禁用/压制、模板资格或死亡使派生资格即时不可用；本地对派生grant的禁用独立保持，不因源重启无操作refresh而擅自恢复。实际移除只撤销对应关系，不碰原生/其他复制；源获得、源重授、owner失去/重授都精确刷新。same owner+named skill使用/转换既有语义不重置，重复grant不放大一次action收益。资格只放行该真实派生instance的Lord技能gate，菜单、response/provider、card policy/passive、contribution/host及运行时AI一致；不是全局Role.Lord替换。SkillsChanged单点不足，binding跨owner alive/Revision变化必须收敛；纯snapshot/AI/read不能产生rule事件。所有同步在规则/command session边界并保留拒绝命令/恢复语义。可用typed来源元数据，无useID/cardID sidecar或parallel pending。

良姻每个实际回合对两个方向的首次越界各提供一次机会。真实移动先占用首次事实，跳过发动或当时没有技能不会顺延；额外阶段不重置。当前 GameEngine.GameDomains.cs 将 RoundHold、PrivateTurnHold、PublicPersistentPile、Authority、BuquWound、Chunlao、PrivateReserve、PojunHold、PublicDeferredPile、WoodenOxGrain 和 OutsideGame 分类为规则游戏外区域；手牌、装备、判定、牌堆、弃牌堆及 Processing 属于普通游戏内区域。只有实际回合已经开始后的真实跨域移动计数，域外区域之间转移不重复算越界。该区域划分是明确工程默认，实体仍由 CardZoneStore 唯一管理。

同batch每方向一次，按真实movements顺序决定双方向首次；在movement batch完成时冻结scalar turn/batch/direction/ordinal并先占first，再开启任何children。无新人物资格分支。一个方向首次所有有效同名skill按owner去重；是否发动和各instance归因按现有trigger框架。良姻选其他living角色，双方实际摸1或各弃自己的合法HE1；一方无可弃按现有顺序执行能执行部分，不伪造支付。全部movement children返回后重新读取当前双方手牌及同source箜声X，可选其中手牌等于X且能回复者回复1；X=0有效。回血可以跳过。

箜声准备可存任意合法HE，0张视为跳过；实际独立source-local任意容量pile，不复用其他堆总数。Ending先获得当时同source非装备并等待完整gain children；选living使用者并由其选择合法剩余装备顺序，逐个真实Use，完整目标/装备替换/禁止槽/支付/children处理，不能直接移动到装备或先假赠Hand。每次真实卡仍在原source，cursor在真实支付前/后按幂等规范推进，子链移走不重复取用；非法或无法用的余牌保留原pile，不新增弃/转赠。当前合同为：**只有实际使用过至少一张装备，结束才 LoseHp1**；初始空pile、取得非装后零装备、所有装备非法或第一张使用前被移走都不扣体力；已真正使用过则余牌循环结束后扣一次，死亡/比赛结束按已有边界停止。LoseHp是真正濒死/死亡可暂停链。该零装备口径是明确采用的工程默认。

张绣注册雄乱与从谏；雄乱使用区域废除、定向政策和手牌限制。真实finalized Trick target集合distinct>1，owner在集合，最终redirect完成后一次候选；赠牌对象须在该最终集合且是真正不同recipient，交给自己的同手牌不作为支付，不制造无移动摸牌。此give语义显式冻结，不暗套任意其他living。选本人HE1，实际赠入对方Hand并完成全部movement children后才摸牌；实体Equipment给2，其他1，判断冻结实体类型而非虚拟输出类型。非法非最终目标/非法HE原子拒绝；decline无cost/reward，owner死亡不继续，真实receipt/child恢复不再支付。同owner同skill多grant对一次action去重，来源保留exactinstance。

配置字段、操作和入口能力见[当前技能配置](RUNTIME.md)；程序续接和帧权限见[结算协议](../../RESOLUTION_PROTOCOLS.md)。
