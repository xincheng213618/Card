# 界关羽、界张飞、界马超共享能力

规则正文和工程默认见[来源档案](../sources/fenglin-ninth-2026-10-02.json)。

## 来源与边界

项目采用普通 OL 界关羽、界张飞、界马超的人物页 300、301、303 正文。完整文本、页面身份和版本取舍见来源档案；其他产品线分别核对。

正式 ID 为 boundary:guan-yu、boundary:zhang-fei、boundary:ma-chao，资料、规则和展示位于对应人物的 boundary 目录；马术复用 classic:mashu。

技能规则由当前内容 hash 区分，格式和包版本的来源见[开发门禁](../../DEVELOPMENT_GATES.md)。

## 共享实际回合非锁定技失效

共享 IssueCurrentTurnNonLockedSkillSuppression(frame, targetSeat) 校验所属程序及来源，签发当前实际回合的非锁定技能抑制。状态绑定该实际回合及回合角色，实际回合结束后失效，包含回合外借刀等真实使用所触发的铁骑。

依据ContentSkillDefinition.Tags.Locked判断非锁定，不把限定技自动当锁定；觉醒沿现有明确metadata归一化。一般技能、动态grant、主公投射、化身来源必须走同一资格查询；不可永久DisableGrant，再以保存grantIds强行恢复，因为后来新增/重授的非锁定技同样应被封至当前实际回合结束，原有local disable不能被错误解封。锁定技保留；失效生效/expiry必须使cached binding shard和公共snapshot资格一致，纯视图/AI读不改变规则、grant或事件。无新cap旧流程字节/顺序保持。

已签发的抑制事实保留至原实际回合结束，来源失去、死亡或重授不提前解除；多来源抑制与局部禁用独立处理，集合事件在提交时冻结。

## 界关羽

武圣：一张本人的有效红色HE牌真实作为Slash使用或打出，含当前合法响应/借刀/武器供杀；本人使用的有效Diamond Slash不受距离限制。保留次数、目标、禁用和实际实体成本；只Diamond，不扩大所有红牌、只使用不是所有打出。复用现有viewAs/query或通用可选政策，不人物ID硬编码。

义绝：本人实际Play阶段限一次，弃本人一张HE牌，然后令一名有Hand的其他存活角色自行选择展示一张Hand。阶段额度同owner+namedSkill+actualPhase，一次接受点消费，source重授不刷新、extraPlay刷新。两个选择者分清，其他viewer在展示前不可获得私牌身份，展示是公开真实行动。成本及真实movement children先完成，再发target私有展示选择；已付款暂停后不重复付款。支付后目标死亡/空Hand有限终止，不能展示不存在的牌或免费尾随效果。

在展示发生点冻结目标原区域有效color/suit：black发共享当前回合非锁定技失效、目标Hand使用或打出禁令、以及本人对该target使用有效Heart Slash伤害+1。禁令只Hand：真装备转换/成本弃牌仍按各用途合法；覆盖人类/AI/供牌/救援/无懈/多实体转换与公开存储来源，不只Play菜单。禁用不能被fake response或内部声明绕过。有效花色在真实Use点冻结，仅作用于施加者自己本实际回合对指定目标的Heart Slash，保留普通伤害修正/酒/属性/多目标目标限定；不泛化所有红杀或其他人杀，源失去后已发事实仍在本回合有效。

red分支获得被展示的那张真实Hand实体，完成gain/loss children后由发动者可选令目标Recover1，满血/死亡有限处理。展示后牌被其他child取得时不得duplicate/take wrong card；不得以展示文本代替真实移动。回复必须await HP/character/dying child typed return，成本、展示、获取、回复均所属帧cursor/一次标记。

## 界张飞

咆哮提供真实使用杀的无限次数，并在每次目标实际完整抵消后积累本实际回合下一次杀的伤害储备。防具使杀无效、伤害防止、尚未满足无双的单张闪等不作为该抵消事实。储备按拥有者、技能和实际回合保存。

当前hero写下一次杀，不是下一次杀造成伤害时：在下一次真实Use接受/commit点把储备冻结到该CardUseFrame并一次消费，整张杀所有目标的该额外伤害一致；该杀又被抵消仍消耗旧储备，取消产生的新储备只给再下一张杀。只Played响应不消耗；借刀/青龙真实Use会消耗，actor替换归属以最终实际使用者为准，多目标重定向/子帧恢复不再消费。回合结束没用则过期，extra实际turn正确重置。

当前工程默认按每次真实目标抵消累计 +1，同一取消通知对同技能实例去重。下一次真实杀接受时冻结并消费储备，所有目标读取同一加值。

替身：限定、可选、准备阶段回复至当前HP上限，摸实际以此法回复的X张。不是上回合结束HP，也不是出牌阶段末取得未伤害杀。复用成熟recover/draw/count能力优先，新op仅不足时；limited named quota接受点一次，HP/character子窗口完整结束后按真实有效恢复receipt摸牌，不以最初maxHP−HP猜数量；满血可以有限选择或跳过，但不能凭0回复白摸牌。源失去/owner死亡/终局按已发流程边界不新发尾随奖励；不恢复重复limitedsource消耗。

## 界马超

马术复用完整锁定距离−1。铁骑非锁定，可选，真实使用杀指定目标后，按最终目标且先保留流离/求援等目标指定交互，再可选使目标当前实际回合非锁定技失效，然后发动者进行真实Judgment。用通用operation/typed judgment返回，包含改判、取判定牌、gain/dying子流程；最终有效Judgment suit由实际结果固定，不拿第一次印刷牌花色。不是旧classic:tieqi红色判定只禁止Dodge。

目标自行选择是否弃一张与最终判定花色一致的本人HE牌。必须实际弃置并完成movement children后才解除此当前目标这张杀的Dodge取消限制；没有同suit/拒绝/付款实际失败则不能抵消此杀，不能以选择文本当付费。有效Suit在本人的当前区域查询；真实HE可以包含装备（付掉装备后其能力不再存在），不强限Hand。各target拥有确切typed parent、judgment返回、付款cursor；多目标和恢复只提示/判定/付款一次，不为另一目标解除禁令。

“不能抵消此杀”沿真实Dodge抵消能力实现，保留Renwang免疫等原本不是Dodge抵消的效果；不能把它扩大成任何响应禁令、强行伤害或所有伤害防止失效。成功支付后真实Dodge/八卦判定按余下装备与锁定技资格正常；失败禁止八卦Dodge抵消等同效用途，但不阻止非抵消转移等已有规则。source死亡/失去后已 issued suppression仍到当前回合末，所属真实杀卡返回和支付movement child只结算一次。

配置字段、操作和入口能力见[当前技能配置](RUNTIME.md)；程序续接和帧权限见[结算协议](../../RESOLUTION_PROTOCOLS.md)。
