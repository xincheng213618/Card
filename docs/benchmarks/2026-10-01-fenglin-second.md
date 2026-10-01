# 诸葛瞻、陈到、孙亮验收记录

2026-10-02完成普通OL诸葛瞻、陈到、孙亮的正式内容、技能、标准选将、图鉴、三张官方立绘和共享交互。来源正文及工程解释见[来源档案](../content/sources/fenglin-second-2026-10-01.json)与[能力契约](../content/skill-composition/FENG_LIN_SECOND_CAPABILITIES.md)。保留测试裁剪、现有dirty工作和版本边界；本批未自行提交、推送或发布。

原915文件副本及原交付不变。runtime迁移后另存914文件原快照、16个Shared支持文件和提交4354914的支持变更，生成930文件有效baseline；三路仅在独立副本port。诸葛瞻、陈到、孙亮的冻结交付分别通过27、38、47项聚焦检查，这些是子代理验证，不能替代根目录验收。

父按逐文件SHA和三方比较整合，保留双方新增操作和测试注册。payment核对同时约束罪论获取以及预算/赠予的精确owner、操作、await和coverage，原共享尾部保留。陈到提供唯一actual Play-phase use ledger，孙亮复用。交叉审发现并修复真实闪accepted事实遗漏，以及虚拟闪/extended双实体无懈的issued-ban入口；后者使用既有extendedUse能力，没有扩大loader。所有新待结算数据随帧保存，集合事件在提交准备时冻结。

| 根目录检查 | 实际结果 | 用时 |
| --- | --- | --- |
| 一次fresh Full构建 | 成功；2项既有GaoDaYiHao nullable警告 | 10.497秒 |
| 同次Full Core | 315/316，1项失败 | 163.279秒 |
| 同次Full WPF | 41/41 | 57.872秒 |
| Full包装器/外部driver | exit1；源码漂移为空 | 231.714/232.403秒 |
| 修复后独立fresh定向 | Core13/13、WPF2/2，exit0 | 19.600秒，含8.756秒构建 |
| 最终无过滤日常检查 | Core92/92、WPF11/11，exit0 | 49.611秒，含1.487秒增量构建 |

Full唯一失败为Program trigger resource contracts：既有反射夹具仍传5个参数，而TryBeginBeforeDamageProgramWindow增加了可选rangeChainOnly参数。补Type.Missing保留默认值与旧JSON断言，定向复验通过。没有重跑Full，因此原Full仍记录315/316，不能写成全量全部通过。开发中的天香夹具修正使用合法固定五人牌池和seed117，未seed sweep或扩大生产规则。

三项新增WPF检查覆盖罪论私有取牌/顶序、往烈公开禁令与使用/打出、立军供牌者与主公各自选择。实际控件、暂停、checkpoint/命令JSON恢复和非法输入均检查，PNG已离线查看。提示移除了“实体成本”“预算冻结”等实现术语。三张正式PNG均750×950，字节SHA与catalog一致。没有声称桌面真人完整对局已验收。

证据根目录为临时目录Card-FengLinSecond-Full-20261001和Card-FengLinSecond-Repair-20261002。原Full完整输出test-changed-full-child.log、logs/core-full.log、logs/wpf-full.log与driver-summary/source-before/source-drift保留。首次修复包装器误复用了Full目录，覆盖原summary.json及build.log；full-summary-captured-fields.json是按父工具已观察字段重建，明确不是原文件。该次还误写FengLin Sun Liang过滤词，Core未执行、WPF2/2。失败记录保留，不计为Core通过。

正确修复使用独立Repair目录和Feng Lin Sun Liang过滤词；repair-summary.json、build-repair/core-selected-repair/wpf-selected-repair保存其原记录。Repair当前summary.json及selected日志为最后日常检查。Full后的赠予渲染被修复重生成，措辞修改前图仍在Card-FengLinSecond-root-all-ui-20261001/renders。文档更新后无需重复行为验证。

下一批当前官网普通毌丘俭413、陆抗414、许攸406已经只读预检，来源目录Card-FengLin-third-candidates-20261001。预检不等于实现；另冻结当前第二批验收后的源码，再复用三个6.1 Sol实施。

协调对话在2026-10-02暂停主区写入后，逐文件复核4860个来源SHA，无漂移；另以独立产物目录补跑一次fresh Full：Core316/316、WPF41/41，0失败，含构建共202.391秒（构建8.697秒，Core142.217秒，WPF51.438秒）。这次记录不改写上面原315/316失败历史；完整新记录保留于 C:\Users\17917\.codex\tmp\card-runtime-next-20261002\main-second-validation。
