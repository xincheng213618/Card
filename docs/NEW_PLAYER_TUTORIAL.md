# 四步新手演练

新手演练把基础操作放进四段短牌局：使用杀、打出闪、用桃回复、选择回合末弃牌。玩家可从开局面板或牌桌顶栏进入，每一段都直接停在需要操作的位置；完成当前目标后，由玩家点击「下一节」。

## 操作流程

1. **使用杀**：选择实体杀、点击亮起的合法目标并确认。亮起只表示规则允许，正式牌局仍需判断身份和敌友。
2. **用闪保护自己**：在真实的杀响应窗口中，从中央候选打出实体闪；选择不响应会被教学边界拦截。
3. **用桃回复体力**：从真实受伤状态主动使用桃，规则核心完成自身目标和回复事件。
4. **决定留下哪些牌**：在真实的回合末超限状态选择准确数量的牌，确认后完成演练。

每节均由固定种子牌局经 `StartGameCommand`、选将、推进、结束出牌、响应和弃牌等正式命令准备，没有直接改手牌、体力、牌区或询问。准备过程有 600 步上限；规则漂移导致目标位置不可达时，独立准备失败，当前正式牌局保持不变。

## 正式牌局保护

进入演练前，待写的正式牌局自动存档会先完成。视图模型保存原 `GameEngine`、自动推进、面板状态、开发者视图、手牌顺序、选牌、目标和弃牌选择，再切换到练习引擎。

演练期间：

- 临时牌局不会进入自动或手动存档；保存、读取、战报和新对局入口禁用；
- 每节只接受当前目标对应的玩家命令，其他提交不改变规则状态，并在教学条中解释当前目标；
- 完成事件来自规则核心的 `CardUseDeclaredEvent`、`CardRespondedEvent`、`RecoveryAppliedEvent` 或 `HandLimitDiscardedEvent`；
- 完成后停止继续推进，玩家可以重来、进入下一节或返回正式牌局；
- 关闭程序时不会把练习位置覆盖到正式存档。

退出后重新挂接原规则引擎，并恢复进入时的界面状态。教学本身不改变 Checkpoint、存档格式或 AI 策略版本，也不保存课程完成进度。

## 验证

最终验证使用隔离 Release 输出：

```powershell
dotnet build .\CardGame.sln -c Release --artifacts-path .artifacts/tutorial-final
& '.\.artifacts\tutorial-final\bin\CardGame.Core.Tests\release\CardGame.Core.Tests.exe'
& '.\.artifacts\tutorial-final\bin\CardGame.Wpf.Tests\release\CardGame.Wpf.Tests.exe' "$PWD\artifacts\tutorial-review\ui-final"
```

- 四个练习位置分别重复创建并比对可信快照，且都能从命令 Checkpoint 重建。
- WPF 测试通过实际命令走完四节，检查错操作无副作用、完成事件、完成后暂停、教学零存档写入，以及原状态和选牌目标的精确恢复。
- 牌桌按 1120×740 离屏渲染，未调用 `Window.Show()`；尚不代表真实鼠标、键盘、焦点和多 DPI 桌面验收。

界面证据：[开局面板入口](../artifacts/tutorial-review/ui-final/30-tutorial-entry.png)、[开始练习](../artifacts/tutorial-review/ui-final/27-tutorial-attack.png)、[单节完成](../artifacts/tutorial-review/ui-final/28-tutorial-step-complete.png)、[全部完成](../artifacts/tutorial-review/ui-final/29-tutorial-complete.png)。
