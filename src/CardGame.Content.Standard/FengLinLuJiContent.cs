using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FengLinLuJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-lu-ji", definition =>
            definition with
            {
                Tags = definition.Id == "classic:huaiju" ? SkillTag.Locked : SkillTag.None,
                ExecutionForms = SkillExecutionForm.Trigger
            });

        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:lu-ji", "陆绩", "lu_ji", "classic:huaiju", "wu", 3,
            ["classic:yili", "classic:zhenglun"], GeneralGender.Male)
        {
            CharacterId = "character:lu-ji",
            VariantId = "classic",
            RulesetId = "sanguosha-ol"
        });
    }
}
