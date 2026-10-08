# R21：晋国左棻完整接入

当前 OL 普通左棻已注册，晋国、女性、基础体力 3；完整诏颂／离思与官方立绘已接入图鉴。[官方技能页](https://www.sanguosha.com/hero/354)。

诏颂在他人实际摸牌阶段结束后由该角色正面交出一张手牌，按类型发给其诔、赋、颂之一；持有者独立于原技能来源，分别在原生濒死、出牌阶段开始、单初始目标杀指定目标时选择消费。诔回复至1并摸1，赋在同一角色的手牌、装备、判定区域选至多2张弃置，颂追加至多2个正常合法目标。当前官方定义无2021旧版惩罚。离思按实际回合归属与真实使用资格，在原素材完成进入弃牌堆后给一名手牌数不大于自己的其他角色；同次使用的多素材归组询问一次，已支付尾部保留原生窗口、恢复及费用边界。

六组新增行为检查围绕三种标记和原生使用／纯打出边界，包含公开赠牌、盲选暗手牌、来源失效、真实 Gain→HP 资格抑制→SkillsChanged 子窗、单／双素材闪及真实无懈可击、费用一次性、只读集合与四视图冷恢复。实际通过的入口及各次失败见验证 JSON；未将静态实现等同于动态覆盖。

定向 114 Core＋4 WPF，0 失败，108.5 秒。常规 422 Core＋21 WPF，0 失败，234.0 秒；一分钟目标仍未达到。未运行人工全量验证。实际开发尝试、失败、日志哈希与耗时见[验证记录](2026-10-09-content-continuation-round21-verification.json)。

验证前后 HEAD `fe90b66788b7d1866fa1e87f02d4993ad7bd0f3e` 与 3582 项输入一致，聚合 SHA `d59c951ec43f0765f58c1e811cfcc7e530eec7174a05720faca84d8b6079c8a8`；定向／常规程序集与各项目副本一致。实际版本源 {'replay': 204, 'schema': 62, 'classic': '1.164.0', 'standard': '1.15.0'}，无逐角色提升。

本轮覆盖边界：No new dynamic fixture for Fu equipment or judgment discards, including Silver Lion or Wooden Ox cleanup.；No new dynamic fixture for off-turn actual materials that have partly left DiscardPile, native pile materials or late-acquired legacy response sources.；No exhaustive combination test for every nested damage, dying, faction request or replacement interaction; narrow typed invoices are statically checked.。

历史临时目录清理仍被先前自动审批拒绝，本轮复用输出并仅做只读审计；精确路径、文件数、字节数和消费者情况见验证记录 cleanup。
