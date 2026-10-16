# 726 工程默认与来源缺口边界

以下为当前普通 OL 实现采用的工程默认，与官方正文和 FAQ 分别标明。

- 成功修改次数和level封顶2；满级殚心可真实Draw2，不再修改或增次数。不从“两级列表”推导为官方最大次数。
- X冻结于殚心发行点，以已完成成功修改为准；DrawX所有原孩子返回以后再进行尚未发行且有效的修改。X0没有实际Draw/movement事实，但0→1合法。
- source失效只取消尚未发行的修改，已真实摸牌/成本/孩子不回滚。owner死/胜负确定排空已发孩子、取消未发动作。已接受矫诏Use冻结资格，不按付款后的shard重新反悔。
- 初始矫诏本人actualPlay每phase一次，合法HE一实体；牌区正文只说一张牌，HE为明确默认。level1/2共享owner+skill+state Round一次，额外turn不推进Round，额外Play不刷新Round额度。
- tier依成熟永久owner/skill/独立state继承，来源移除/重获新instance不刷新层级或Round额度。并存来源不叠加相同技能状态/额度。
- Level0牌名可用性基于本trueRound所有角色真实Use的effectiveKind；查询按成熟ProgramBasicCardName把三种Slash归为同名“杀”，公开事实保留真正effectiveKind。此元素Slash同名口径属于未核FAQ默认。声明/材料printedKind或普通打出不计。Nullification为真实Use，有原窗口/Action/actor/provider/responder证明；本人防御Slash的真正Dodge Use计名，包含原生实体、其他转换及八卦；群体Dodge、供牌和Slash打出不凭裸facts计Use。
- level2真0材料Use无materialcardId、无伪Hand/Processing移动，原动作普通目标/距离/额度/防具/有效颜色限制照常；不是无限无距虚拟Slash。
- 修改后动作只在原生合法使用时点发行；Round 份额、零实体使用、失技/死亡和满级行为分别按准确生产入口校验。普通打出响应不自动授权新的使用动作。
- 所有公共事件集合须显式冻结；所有pending集合构造/init/with/JSON均克隆只读；私有material与opaqueHand不得进入公开usage事实或AI隐藏ID推断。
