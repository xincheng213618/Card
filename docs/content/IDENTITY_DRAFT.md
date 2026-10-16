# 八人经典身份的选将偏好

八人经典身份采用按人物抽取、不放回的加权候选。每个座位抽取自己的候选，同人物多个版本占一个格子；选定版本后，该人物其他版本一并退出本局将池。版本仍可在候选格内预览和确认。

## 权重与选择

`IdentityGeneralSelectionWeights.EightPlayer` 按具体版本维护 8、5、3 倍权重，未列出的版本为 1。抽取人物时使用该人物当前可用版本的最高权重；多个版本不会累加票数，候选默认预览最高权重版本之一。

权重是一次抽取的相对票数，不是整局出现率、胜率或保底名额。AI 在自己的实际候选评分上增加 `12 × log2(权重)` 偏好，仍使用本座身份、候选技能和公开信息。

最高权重包含谋诸葛亮、OL 神司马懿、OL 杜预、OL 曹纯、OL 神陆逊、OL 神孙权。具体集合以 [IdentityGeneralSelectionWeights.cs](../../src/CardGame.Content.Standard/IdentityGeneralSelectionWeights.cs) 为准，其他档位保留普通武将的随机机会。模式是否启用加权由 `ContentModeDefinition.GeneralSelectionWeights` 决定，当前仅经典八人模式配置该偏好。

## 随机与隐私边界

`GameEngine.GeneralDrafting` 使用确定性随机源按人物抽取。完整名录浏览不消耗抽样随机数，返回候选时重新抽取。候选只对所属玩家可见；权重经过注册验证、冻结并进入内容指纹，命令重放重建相同的选择过程。

## 偏好依据

这些权重是本项目的人工体验参数，参考军八环境下的输出、过牌、控场、辅助与身份适应性，不是官方统计梯度。参考资料有不同服务器和技能版本，不能把同名人物的所有版本等同处理：

- [军八梯度表武将评析，2024-02-21](https://www.bilibili.com/opus/900206633978691603)：军八评价维度参考。
- [三国咸话军八与综合评价，2025-12-24](https://xianhua.sanguosha.cn/postDetails?id=11747801)：社区候选强度参考。
- [新版曹纯强度分析，2025-03-18](https://game.xiaomi.com/viewpoint/1624366642_1742280132951_13)：OL 缮甲与身份适应性参考。

候选偏好检查可通过 `pwsh -File .\tools\Test-Changed.ps1 -CoreFilter 'weighted identity draft'` 运行；覆盖配置、人物去重、候选隐私、AI 选择、名录浏览与恢复。抽样频率不代表对局胜率。
