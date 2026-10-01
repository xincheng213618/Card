using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FengLinLuKangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-lu-kang", definition =>
            definition with
            {
                Tags = definition.Id == "classic:qianjie" ? SkillTag.Locked :
                    definition.Id == "classic:poshi" ? SkillTag.Awakening : SkillTag.None,
                ActionForms = definition.Id is "classic:jueyan" or "classic:huairou" ?
                    SkillActionForm.Active : SkillActionForm.None,
                ExecutionForms = definition.Id == "classic:poshi" ? SkillExecutionForm.Trigger : SkillExecutionForm.State
            });
        builder.AddGeneral(new ContentGeneralDefinition("classic:lu-kang", "陆抗", "lu_kang", "classic:qianjie",
            "wu", 4, ["classic:jueyan", "classic:poshi"], GeneralGender.Male)
        {
            CharacterId = "character:lu-kang",
            VariantId = "classic",
            RulesetId = "sanguosha-ol"
        });
    }
}
