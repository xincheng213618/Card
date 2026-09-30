# 张春华批次：绝情 / 伤逝（2011 官方现行口径）

- 日期：2026-09-30
- 批次：classic:zhang-chun-hua（张春华，魏，3 体力，一将成名2011）单武将
- 运行时：schema 62，规则版本 193，经典包 1.164.0（基线 c4e688a 为三分支整合态 192/1.163.0；RUNTIME 文档 V70）
- 状态：**已完成**——引擎扩展、注册、定向检查、全量回归、WPF 过滤器、Release 构建与立绘登记全部落地
- 交付方式注记：隔离副本在批内被外部目录清理事故删除一次（merge-audit 清理，未提交工作因尚未写盘而无损失，/tmp 立绘幸存），分支 batch/zhang-chun-hua 自 main-verify 仓库重建后继续交付；批主体（引擎+内容+注册）在行为检查通过前即先行本地提交（b8ce62f），符合防外部删除纪律

## 官方文本与版本口径（专节）

来源：`docs/content/sources/zhang-chun-hua-2026-09-30.json`。

十周年官方武将库 **gid 295**（`https://x.sanguosha.com/hero/295.html`）现行页面文本：

> 【绝情】锁定技，你即将造成的伤害视为失去体力。
> 【伤逝】每当你的手牌数小于你已损失的体力值时，你可以将手牌数补至等同于你已损失的体力值。

- 三源核对：BWIKI（`wiki.biligame.com/sgs/张春华`：经典版 3 勾玉、魏、女、一将成名-2011；绝情同文、伤逝作"当你的手牌数小于X时，你可以将手牌摸至X张（X为你已损失的体力值）"，同义异措以 x 站为准）与官方一将成名2011 十一人名单公告（张春华在列，曹植、高顺、陈宫、法正、凌统、马谡、吴国太、徐盛、徐庶、于禁、张春华）。
- x 站武将总表 gid 295 分组标注"一将成名2011"；gid 1605 为另一版张春华（界/晋），非本批口径。
- 页面无绝情/伤逝单列 FAQ 细则（BWIKI 页亦无），伤害→体力流失转化的边界语义（不解铁索、无伤害事件、无来源死亡奖惩）按通行规则通读实现并照实记录于诚实归因。
- 立绘：gid 295 经典形象皮肤 **129501**（`https://web.sanguosha.com/10/pc/res/assets/runtime/general/big/static/129501.png`，574×761，sha256 `1cca3cbc…0718c4`），下载入库 `src/CardGame.Wpf/Assets/official-zhang-chun-hua.png`。

## 实现设计

| 技能 | 形态 | 依赖 |
| --- | --- | --- |
| 绝情（锁定技） | 技能级 `cardPolicies`：`convertOutgoingDamageToHpLoss`（cardKinds 空=全部牌类，condition always） | 新共享 policy 消费臂 |
| 伤逝 | 三触发：`afterDamageApplied`（subject owner、perDamage）/ `afterHpLost`（subject owner）/ `cardsMoved`（sourceZones [hand]、perBatch），均 optional + condition compare(currentHandCount < currentLostHp) → draw（numberExpression `ownerLostHpMinusHandCount`） | 既有窗口 + 两个新语汇成员 |

- **绝情＝策略消费，不是触发**：锁定技无触发时机，全量在伤害结算漏斗处按来源策略转化。引擎既有 policy 机制（`SkillProgramCardPolicy` + `CardPolicies(owner, kind, effectiveKind)` 查询，力场 `PreventIncomingTrickDamage` 同款）天然承载"持有者造成的伤害"这一持续语义，新增枚举成员即可，无人物分支。
- **转化位置与语义边界**：转化臂放在 `ApplyAttackDamage` 内无前防护、before-damage 窗口、寒冰剑、麒麟弓之后、铁索传导之前——①对既有内容零顺序位移（该函数是全部伤害唯一漏斗，杀/决斗/南蛮/万箭/火攻/闪电共走）；②武器 rider 与锦囊伤害防护在"伤害"语境下先结算，转化后无伤害可挂；③转化不产生 `DamageRequested/DamageApplied/AfterDamage` 事件、不开伤害触发窗、不传导铁索、不计 `_playPhaseDamageDealtByCurrentPlayer` 与 `CausedDamage`；④以 `ProgramSkillHpLostEvent`（技能 id=绝情）+ `RecordHpChange(Loss)` 记账，`AfterHpLost` 窗口照常可见（含对方张春华的伤逝）；⑤清空体力走新增 `BeginHpLossDying`（复用 `DyingContinuation.Damage` 续接攻击收尾，但 killerSeat=null：体力流失致死无伤害来源、无奖惩）。
- **伤逝三窗口**：官方"每当你的手牌数小于你已损失的体力值时"落在三个可新满足的时机——受到伤害后（X 增）、非伤害体力流失后（X 增，含对方绝情转化）、手牌离开后（手牌数减；弃牌阶段弃置、被顺被拆被换均属之）。摸牌/获得他人牌不触发（入只有利方向，照 OL 实装惯例，边界见诚实归因）。
- **补至语义**：新数值表达式 `ownerLostHpMinusHandCount`（draw 白名单放行）＝max(0, 已损失体力 − 当前手牌数)，与既有 `targetMaxHpMinusHandCount`（补至体力上限）同构；配合 compare 条件，手牌 ≥ X 时连提示都不暴露。
- **触发值** `currentLostHp`（枚举 27）＝`CurrentMaxHp − CurrentHp`，纯事实推导，无新引擎台账。

