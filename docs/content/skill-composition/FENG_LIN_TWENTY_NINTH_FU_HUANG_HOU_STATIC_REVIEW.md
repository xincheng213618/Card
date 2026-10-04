# 界伏皇后主区静态整合记录

当前普通 OL 671 的惴恐、求援完整正文、登记入口、四个既有 runner 方法和首项 routine 前缀已写入主区；官方原始 PNG 也已登记。源文件保持冻结散列，性别采用官传与经典同人物补证，不把 API 缺失字段写成已取得。

原交付为 8 NEW、19 OLD、26 support。主代理逐项核对原始 SHA、无 BOM 的 LF 散列、OLD 的主区 before 与 preview after、单文件单 Update，再按窄补丁整合。原 manifest 与 stage 不改写，独立 followup 记录主区两项修正。

求援的原 Finalized 窗口改为按冻结 `ParentFrameId` 精确查找。赠牌后的真实收益孩子可以进入另一张牌的窗口，原程序校验仍须使用自己的父窗口；原用牌、实际 actor/provider、牌型、目标及回合校验均保留。现有第二个检查方法增加赠牌后真实失血、濒死、实体桃完成窗口与四视角 JSON 恢复续行的草稿，断言原赠牌与原杀各完成一次，没有新增 runner 或虚构帧。

公开交牌事实只带原用牌、席位和真实 movement 序号范围；隐藏实体 ID、印刷牌种及原区只在可信 owning receipt 和移动账中。公开拼点结果自身为已展示的标量；新外人回合窗口在 constructor/init/with/JSON 路径冻结 Candidates、Contexts 及其 Facts。没有新公开集合载荷需要另行投影。

真实零实体杀保留实际 producer 的 actor/provider 与 typed return。神速仍保留 Action=null；零实体决斗追加目标仅在原用牌有真实求援追加事实时复用成熟 representation。物理杀保留完整材料、冻结有效花色/颜色/点数和原距离权限，不制造新普通杀次数。对已有 AOE 目标只允许合法交牌，不重复追加目标。

“伤害锦囊”四种普通直接伤害牌的局部分类、HE 付款区、共有杀名、原拼点牌仍在弃牌区才领取，以及回合开始窗口与恒业的排序均为单列工程默认。2018 官网刊载社区教程提供辅助分类证据，2025 公告补证完整正文；没有当前逐牌 FAQ 验收。

**未进行 C# 编译、生产 loader、任何检查、AI 实测、基准、routine 或 Full 运行。** 四个方法及新增嵌套桃场景均为待运行草稿；Source loss、银狮/木牛、实际 Damage/AttackHpLoss/Dying/救援、4901/600/Fire 与多目标跨技能组合仍只有静态证明，运行验收为 false。详细来源、默认及原代理接线说明分别见同名前缀的主文档、DEFAULTS、INTEGRATION 和第29批 validation。
