using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinYuanShuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-yuan-shu", definition => definition with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None, Tags = SkillTag.Locked });
        builder.AddGeneral(new ContentGeneralDefinition("classic:yuan-shu", "袁术", "yuan_shu", "classic:yongsi", "qun", 4, ["classic:weidi"], GeneralGender.Male)
        { CharacterId = "character:yuan-shu", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
