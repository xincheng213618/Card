# 固定手牌响应夹具与日常验证耗时

本批只替换 `HandResponseChecks.Controls` 的场景搜索及增加一个测试夹具；生产源码、测试登记、验证范围、内容、规则版本和资源维持基线。七个实际响应场景采用固定四武将池、80张实体牌库、已验证 seed31，以及9至15条真实 setup commands；不再扫描种子和完整对局。

原 `VerifyBoundary` 与 `Shortcut` 整个后缀逐字保留，包含17个断言。六类原生响应及急救仍实际进入：实体杀→闪、实体南蛮→杀、真实体力流失濒死→实体桃、实体桃园群体→无懈、实体火攻展示与弃牌、回合外红闪急救。所有实体用牌记录 Hand→Processing 成本与真实 actor/provider；急救不把体力流失描述为伤害。实际无效牌见证5张；歧义见证0，已验证现有原生桃只生成普通来源。没有为性能删除断言或制造候选。

各边界经过公开 contentRegistry 注入与真实手动存档载入。响应前后 checkpoint 冷回放比较四位 viewer、拥有状态的 typed resolution stack，保留所有80张实体的区域守恒。原按钮、选择不支付成本、Esc/数字键/F1/Enter、教程保留、上下文绑定、1120×740离屏渲染、精确一次 AnswerPromptCommand 和重复确认保护均运行。

| 主区验证 | Core | WPF | 构建 | Core进程 | WPF进程 | 总耗时 |
|---|---:|---:|---:|---:|---:|---:|
| 完整回归 | 487/487 | 48/48 | 7.822s | 149.704s | 39.339s | 196.922s |
| 无过滤日常 | 136/136 | 14/14 | 7.695s | 28.041s | 20.028s | 55.822s |

同一73个Core过滤器和14个WPF过滤器的日常验证，原实测90.830s，本次55.822s，均含构建。原独立日常 profile 的手牌响应单项35.233s，本次日常同项3.528s；本次Full中该项1.950s。这些是不同调用顺序下的单次测量，不将各阶段差值全部归因于同一优化，也不声称统计稳定收益。

验证命令为 `tools/Test-Changed.ps1 -Full` 和无过滤 `tools/Test-Changed.ps1`，使用既有 `-Verbose` 收集单项计时。构建0错误，原 `GaoDaYiHaoChecks` 两处nullable警告保留。开发01至09的失败/临时成功原日志均保留，最终09仅定向1/1通过，不代替主区Full。

冻结证据在 `%TEMP%/Card-HandResponseFixture-20261002`：`worker/delivery-01/source-manifest.json`、`assertion-preservation.json`、`main-import-01/proof.json`、`main-full-01/summary.json`、`main-routine-01/summary.json`及对应完整日志/截图/commands/checkpoints/viewers。父逐SHA核验5048个基线路径及673个交付证据，导入仅两份测试源码，其他字节一致。真实离屏WPF命令验证不等于人工桌面或官方客户端验收。

第十批界夏侯惇313、界李典317已按当前普通OL正文与冻结共同基线在独立副本实现中，尚未导入或验收。清俭采用用户确认口径：只排除技能owner自己的真实摸牌阶段；其他角色的摸牌阶段获得牌仍可发动。界徐庶304所借荐言完整当前正文仍单独核对，不阻塞这两将。
