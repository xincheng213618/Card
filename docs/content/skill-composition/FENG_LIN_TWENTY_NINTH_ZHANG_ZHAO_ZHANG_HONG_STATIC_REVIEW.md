# 界张昭张纮静态集成记录

2026-10-04，按冻结的当前普通 OL 563 完整正文集成直谏、固政。交付 manifest SHA256 为 `a144e63012bde231b0e5780f2ea8919d5501a164d041d5b585c5b02582377508`，patch SHA256 为 `5c25617b96b20475170f2342cfb8b605b0884d1b87b0419c5a8257d46f42a5df`。主基线 cecc456e 的 Core 与 64f10e3c 相同，九个 NEW、十一个 OLD 原始前值与预览、八个支持文件、原始 source 散列均精确匹配；应用后九个 NEW 原字节和十一个 OLD 的 BOM-free LF 文本匹配冻结结果。

直谏按所选原装备实体和原对象执行实际替换，保存首次被替换实体；生成武器、白银狮子和木牛流马沿已有移动机制处理。替换、移入及其恢复、体力与得牌子结算结束后才实际摸一张，摸牌子结算仍归原付款帧。固政按原弃置批次冻结其他角色的真实 HEJ 弃牌实体，先交还一张，待交还的得牌及濒死子结算结束后再询问是否一次领取剩余原实体。被取走再弃入的实体不能借旧批次领取。

阶段次数依赖原实际阶段 token 和已发生的交还事实，按 owner、skill、stateId 计算；失技再获得不刷新已使用机会。新增阶段事实仅在注册 6201 能力时启用，额外出牌和摸牌阶段各有新 token；原准备、普通摸牌 token 分别保存在既有 schedule 和 DrawPhaseObligationFrame，按准确父帧恢复。没有新增平行 pending 状态。付款后的原实体 ledger 与第一个移动或恢复子帧匹配后，才允许局部原生 Damage、Dying 及救援后缀。

领取事件的 CardIds 明确列入 CommittedEventProjection，原实体和领取列表均在构造、init、with 及序列化恢复路径保存只读副本。公开实体仅来自真实已弃置或装备移入事实。未修改经典正文、全局规则版本、JSON schema 或包版本。

主模块已登记一次，既有 Core runner 已登记四个方法，routine 仅增加 `boundary two zhangs equipment replacement`。官方 750×950 原始 PNG 已复制并登记，其 SHA256 为 `2c3d9b4bc04530355758b817a026b3802c1d254851dc2c55ae933c92fa7caea1`。仅解析触及 JSON 的语法、读取 PSD1 数据并核对登记字符串，未执行生产 loader。合并后 diff whitespace 检查属于静态检查。

按用户睡醒后统一测试的要求，未编译、构建或运行任何检查、基准与完整离线素材验收。固定 seed、真实命令、冷恢复、生成武器和木牛变体、额外摸牌返回、复杂救援及来源失效均待运行验证。source 的 capture 状态和交付合同保留其冻结时快照；当前集成状态见第29批 validation 与 art 记录。
