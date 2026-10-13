# 诸葛版本与友系列接入

## 范围与版本

用户选择 OL 谋诸葛亮，并要求补齐移动版友系列。常规官网列表中的三位与老友季活动推出的两位均属于本批范围。

| 武将 | 版本来源 | 技能 | 体力上限 |
| --- | --- | --- | --- |
| 谋诸葛亮 | OL 官网 717 当前版 | 知天、武靖、智绝 | 3 |
| 友诸葛亮 | 大陆移动版官网 604 | 演策、方遒、共砺 | 3 |
| 友庞统 | 大陆移动版官网 605 | 漫卷、养名、共砺 | 3 |
| 友徐庶 | 大陆移动版官网 606 | 侠行、启诲、共砺 | 3 |
| 友石韬 | 大陆移动版 2025 老友季活动版本 | 钦英、论雄、共砺 | 4 |
| 友崔钧 | 大陆移动版 2025 老友季活动版本 | 顺逸、鄙位、共砺 | 3 |

友石韬的大陆活动版与台湾后续正式版存在技能差异，本批不混用。三位常规友将的共砺在身份模式无效；两位活动将按各自技能正文处理。

## 接入边界

新增武将通过 `GeneralContentModule` 进入现有正式身份武将池。诸葛亮、庞统与徐庶使用既有 `CharacterId`，同人物版本由真实模式注册表生成，不增加额外候选槽位。图鉴新增“友”系列，谋诸葛亮仍归“谋”系列。

立绘使用来源可核对的本地图片；来源地址与哈希记录在 `docs/content/sources/zhuge-and-friends-2026-10-13.json`。图片作为 WPF 资源编译，不要求运行时联网。

新规则只随新内容定义生效，未提高历史规则版本、JSON 格式版本或内容包版本。状态与付款续接保留在所属运行帧；玩家只能通过 `CreateSnapshot(viewerSeat)` 读取公开或本人私有信息。

## 验证

最终定向命令：`tools/Test-Changed.ps1 -CoreFilter 'Mou Zhuge Liang','Friend trio','Friend Pang Tong','Friend events' -WpfFilter 'Zhuge and friend series:' -ArtifactsPath .artifacts/zhuge-friends`。13 项 Core 与 1 项 WPF 全部通过，总耗时 21.470 秒。摘要见 `2026-10-13-zhuge-friends-focused-summary.json`，实际日志见同目录的 `2026-10-13-zhuge-friends-core.txt` 与 `2026-10-13-zhuge-friends-wpf.txt`。

Core 覆盖私有观顶与真实火攻付款、智绝阴阳转换和未伤害收益、武靖全局非手牌弃置与次数、演策完整奖励和全错付款、方遒公开预测、漫卷本次获得材料及牌堆顺序、养名真实用牌和花色排除、玄剑生成装备和同实体回收、启诲实际使用与纯打出区分、钦英真实决斗和双方共享弃牌次数、鄙位退款、论雄点数递增、顺逸私有扣置与实际回合返还。重点付款与子结算包含冷恢复及四视图回放；预测视图与公开事件的集合包含冻结断言。

WPF 使用真实身份模式注册表，点击版本按钮并确认友诸葛亮，核对诸葛亮两项新增版本及庞统、徐庶友版本均不增加候选槽位；核对“友”系列五位均可用且有各自立绘。首轮通过真实提示提交秘密预测后，技能栏保持剩余顺序与进度。代表渲染图：`2026-10-13-zhuge-new-editions.png`、`2026-10-13-friend-series-gallery.png`、`2026-10-13-friend-private-prediction.png`。

玄剑复用既有通用武器牌面与装备条，运行时覆盖“玄剑”名称；完整牌面渲染见 `2026-10-13-zhuge-friends-card-artwork.png`。两位活动友将采用大陆 2025 活动正文，共砺按各自活动正文处理。

日常回归发现并修正玄剑图片缺失、标准注册表新衍生牌总数断言，以及两项旧版负向夹具未排除新增 Round/实际结束返还能力的问题。旧规则边界断言保留。随后 16 项 Core、4 项 WPF 定向回归全部通过，共 19.838 秒（含 7.479 秒构建）；此范围包含本批 13 项规则和 1 项界面检查、上述共享注册和旧规则检查，以及牌面、装备条和当前并行开发的判定反馈检查。摘要见 `2026-10-13-zhuge-friends-final-focused-summary.json`。本批没有修改判定反馈实现。

审核文件的实际 SHA-256 记录见 `2026-10-13-zhuge-friends-source-hashes.json`；共享文件仍保留其他并行修改。

日常命令 `tools/Test-Changed.ps1 -ArtifactsPath .artifacts/zhuge-friends`：Core **482/482**、WPF **72/72** 全部通过，共 **172.446 秒**（构建 1.614 秒，Core 71.105 秒，WPF 99.387 秒）。摘要与实际日志分别见 `2026-10-13-zhuge-friends-routine-summary.json`、`2026-10-13-zhuge-friends-routine-core.txt`、`2026-10-13-zhuge-friends-routine-wpf.txt`。当前日常范围来自 `tools/VerificationScopes.psd1` 的 301 项 Core 过滤器与 39 项 WPF 过滤器；实测超过约一分钟目标，本批定向 20 项为 19.838 秒。未执行手动 Full、发布、打包、提交或推送。

WPF 控件渲染属于自动检查，未做前台鼠标键盘试玩或多 DPI 验收。

## 临时输出

本任务的隔离构建根为 `.artifacts/zhuge-friends`、`.artifacts/mou-zhuge`、`.artifacts/friend-trio`、`.artifacts/friend-events`。自动审批拒绝批量删除，返回 `blocked by policy`；未改用其他删除方法重试。现存 10 个 `bin`/`obj`/`renders` 子目录保留并登记到 `tools/cleanup_queue.json`。绝对路径和原因见 `2026-10-13-zhuge-friends-cleanup.json`，用户可运行根目录 `clean.cmd` 重试。摘要、日志与必要渲染证据保留。
