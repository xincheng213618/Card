using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FengLinLuJiContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:lu-ji"];

    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "classic:huaiju", "classic:yili", "classic:zhenglun" })
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-lu-ji", id) with
            {
                Tags = id == "classic:huaiju" ? SkillTag.Locked : SkillTag.None,
                ExecutionForms = SkillExecutionForm.Trigger
            });
        }

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
