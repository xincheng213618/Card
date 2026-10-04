# 官方事实与工程默认

以下不是已核官网 FAQ、移动版或经典口径。官网当前两段全文与蜀3 已单独冻结；经典保持。

## 樵拾

- “每个结束阶段”覆盖本人及他人真实正常/额外回合的 Ending。以两个互斥 own/otherLiving trigger 组合完整表达；没有经典的发动前相等限制、没有 Other 限制。
- 同一 owner/current seat 仍顺序发两次 Draw1，所有第一份孩子回返后才发行第二份，所有第二份孩子回返后才查最终手牌。比较不是激活时、首份摸牌后或摸牌前冻结数。
- 真正 Round 失效按 owner+skill+state 保存，来源失效/重新获得不同实例仍保持到真实下一轮；extraTurn 不推进轮。额外 Play 不重置 Round，后续真正 Ending 仍受同一块额度限制。跳过阶段没有假 Ending。
- 已接受 6700 后，source-loss 本身不撤回已签发的两份收益和最终比较；返回靠原定义/hash/实例发行事实，不重查当前 shard。owner/current actor 真死亡或胜负确定时排空已发生孩子、取消未发行份额和最终比较，账单不回滚。空牌堆实际 Draw0 仍保留该真实请求/数量，两份完成后正常比较。以上死亡/零 draw 只静态未运行。

## 燕语

- 正文没限定重铸牌区，沿成熟本人手牌实体重铸默认。仅 printed Slash/FireSlash/ThunderSlash 可重铸，virtual Slash 或其他实体当杀不制造重铸材料。
- 失去数默认按真实 Hand/Equipment/Judgment→非本人 owned 区域的 printed Slash 三种移动次数；Use、Response、重铸、Give、真弃置等统一 ledger，不只数重铸。Processing→Discard 的收尾不是本人区域转出，不重复。
- 本人 Hand→本人 E/J/木牛/公开或私有 pile 等内部移区不算；私有 grain/pile 起始离区暂排除，避免储存→消耗再算两次。printed非Slash的龙胆/丈八/虚拟杀材料不算“实体【杀】”；已存在身份/转换仍保留其用牌行为，不把 effective Use 名称替代材料名。
- 同一实体真的获得回 Hand 后又真实离区，按两次移动计；没有每实体整阶段去重。actual phase instance 分离额外/下一 Play，按真实阶段 actor/actual turn owner 冻结 stamp。没有回合末重算来源实例或倒推材料。
- 至少2 在 actual PlayEnded 候选复验时读取该阶段当前可信账单；男性可包含男性技能持有人本身，存活/真实当前性别资格沿成熟 AnyLivingMale。来源已经失效则未发动机会取消；角色死亡则成熟 Draw dead-target 跳过。
- 重铸成本完成后 source-loss 不撤回 discard、次数和成本孩子；未发奖励按成熟重铸来源资格取消，实际 Draw0/CardRecastEvent保留。最后一节点没有额外授技/状态后继。

## 静态限制

未编译、未运行 loader、任何测试或基准。所有深层 cost/gain→Damage、AttackHpLoss、SelfDying/酒/醇醪/RecoveryReplacement 等只有精确 producer/连续 typed 边静态证明。没有把草稿当运行验收。最终 OLD 基于 da931c4c97600741ac521bd4c1be4d83d9ed9cce 的逐文件原始字节；含 BOM 的 LF 与 BOM-free LF 两种散列分列。
