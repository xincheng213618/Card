# 640 工程默认与未核 FAQ

以下是根据当前官方全文作出的工程默认，没有取得额外官方 FAQ；与明确正文分开记录，不混用经典吴国太、其它产品或历史增强版。

- 甘露选定两名 living 角色时读取其当前装备数及本人当前 lostHP。差 X 大于 lostHP 时支付整个 X，不支付 X-lostHP；这部分来自当前明文。freeze 点为真实角色选择 commit，不是激活按钮或付款结束。
- 付款区默认本人 HE。source equipment 不作为该来源技能的合法自付实体，防止可用数包含无法选择的成本。一般真实可弃手牌和装备均可选择；不足 X 不提供需要该成本的 pair，未付时不先交换。
- 沿成熟无效交换过滤，两个装备区都空的 pair 不提供。官方没有明说双空限制，因此这是减少无效发动的工程默认。已经冻结并足额付款后，同一 pair 经孩子变化为双空时，完成无动作交换尾部，不退成本也不重新计价。
- 支付完毕后交换原 pair 的当前装备集合：先让真实支付/恢复/得失牌孩子返回，不重新读取 X/lostHP 收费；装备增减可能改变实际交换的实体。slot capacity、木牛流马附属牌、溢出弃置、生成武器归 OutsideGame 等均沿成熟原子交换规则。没有把“冻结后忽略装备变化”冒称官方。
- 已支付后原 source instance 死亡/失效、owner 死亡、任一 pair target 死亡或确定 winner，保留真实支付和完成孩子，取消未执行交换；没有 source-loss 换 instance 续接、恢复原成本或重付。技能本来在新出牌阶段的 phase usage 照既有机制重置。
- 补益“一张牌”默认 HEJ。正文没有限定手牌或提供具体区域 FAQ；此默认区别于经典仅手牌版本。手牌为本人可见/他人 opaque slot，装备与判定公开；不因 Basic/NonBasic 预过滤他人的暗牌，AI 不检查暗牌实体身份。
- Basic 结果仅公开效果失败这个分类事实，不展示实体，不把实体或牌名放到 chooser 私密 program binding、公开事件、receipt 或 PrivateRevealedCards。NonBasic 以 victim 为私密 binding 的 selection actor；真正 discard 后实体经成熟 public movement 才公开。没有独立 reveal 步骤。
- 类别沿成熟 bound-card 分类，以实际实体的 Kind 判断 Basic/Trick/Equipment；不把一次可选的视为用牌宣言当作这张区域牌已经变类。持续身份规则改变区域牌类别的额外 FAQ 没有核实，本批没有扩大旧分类查询。
- 非基本牌实际弃置一次，完整成本的 SilverLion/HP/movement children 返回后再实际回复一点。移除 SilverLion 本身先回复一次，因此受益者可能从0到1，再因补益从1到2；正文的“回复1点”不按恢复至1实现。
- 选牌时 victim 已无牌/已不濒死/死亡、source instance 失效或 winner 已定，取消尚未发生的成本；真实付款后 victim/source 的死亡或失效同样只取消未发生后继。source/victim 是精确入口身份，不改为后来其它 Dying 角色。
- 自然存在的装备保护、slot capacity、国战私密主副将视图沿既有规则；本能力不新增公开隐藏势力字段。API 没有性别字段，Female 来自已有经典身份而非当前 API 验证。

未运行边界：所有四 checks 与 prepared/cold 断言都是静态草稿，未加载、编译或执行。真 Jiuyuan 的 Wu 获技 SilverLion Replacement、复杂 Dying 自救/酒救援后缀、费用中死亡/赢家、木牛/多装备槽、判定牌支付、来源装备授技撤销，以及 AI 主动甘露的角色态度最优选择，本轮只有代码审查或复用成熟 proof，没有声称新的实际验收。
