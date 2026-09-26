# 剩余 36 项旧技能绑定整体切换

本轮按“拆掉边界、迁移代码、解决报错后结束”执行，在 `main` 上由主任务与三个 Sol 同时迁移，统一编译和加载资源。基线提交为 `e378a0d23b2036fc494fd64b679b58cc95dbf682`；批次记录开始时间为 2026-09-26 23:56:59 +08:00，最终验证在 2026-09-27 00:50 左右完成。

## 结果

- 36 项真实旧绑定全部转为编译后的 Program：判定 7 项、伤害与濒死 7 项、卡牌与回合策略 14 项、响应与杀结算 8 项。
- 删除 `SkillKind`、`SkillRegistry`、`SkillRuleDefinition` 及旧卡牌、伤害、判定技能接口；删除旧执行入口、专用待决状态和兼容字段。
- 公共能力覆盖判定牌获得、连续判定、牌堆排序、阶段替换、虚拟杀、伤害转移、唯一点数濒死区、响应禁止、目标转移与势力代响应。技能条件和具体参数由内容定义提供。
- 规则版本 171，Program schema 61；基础包 1.15.0、经典包 1.142.0、主动扩展包 1.2.0。旧版本不提供兼容执行链。

## 本轮验收

| 检查 | 结果 |
| --- | --- |
| `dotnet build CardGame.sln --no-restore --nologo -v:q` | 0 警告、0 错误 |
| 全部内嵌规则及展示资源加载 | 73/73 通过 |
| 基线剩余真实旧绑定 | 36/36 有 Program，遗漏 0 |
| 已删除接口、枚举、注册器及兼容字段静态扫描 | `src` 与 `tests` 引用 0 |
| 技能 ID、武将技能列表、模式武将池对比 | 保持一致：160 技能、118 武将、11 模式 |
| `git diff --check` | 通过 |

按照本轮收尾要求，没有执行 Full、逐武将行为测试或 Replay 测试。旧测试接口已迁移至能够编译；这些结果不代表所有交互夹具或运行时行为已经回归通过。

本轮“36 项”指基线剩余的旧接口绑定，不等于项目所有内容均由 Program 实现。目录内另有既存原生技能 `mou:hengye`、`mou:yingbo`、`sp:nuzhan`，以及无技能占位 `standard:none`；它们不属于本轮旧接口绑定清单。

## 证据

本地证据目录：`C:\Users\17917\AppData\Local\Temp\Card-PassiveCutover-20260926`。

- `batch-start.json`、`registration-bundles.json`：批次起点和固定 36 项范围。
- `final-build.log`、`final-boundaries.log`：最终构建和边界扫描。
- `catalog-after.json`、`catalog-after.json.resources.json`、`catalog-comparison.json`、`catalog-fifth.log`：当前目录、资源加载和名单完整性。
- `damage-progress.json`、`li-progress.json`：并行工作记录；不将并行时长合计视为实际总耗时。
