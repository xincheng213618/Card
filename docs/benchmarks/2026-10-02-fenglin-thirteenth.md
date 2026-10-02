# 界黄忠、界魏延第十三批

两位当前普通OL武将的正式技能、选将、界限突破图鉴和官方原始立绘已接入。Core Full 527/527通过；当前主区工作输入的冻结快照WPF Full 51/51通过，含构建42.951秒。无过滤日常Core 151/151、WPF 16/16，含构建59.923秒。Core Full原wrapper 208.807秒（其中Core 165.394秒）。详情见[来源档案](../content/sources/fenglin-thirteenth-2026-10-02.json)、[能力契约](../content/skill-composition/FENG_LIN_THIRTEENTH_CAPABILITIES.md)、[口径记录](../content/skill-composition/FENG_LIN_THIRTEENTH_RULINGS.md)。

| 武将 | 接入行为 |
| --- | --- |
| 界黄忠 | 具体杀的正有效点数替代普通攻击范围，真实丈八两成本求和上限13；无点数工程默认回到普通范围。最终每目标冻结已付杀后的HP/手牌比较，两条件独立给予防抵消和直接伤害+1。保留真实闪支付、八卦、防具和链伤结算；已经发出的事实失去来源不撤销，换actor/移除target不转赠。 |
| 界魏延 | 狂骨复用已有逐实际伤害点的回复/摸牌选择。奇谋整局一次，数量1..当前HP，真正HP apply后立即冻X，救援/HP/gain子窗后按X摸牌并发两个真实回合修正。实际ExtraPlay保留，真实新回合不继承过期修正。X21不截断：仅新增scalar付款来源放宽两种精确修正，旧null范围和序列省略保留。 |

共同实现基线a0f0003，整合时外部WPF提交2b13c543已进入主区，随后界面仍持续写入。两名GPT6.1 Sol交付18/16源文件，原日志/证据409/607逐SHA核验；28源并集中的6共享文件三方融合，2处enum/test新增块明确保留两组。根定向27Core/2WPF、29.826秒。独立最后审查仅查冻结交付与原运行证据，没有另跑测试，也不冒称审查父融合产物。

黄忠独立定向11/11、24.408秒；魏延16/16、9.078秒（原summary构建5.272秒，冻结交付报告曾写5.05秒，按原summary校正）。真实借刀/受令丈八、Fangtian多目标、actor改写、实际Dodge付款和链伤/银狮有accepted命令与冷视角恢复；source移除是host审计，target移除和代供组合含静态核对。魏延真实HP23选择21、实体桃救援、付款后source失效及实际ExtraPlay/ExtraTurn有四视角命令恢复；付款前source移除和账本损坏predicate分别列host/校验审计。

第一Main Full实际527Core/52WPF全绿，但并行WPF源码在其运行期间改变。Core/Content/核心测试的952个输入再次逐SHA相等，所以保留Core Full；为界面捕获5125文件的当前工作快照，其4955编译输入捕获期间相等，随后冻结WPF Full 51/51，源与DLL均不变。此结果覆盖那个快照，不承诺其后继续修改的live界面。原失败与输入漂移清单均保留，既有WPF检查新增/移除属于并行工作，未恢复历史套件。

第一日常152/16、73.566秒。保留原85Core/16WPF filters，仅将本批完整rank/parser矩阵移出日常，仍在Full登记和527通过中；最终88Core/16WPF filters执行151/16、59.923秒，为单次实测。中间并行WPF检查曾有Path与Shapes.Path重名构建错误；读取最新文件时已经改用Rectangle别名，父没有覆盖其文件。最后日常运行期间仍有两处外部WPF输入漂移，不能把该日常结果当作后来live界面验收。Core/content和本批生产文件保持不变；当前界面另由上述冻结Full证明。

官方26/456正文和2600/45600两原PNG作为来源；API无结构化gender，男性沿仓库同角色惯例。2016官方托管自编FAQ仅是历史参考，当前文本优先，rank0/数量/付款后source尾段等默认明列在规则记录。版本声明未改变。原图离线177将/974PNG通过；冻结WPF编译资源两新PNG原SHA匹配、GeneralArt解码750×950，primary/test/harness三份Core/Content/WPF DLL相等，六实际读取输入SHA不变。

自动化和离屏检查不代表官方客户端或人工桌面试玩；native只证普通可选发动与有限完成。主区之外无提交/推送/发布。证据根`%TEMP%\Card-FengLinThirteenth-20261002`。下一批界公孙瓒320、界华雄446完整当前文本、最小能力设计及两原图已经冻结，尚未实施。
