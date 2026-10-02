# 界诸葛亮、界孙尚香规则口径

依据当前普通 OL 官网[界诸葛亮440](https://www.sanguosha.com/hero/440)、[界孙尚香443](https://www.sanguosha.com/hero/443)完整正文；HTTP UTC、原文、结构化体力和原始立绘 SHA见[来源档案](../sources/fenglin-twelfth-2026-10-02.json)。官网 API性别为空，两位 Male/Female沿仓库既有人物元数据，不能说是API明示。

观星每次启动实际观看时，存活角色至少四人看五，否则三。短堆只看实际可用牌、零张不授皆置底资格为工程解释；一次观看固定同组实体，继续展示已观看牌面不等于再次观看。准备实际全部置底后，真实结束阶段可以再次发动；结束的第二次完成不会重新武装自身。成熟运行时只有正常/额外实际回合的 TurnEnding，没有单独可插 Ending，不能声称覆盖不存在的调度。

准备全底资格是 private、same-instance、Turn BooleanState。原始状态下一实际 BeginTurn重置；结束后没有可用动作与raw store删除不是同一事实。来源压制后同instance可保留已提交事实，新instance不继承，属于既有key的工程解释。新估值只给 opt-in公开控制收益的小正prior，不读未观看牌/RNG，不代表最优AI策略。空城沿classic完整Locked定义。

结姻官网“然后你与其中体力值较大的角色”按你与目标两人中较大者摸1、较小者回复1解释；体力相等没有额外摸/回，都是工程解释，未声明为额外官方FAQ或用户裁决。比较在真实付款和所有付款子流程结束后冻结，先较大者摸1，再较小者回复1；第一步的后续HP变化不改已冻结的第二步。

两种费用共享同实际出牌阶段一次。任意一手牌真实弃置，或本人手牌/装备区一实体装备真实置入男性装备区。按实际有效Gender允许Male owner自选；self Hand弃/Hand装备真正置入可行，原Equipment同区无位移不作费用也不触发枭姬。装备置入按真实区域/slot容量、替换与离区钩子处理；Transfer/Replacement不被奇才外方弃置保护拦截。枭姬沿classic实际装备离区per-card定义。

当前普通界孙权救援与classic桃bonus不同，另做回复生效前的替代和原recipient决策设计。界庞统/界徐庶借用全文的资料缺口沿此前 bounded预检保留，不猜API或以历史文本填补。[能力契约](FENG_LIN_TWELFTH_CAPABILITIES.md)定义具体拥有帧和验证边界；当前文档是计划，主区验收前不称已完成。


## 最终新增能力的边界

观星使用 nullable PopulationThresholdCount 与拥有的 TopReorder.Population，成功实体排序后才授予 private all-bottom state。完整牌面仅 owner 可见；新增 SkillPrompt 的人工/AI路由仅 Population 非空分支。旧 free/exact 路由与估值保留。来源失去/重授采用单独 host 生命周期审计；真实低人口、结束/额外回合、scheduled阶段和死亡按接受命令验证。

装备置入冻结原 captured 单卡 HE SourceLocation，通过成熟 SelectedCardPayment 等待真实 HP/移牌子窗。新 resource 必须是原 unconditional CaptureSelectedCards binding，派生或其他 producer 拒绝。HpPairSnapshot 冻结付款子窗后的两人 HP/存活与较大/较小方；Freeze 之后仅允许 Draw/Recover 尾序列，不再重选参与者、插入独立阶段或追加付款。新字段旧 null 序列省略，旧 source/target 组合不追加限制。

结姻原生 AI 的公开机会 prior 为14、观星控制 prior 为4，仅新节点估值，不制造 Draw 收据或读取敌方私有手牌。证明范围为正式技能普通可选激活和有限完成，不是最优策略。结姻非死亡失去技能沿旧 source-valid 路径静态复用，未新增该情形命令探针；真实装备失去后死亡及原手实体桃救援有命令与四视角冷恢复。