## 引擎扩展清单（全部为共享语汇追加，无人物分支）

- **E1** `SkillProgramCardPolicyKind` 尾部追加 `ConvertOutgoingDamageToHpLoss = 18`；`ParseCardPolicy` 放行该 kind 的空 `cardKinds`（全部牌类）。
- **E2** `GameEngine.ApplyAttackDamage` 增加转化臂 + 新私有 `TryConvertOutgoingDamageToHpLoss`（见上）。
- **E3** 新私有 `BeginHpLossDying`（killerSeat=null 的 Damage 续接濒死）；`CompleteDamageAfterDying` 对 `DamageConvertedToHpLoss` 攻击跳过 AfterDamageApplied 窗口与 `AfterDamageEvent`，仍走 Pop + `CompleteDamageAttack` 收尾（攻击牌照常进弃牌堆）。
- **E4** `AttackResolution` 增加旗标 `DamageConvertedToHpLoss`。
- **E5** `SkillProgramTriggerValueKind` 尾部追加 `CurrentLostHp = 27`（Resolve=事实推导）；`SkillProgramNumberExpression` 尾部追加 `OwnerLostHpMinusHandCount = 16`，`DrawProgramCards` 求值臂 + draw 描述符解析白名单放行。
- 新触发窗口/新 EffectOp 为零——五点注册清单不适用；`ProgramCompositionDefinitionChecks` 的 EffectOp fixture 无新行（未新增操作）。

## 内容与测试

- `src/CardGame.Content.Standard/SkillPrograms/classic-zhang-chun-hua.rules.json`（schema 62，minimumRulesVersion 193）：绝情=cardPolicies 单项；伤逝=三触发同条件同效果。
- `src/CardGame.Content.Standard/SkillPrograms/classic-zhang-chun-hua.presentation.json`（schema 3，名称+官方文本描述）。
- 注册：`StandardClassicGeneralPackage.cs` 资源常量 + 懒加载目录 + `ZhangChunHuaProgram` + 技能块（绝情 Locked+State、伤逝 OptionalTrigger）+ `AddGeneral`（魏，3 体力）+ `CurrentGeneralIds` 尾部；包版本 1.163.0→1.164.0；`Replay.cs` 规则 192→193（版本源注释记明转化 policy/触发值/表达式三项）。
- `tests/CardGame.Core.Tests/ZhangChunHuaChecks.cs`（四项，CaoZhiChecks/LingTongChecks 模板）+ `Program.cs` 条目表四行。
- 立绘：`official-zhang-chun-hua.png`（gid 295 经典 129501）；`sync_general_art.py` CLASSIC_HEROES `"zhang-chun-hua": 295`；`general-art-catalog.json` entries 按字典序插入；`GeneralGalleryCatalog.cs` fame-1 组（官方 2011 名单）新增。

## 验证结果

- 定向检查：ZhangChunHuaChecks **4/4**。拒收面十项：绝情 policy 带 factionId、带 inputSuit/outputSuit、policy 缺失；伤逝 afterDamage 分支缺 damageOccurrence、带 hpChangeOccurrence；cardsMoved 分支带 turnOwnerScope、带 destinationZones；比较值右目（非整常数）带 value；draw 常量与表达式并存、表达式换 integerConstant、draw 目标改 selectedTarget。
- 行为检查：杀伤害→体力流失（单条 `ProgramSkillHpLostEvent`、切片内零 `DamageAppliedEvent`、目标 HP−1）+回放一致；全决斗牌堆决斗伤害→银行体力流失（银行全决斗牌堆无杀可响应，转化确定发生；owner 来源零伤害事件）+回放一致；伤逝受到伤害后补牌（受伤致 hand<X→接受→摸至恰为 X，refill 移动条数=差值）+回放一致。种子扫描动态停止（120/220 内首个命中），无静态白名单。
- Debug 全量 Core：**664/664 全绿**（基线 660 + 本批 4；无任何入池位移失败，无需归因实验）。
- Release 构建：0 error（2 warning 为赵云批 GaoDaYiHaoChecks 既有 CS8602，未代改）。
- WPF 过滤器：`general portraits share selected skins` 缺立绘名单为既有 8 名（xu-sheng、boundary:xu-sheng、shen-lu-meng、boundary:zhao-yun、zhang-xiu、ol:shen-guan-yu、shen-zhao-yun、gao-da-yi-hao），**不含 zhang-chun-hua**；`general gallery combines registered series` 通过。
- `sync_general_art.py --phase verify`：106 武将 / 887 PNG 记录全过（含本批新增）。
- 版本分层：Checkpoint SchemaVersion 3 不变；规则版本 192→193（同指纹重放行为改变：伤害可被策略转化为体力流失、新触发值与新 draw 表达式）；技能 JSON schema 62 不变（cardPolicies/触发/表达式均为既有节点类型 + 枚举新成员）；经典包 1.163.0→1.164.0。

