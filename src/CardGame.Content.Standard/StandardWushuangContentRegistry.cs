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
        builder.AddMode(new(
        WushuangMode.ModeId, "单骑无双 · 本地版", 2, 2, new Dictionary<string, int>(),
        "classic:standard-deck", 5, StandardClassicGeneralPackage.CurrentGeneralIds.Concat(StandardClassicGeneralPackage.FanGeneralIds).ToArray(),
        ModeKind: ContentModeKind.Team,
        TeamCounts: new Dictionary<string, int> { [WushuangMode.PlayerTeamId] = 1, [WushuangMode.AiTeamId] = 1 }));
    }
}
