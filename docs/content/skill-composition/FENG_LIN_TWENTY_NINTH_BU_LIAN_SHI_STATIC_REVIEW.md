# 界步练师静态集成记录

2026-10-04，按当前普通 OL 775 完整安恤、追忆集成。原冻结 manifest SHA256 为 `821e685877dc43d76480a67e8da23c599f4dac2adb021908e7c22bc7ebaf2ec2`，patch SHA256 为 `2088ec13f1129223d5b4a36703e4ed832693c0367d431b3faad6f2f22115df31`，主基线为 7e0814da。十个 NEW 原字节、九个 OLD raw-before/preview raw、七个 stage 支持文件、九个主线支持文件及 source 散列逐项匹配；窄补丁每 OLD 只有一个 Update，不整份覆盖预览。

原 SkillProgramExecutor 预览含 UTF-8 BOM，manifest 的 normalizedLfAfterSha256 实际保留 BOM：`ac48cca0ceadb9fba4354bbf8a2ad50aee34e499582844023a1de077efbe062e`。去 BOM 的 LF SHA256 为 `ff902c3a591dfde07866a7e0cabc537fe3e0128c4283324f50537057dee8476f`，应用后匹配该值。这是散列口径说明，没有改变原冻结文件或额外修改 Executor。

静态对照现有 LoadPresentations 源码，发现原 presentation 缺少必须的 schemaVersion、并使用不支持的 triggerLabels。主线补当前必需的 presentation schema 字段，并改为支持的 triggerChoices，保留两项原 trigger ID、标签和官方完整正文。原冻稿不修改；独立修正证据在 `docs/content/sources/fenglin-twenty-ninth-775-static-followup-2026-10-04.json`。修正后的 presentation 原 SHA256 为 `686bbd685c4ad22f32056beb5f1c4498ea2e274e13ed84355df0b62c057c862c`。只按源码允许字段、引用 ID 和来源正文静态核对，未执行生产 loader。

安恤按原两名角色顺序获得真实实体；本人的 Hand 选择保持同区且不制造移动或得牌，其他真实获得的银狮、HP 与 movement/gain 子链返后才进入下一名。公开展示后冻结有效花色和原 pair 此时手牌数，再交给原少牌角色；真实交牌子链结束后执行非黑桃摸牌。来源在已付子链中失效时，成本保留、未付后继按显式默认取消。未展示手牌 ID 仅留 owning receipt，新获得事实只公开安全标量。

追忆在真实 own Ending 一次发行原对象，真实 Draw3/Recover1 的数量、账单和 HP 请求归原拥有帧；本人真实 OwnerDied 时，只允许原实例、原发行事实中的对象再次收益，不改选、不第二次扣限定额度。死亡收益中的普通 gain/HP 子选择保留准确 Death 父链。新 pending 状态均在已有 ProgramSkillFrame，新增公开事件没有集合字段；成熟选择与 reveal 的集合继续沿既有冻结投影。

已知共享机制限制：OwnerDied 收益观察者若进一步发起 Damage 或新的 Dying，旧 BeginProgramSkillDamage、BeginProgramSkillDying、CompleteProgramAttack 仍拒绝原 ActiveDying 残留。该组合尚未支持，需要专门的原 Death/Dying suspension/return 协议。本次未宽化通用入口、吞掉触发或伪造体力事实；这项限制与仅未运行的检查明确分开。

模块登记一次，既有 Core runner 四方法、routine 一个前缀已静态登记。官方 750×950 原始 PNG 已复制并入 catalog，SHA256 为 `88df186050ed556c59fe3ac44a0fa55a586fc24a42d2e0bebcab8df5f8140d48`。没有改变经典定义、全局规则版本、规则 schema 或包版本。触及 JSON 仅解析语法，PSD1 仅读取数据；检查草稿、编译、生产 loader、测试、基准和全资源验收均未执行，运行接受状态仍为 false。
