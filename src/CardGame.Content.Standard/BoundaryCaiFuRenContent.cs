using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryCaiFuRenContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-cai-fu-ren", definition => definition with
        {
            ExecutionForms = definition.Id == "boundary:xianzhou-current" ? SkillExecutionForm.None : SkillExecutionForm.Trigger,
            ActionForms = definition.Id == "boundary:xianzhou-current" ? SkillActionForm.Active : SkillActionForm.None,
            Tags = definition.Id == "boundary:xianzhou-current" ? definition.Tags | SkillTag.Limited : definition.Tags
        });
        // Female inferred from the current official biography identifying Liu Biao's wife; the current info API omits gender.
        builder.AddGeneral(new("boundary:cai-fu-ren", "界蔡夫人", "boundary_cai_fu_ren", "boundary:xianzhou-current", "qun", 3,
            ["boundary:qieting-current"], GeneralGender.Female)
        { CharacterId = "character:cai-fu-ren", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
