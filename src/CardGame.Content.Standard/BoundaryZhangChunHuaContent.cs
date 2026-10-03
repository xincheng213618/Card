using CardGame.Core;

namespace CardGame.Content.Standard;

// Current ordinary OL gid625. The unchanged skills retain their frozen shared definitions.
internal static class BoundaryZhangChunHuaContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-zhang-chun-hua", definition => definition);
        builder.AddGeneral(new("boundary:zhang-chun-hua", "界张春华", "boundary_zhang_chun_hua",
            "classic:jueqing", "wei", 3, ["classic:shangshi", "boundary:jianmie-current"], GeneralGender.Female)
        { CharacterId = "character:zhang-chun-hua", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
