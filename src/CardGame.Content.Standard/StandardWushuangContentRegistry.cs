using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>Opt-in single-match local adaptation; existing showcase hashes stay unchanged.</summary>
public static class StandardWushuangContentRegistry
{
    public static ContentRegistry Create() => ComposedSkillContentRegistry.CreateShowcase(
        additionalPackages: [new StandardWushuangContentPackage()]);

    public static GameOptions CreateOptions(int seed) => new()
    {
        Seed = seed, ModeId = WushuangMode.ModeId, PlayerCount = 2, HumanSeat = 0,
        HumanRole = null, HumanTeamId = WushuangMode.PlayerTeamId, UseInteractiveSetup = true,
        AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 300
    };
}

public sealed class StandardWushuangContentPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("standard-wushuang", new Version(1, 0, 0),
        [new PackageDependency("standard-classic-generals", new Version(1, 0, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(new("wushuang:catalogue", "单骑无双商品配置", WushuangCatalogue.Fingerprint));
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-tactics", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-skills", skill => skill with
        { Tags = skill.Id == "wushuang:skill-3226" ? SkillTag.Awakening : skill.Id == "wushuang:skill-3260" ? SkillTag.Limited : SkillTag.None });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-skills-a", skill => skill with
        { Tags = skill.Id is "wushuang:skill-3004" or "wushuang:skill-3131" or "wushuang:skill-3246" or "wushuang:skill-3265" ? SkillTag.Locked : SkillTag.None });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-skills-b");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-skills-b-audit");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-audit-a", skill => skill with
        { Tags = skill.Id == "wushuang:skill-3106" ? SkillTag.Locked : SkillTag.None });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-audit-combat", skill => skill with
        { Tags = skill.Id == "wushuang:skill-3263" ? SkillTag.Locked : SkillTag.None });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wushuang-combat", skill => skill with { Tags = SkillTag.Locked });
        foreach (var (bundle, id) in new[]
        {
            ("hulao-activity", "hulao:aozhan"), ("hulao-activity", "hulao:shenji"),
            ("ghost-activity", "ghost:guiji"),
            ("shanhe-s5-zaiqi", "shanhe:s5-zaiqi-887"), ("shanhe-s5-yudu", "shanhe:s5-niepan-95"),
            ("shanhe-current-yuanji", "shanhe:current-yuanji-2129")
        }) builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(bundle, id));
        foreach (var (bundle, id) in new[]
        {
            ("shanhe-current-jinnangji", "shanhe:current-jinnangji-2011"),
            ("shanhe-current-slash-tactics", "shanhe:current-lianji-2051"), ("shanhe-current-slash-tactics", "shanhe:current-lianji-2052"),
            ("shanhe-current-duoduo-yishan", "shanhe:current-duoduo-yishan-2082"), ("shanhe-current-duoduo-yishan", "shanhe:current-duoduo-yishan-2083"), ("shanhe-current-duoduo-yishan", "shanhe:current-duoduo-yishan-2084"),
            ("shanhe-current-yaoli", "shanhe:current-yaoli-2191"), ("shanhe-current-yaoli", "shanhe:current-yaoli-2192"),
            ("shanhe-current-yaoli", "shanhe:current-yaoli-2319"), ("shanhe-current-yaoli", "shanhe:current-yaoli-2320"), ("shanhe-passives", "shanhe:dushu-1"),
            ("shanhe-passives", "shanhe:ruoxi"), ("shanhe-passives", "shanhe:miaoji-1"), ("shanhe-passives", "shanhe:miaoji-2"),
            ("shanhe-reactions", "shanhe:shixue-1"), ("shanhe-reactions", "shanhe:fanci"),
            ("shanhe-current-slash-tactics", "shanhe:current-dangtou-2036"), ("shanhe-current-slash-tactics", "shanhe:current-dangtou-2037"),
            ("shanhe-current-slash-tactics", "shanhe:current-leihuo-2100"), ("shanhe-current-slash-tactics", "shanhe:current-leihuo-2101")
        }) builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(bundle, id) with { Tags = SkillTag.Locked });
        var catalog = SkillProgramCatalog.Load($$$$"""
            {"schemaVersion":{{{{SkillProgramCatalog.RulesSchemaVersion}}}},"skills":[{"id":"wushuang:boming","revision":1,"modifiers":[
              {"id":"extra-slash","query":"slashLimit","operation":"add","value":1,"priority":0},
              {"id":"less-hand","query":"handLimit","operation":"add","value":-2,"priority":0}]}]}
            """, $$$$"""
            {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},"skills":{"wushuang:boming":{"name":"搏命","description":"手牌上限-2，出杀次数+1。"}}}
            """);
        builder.AddSkill(new("wushuang:boming", "搏命", "手牌上限-2，出杀次数+1。")
        { Tags = SkillTag.Locked, Program = catalog.Programs["wushuang:boming"], ProgramPresentation = catalog.Presentations["wushuang:boming"] });
        builder.AddDeck(new("wushuang:duel-deck", "单骑无双无坐骑军争牌堆", 4, 2, [])
        { PhysicalCards = StandardClassicGeneralPackage.CreateWushuangPhysicalDeck() });
        builder.AddMode(new(
        WushuangMode.ModeId, "单骑无双 · 本地版", 2, 2, new Dictionary<string, int>(),
        "wushuang:duel-deck", 5, StandardClassicGeneralPackage.CurrentGeneralIds.Concat(StandardClassicGeneralPackage.FanGeneralIds)
            .Concat(WushuangCatalogue.AdditionalGeneralIds).Distinct(StringComparer.Ordinal)
            .Where(WushuangCatalogue.IsGeneralAllowed).ToArray(),
        ModeKind: ContentModeKind.Team,
        TeamCounts: new Dictionary<string, int> { [WushuangMode.PlayerTeamId] = 1, [WushuangMode.AiTeamId] = 1 }));
    }
}
