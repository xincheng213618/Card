using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FengLinXuYouContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-xu-you", definition =>
            definition.Id switch
            {
                "classic:chenglue" => definition with { Tags = SkillTag.Conversion, ExecutionForms = SkillExecutionForm.State },
                "classic:shicai" => definition with { ExecutionForms = SkillExecutionForm.Trigger },
                "classic:cunmu" => definition with { Tags = SkillTag.Locked },
                _ => definition
            });
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:xu-you", "许攸", "xu_you", "classic:chenglue", "qun", 3,
            ["classic:shicai", "classic:cunmu"], GeneralGender.Male)
        {
            CharacterId = "character:xu-you",
            VariantId = "classic",
            RulesetId = "sanguosha-ol"
        });
    }
}
