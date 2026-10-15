# 人物目录

每个人物使用一个目录，同人物的不同规则版本位于各自子目录。以王粲为例：

```text
wang-can/ol/
  Content/ol-wang-can.character.json
  Skills/ol-wang-can.rules.json
  Skills/ol-wang-can.presentation.json
  Runtime/GameEngine.WangCanPrograms.cs
```

`Content` 声明人物资料、初始与授予技能的基本信息、规则包引用、注册和选将池顺序；`Skills` 保存技能规则和展示文本；`Runtime` 只在现有通用能力不足时提供专属实现，并通过现有注册特性接入引擎。同人物的跨版本实现可以放在人物直属的 `Runtime`。

`_shared` 保存多个角色共用的技能规则和装备效果。通用流程、结算和可复用机制位于 `CardGame.Core`。外部自定义内容仍通过 `ComposedSkillContentPackage` 加载。素材继续由原有资源目录和目录表管理。

普通新人物只增加自己的资料与技能文件，内容目录会自动发现并注册到所属包。完整的资料格式、注册边界及验证方式见 [人物模块开发说明](../../docs/CHARACTER_MODULES.md)。
