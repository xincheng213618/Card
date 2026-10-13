using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class MouZhugeLiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "mou-zhuge-liang", definition =>
            definition.Id switch {
                "mou:wujing" => definition with { Tags = definition.Tags | SkillTag.Locked },
                "mou:zhijue" => definition with { Tags = definition.Tags | SkillTag.Conversion },
                _ => definition
            });
        builder.AddGeneral(new("mou:zhuge-liang", "谋诸葛亮", "mou-zhuge-liang", "mou:zhitian", "shu", 3,
            ["mou:wujing", "mou:zhijue"], GeneralGender.Male)
        { CharacterId = "character:zhuge-liang", VariantId = "mou", RulesetId = "sanguosha-ol" });
    }
}
