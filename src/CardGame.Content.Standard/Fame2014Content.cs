using CardGame.Core;

namespace CardGame.Content.Standard;

// Current OL editions: docs/content/sources/fame-2014-2026-09-30.json.
internal static class Fame2014Content
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } =
        ["classic:cao-zhen", "classic:han-hao-shi-huan", "classic:chen-qun", "classic:wu-yi", "classic:zhou-cang", "classic:sun-lu-ban"];

    internal static void Register(IContentRegistryBuilder builder)
    {
        Add("cao-zhen", "曹真", "wei", 4, GeneralGender.Male, [("classic:sidi", SkillTag.None)]);
        Add("han-hao-shi-huan", "韩浩史涣", "wei", 4, GeneralGender.Male, [("classic:shenduan", SkillTag.None), ("classic:yonglue", SkillTag.None)]);
        Add("chen-qun", "陈群", "wei", 3, GeneralGender.Male, [("classic:pindi", SkillTag.None), ("classic:faen", SkillTag.None)]);
        Add("wu-yi", "吴懿", "shu", 4, GeneralGender.Male, [("classic:benxi", SkillTag.Locked)]);
        Add("zhou-cang", "周仓", "shu", 4, GeneralGender.Male, [("classic:zhongyong", SkillTag.None)]);
        Add("sun-lu-ban", "孙鲁班", "wu", 3, GeneralGender.Female, [("classic:zenhui", SkillTag.None), ("classic:jiaojin", SkillTag.None)]);

        void Add(string key, string name, string faction, int hp, GeneralGender gender, (string Id, SkillTag Tags)[] skills)
        {
            foreach (var (id, tags) in skills)
            {
                var definition = EmbeddedSkillProgramCatalog.Definition("classic-" + key, id);
                var program = definition.Program!;
                builder.AddSkill(definition with
                {
                    Tags = tags,
                    ExecutionForms = (program.Triggers.Count > 0 ? SkillExecutionForm.Trigger : SkillExecutionForm.None) |
                        (program.Modifiers.Count + program.CardPolicies.Count + program.CardIdentities.Count > 0 ? SkillExecutionForm.State : SkillExecutionForm.None),
                    ActionForms = program.Activations.Count > 0 || program.ViewAs.Any(rule => rule.ForPlay) ? SkillActionForm.Active : SkillActionForm.None
                });
            }
            builder.AddGeneral(new ContentGeneralDefinition("classic:" + key, name, key.Replace('-', '_'),
                skills[0].Id, faction, hp, skills.Skip(1).Select(skill => skill.Id).ToArray(), gender)
            { CharacterId = "character:" + key, VariantId = "classic", RulesetId = "sanguosha-ol" });
        }
    }
}
