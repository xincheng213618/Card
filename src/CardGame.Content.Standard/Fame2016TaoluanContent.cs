using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2016TaoluanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-zhang-rang", definition =>
            definition with { ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new ContentGeneralDefinition("classic:zhang-rang", "张让", "zhang_rang", "classic:taoluan", "qun", 3, [], GeneralGender.Male)
        { CharacterId = "character:zhang-rang", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
