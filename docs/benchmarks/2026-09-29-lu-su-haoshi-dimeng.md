# 鲁肃（好施/缔盟）单武将交付批记录

日期：2026-09-29。武将：经典鲁肃（吴，3体力，神话再临·林 2010；官方 gid 50 / detail-49）。技能：好施、缔盟。前序：孙策批（1.151.0）、蔡文姬批（1.152.0）、并行邓艾/沙摩柯在制（1.153.0/规则183在制）；本批规则 184 / 经典包 1.154.0。

## 官方口径

- 好施：摸牌阶段，你可以多摸两张牌，然后若你的手牌数大于5，则你将一半的手牌（向下取整）交给手牌最少的一名其他角色。
- 缔盟：出牌阶段限一次，你可以选择两名其他角色并弃置X张牌（X为这两名角色手牌数的差），然后令这两名角色交换手牌。
- 来源与体力口径见 [lu-su 来源记录](../content/sources/lu-su-2026-09-29.json)（官方页不渲染体力，3勾玉取 BWIKI 二手核对口径；包归属神话再临·林，图鉴入 myth-forest 组）。

## 公共能力与实现要点

1. **触发条件/值表达式 `handHalfFloor`**（数值表达式 12）：floor(手牌数/2)，selectOwnedCards 支持的表达式集合扩容，限定 zones=[hand]。
2. **数值表达式 `selectedPairHandDifference`**（数值表达式 13）：按选中目标对的手牌数差求缔盟弃牌数，X=0 时既有选择链自动产出空绑定并跳过移动（count<=0 既有路径）。
3. **目标类型 `otherLivingPair`**（目标类型 22）：任意两名其他存活角色，枚举与结算均按手牌数升序（复用既有 unequalHandPair 的排序管线并放宽不等约束），主动技目标资格枚举（GameEngine.SkillPrograms 第二 switch）与触发/选择目标枚举（GetProgramTargetSeats）双路径同步支持。
4. **目标类型 `otherLivingLeastHandCount`**（目标类型 23）：手牌最少的其他存活角色（并列最少全部为候选，按座位序）。
5. **新操作 `exchangeSelectedTargetHands`**：互换两个选中目标的全部手牌（按牌 id 升序经 Processing 中转成对移动，公开移动记录 reason=skill-program.{skillId}.exchange，收尾走 cardsMoved 窗口管线；双方手牌皆空直接续接）。描述符读取有序目标对（target 固定 owner 占位，Resources=ReadTargetSet(2,2)），AI 语义按“升序对把手小的换给对方视角”估算。
6. **好施因果链接**：多摸与给予是同一选择的因果两段——触发器以 draw+grantTurnSkills(classic:haoshi-give) 承载，本轮技能在 afterNormalDraw 强制结算给予（条件手牌>5）；拒绝多摸即无授予、无给予，与官方“然后”因果一致。给予目标用 selectTarget(otherLivingLeastHandCount) + selectOwnedCards(handHalfFloor) + moveBoundCards(destination selectedTargetHand)（引擎既有目的地，描述符白名单扩容）。
7. **AI 估算**：SelectOwnedCards 估算器补 handHalfFloor/selectedPairHandDifference 两臂；目标枚举两路径的空集回退保持既有取消语义。

## 内容与验证

内容：`classic-lu-su.rules.json`（classic:haoshi / classic:haoshi-give / classic:dimeng，revision 1，最低规则184）+ presentation（好施/缔盟官方逐字文本；haoshi-give 与好施同文，作为本轮挂载技能需要独立展示键）。注册：经典包 1.154.0，AddSkill（haoshi/haoshi-give 可选触发元数据；dimeng 按 quhu 先例裸定义承载主动激活），AddGeneral（classic:lu-su，wu，3体力，AdditionalSkillIds=[dimeng]），CurrentGeneralIds 追加；规则版本 183→184（Replay.CurrentRulesVersion，183 为并行在制占用）。官方立绘（gid 50，经典形象105001，574×761）入 general-art-catalog（95 武将 868 PNG，--phase verify 通过），图鉴 myth-forest 组新增鲁肃（并行预检 BWIKI 口径纠正了本批最初的 fame-1 误分组）。

