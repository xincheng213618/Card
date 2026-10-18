# 新增武将 / Add a general

[开发入门 / Quick start](GETTING_STARTED.md) · [完整人物模块说明](CHARACTER_MODULES.md)

## 中文

先复用现有技能完成一个小改动，再实现新机制。控制台示例提供了可运行的源码扩展：

```powershell
dotnet run --project samples/CardGame.Headless -- --custom-general
```

[TrainingGeneralPackage.cs](../samples/CardGame.Headless/TrainingGeneralPackage.cs) 实现 `IGameContentPackage`，在 `Register` 中调用：

```csharp
builder.AddGeneral(new ContentGeneralDefinition(
    Id: "sample:training-general",
    Name: "练习武将",
    PortraitKey: "zhang_fei",
    SkillId: "standard:paoxiao",
    FactionId: "shu",
    BaseHp: 4));
```

完整示例声明对标准包的依赖，并从标准五人模式派生一个独立模式，将练习武将加入 `GeneralPoolIds`。宿主通过 `ContentRegistry.Build(new StandardContentPackage(), tutorialPackage)` 显式组装内容；原标准模式不会因此增加人物。为了不依赖随机选将命中，练习模式展示整个选将池。肖像键仅作为沿用已有界面约定的占位，控制台不会加载图片。

### 在内置人物目录中新增配置

如果目标是扩充随客户端编译的内容，可以在独立人物文件夹添加 `Content/*.character.json`。例如新建 `src/Characters/training-general/sample/Content/sample-training-general.character.json`：

```json
{
  "formatVersion": 1,
  "packageId": "standard",
  "characterId": "character:training-general",
  "skills": [],
  "generals": [
    {
      "registrationOrder": 1000000,
      "pools": { "standard": 1000000 },
      "definition": {
        "id": "sample:training-general",
        "name": "练习武将",
        "portraitKey": "zhang_fei",
        "skillId": "standard:paoxiao",
        "factionId": "shu",
        "baseHp": 4,
        "gender": "Male"
      }
    }
  ]
}
```

这是复用现有技能的原创教学人物，不是新的官方人物版本。`registrationOrder` 在所属包内唯一，`pools.standard` 的顺序在该池内唯一；上面的数值仅作示例，添加前检查是否仍未占用，不要重排现有人物。不要同时启用这个文件和控制台的同 ID 教学包，否则注册器会拒绝重复 ID。

此文件会由 [Content.Standard 项目](../src/CardGame.Content.Standard/CardGame.Content.Standard.csproj) 的通配规则嵌入，`CharacterContentCatalog` 按所属包和选将池注册；需要重新编译。完整的现有人物示例是 [标准张飞](../src/Characters/zhang-fei/standard/Content/standard-zhang-fei.character.json)。

### 继续添加技能

- 复用已有技能：引用已注册的 `skillId`；多个技能使用 `additionalSkillIds`，无需再次声明相同技能。
- 组合已有规则能力：在同一人物目录增加 `Skills/*.rules.json` 和 `Skills/*.presentation.json`，在人物文件的 `skills` 中声明技能，并用 `programBundle` 绑定文件名前缀。先参考体积较小的 [经典黄盖](../src/Characters/huang-gai/classic/Content/classic-huang-gai.character.json)、[规则](../src/Characters/huang-gai/classic/Skills/classic-kujin-skills.rules.json) 与 [展示](../src/Characters/huang-gai/classic/Skills/classic-kujin-skills.presentation.json)，再查 [现有节点](content/skill-composition/RUNTIME.md)。
- 新机制：现有 JSON 无法表达时才添加必要的 `Runtime` 能力，并按 [人物模块](CHARACTER_MODULES.md) 接入现有注册机制。不要把角色名判断加入通用流程。

普通人物增加无需提升全局规则版本、技能 schema 或包版本。格式版本取自现有加载器与规则定义；内容 hash 会体现配置变化。新增规则保留明确的来源和版本，区分原始说明与实现推论；自创人物明确标注自创，不复制无权使用的图片、音频或长篇规则资料。

纯配置沿用共享加载与注册检查；新机制或缺陷修复增加真正覆盖行为的 focused check。验证命令见 [开发门禁](DEVELOPMENT_GATES.md)，不要为每个人物复制定义快照，也不要自动运行完整验证。控制台的 `--custom-general` 路径验证内容注册、选将与回放，没有验证新技能的战斗效果。

## English

Start with a small extension that reuses an existing skill:

```powershell
dotnet run --project samples/CardGame.Headless -- --custom-general
```

The complete [TrainingGeneralPackage.cs](../samples/CardGame.Headless/TrainingGeneralPackage.cs) implements `IGameContentPackage`, declares its standard-package dependency, registers an original tutorial general using `ContentGeneralDefinition`, and derives a separate five-player mode. Its general uses the existing `standard:paoxiao` skill. The host explicitly builds the registry from the standard and tutorial packages. Adding a general alone does not add it to an existing mode's general pool.

The sample shows every general in its training draft so no seed search is needed. Its portrait key is a placeholder using the existing UI convention; the console does not load artwork. This is a compiled source extension, not hot loading.

### Built-in character files

For content shipped with the client, add a uniquely named `Content/*.character.json` under a new `src/Characters/<character>/<edition>/` directory. The full JSON example above is a minimal original tutorial general reusing an existing skill. The `skills` array can be empty because the referenced skill already exists in the standard package.

Choose an unused `registrationOrder` in the package and an unused ordinal in each named pool; the large values in the example are illustrative, not reserved. Do not reorder existing entries. Do not register the same tutorial ID through both the JSON file and the optional console package.

The [Content.Standard project](../src/CardGame.Content.Standard/CardGame.Content.Standard.csproj) embeds these files at build time. `CharacterContentCatalog` registers them with their package and pool. Rebuild after edits. See [standard Zhang Fei](../src/Characters/zhang-fei/standard/Content/standard-zhang-fei.character.json) for an existing small manifest.

### Skills and verification

Reuse an existing `skillId`, or list additional skills with `additionalSkillIds`. To compose new behavior from supported nodes, add paired `Skills/*.rules.json` and `Skills/*.presentation.json` files, declare the new skill, and bind it with `programBundle`. The compact [classic Huang Gai manifest](../src/Characters/huang-gai/classic/Content/classic-huang-gai.character.json), [rules](../src/Characters/huang-gai/classic/Skills/classic-kujin-skills.rules.json), and [presentation](../src/Characters/huang-gai/classic/Skills/classic-kujin-skills.presentation.json) illustrate the relationship. Consult the [node documentation](content/skill-composition/RUNTIME.md) before designing new runtime capabilities.

Add `Runtime` code only for behavior the shared capabilities cannot express, following [character module registration](CHARACTER_MODULES.md). Preserve payment, card movement, private views, and frame-owned continuation state. Ordinary character additions do not require a rules epoch, schema change, or package version bump; the content hash records configuration changes.

Keep rule sources and edition choices explicit, distinguish implementation inferences, and label original designs as original. Supply assets only with appropriate rights. Reuse shared loading/registration coverage for pure configuration, add focused behavior checks for new mechanisms or bug fixes, and follow the [development gates](DEVELOPMENT_GATES.md). Full verification requires an explicit manual request. The sample checks registration, selection, and replay; it does not exercise the skill's combat behavior.
