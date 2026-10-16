# SP 赵云

`sp:zhao-yun` 已进入经典身份当前武将池，群、男性、3 体力，技能顺序为 `sp:longdan` 龙胆、`sp:chongzhen` 冲阵。人物与规则位于 `src/Characters/zhao-yun/sp/`。

## 龙胆与冲阵

龙胆允许将一张闪当杀使用或打出，将一张杀、火杀或雷杀当闪响应。转换保留实体牌、有效牌型与确切转换来源，费用由当前发布的合法动作或 Choice 提供。

冲阵是可选触发，匹配本实例龙胆的 `dodge-to-slash` 或 `slash-to-dodge` 绑定：主动杀在 `cardUseTargetsFinalized` 窗口处理，杀／闪响应在 `cardResponseAccepted` 窗口处理。技能从对应事件目标的手牌中选择并取得一张，目标没有手牌时跳过取得。

“对方”来自当前用牌／响应关系：主动杀为该目标，闪响应为杀来源，决斗与群体牌的杀响应使用当前响应上下文。目标手牌按不透明牌位选择，其他玩家不能由选择面看到牌面。冲阵完成后恢复原牌或响应链。

触发身份包含技能与转换绑定，不能仅凭最终牌型为杀或闪推定发动过龙胆。朱雀羽扇等后续变化仍受 Core 的转换合法性限制，来源链本身不授予额外转换动作。

## 配置与依据

- [人物注册](../../../src/Characters/zhao-yun/sp/Content/sp-zhao-yun.character.json)保存基础资料、技能顺序与当前池。
- [规则程序](../../../src/Characters/zhao-yun/sp/Skills/sp-zhao-yun.rules.json)分别配置两种转换及三组冲阵窗口。
- [官方冲阵 FAQ](https://www.sanguosha.com/news/20161215_4752_4616)是“对方”和插入时点的来源依据；[官方 SP 赵云资料](https://www.sanguosha.com/hero/108)及仓库 `data/reference/bwiki` 提供技能版本资料。

通用转换与私有选牌规范见 [运行时规范](../skill-composition/RUNTIME.md)。