定向检查 `tests/CardGame.Core.Tests/LuSuChecks.cs`（5项，自然命令与真实决策应答）：

1. 定义与触发schema：吴3体力、身份池；好施 drawPhaseStarting 可选 [draw2, grantTurnSkills]；给予 afterNormalDraw 强制 + 手牌>5 条件 + 链序（selectTarget least-hand → selectOwnedCards handHalfFloor → moveBoundCards selectedTargetHand）；缔盟 usesPerTurn 1、pair 目标 2、效果链（差值弃牌→换牌）；schema拒收样例（handHalfFloor 非手牌区、pair 目标数≠2、exchange 非 owner 占位）。
2. 好施多摸+给予并回放：接受后摸4（2+2）、给予恰 floor(半数)、受牌者为并列最少手牌者、checkpoint 暂停→还原→同决策全等。
3. 拒绝好施跳过给予：无给予移动。
4. 缔盟等手牌：X=0 无弃牌、恰互换两手牌区、ProgramSkillResolved、回放全等。
5. 缔盟差值弃牌：借助好施先给予构造手牌差，弃置恰=差值（来自鲁肃手/装备区）、互换后手牌数对调。

## 门禁

- 定向：Lu Su 5/5 全绿。
- Release 全解构建：0警告0错误。
- 全量 Core：587/591（4 失败全部为并行批次在制测试：Classic Deng Ai Zaoxian/Jixi ×2、Classic Sha Mo Ke Jili ×2，其作者迭代中；本批引入测试与既有面无一失败）。另修目录 fixture 一处：moveBoundCards 目的地白名单扩容后 selectedTargetHand 不再是拒绝样例，改为枚举层非法值样例（意图保持）。
- WPF 过滤：general portraits 过滤器缺立绘名单不含 classic:lu-su（余 4 项 classic:xu-sheng/boundary:xu-sheng/classic:zhang-xiu/ol:shen-guan-yu 为并行批注册先行的既有缺口）；complete matches 过滤器通过（含鲁肃入池完整对局）。
- 格式：本批全部编辑文件（含既有与新增）dotnet format whitespace 0 条。
- 一处意图保持修复（并行在制回归，非本批引入）：并行写入者把单目标 selectTarget 的 skipIfNoTarget 豁免从 pindianWon 改为 IsCardActionCategoryBranch，误删拼点豁免、破坏已提交驱虎（包加载即失败，本批定向测试首先暴露）；恢复 `pindianWon || IsCardActionCategoryBranch` 双豁免。

## 批次边界与并行状态说明

- 收尾时主工作区含并行批次（邓艾/沙摩柯）在制内容与两处在制回归：classic:zaoxian choice-group 校验错（326 项全量失败的归因面）与 selectTarget 拼点豁免被其 card-action-category 改写误删（破坏已提交的驱虎，本批按意图保持恢复 `pindianWon || IsCardActionCategoryBranch` 双豁免并如实记录）。本批全量与 WPF 面验证以 HEAD 基线+本批文件的快照 worktree 复核，规避在制撕裂。
- 好施给予在 afterNormalDraw 而非“摸牌阶段结束”字面窗口：摸牌阶段内无其他摸牌事件，二者等价；若后续批次的摸牌阶段内插入型效果落地，需复核该等价性。
- 缔盟弃置牌不足（手牌+装备区<X）时技能取消，未实现官方细则备选；AI 的缔盟发动/目标选择为保守估算（非强度优化），记录为后续改进面。
- 图鉴分组采纳并行预检的神话再临·林（2010）归属，myth-forest 组；经典形象皮肤 105001 已下载入库并登记哈希。
