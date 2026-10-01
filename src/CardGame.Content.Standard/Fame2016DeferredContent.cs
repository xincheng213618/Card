using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2016DeferredContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        Add("li-yan", "李严", "shu", 3, GeneralGender.Male, [("classic:duliang", SkillTag.None), ("classic:fulin", SkillTag.Locked)]);
        Add("sun-deng", "孙登", "wu", 4, GeneralGender.Male, [("classic:kuangbi", SkillTag.None)]);
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
