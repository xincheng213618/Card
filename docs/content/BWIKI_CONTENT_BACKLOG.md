# 武将规则来源与版本核对

来源用于确定本项目采用的具体产品线与技能版本。当前是否实现由人物资料、技能规则、运行时注册和行为证明共同确定；页面收录数与抓取结果不是仓库完成度。

## 来源优先级

1. 所选产品线的官方武将页、公告、FAQ 与精确客户端技能条目。
2. [BWIKI 武将图鉴](https://wiki.biligame.com/sgs/%E6%AD%A6%E5%B0%86%E5%9B%BE%E9%89%B4)用于发现和区分版本；页面修订号与快照用于复核当时文本。
3. [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/%E6%8A%80%E8%83%BD%E6%A6%82%E5%BF%B5%E4%BB%8B%E7%BB%8D)用于标签、执行形态和术语。
4. 第三方规则重印与开源实现作为补充，注明来源等级和适用边界。

离线参考数据及刷新命令见[参考数据](BWIKI_REFERENCE_DATA.md)。每个人物的来源 JSON、合同与工程默认保存于 `sources/`、`generals/` 和 `skill-composition/`。

## 记录内容

- 稳定人物/技能 ID、产品线、规则版本或页面日期、人物属性、技能顺序与原文。
- 实际来源 URL、抓取状态、页面/客户端条目、原始证据及需要的内容哈希。
- 官方 FAQ、来源之间的差别、已选择口径和无法由资料证明的工程解释。
- 头像/立绘来源、资源键和哈希；素材下载、绑定与规则行为分别核实。

当前页面与历史公告的同名技能不自动继承。原文完整也不证明派生技能、随机池、选项顺序、期限和特殊牌区处理已经明确。

## 当前几个独立版本的规则入口

| 人物/规则族 | 当前说明 |
| --- | --- |
| SP 赵云 | [龙胆转换来源与冲阵对抗主体](generals/SP_ZHAO_YUN.md) |
| 张角 | [经典、界与国战判定规则](generals/ZHANG_JIAO_JUDGMENT_MIGRATION.md) |
| 神关羽 | [经典武神与武魂](generals/CLASSIC_SHEN_GUAN_YU.md)；OL 版独立配置 |
| 曹丕 | [当前经典身份定义](generals/CLASSIC_CAO_PI.md) |
| 风林山人物 | [来源与当前配置定位](generals/CLASSIC_FENG_LIN_SHAN_REMAINS.md) |

规则文件位置由[人物模块说明](../CHARACTER_MODULES.md)定位。[运行时](skill-composition/RUNTIME.md)说明字段与执行边界。具体裁定仍以对应合同的来源和明确工程默认为准，不把人物注册或某次测试结果替代规则证据。
