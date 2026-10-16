# 张角版本与判定链

经典、界限突破和国战张角使用独立内容 ID 与技能程序。技能名称相同不表示窗口、目标或牌移动政策相同。

| 武将 | 基础资料 | 技能 |
| --- | --- | --- |
| `classic:zhang-jiao` | 群、男性、3 体力 | `classic:leiji`、`classic:guidao`、`classic:huangtian` |
| `boundary:zhang-jiao` | 群、男性、3 体力 | `boundary:leiji`、`boundary:guidao`、`boundary:huangtian` |
| `national:zhang-jiao` | 群、男性、基础体力 3 | `national:leiji`、`national:guidao` |

## 经典张角

有效闪响应被接受后，可以选择一名其他存活角色判定。最终黑桃造成 2 点雷电伤害；梅花先令张角回复 1 点体力，再对判定角色造成 1 点雷电伤害。最终判定触发限定在本次经典雷击原因与来源，不读取中途被替换的牌。

鬼道在判定替换窗口可支付自己的黑色手牌或装备牌，替换当前判定牌，并将被替换的牌收入自己手牌。黄天属于主公技：其他群势力角色每出牌阶段可交一张实体闪或闪电给技能有效的主公张角，次数记在该提供者的绑定使用记录中。

规则见 [经典程序](../../../src/Characters/zhang-jiao/classic/Skills/classic-zhang-jiao.rules.json)。

## 界张角

使用或打出有效闪、使用闪电时，可以令自己判定。自己的判定最终生效后，黑桃可以选择任意存活角色造成 2 点雷电伤害；梅花先回复自己 1 点体力，再可选择任意存活角色造成 1 点雷电伤害。梅花回复与可选伤害是独立绑定，跳过伤害选择保留已完成回复。

界鬼道支付黑色手牌或装备牌替换判定，原判定牌进入弃牌堆；本次替换牌为黑桃 2–9 时自己摸一张牌，随后改判不撤销该次摸牌。界黄天允许其他群角色交给主公一张闪或黑桃手牌，每提供者每出牌阶段限一次。

规则见 [界张角程序](../../../src/Characters/zhang-jiao/boundary/Skills/boundary-zhang-jiao.rules.json)，版本依据见 [来源档案](../sources/boundary-zhang-jiao-2026-09-20.json)。

## 国战张角

有效闪响应后，可令其他存活角色判定，仅黑桃造成 2 点雷电伤害。鬼道支付自己的黑色手牌或装备牌，并取得原判定牌。两项技能随所在槽位明置而启用，另一暗置槽位继续隐藏；双将体力按基础值平均并向下取整。

规则见 [国战程序](../../../src/Characters/zhang-jiao/national/Skills/national-zhang-jiao.rules.json)，模式与来源见 [国战档案](../sources/national-zhang-jiao-2026-09-20.json)。默认大厅入口与国战简化范围见 [模式说明](../../MODES_AND_RULESETS.md)。

## 共用判定边界

`JudgmentFrame` 保存当前判定牌、候选、游标与父返回信息，`JudgmentTriggerCandidate` 保存程序、绑定、技能实例和玩法哈希。替换候选和提交均通过当前合法性校验，正在提供该次八卦判定的来源八卦阵受到装备保护。

每次替换先完成原判定牌政策、费用移动与由此引起的子结算，再推进同一候选游标。最终判定窗口只消费最后的有效牌与花色；公开结果和私有支付候选分别投影。技能程序、黄天贡献与判定子流程均由通用运行模块执行，详见 [运行时规范](../skill-composition/RUNTIME.md)。
