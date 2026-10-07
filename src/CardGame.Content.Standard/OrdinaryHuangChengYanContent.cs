using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryHuangChengYanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-huang-cheng-yan", d => d.Id switch
        {
            "ol:zecai" => d with { Tags = d.Tags | SkillTag.Limited },
            "ol:yinshi" => d with { Tags = d.Tags | SkillTag.Locked },
            _ => d
        });
        // BWIKI lists the title 捧月共明 and the 群 faction; the official API
        // (gid 481) returns figure_py=qun with hp 3.
        builder.AddGeneral(new("ol:huang-cheng-yan", "黄承彦", "ol-huang-cheng-yan", "ol:jiezhen", "qun", 3,
            ["ol:zecai", "ol:yinshi"], GeneralGender.Male)
        { CharacterId = "character:huang-cheng-yan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
