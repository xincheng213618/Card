# 当前普通 OL 界郭皇后 726 暂存合同

状态：设计/NEW 暂存中。禁止执行编译、loader、测试、native AI、benchmark、HTTP、媒体或主区写入。OLD 最终窗口等待 root 在756/775之后确认；不更改已冻结 source-preflight 与 linked-rule-followup。

## 来源和完整主文

当前 hero/API：`batch31/source-preflight/726-official-source.json` SHA `9387c8212c44a4909ee2ace1e5657ac9d8632151f8f3093035ec0bcd7e06251e`。当前公告补全文：`batch31/linked-rule-followup/726-linked-rule-evidence.json` SHA `f66822510b9c9716788f0d0768e79ebc40ae3933dcf29c6885400b21a05b8bda`，原followup manifest SHA `4d45a6e35c9913e19b5081e1b8df6b794f443e56ac97ea74cbb5904064525ec1`。二者已按原始字节核对。

官方 https://www.sanguosha.com/news/20260612_7102_0710 发布显示2026-06-17 17:14:20；主文与当前 hero726 相同，身份表明确魏3女。来源为 web 成功打开的官方 HTML 可读段落；工具未提供该公告 raw HTTP字节/headers/status，不声称已取得 raw HTTP SHA。

- 矫诏：出牌阶段限一次，你可将一张牌当本轮未有角色使用过的基本或普通锦囊牌使用。
- 修改一：每轮限一次，你可将一张牌当任意基本牌或普通锦囊牌使用。
- 修改二：每轮限一次，你可视为使用一张基本牌或普通锦囊。
- 殚心：当你受到伤害后，你可摸X张牌，然后修改“矫诏”（X为你修改“矫诏”的次数）。

不导入2022十周年最近角色声明线、不改变classic:jiaozhao/classic:danxin。

## 能力复用和缺口

成熟 configured conversion 保存永久 owner/state tier0..2，现有 single-card viewAs、ordinary-trick options、accepted action/material成本、Nullification真正Use、actual Round边界、SkillRuntimeState精确Round usage、公共counter投影均可复用。

四个缺口须闭合而非用现有经典配置冒充：

1. 当前全场真正Round完整牌名Use历史。英博仅damage-card kind，4400/658只actualTurn；需要新 opt-in scalar fact/proved producer，普通Slash打出与群体Dodge响应不计。本人防御Slash的真正Dodge Use沿成熟分类计名，包括原生实体、其他转换及八卦，不能只认本新政策。Nullification接受其真实counterspell父链作Use；Action=null仅准确既有真实virtual producer，不从裸CardUsed/材料movement倒推。
2. level0 actual ownPlay每phase一次、level1/2同一owner+skill+state每Round一次，不带instance的额外fresh额度。接受动作前资格/额度/当前tier确认，接受后freeze原source tuple/hash/materials/level/actualRound，source失效不反悔已付动作。
3. 真实0材料basic/ordinaryTrick用牌。现InputCount正数、旧virtual op只专属Slash/酒/Duel，不能用假实体或假AcceptedAction凑齐。新增可选viewAs opt-in及准确zero-material producer，cardId0/physicalIds空/成本空，实际进入成熟Use/response-use链，normal distance/quota/target/shield/Nullification完整保持；receipt保存在原CardUse/真实counterspell帧，不建parallel use-ID pending侧表。
4. 6900 `drawBeforeCappedConversionTierUpgrade`：可选own AfterDamageApplied原候选、perDamage。原发行点冻结source/tier X/Damage window/token。先真正DrawX并排空原gain/HP/Recovery/Damage/Dying全部孩子，再复验未发行修改的当前来源并只允许0→1/1→2。X0不调用虚假CardsDrawn或制造movement，修改一仍可发生。

6900–6999现静态查无占用。6900 EffectOp与新的6901可选policy/receipt名字若同号属于不同enum；OLD显式值保持既有值不漂移。普通新增人物不提升全局epoch/schema/package。

## 发行与返回

初始material默认合法本人HE单实体（主文未限制牌区，明确为未证FAQ工程默认）；有效material花色/rank保持真实转换冻结，私牌在实际使用公开前不通过新增public event泄露。level2没有物理材料、没有Hand或Processing伪移动。

新 pending 必须写原 owning frame。所有原cost/reward孩子完整typed回返、原成本一次；零材料Use与special Nullification的typed return不能靠任意frame presence。公共新增事实只使用真正已宣告/接受的effectiveKind/actor/round/来源标量；若有公开集合，新增明确CommittedEventProjection冻结；pending集合构造/init/with/JSON均克隆只读。

## 满级与FAQ工程默认（非官方裁定）

仅主源列出的两次成功修改，成功修改次数/等级最大2。X取发行时已成功次数；先DrawX完整孩子后再有效修改。满级仍可按原殚心选择真实Draw2，不发行第三种版本、不发假成功修改、不增X。官方公告没有满级FAQ，这是root授权暂定，待实际运行/用户裁定。

level0只本人真实Play，一actualPhase一次，额外Play重置phase额度。level1/2沿修改后仍在合法“使用”时点，共享owner+skill+state Round额度；成功换level、失去/重获不同instance均不刷新Round额度。永久tier默认沿成熟owner/skill/独立state继承，不利用新instance重获绕过。trueRound遇extraTurn不推进、真正新Round才重置Round usage。

未取得FAQ：修改后时点/Nullification真正Use、Level0与升级后首Round共享边界、第三次殚心、不足合法material/死亡/赢家、tier重获继承与转换的牌区；defaults将逐一声明，不写成官方原文。

## 至多四项真实命令草稿

沿现有runner注册，无人物定义snapshot、新runner开关或运行：

1. 真实Round首次牌名Use与level0材质转换：普通响应不占名、真正Nullification计Use、当前actor/provider/material与四视角真实cold恢复；routine候选。
2. 殚心X0→1、Draw1真实gain→Recovery/HP暂停→JSON恢复实例续行→2、满级Draw2不再升级。source-loss后已draw保留且取消未发修改只有静态合同，本草稿不冒称此分支已实测。
3. level1/2共享Round额度，成功tier变化/失技重获不刷新、extraTurn保持/新Round恢复；level2真正0实体Use并经原目标/无懈/完成typed return。
4. 真zero-material Nullification Use与特殊response typed return、normal Slash/Dodge不误计；prepared公开事实/私有材料与集合不可变草稿。

四个真实命令检查草稿已经落盘，全部未经编译、loader或运行，不是运行覆盖证据。最终manifest必须所有执行标记false，并列出实际尚未闭合或仅静态证明的producer边界。
