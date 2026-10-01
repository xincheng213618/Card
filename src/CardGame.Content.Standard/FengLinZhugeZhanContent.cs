using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FengLinZhugeZhanContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:zhuge-zhan"];

    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "classic:fuyin", "classic:zuilun" })
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-zhuge-zhan", id) with
            {
                Tags = id == "classic:fuyin" ? SkillTag.Locked : SkillTag.None,
                ExecutionForms = SkillExecutionForm.Trigger
            });
        }

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
