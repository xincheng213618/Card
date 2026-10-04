# 当前普通 OL 界鲁肃485暂存合同

仅自有stage写入。未编译、未执行生产loader、未运行测试或benchmark，无HTTP/媒体/主目录修改、登记、提交或版本变化。OLD最终基线为根确认的含627/457主树d2990fba；交付记录逐文件实际raw-before、preview raw-after及normalized-LF after。

来源：`docs/content/sources/fenglin-twenty-eighth-485-source-2026-10-04.json`，原SHA256 `612f55221790ad7a9d947e33e3e00c955c076d82ffc8daedf9699f3d954e69e9`。普通OL页485及info API HTTP200，完整好施/缔盟；吴3，API无性别字段，男性仅以保留经典鲁肃同人物身份补证。

好施：摸牌阶段，你可以多摸两张牌，然后若你的手牌数大于5，你将一半的手牌（向下取整）交给手牌最少的一名其他角色，然后直到你的下回合开始，当你成为【杀】或普通锦囊牌的目标后，其可以交给你一张手牌。

缔盟：出牌阶段限一次，你可以令两名其他角色交换手牌（两者手牌数之差不大于你的牌数量），若如此做，出牌阶段结束时，你弃置X张牌（X为这两名角色手牌数之差）。

6000 DrawExtraAndArmHalfHandSupport 只在原本人实际DrawPhaseStarting候选中真实Draw2，owning receipt冻结source/instance/hash/actualTurn和真实draw ledger区间。6001 GiveHalfHandAndIssueTargetSupport 只在同实例本轮好施发动后的AfterNormalDraw，成熟SelectTarget(least hand)→SelectOwnedCards(half floor)后冻结精确原Hand实体，原子全交付并确认实际ledger，才发行一个受赠者资格。该节点在自身owning frame保存已付receipt，全部movement/gain/HP/濒死孩子返还后结束；不把资格发行误写成全部孩子之后。资格至下一次本人实际TurnStarted（包含额外、自然跳过回合）终止。

6002 ExchangeHandsAndArmPhaseDebt 零实体单个原pair activation。冻结接受时两个真实手牌集合与差额X，完整原子交换，之后才发行原source/actualTurn/真实PlayPhaseInstance债务；没有提前弃牌。6003 SelectFrozenHandExchangeDebtPayment 在同实际PlayEnding原候选读取尚未付的债务，冻结当时合法HE足额/短缺数量，沿成熟SelectOwned→MoveBound→Await完整成本孩子。每份债务开始付款一次，已完成成本不可重付；不同真实Play分别归属。

6004 OfferHalfHandRecipientSupport 复用621 ActualUseTargetWindow，只接真实Slash或普通Trick拥有用牌及准确目标/候选/context。真正受赠者仅私选自己的Hand或放弃；不向鲁肃或第三者暴露其未交付手牌。提交仍验证原资格、真实用牌未失效、双方存活及原source实例。已付赠牌的movement/HP/Dying孩子完整返回原use；源失效仅取消未付款后继。

新增事件仅标量；新pending集合构造、init、with和JSON均独立只读冻结。原实体移动事实及公开/私密投影沿成熟CardMovement/CommittedEventProjection，不能把private pending实体写入公开receipt/event。所有hook新op opt-in，无旧classic好施/缔盟predicate变化或任意observer/framepresence放行。6004专属无中生有自目标推导只承认真正当前成熟DrawTwo/CardUse.Action/原声明材料，保留原Action与TargetSeats=[]；付款后原source失效不使已付原窗口身份证明消失，旧621不能取得泛化自目标许可。

最多四项小fixed真实命令/四视角冷恢复草稿。关键成本、交换、实际目标援助及失效/过期边界必须用实际命令，恢复新实例继续；不每将定义快照、不HOST/反射state、不seed搜索。单routine代表待最终方法清单。
