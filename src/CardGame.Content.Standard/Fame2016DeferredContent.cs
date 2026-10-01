using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2016DeferredContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:li-yan", "classic:sun-deng"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        Add("li-yan", "李严", "shu", 3, GeneralGender.Male, [("classic:duliang", SkillTag.None), ("classic:fulin", SkillTag.Locked)]);
        Add("sun-deng", "孙登", "wu", 4, GeneralGender.Male, [("classic:kuangbi", SkillTag.None)]);
        void Add(string key, string name, string faction, int hp, GeneralGender gender, (string Id, SkillTag Tags)[] skills)
        {
            foreach (var (id, tags) in skills) RegisterSkill(key, id, tags);
            builder.AddGeneral(new ContentGeneralDefinition("classic:" + key, name, key.Replace('-', '_'),
                skills[0].Id, faction, hp, skills.Skip(1).Select(skill => skill.Id).ToArray(), gender)
            { CharacterId = "character:" + key, VariantId = "classic", RulesetId = "sanguosha-ol" });
        }

        void RegisterSkill(string key, string id, SkillTag tags)
        {
            var definition = EmbeddedSkillProgramCatalog.Definition("classic-" + key, id);
            var program = definition.Program!;
            builder.AddSkill(definition with
            {
                Tags = tags,
                ExecutionForms = (program.Triggers.Count > 0 ? SkillExecutionForm.Trigger : SkillExecutionForm.None) |
                    (program.Modifiers.Count + program.CardPolicies.Count + program.CardIdentities.Count > 0 ? SkillExecutionForm.State : SkillExecutionForm.None),
                ActionForms = program.Activations.Any(activation => !activation.Effects.Any(effect =>
                        effect.Op == SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge)) || program.ViewAs.Any(rule => rule.ForPlay)
                    ? SkillActionForm.Active : SkillActionForm.None
            });
        }
    }
}
