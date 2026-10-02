# 界黄忠、界魏延规则口径

依据当前普通 OL 官网[界黄忠26](https://www.sanguosha.com/hero/26)、[界魏延456](https://www.sanguosha.com/hero/456)完整正文；原文、API元数据、HTTP UTC及原始立绘SHA见[来源档案](../sources/fenglin-thirteenth-2026-10-02.json)。API无gender字段，男性沿仓库已有同角色定义；initial_hp零值不作游戏实际初始体力。

烈弓只改变具体杀的范围：正有效点数替代该杀普通范围，既有明确无距离限制资格保留，玩家通用攻击范围不变。2016官网托管[黄忠攻略](https://sanguosha.com/news/20161026_7095_5710)明确标为自编FAQ，其丈八两牌点数和上限13只作为真实丈八转换的有限参考。当前正文未说明其他无点数杀，沿普通范围为工程默认，不从任意多成本牌求和或最大值。当前正文优先，历史攻略的不能响应与当前不能抵消不可互换。

指定目标后采用现真实finalized窗口，流离后最终目标分别固定双方HP/手牌，杀已经支付。一个可选发动按两条件分别生效：手牌条件防抵消、HP条件伤害+1，同时符合两项一起执行。多目标各自receipt、各自增伤，不把一名目标条件授予全体；链伤不再多一份增伤。当前正文未给完整多目标/timingFAQ，这些是工程解释。

不能抵消保留真实闪、八卦及代供响应的支付和子窗，再令抵消失败；不能使用闪或不能响应是另一语义。防具/技能原有无效与免疫不自动被烈弓穿透。增伤、酒、白银狮子、藤甲、减伤与转移继续走成熟结算。

狂骨复用已有逐实际伤害点、伤害时距离冻结的boundary:kuanggu，不变成旧只能回血定义。奇谋整局一次，实际出牌阶段本人发动，选1..当前HP；范围参考2016官网托管[魏延攻略](https://www.sanguosha.com/news/20161026_9772_0011)的自编FAQ，不能称当前正式FAQ。旧攻略奇谋没有摸X张，当前456正文才是本批完整效果依据。

X在本次真实体力apply后、规则事实与救援/HP/gain子窗之前固定，按实际loss摸X，不用救援后HP差或先前损失。四段有限序列为付款、摸牌、减距、杀次数；各自只执行一次。活着的付款者在精确同实际回合已付tail内来源失效仍完成收益，真实死亡则终止剩余收益，为明确工程默认；已发回合修正来源失效不撤销，实际回合结束到期。额外Play保持修正与整局限次，真正额外回合不继承已过期修正。

[能力契约](FENG_LIN_THIRTEENTH_CAPABILITIES.md)列出拥有帧、输入、暂停恢复及新能力边界。本批实现已冻结并在隔离整合副本融合，主区验收仍待实际检查；界孙权恢复前替代及界庞统/界徐庶借用全文资料另行预检。


新两种烈弓事实各自核真实 owning CardUse/action/issuedActor/最终 target 与正式 trigger/effect identity；source 失效不撤销，角色重写与目标移除保留 inactive 事实而不转赠。实际 multi-target、chain、银狮、丈八受令与借刀付款有接受命令和各视角冷恢复。失去来源采用明确 host 生命周期审计；本批未新增独立 target-death 命令 fixture。旧借刀 AI 不识别只丈八可出仍 decline 的限制保留。

奇谋实际 X 可大于20。只有两种新增 reader 能发行 scalar PaidHpLossOrigin 的精确 producer/query/amount 修正，旧 null 距离上下限保持。receipt 的两项 GrantSequence 必须关联真正账本对应 owner/parent/effect/source/实际 turn/query/amount 与原始付款事实，空 sequence 不可隐藏已发修正。实体桃救援、付款后 source 失效、实际 ExtraPlay/ExtraTurn 与 X21 采用真实接受命令；付款前移除 source 和账本损坏 predicate 另列 host/校验审计，不冒称篡改 checkpoint 命令证明。
