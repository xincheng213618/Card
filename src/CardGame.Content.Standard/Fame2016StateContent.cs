using CardGame.Core;

namespace CardGame.Content.Standard;

// Current OL editions: docs/content/sources/fame-2016-2026-10-01.json.
internal static class Fame2016StateContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        Add("liu-yu", "刘虞", "qun", 2, GeneralGender.Male, [("classic:zhige", SkillTag.None), ("classic:zongzuo", SkillTag.Locked)]);
        Add("cen-hun", "岑昏", "wu", 3, GeneralGender.Male, [("classic:jishe", SkillTag.None), ("classic:lianhuo", SkillTag.Locked)]);

        Add("sun-zi-liu-fang", "孙资刘放", "wei", 3, GeneralGender.Male, [("classic:guizao", SkillTag.None), ("classic:jiyu", SkillTag.None)]);

        Add("huang-hao", "黄皓", "shu", 3, GeneralGender.Male, [("classic:qinqing", SkillTag.None), ("classic:huisheng", SkillTag.None)]);

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
                        (program.Modifiers.Count + program.CardPolicies.Count + program.CardIdentities.Count + program.DamageModifiers.Count > 0 ? SkillExecutionForm.State : SkillExecutionForm.None),
                    ActionForms = program.Activations.Any(activation => !activation.Effects.Any(effect =>
                            effect.Op == SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge)) || program.ViewAs.Any(rule => rule.ForPlay)
                        ? SkillActionForm.Active : SkillActionForm.None
                };
            }, skills.ToDictionary(skill => skill.Id, skill => skill.Tags, StringComparer.Ordinal));
        }
    }
}
