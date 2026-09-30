using CardGame.Core;

namespace CardGame.Content.Standard;

// Current OL definitions are kept separate from retained classic/DIY editions.
// Official roster and provenance: docs/content/sources/ol-gods-2026-09-30.json.
internal static class OlGodContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } =
    [
        "ol:shen-sima-yi", "ol:shen-liu-bei", "ol:shen-lu-xun",
        "ol:shen-gan-ning", "ol:shen-zhang-liao", "ol:shen-zhou-yu", "ol:shen-zhuge-liang",
        "ol:shen-lu-bu", "ol:shen-zhao-yun", "ol:shen-sun-quan", "ol:shen-zhang-jiao", "ol:shen-dian-wei", "ol:shen-huang-zhong"
    ];

    internal static void Register(IContentRegistryBuilder builder)
    {
        RegisterBundle("ol-shen-sima-yi",
            [("ol:renjie", SkillTag.Locked), ("ol:baiyin", SkillTag.Awakening),
             ("ol:lianpo", SkillTag.None), ("ol:jilue", SkillTag.None), ("ol:jilue-wansha", SkillTag.Locked)]);
        RegisterBundle("ol-strategic-gods",
            [("ol:longnu", SkillTag.Locked | SkillTag.Conversion), ("ol:shen-liu-bei-jieying", SkillTag.Locked),
             ("ol:junlue", SkillTag.Locked), ("ol:cuike", SkillTag.None), ("ol:zhanhuo", SkillTag.Limited),
             ("ol:poxi", SkillTag.None), ("ol:shen-gan-ning-jieying", SkillTag.None),
             ("ol:duorui", SkillTag.None), ("ol:zhiti", SkillTag.Locked)]);
        RegisterBundle("ol-shen-zhou-yu", [("ol:qinyin", SkillTag.None), ("ol:yeyan", SkillTag.Limited)]);
        RegisterBundle("ol-shen-zhuge-liang", [("ol:qixing", SkillTag.None), ("ol:kuangfeng", SkillTag.None), ("ol:dawu", SkillTag.None)]);
        RegisterBundle("ol-shen-lu-bu", [("ol:kuangbao", SkillTag.Locked), ("ol:wumou", SkillTag.Locked),
            ("ol:wuqian", SkillTag.None), ("ol:shenfen", SkillTag.None)]);
        RegisterBundle("ol-shen-zhao-yun", [("ol:juejing", SkillTag.Locked), ("ol:longhun", SkillTag.None)]);
        RegisterBundle("ol-shen-sun-quan", [("ol:yuheng", SkillTag.Locked), ("ol:dili", SkillTag.Awakening),
            ("ol:shengzhi", SkillTag.Locked), ("ol:quandao", SkillTag.Locked), ("ol:chigang", SkillTag.Locked | SkillTag.Conversion)]);
        RegisterBundle("ol-shen-zhang-jiao", [("ol:yizhao", SkillTag.Locked), ("ol:sijun", SkillTag.None), ("ol:tianjie", SkillTag.None)]);
        RegisterBundle("ol-shen-dian-wei", [("ol:juanjia", SkillTag.Locked), ("ol:qiexie", SkillTag.Locked), ("ol:cuijue", SkillTag.None)]);
        RegisterBundle("ol-shen-huang-zhong", [("ol:shenyu", SkillTag.None), ("ol:huaren", SkillTag.Limited)]);

        Add("sima-yi", "神司马懿·OL", 4, ["ol:renjie", "ol:baiyin", "ol:lianpo"]);
        Add("liu-bei", "神刘备", 6, ["ol:longnu", "ol:shen-liu-bei-jieying"]);
        Add("lu-xun", "神陆逊", 4, ["ol:junlue", "ol:cuike", "ol:zhanhuo"]);
        Add("gan-ning", "神甘宁", 6, ["ol:poxi", "ol:shen-gan-ning-jieying"], initialHp: 3);
        Add("zhang-liao", "神张辽", 4, ["ol:duorui", "ol:zhiti"]);
        Add("zhou-yu", "神周瑜", 4, ["ol:qinyin", "ol:yeyan"]);
        Add("zhuge-liang", "神诸葛亮", 3, ["ol:qixing", "ol:kuangfeng", "ol:dawu"]);
        Add("lu-bu", "神吕布", 5, ["ol:kuangbao", "ol:wumou", "ol:wuqian", "ol:shenfen"]);
        Add("zhao-yun", "神赵云·OL", 2, ["ol:juejing", "ol:longhun"]);
        Add("sun-quan", "神孙权", 4, ["ol:yuheng", "ol:dili"]);
        Add("zhang-jiao", "神张角", 3, ["ol:yizhao", "ol:sijun", "ol:tianjie"]);
        Add("dian-wei", "神典韦", 4, ["ol:juanjia", "ol:qiexie", "ol:cuijue"]);
        Add("huang-zhong", "神黄忠", 4, ["ol:shenyu", "ol:huaren"]);

        void Add(string key, string name, int hp, string[] skills, int? initialHp = null) =>
            builder.AddGeneral(new ContentGeneralDefinition("ol:shen-" + key, name,
                "ol_shen_" + key.Replace('-', '_'), skills[0], "god", hp, skills.Skip(1).ToArray())
            {
                CharacterId = "character:" + key,
                VariantId = "ol-god",
                RulesetId = "sanguosha-ol",
                InitialHp = initialHp
            });

        void RegisterBundle(string bundle, (string Id, SkillTag Tags)[] definitions)
        {
            foreach (var (id, tags) in definitions)
            {
                var definition = EmbeddedSkillProgramCatalog.Definition(bundle, id);
                var program = definition.Program!;
                builder.AddSkill(definition with
                {
                    Tags = tags,
                    ExecutionForms = (program.Triggers.Count > 0 ? SkillExecutionForm.Trigger : SkillExecutionForm.None) |
                        (program.Modifiers.Count + program.CardPolicies.Count + program.CardIdentities.Count > 0
                            ? SkillExecutionForm.State : SkillExecutionForm.None),
                    ActionForms = program.Activations.Count > 0 || program.ViewAs.Any(rule => rule.ForPlay)
                        ? SkillActionForm.Active : SkillActionForm.None
                });
            }
        }
    }
}
