using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinYuJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-yu-ji", definition => definition with
        {
            ExecutionForms = definition.Program!.CannotChallengeDeclarations ? SkillExecutionForm.State : SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None,
            Tags = definition.Program!.CannotChallengeDeclarations ? SkillTag.Locked : SkillTag.None,
            SuppressionRule = definition.Program.CannotChallengeDeclarations ? new SkillSuppressionRule(1) : null
        });
        builder.AddGeneral(new ContentGeneralDefinition("classic:yu-ji", "于吉", "yu_ji", "classic:guhuo", "qun", 3, Gender: GeneralGender.Male)
        { CharacterId = "character:yu-ji", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
