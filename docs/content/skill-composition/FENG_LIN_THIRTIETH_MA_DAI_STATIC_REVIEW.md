# 界马岱主区静态整合

696 当前普通 OL 马术、潜袭已主登记。马术复用既有 `classic:mashu`；潜袭按当前“实际出牌阶段开始展示实体”的全文实现。当前 API 为蜀、4HP，无性别字段；Male 是既有同人物身份补证，`initial_hp=0` 不作初始零体力解释。

交付基底为 `141474a2`，manifest 原始 SHA 为 `38220a6c6785d26e7f0ad14b49952c5ea755a831925619c7662fbfd241cc3cc4`。主代理核对 6 NEW、7 OLD、6 support 与 3 个版本来源基线，按单文件单 Update 应用窄补丁；NEW 原始字节与 OLD 无 BOM LF after 均与冻结清单一致。没有改写原冻稿或升版本。

实际本人 Play-start 私选手牌后，成熟展示事实冻结有效花色，发行同实际回合、原实体和当时距离恰为1的其他存活角色队列。新增 `RequiresChromaticSuit` 只让这项限制区分无色牌；旧限制省略新字段并保留原行为。展示区域、无色、额外 Play、来源失去等未核复杂 FAQ 的规则边界见 DEFAULTS。

真实用牌的 owning `CardUseFrame` 保留全部材料、原 actor、真实 provider 与已展示实体政策。候选不按初始 actor 筛掉，伤害时按实际当前 user/source 判定；两方向成熟 actor replacement 都沿原 Use 的真实事实链验证，不重写 Accepted/Declared。真实转交和再取得不改变实体身份，不为新能力增加供牌、牌堆或私有牌堆使用权限。相同技能/状态的多份展示材料对同一 Use 最多贡献一次+1，连环传导不重复加成。

两个公开政策事件只含已经真正展示的实体；其它材料身份留在可信 owning receipt。事件内 AffectedSeats/RestrictionSequences、私有 PhysicalMaterials 及帧的外层列表冻结 constructor/init/with/JSON 输入；两个公开事件均有显式 `CommittedEventProjection` 深层复制。候选与 AI 继续使用成熟本人视图和选牌机制。

四个方法已登记到既有 Core runner，首项 `boundary ma dai shown color hand ban` 加入 routine。它们覆盖手牌限制与设备响应、完整多材料伤害及暂停恢复、额外 Play 的独立队列与同实际回合过期，以及原生展示/真实异 provider 供牌。四视角 JSON 恢复后的继续命令写在草稿中，不代表已经运行。

官方原始 PNG 已按冻结 API cover、来源 SHA 和原图 SHA 登记。**未编译、执行生产 loader、运行任何检查、原生 AI、基准、routine 或 Full。** 四方法、来源失去、无色、源无关/连环伤害、牌堆/私有牌堆/声明、多份材料与双方 actor-change 组合仍待用户统一运行；运行验收为 false。
