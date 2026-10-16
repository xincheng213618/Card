# 当前内容目录与注册入口

内容的权威来源是当前包注册代码、人物资料和嵌入技能资源。本文说明如何定位它们，不复制会随内容开发变化的武将总数、包版本或牌堆张数。

## 包与组合

`StandardContentRegistry` 位于 `src/CardGame.Content.Standard/StandardContentPackage.cs`。`Create()` 构造基础内容；其他 `CreateWith…` 入口显式组合所需包。

| 包 ID | 当前用途 |
| --- | --- |
| `standard` | 基础卡牌、装备、技能、武将、演示牌堆与身份模式 |
| `standard-active-skills` | 主动技能演示与对应模式 |
| `standard-rescue-skills` | 急救演示及扩展模式的救援层 |
| `standard-classic-generals` | 经典、界、SP、神、谋与 OL 等人物版本、经典牌堆和相关模式 |
| `standard-team-modes` | `team:standard-2v2` 公开阵营模式 |
| `standard-national-war-lite` | `national:lite-4` 四人双将国战 Lite |
| `standard-national-war-ambitious` | `national:ambitious-6` 六人独立势力实验 |
| `standard-national-zhang-jiao` | 国战张角/华佗与独立试验模式 |
| `standard-composed-skills` | 技能程序体验及其组合内容 |

每个包的版本和依赖取其 `Manifest` 与 `CurrentVersion`。`ContentRegistry.Build` 校验依赖、重复 ID 和引用，冻结字典及嵌套集合，并计算规范化内容指纹。普通新增配置通过该指纹区分。

## 人物与技能

人物路径为 `src/Characters/<人物>/<版本>/`：

- `Content/*.character.json`：人物 ID、属性、包归属、技能绑定、注册和将池顺序。
- `Skills/*.rules.json`：可执行技能规则；`*.presentation.json`：名称、说明与选择标签。
- `Runtime/`：现有通用能力不足时，通过现有协议注册的运行时实现。
- `_shared/`：跨人物复用的技能与装备规则。

完整格式见[人物模块说明](../CHARACTER_MODULES.md)，技能配置见[运行时](skill-composition/RUNTIME.md)。同名人物的经典、界、SP、神、谋和 OL 版本分别定位；技能中文名不是规则身份。

## 卡牌、装备、模式与牌堆

卡牌和装备定义由 Standard 包及经典扩展注册，规则读取有效牌型，实体 ID、物理牌面及实际来源区保持独立。原生牌行为与可注册技能能力共享命令、移动和子流程边界。

模式定义指定 `DeckId`、人数、身份/阵营、候选和 `GeneralPoolIds`。牌堆定义保存实际实体配方、起手数和阶段摸牌数；不同模式不共享一份人为维护的张数清单。身份选将的当前权重与人物去重规则见[选将偏好](IDENTITY_DRAFT.md)。

## 来源与素材

`docs/content/sources/` 保存选定产品线的规则档案、官方页/API/客户端证据及来源差异。来源合同、裁定与工程默认值见 `generals/` 和 `skill-composition/`。图鉴、资源键与美术目录表共同决定实际显示；素材下载状态不等于已绑定，也不证明技能规则完整。

[参考数据](BWIKI_REFERENCE_DATA.md)用于发现与版本对照。[来源核对规范](BWIKI_CONTENT_BACKLOG.md)说明证据等级。[牌图](card-artwork.md)、[音频](game-audio-sync.md)和[动态皮肤](DYNAMIC_SKIN_RESEARCH.md)分别维护资源范围。
