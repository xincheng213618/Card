两个完整原用例仍未达到各 100ms。本批继续保留吴苋四种分支、12 次独立恢复，公共牌堆 17 次独立恢复，以及四名观察者、全部付款、移动、隐私和回放断言。没有删除注册检查，也没有用单次动作计时替代原完整方法。

| 原完整方法，预热七次中位数 | 前一批 | 本批 | 累计分配量，前 → 后 |
| --- | ---: | ---: | ---: |
| 吴苋借刀 | 355.111ms | 321.343ms | 327.6 → 304.1MB |
| 公共牌堆 | 342.815ms | 353.015ms | 272.4 → 260.5MB |

吴苋本轮范围 317.682–326.344ms，前批 329.309–374.218ms，中位数下降约 9.5%。公共牌堆本轮范围 343.397–376.302ms，前批 340.418–348.292ms，本轮没有确认提速。分配量分别下降约 7.2%／4.4%；减少分配不等于已经满足时间目标。

相同正常探针执行八次完整方法，DOTNET_TieredCompilation=0，GC 在计时外，排除第零次后取七次中位数。每次仍创建新引擎并完成全部独立恢复和断言。另用默认 tiering 的三个独立正常进程执行两个原名称过滤器，冷时间中位数为吴苋 1,086.488ms、公共牌堆 1,802.525ms，范围分别 1,063.544–1,190.696ms／1,791.372–1,897.797ms。冷启动内容和 JIT 成本仍包含在内，冷、热结果分别记录。

付款归属问题已完成运行复现与修复。仅重建 Tests、保持上一批正常 Core 哈希不变时，真实暂停的出杀链能够同时改动 movement ID、batch ID 和全部冻结上下文，而真实材料及 ledger 不变；完整 AssertCoreInvariants 仍接受，因此新的拒绝断言失败。这证明了 trusted-host 不变量缺口，没有将其称为外部命令可利用漏洞。

现在实际 completed batch 发行点把 owning Use ID、Action ID、真实移动序号范围和完整深冻结批次保存在 CardUse 帧。付款批次匹配先验证发行收据，再执行原材料和 ledger 校验。扩展既有 atomic movement 检查，覆盖单／多材料、替代支付、真实暂停冷回放、来源禁用，及未发行 ID、foreign Use／Action、无收据、序号、批次来源／数量、冲突返回和使用先前同一实体的真实历史批次。历史批次反例先证明原材料／ledger 谓词确实为 true，再要求最终不变量拒绝。

通用 Slash 付款仍排除 ForeignSelectedCardSlash 专属协议和非空 native draw proof。白银狮子的只读复核没有发现发行漏捕：直接恢复先平衡 push/pop，替代恢复排队到 owning frame，实际 HP／Recovery 子窗口在付款完成、发行收据保存之后启动。这是生产路径的静态核查；没有把它写成新增运行复现。

普通 CardSnapshot 按每个引擎复用不可变值，要求正实体 ID 的六个公开字段全部相同；同实体外观、动态武器或字段变化会替换当前缓存条目，老视图不被改写。虚拟 ID 不缓存，判定牌仍走原 effective kind／catalog name 投影。RankText 继续执行原文化相关表达式，本批没有声称消除了它的分配。共享检查覆盖六字段、虚拟 ID、动态武器、判定改名、两引擎隔离、文化变化、观察者集合拒改、外国手牌隐藏和真实 Start 命令冷回放。

SkillRuntimeState.CreateSnapshot 用具体字典循环和延迟列表替换 LINQ 分配，保留 owner／skill 过滤、Scope＋Ordinal UsageId 排序、IsAcquired、实时 polarity、reset 和旧快照冻结。实际 RewriteSuit 注册桶为空时直接返回原牌花色；非空条件和顺序保留。Program-aware 状态投影仅在实际策略 ledger 为空时跳过查询与资格闭包，公开 BooleanStates 列表按需创建，三个空输出仍是 ReadOnlyCollection。完整 JSON 对照检查覆盖非空插入顺序、不同 owner／skill／instance、来源禁用／移除／重新获得、turn／seat、usage／polarity／boolean 和外层及嵌套冻结；组装 host 状态明确不冒称命令回放。实体守恒仍逐牌检查，仅把局部 HashSet 预分配到现有位置索引数量，没有增加验证缓存。

零材料目标组合已修复实际空引用，并补齐查询、提交、AI 重建的一致性。三族 producer 保留 entity 0 的真实零成本中性外观，复用已有虚拟目标合法性和逐目标结算；Tiered 普通锦囊只扩展一次；DrawFunded 在真实摸牌后再验证 exact targets。付款孩子启动前清空已有临时目标值，准确选中目标保留在 owning frame。新共享检查通过八个真实命令分支：三族 Slash、DrawFunded／Tiered 的 Peach 和 Alcohol、Tiered DrawTwo，覆盖真实技能获得、连环、赢拼点、Fumian，私有暂停、拒绝不变、效果／消耗／结束一次和四观察者独立冷回放。此前旧 Core 的四个空引用诊断使用真实已付拼点／Fumian，加 host 组装 Xianwan／连环；报告明确区分这份红诊断与最终真实命令覆盖。

新组合改变同内容指纹下可接受的命令及效果，因此规则 epoch 为 206。技能 JSON schema 和内容包版本没有改变。

最终相关 259／259 检查通过，包含最初全量报告的 40／40 个失败名称；两个家族 17／17 再次通过。实际默认日常范围 Core 470／470、WPF 23／23，通过，总计 108.920s，其中构建 10.192s、Core 81.976s、WPF 16.667s。没有追加全量测试，日常约一分钟目标仍未达到。

嵌套伤害长兼容名单尚未全部迁移。下一族有较明确的现成凭据：ResponseCompletion 原生付款健康子流程的冻结 cost invoice、ActiveChild ID 和 exact return。其 HP 原生 ParentFrameId 与返回 window ID 不同，不能按直父关系硬改；既有白银狮子装备 Dodge 检查验证 invoice 和冷回放，普通 LoseHp→Dying 尾部还需新增回归，才能删除对应兼容分支。零材料 BorrowedSword 增加第二 pair 的恢复路径和旧 legacy 目标 grant 仍有实体假设，属于未运行复现的候选，本批八分支不声称覆盖全部普通锦囊。

本批没有取得新的逐站点 GC 采样。选点仍依据此前已匹配源码的真实采样；本批总分配量来自正常完整用例探针。此前采样及清理组合命令的自动审批拒绝没有重试或绕过。

完整样本、全部通过名称／过滤器、初步与组合定向构建、旧 Core 红证据、诊断源码、114 个当前变更源码哈希和正常 DLL 哈希见[验证数据](2026-10-09-performance-payment-receipts-verification.json)。相对前批有 24 个源码／测试／范围文件变化。当前本任务没有测试、探针或采样消费者，也没有 Core 插桩源码。以下旧临时输出仍保留：

- C:\Users\17917\AppData\Local\Temp\Card-perf-20261009-f78dba55：26,250,737 字节，约 25MiB。
- C:\Users\17917\AppData\Local\Temp\Card-slowchecks-20261009-07ac82c4：2,840,687,018 字节，约 2.65GiB，含此前保留的 continued-allocation-parser\bin 和 obj。

自动审批审查拒绝了上述精确根目录及解析器 bin／obj 的递归清理，只返回 blocked by policy。没有重试或绕过，现有构建输出继续复用。
