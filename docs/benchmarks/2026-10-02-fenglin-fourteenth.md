# 界公孙瓒、界华雄第十四批

当前普通OL两人的正式技能、选将、界限突破图鉴和两张官方原画接入。父定向Core34/34、WPF2/2，24.937秒。一次combined Full为Core534/537、WPF50/52，219.802秒；三个旧反射夹具修复后Core3/3，1.977秒。当前冻结界面WPF52/52，33.643秒，使用另一次8.560秒构建产物；没有重新报告Core Full全绿。无过滤日常Core154/154、WPF17/17，含构建67.846秒，接近约一分钟目标。

| 武将 | 正式行为 |
| --- | --- |
| 界公孙瓒 | 义从始终向外距离−1，HP≤2时别人向其距离+1。趫猛对实际本人Use杀的最终受伤角色选择HEJ一牌弃置，真登记后冻结付款记录；印刷坐骑仍在该弃牌位置才获得。自源火链回传本人、手中坐骑、子窗拿走卡、来源失效、死亡和实体桃回复子窗均有明确有限续接。 |
| 界华雄 | 耀武在实际牌伤害后、濒死前冻结牌存在/有效颜色/真实来源/受益者，每次伤害摸1。红色真实存活来源摸、非红本人摸；无来源红闪电不误取占位seat。势斩每实际Play限2次，指定角色真正使用零实体无色决斗，完整走无懈、集智、响应、伤害及typed完成返回。 |

两名GPT6.1 Sol各交付16源码；父逐SHA核验公孙928 artifacts、华雄1791 artifacts和240外部DLL/PDB引用。25源并集、7共享文件三方融合，3处冲突保留两套窄恢复校验、两个receipt validator及2300/2400/2401指令。独立审计分别复核先前P2和父融合，属于只读审查，不冒称另跑测试。原5127 working-byte基线已包含并行Core手牌引导和WPF修改；整合捕获最新外部字节，不改写基线。

原Full三Core错误是private MoveCard新增可选回调后的反射参数数量；四次Invoke补第5参数null，断言和其他字节全部保留。旧表格性能夹具依赖扩展选将池而没有装备，改用基础池，单次有界probe只实查31和1，确认seed1/孙权的青釭剑79合法，保留装备实用、提交次数、缓存和布局断言。中间基础721019仍缺装备与probe编译/字段名错误都保留原日志。并行自动保存与装备/HP rail缓存代码在验证间更新，按实际SHA捕获后目标复验和52项WPF全量通过；父只拥有表格开头两行修补，其余UI工作不入本批提交。

核心全量原174.017秒，WPF原35.013秒。最新WPF完整运行的4973 source输入和28 DLL都不变；无过滤日常同样4973输入不变。日常91Core/17WPF filters保留前88Core及并行新增overlapping-hand，仅本批增3代表行为。构建9.177/Core39.604/WPF18.982秒。没有匹配旧实验，不报告提速倍数。

官方320/446完整正文、literal API cover和32000/44600原PNG冻结；离线179将/976PNG通过。新编译资源与官方原SHA相等，GeneralArt解码750×950，primary/test/harness Core/Content/WPF三份DLL逐SHA相等。版本声明未变；新增nullable scalar默认省略。详见[来源档案](../content/sources/fenglin-fourteenth-2026-10-02.json)、[能力契约](../content/skill-composition/FENG_LIN_FOURTEENTH_CAPABILITIES.md)与[规则口径](../content/skill-composition/FENG_LIN_FOURTEENTH_RULINGS.md)。

真实accepted命令、四席与旁观冷恢复和host fault injection分开记录。公孙transfer仅默认及静态核对，before-paid source取消/Response排除含host审计；华雄actor/target合法改写、dead-source、chain颜色部分沿成熟路径静态核对，native只证普通可选真实发行与有限完成。自动化/离屏结果不代表官方客户端或人工桌面试玩，也不承诺后来live UI。所有失败与修复尝试在%TEMP%\Card-FengLinFourteenth-20261002保留。仅做本地有范围提交，不推送/发布。

下一批界吕布319、界袁绍450：完整当前来源、两原画和利驭取牌/真实决斗、乱击排除、血裔人口/标记设计已冻结，尚未实现。
