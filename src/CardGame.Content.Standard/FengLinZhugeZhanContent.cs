using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FengLinZhugeZhanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-zhuge-zhan", definition =>
            definition with
            {
                Tags = definition.Id == "classic:fuyin" ? SkillTag.Locked : SkillTag.None,
                ExecutionForms = SkillExecutionForm.Trigger
            });

        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:zhuge-zhan", "诸葛瞻", "zhuge_zhan", "classic:fuyin", "shu", 3,
            ["classic:zuilun"], GeneralGender.Male)
        {
            CharacterId = "character:zhuge-zhan",
            VariantId = "classic",
            RulesetId = "sanguosha-ol"
        });
    }
}
