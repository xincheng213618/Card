# 未获官方 FAQ 的工程默认

以下与官网正文分开，尚未通过用户统一实测。

- 距离是有向最终值：仅 other，目标当前 HP≤自己 HP 时为 1，抵消马与其它加减距离；本人仍 0。在实际公告目标时冻结该目标资格，前面目标的付款/HP 孩子不回头重判已公告目标；真正后来追加目标另行冻结一次。
- 追击的“一张牌”采用本人合法手牌/装备 HE。没有合法牌且没有可重铸装备时，此机会无付款结束；不伪造弃牌、重铸或摸牌。
- 空装备不提供重铸选项。全部装备包含一般装备；包含 generated 武器时不提供 all-equipment 重铸，避免部分成本假作“全部”。单牌弃置则允许 generated 装备，沿成熟 MoveCard 真正销毁到 OutsideGame 及确切原 Equipment ledger；不把该弃置误认普通重铸。
- 重铸 all-equipment 成本在实际选择提交时固定；装备的真实弃置 atomic batch 和白银狮子回复/替代回复全部先结束，再尝试等量真实摸牌。真实尝试摸零牌与未尝试收益分开。
- 已付目标的重铸动作不依赖原马超随后失源/死亡；目标死亡或 winner 落定取消未发行 reward，但保留真实 CardRecast(0) 成本完成事实，不发行不存在的 DrawIssued。已经发行的 draw 孩子仍 drain，未付后继取消。
- 同一已公告目标在失源/重获或装备变化后不获得第二机会。来源 actor 真替换后仅沿成熟真实 actor-chain 准入；provider 和原材料不重写。target 当前不再合法/已死的未付机会取消。
- 誓仇保留普通次数、目标禁令、防具、实体/转换规则，单独只扩目标数。两材料主动 UseSelectedCardsAs Slash/FireSlash 与丈八保持独立 typed return；generated 武器不能成为此新增多材料模板的 Processing 付款。原单材料/旧能力默认不变。
- 原有 next-use 600 加成在付款/宣告之前一次冻结；实际宣告对原 token 的消费照旧。已经发行能力不得通过消费后重新读 current token 降低 FrozenMaximum。合法后续增加目标也不拿原 FrozenMaximum 拒绝自身已付返回。
- 620/5403 的真实 fire 转换用原 OriginalAction 为已发行材料资格基准，当前 fire/action/追加 source 的准入仍由成熟 assertion 与 changed fact 精确核实。没有任意转换链例外。
- legacy 神速等成熟固定选取不重塑为 Action Use；现有追加目标能力的真实事实仍接受一次追击。尚未新增任意 legacy 固定 producer 的选择 UI 或虚构实体。
- 公共付款 ID 仅在真正公共弃置/销毁之后出现；未付 foreign hand 的选择只供实际目标本人。原牌名及 ID 不进入 Offered 事件。AI 只读自己的公开 choices 及公共装备，不读别人的暗手。

检查均为未运行草稿。仅源码静态复核的组合包括 600+7341 的 token 消费上限、5403+丈八、generated 单弃、winner/目标死亡取消、嵌套 Damage/AttackHpLoss/Dying 的完整救援返回与后来追加目标交替 7000/7200。四草稿包含普通实体、两材料、丈八、白银狮子 HP+cost movement+recast gain、paid 失源、legacy/coexisting 窗口及 native commands；这些也没有声称已经通过。
