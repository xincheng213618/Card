using CardGame.Core;

namespace CardGame.Content.Standard;

// Current OL editions: docs/content/sources/fame-2015-2026-09-30.json.
internal static class Fame2015Content
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        Add("cao-rui", "曹叡", "wei", 3, GeneralGender.Male,
            [("classic:huituo", SkillTag.None), ("classic:mingjian", SkillTag.None), ("classic:xingshuai", SkillTag.Lord | SkillTag.Limited)]);
        Add("cao-xiu", "曹休", "wei", 4, GeneralGender.Male,
            [("classic:qianju", SkillTag.Locked), ("classic:qingxi", SkillTag.None)]);
        Add("zhong-yao", "钟繇", "wei", 3, GeneralGender.Male,
            [("classic:huomo", SkillTag.None), ("classic:zuoding", SkillTag.None)]);
        Add("liu-chen", "刘谌", "shu", 4, GeneralGender.Male,
            [("classic:zhanjue", SkillTag.None), ("classic:qinwang", SkillTag.Lord)]);
        Add("xiahou-shi", "夏侯氏", "shu", 3, GeneralGender.Female,
            [("classic:qiaoshi", SkillTag.None), ("classic:yanyu", SkillTag.None)]);
        Add("zhang-ni", "张嶷", "shu", 4, GeneralGender.Male,
            [("classic:wurong", SkillTag.None), ("classic:shizhi", SkillTag.Locked)]);
        Add("sun-xiu", "孙休", "wu", 3, GeneralGender.Male,
            [("classic:yanzhu", SkillTag.None), ("classic:xingxue", SkillTag.None), ("classic:zhaofu", SkillTag.Lord | SkillTag.Limited)]);
        Add("quan-cong", "全琮", "wu", 4, GeneralGender.Male, [("classic:yaoming", SkillTag.None)]);
        Add("gongsun-yuan", "公孙渊", "qun", 4, GeneralGender.Male, [("classic:huaiyi", SkillTag.None)]);
        Add("guo-tu-feng-ji", "郭图逢纪", "qun", 3, GeneralGender.Male,
            [("classic:jigong", SkillTag.None), ("classic:shifei", SkillTag.None)]);

        void Add(string key, string name, string faction, int hp, GeneralGender gender, (string Id, SkillTag Tags)[] skills)
        {
            RegisterBundle(key, skills);
            builder.AddGeneral(new ContentGeneralDefinition("classic:" + key, name, key.Replace('-', '_'),
                skills[0].Id, faction, hp, skills.Skip(1).Select(skill => skill.Id).ToArray(), gender)
            { CharacterId = "character:" + key, VariantId = "classic", RulesetId = "sanguosha-ol" });
        }

        void RegisterBundle(string key, (string Id, SkillTag Tags)[] skills)
        {
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-" + key, definition =>
            {
                var program = definition.Program!;
                return definition with
                {
                    ExecutionForms = (program.Triggers.Count > 0 ? SkillExecutionForm.Trigger : SkillExecutionForm.None) |
                        (program.Modifiers.Count + program.CardPolicies.Count + program.CardIdentities.Count > 0 ? SkillExecutionForm.State : SkillExecutionForm.None),
                    ActionForms = program.Activations.Any(activation => !activation.Effects.Any(effect =>
                            effect.Op == SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge)) || program.ViewAs.Any(rule => rule.ForPlay)
                        ? SkillActionForm.Active : SkillActionForm.None
                };
            }, skills.ToDictionary(skill => skill.Id, skill => skill.Tags, StringComparer.Ordinal));
        }
    }
}
