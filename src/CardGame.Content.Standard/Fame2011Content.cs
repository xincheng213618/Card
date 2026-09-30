using CardGame.Core;

namespace CardGame.Content.Standard;

// Rules and official portraits: docs/content/sources/fame-2011-2026-09-30.json and fame-2011-next-2026-09-30.json.
internal static class Fame2011Content
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } =
    ["classic:zhang-chun-hua", "classic:ling-tong", "classic:chen-gong", "classic:wu-guo-tai", "classic:fa-zheng", "classic:ma-su"];

    internal static void Register(IContentRegistryBuilder builder)
    {
        RegisterBundle("classic-zhang-chun-hua", [("classic:jueqing", SkillTag.Locked), ("classic:shangshi", SkillTag.None)]);
        RegisterBundle("classic-ling-tong", [("classic:xuanfeng", SkillTag.None)]);
        RegisterBundle("classic-chen-gong", [("classic:mingce", SkillTag.None), ("classic:zhichi", SkillTag.Locked)]);
        RegisterBundle("classic-wu-guo-tai", [("classic:ganlu", SkillTag.None), ("classic:buyi", SkillTag.None)]);
        RegisterBundle("classic-fa-zheng", [("classic:enyuan", SkillTag.None), ("classic:xuanhuo", SkillTag.None)]);
        RegisterBundle("classic-ma-su", [("classic:sanyao", SkillTag.None), ("classic:zhiman", SkillTag.None)]);
        Add("zhang-chun-hua", "张春华", "wei", 3, ["classic:jueqing", "classic:shangshi"], GeneralGender.Female);
        Add("ling-tong", "凌统", "wu", 4, ["classic:xuanfeng"]);
        Add("chen-gong", "陈宫", "qun", 3, ["classic:mingce", "classic:zhichi"]);
        Add("wu-guo-tai", "吴国太", "wu", 3, ["classic:ganlu", "classic:buyi"], GeneralGender.Female);
        Add("fa-zheng", "法正", "shu", 3, ["classic:enyuan", "classic:xuanhuo"]);
        Add("ma-su", "马谡", "shu", 3, ["classic:sanyao", "classic:zhiman"]);

        void Add(string key, string name, string faction, int hp, string[] skills, GeneralGender gender = GeneralGender.Male) =>
            builder.AddGeneral(new ContentGeneralDefinition("classic:" + key, name, key.Replace('-', '_'),
                skills[0], faction, hp, skills.Skip(1).ToArray(), gender)
            {
                CharacterId = "character:" + key,
                VariantId = "classic",
                RulesetId = "sanguosha-ol"
            });

        void RegisterBundle(string bundle, (string Id, SkillTag Tags)[] skills)
        {
            foreach (var (id, tags) in skills)
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
