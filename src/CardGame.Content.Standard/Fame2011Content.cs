using CardGame.Core;

namespace CardGame.Content.Standard;

// Rules and official portraits: docs/content/sources/fame-2011-2026-09-30.json and fame-2011-next-2026-09-30.json.
internal static class Fame2011Content
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        RegisterBundle("classic-zhang-chun-hua", ("classic:jueqing", SkillTag.Locked));
        RegisterBundle("classic-ling-tong");
        RegisterBundle("classic-chen-gong", ("classic:zhichi", SkillTag.Locked));
        RegisterBundle("classic-wu-guo-tai");
        RegisterBundle("classic-fa-zheng");
        RegisterBundle("classic-ma-su");
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

        void RegisterBundle(string bundle, params (string Id, SkillTag Tags)[] tagOverrides)
        {
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, bundle, definition =>
            {
                var program = definition.Program!;
                return definition with
                {
                    ExecutionForms = (program.Triggers.Count > 0 ? SkillExecutionForm.Trigger : SkillExecutionForm.None) |
                        (program.Modifiers.Count + program.CardPolicies.Count + program.CardIdentities.Count > 0
                            ? SkillExecutionForm.State : SkillExecutionForm.None),
                    ActionForms = program.Activations.Count > 0 || program.ViewAs.Any(rule => rule.ForPlay)
                        ? SkillActionForm.Active : SkillActionForm.None
                };
            }, tagOverrides.ToDictionary(skill => skill.Id, skill => skill.Tags, StringComparer.Ordinal));
        }
    }
}
