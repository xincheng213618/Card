using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017CaiYongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-cai-yong", definition =>
            definition with
            {
                ExecutionForms = SkillExecutionForm.Trigger |
                    (definition.Id == "classic:bizhuan" ? SkillExecutionForm.State : SkillExecutionForm.None),
                ActionForms = SkillActionForm.None
            });
        builder.AddGeneral(new ContentGeneralDefinition("classic:cai-yong", "蔡邕", "cai_yong", "classic:bizhuan", "qun", 3, ["classic:tongbo"], GeneralGender.Male)
        { CharacterId = "character:cai-yong", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