## 诚实归因

- **事件对被并入既有 `ProgramSkillHpLostEvent`**：转化走该既有事件（skillId=绝情）而非新事件类型。该事件原语义是"主动程序技能的 LoseHp 操作"，现承载策略转化；观察方若按 skillId 过滤互不干扰，但按"事件类型=主动操作"假设编写的消费方会看到新来源——现仓无此类消费方，不新增事件类型以避免 schema 面扩大。
- **武器/防护先于转化的顺序选型**：官方页无绝情×寒冰剑/麒麟弓/无前系防护的细则。本实现取"伤害语境结算先于转化"：绝情者自己的武器 rider 仍可在转化前触发（冰剑弃牌代伤、麒麟弓弃马），目标侧锦囊伤害防护（力场系）仍可整体防掉该次（防掉后无伤害亦无流失）。反向读法（转化先行、武器失效）同样可辩护；取当前顺序的理由是它对引擎既有管线的观察行为零位移，且"防具/防护不防体力流失"的通则仍由"转化后无伤害事件"保证。
- **伤逝不因"获得手牌"触发**：官方"每当"的字面可读出获得手牌后（若仍<X）再触发；本实现只在手牌离开、受伤、非伤害流失三个时机检查（与十周年实装惯例一致），获得方向的补牌通过后续任一触发时机自然收敛。差异仅存在于"获得后仍<X 且随后长期无触发时机"的极端牌流，照实记录。
- **转化不复活 `_playPhaseDamageDealtByCurrentPlayer` 与 `CausedDamage` 事实**：绝情转化不进入任何"本回合造成过伤害"事实（许褚系计数、`CardUseCausedDamage` 触发条件）——官方口径转化非伤害，故这些事实不应置位；依赖这些事实的既有技能在与绝情同局时表现为"看不到这次结算"，是口径的必然结果而非缺陷。
- **首版转化臂返回值缺陷（本批自测拦截）**：`TryConvertOutgoingDamageToHpLoss` 首版把"未濒死"返回 false，导致转化后穿透到普通伤害路径、双份扣血并补发伤害事件——行为检查的切片断言（事件名清单带出）当场拦截，改为 `out awaitingDying` 双信号。同次修订还修复了编辑造成的 `TryPreventWuyanDamage` 重复调用臂（重复防护对既有内容无观察行为差异，仍按零位移原则移除）。
- **工作树外部删除事故（环境，非代码）**：隔离副本在研读阶段被并行清理流程删除（`card-work` 下 merge-audit，23:53–23:55）；因执行"先读后写"且尚未写任何实现文件，零工作损失；`batch/zhang-chun-hua` 分支引用幸存于 main-verify 仓库，worktree 由 `git worktree add` 原地重建（基线内容与派发快照一致），此后按防删除纪律分三次提前提交（b8ce62f 主体 / 402b26e 测试 / 1944893 立绘）。
- 仓库遗留噪音注记：main-verify 侧 git 对象库存在失效的 `refs/codex/turn-diffs/*` 与悬空提交引用，`git commit` 时 commit-graph 维护报错但不影响提交结果；本批未清理他人 refs（保留他人工作）。

## 边界与遗留

- 绝情与"伤害视角"技能的交互面（天香类 before-damage 重定向后转化目标、狂暴类自伤、以及转移伤害 `RedirectCurrentDamage`）未逐项立检；管线位置保证这些窗口先于转化结算，语义上重定向后的最终目标承担流失。
- 界张春华（伤逝改为锁定 + 翦灭）与晋势张春华不在本批范围；gid 1605 备查。
- 伤逝 `afterHpLost` 分支在本批内容中的自然触发面较窄（现仓非伤害体力流失仅崩坏/酒类主动技能与绝情转化），已由 `afterDamage` 与 `cardsMoved` 两分支覆盖主路径；如后续官方细则要求获得方向也触发，仅需增加一个 `cardsGained` + destinationZones [hand] 触发（解析面已支持），不需引擎改动。
