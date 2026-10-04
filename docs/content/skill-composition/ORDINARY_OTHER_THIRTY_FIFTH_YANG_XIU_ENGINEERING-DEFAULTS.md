# 杨修工程默认（非官方 FAQ）

- 当前完整正文仅有啖酪/鸡肋，没有另行复杂 FAQ；以下未由官网声明的细节均为工程裁定。
- 啖酪按真实用牌目标集合判断；借刀的杀受害者不算锦囊目标。当前延时锦囊成熟入口只能单目标，故无额外虚构延时多目标入口。短牌堆实际 Draw0 仍继续同牌对本人无效，不发假的获牌/实体。
- 啖酪先提交实际摸牌再结清其完整 children。已签发后 source suppressed/失技或本人死亡不重复摸牌，仍完成同 owning Use 的本人无效及 typed parent return；winner 沿原成熟用牌清理入口，不新开回应/效果窗口。
- 鸡肋无伤害来源或未付窗口显式来源已死亡则不发行；自伤来源本人有效。已声明后来源技能失效不撤销；距离变化不相关，实际额外阶段不重置，实际回合结束统一失效。
- 限制实体当下 Hand 的 intrinsic 有效类别：锁定本人身份改变采用既有机制；任意可选 viewAs 不先把实体改类。多个受伤声明类型合并；不把禁弃等同不计手牌上限。
- 鸡肋牌超限，先按全部计入上限的牌求 overflow，再弃 min(overflow,可弃数量)；保护牌可留在超限手牌中，无可弃牌时不发布无解提示。
- 给出、获得、转移、放顶、普通 equipment 替换引发的已装备移除不等同本人弃置受保护手牌；别人弃该人的手牌不受该 self policy 影响。真实废弃费用必须 full legal payment，否则未付款取消/不获收益；cleanup 到弃牌堆不借用 self-discard 禁制。
- 私有 owned-selection 实际选择者沿成熟 cardOwner chooser / SelectionActorSeat；foreign chooser 必须沿真实参数。未来未选弃置分支不得误拦给出分支。公开政策不表示受限手牌实际包含任何类别，未暴露私有 IDs。
- 不修改中央版本常量。当前 schema 值来自权威源；新测试使用 RulesSchemaVersion/PresentationSchemaVersion，未复制数值。

未执行：compiler/build/production loader/Core checks/WPF checks/UI/native game/benchmark。固定小夹具中每个行为仍需 root 后续统一实际验证；没有种子扫描、长局、直接写状态、假事件或帧注入。
