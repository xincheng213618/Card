# 界诸葛亮、界孙尚香第十二批

当前普通 OL 两位武将、正式技能、选将、界限突破图鉴与原始立绘已在主区接入。一次 Full 核心517/517、WPF50/50，含构建192.765秒；无过滤日常核心148/148、WPF16/16，含构建58.989秒。共同基线 `d8acdc5c4321042cd2721ddcf4d6b8443ec4d472`；两名 GPT 6.1 Sol 在隔离副本实现，父核验21/16源码及原运行证据，在32个源文件并集中三方合并5共享文件，无冲突。原测试登记与剪裁、全局版本声明保留。

正式 ID 为 `boundary:zhuge-liang`、`boundary:sun-shangxiang`；官方440/443页面完整正文及44000/44300原始PNG作为来源。结构化性别缺字段，沿现有同人物 Male/Female 定义；详见[来源档案](../content/sources/fenglin-twelfth-2026-10-02.json)、[能力契约](../content/skill-composition/FENG_LIN_TWELFTH_CAPABILITIES.md)、[口径记录](../content/skill-composition/FENG_LIN_TWELFTH_RULINGS.md)。

| 技能 | 实际实现 |
| --- | --- |
| 观星、空城 | 每次启动按真实存活数看最多5或3，短堆照实；完整牌面仅本人可见，部分排序与冷恢复保留初始观看集合。准备阶段成功把非空观看集合全部置底，才授予本实际回合结束再次观星资格。两次各自限一次；正常/额外回合及scheduled子阶段走真实拥有帧返回。空城复用原锁定杀系和决斗目标禁止。 |
| 结姻、枭姬 | 两个主动入口共享真实出牌阶段一次额度：弃一手牌，或把一件真实手牌/装备区装备置入男性装备区。区域/槽容量/no-op纯查询同时用于菜单、提交和付款；真实替换/转移/枭姬/白银狮子及HP子窗完成后冻结两人HP，较大者摸1，较小者回复1，后续摸牌子窗不重算比较。枭姬复用原定义。 |

相等/self无奖励、Male self手牌真置入可行而原装备原区no-op不可付款、全底资格同实例而新实例不继承，是本批工程解释，未宣称官网FAQ或额外用户裁决。新共享能力通过明确节点/nullable字段启用；旧null序列、排序/付款/AI路径保持。加载器限定装备必须源于原单卡unconditional CaptureSelectedCards；HP Freeze后只允许Draw/Recover尾序列，拒绝重新选择、独立阶段或追加付款。

独立定向：诸葛亮新五组与旧free/exact共8/8、6.342秒；新私有WPF行为1/1、10.771秒；最后共享解析并集8/8、6.192秒。孙尚香五新组与七旧回归12/12、13.397秒。父合并定向核心23/23、WPF2/2、24.216秒。这些都是开发过滤范围；完整验收数字来自主区原Full日志。

实际命令覆盖观星低人口/短堆/分区/真实Normal与Extra Ending/scheduled父恢复/私有输入/可选native AI；17正向停点与旧Pindian路由、AI停滞故障前缀保留。唯一实际观星提示路由修补仅Population非空分支。结姻覆盖共享quota/真实额外Play、手牌与装备付款/替换/容量0和2/self/equal、付款后与Draw子窗后HP变化、普通native两入口、白银狮子HP observer activate/skip，以及装备失去至HP0后死亡或原手实体桃救援。accepted命令与四视角冷恢复分列；可信栈不作玩家私有快照。

诸葛亮 source失去/重授单独采用host审计，死亡等使用真实命令；孙尚香非死亡source loss未另做命令探针，沿原source-valid路径静态复用。独立最终审查复核两份不可变交付，未发现限定范围可达缺陷；审查未另跑验证、未审父三方合并产物，父合并产物由定向与主区Full实测。原失败日志均保留，fixture身份额外HP、隐藏Hand.Count、缺mode、编译和Completed拒绝与真实产品异常区分，没有据此宽化通用observer/parent-return。

Full各步：Build selected scope 7.947秒、Core full checks 145.201秒、WPF full checks 39.580秒。日常：Build selected scope 8.150秒、Core selected checks (85 filters, union) 31.968秒、WPF selected checks (16 filters, union) 18.806秒；保留原81Core/15WPF过滤项，只增加4Core代表与1WPF新私看行为。本次58.989秒是单次实测，接近约一分钟目标，未冒称统计性能改善。构建零错误，两个既有GaoDaYiHao nullable警告保留。

离线原图目录175名武将、972张PNG通过；Full编译`.g.resources`两张新立绘原始SHA等于官方PNG、GeneralArt解码750×950，三份primary/test/harness的Core/Content/WPF DLL相同，六实际输入读前后不变。自动化及离屏验证不代表官方客户端或人工桌面试玩。证据根`%TEMP%\Card-FengLinTwelfth-20261002`。

下一批界黄忠26、界魏延456当前打印正文完整，最小能力设计已冻结；界赵云302已有正式登记，不重复新增。界孙权442当前救援需要真正回复前替代及显式typed返回，已有独立恢复producer架构设计，仍未实施。界庞统借用技能和界徐庶荐言完整正文缺口继续保留，不猜引用接口或历史版本。
