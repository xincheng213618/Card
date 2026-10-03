using CardGame.Core;

namespace CardGame.Content.Standard;

// Current ordinary OL gid 486; the module uses the shared actual-phase capabilities.
internal static class BoundaryZhangHeContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-zhang-he", definition => definition);
        builder.AddGeneral(new("boundary:zhang-he", "界张郃", "boundary_zhang_he",
            "boundary:qiaobian", "wei", 4, [], GeneralGender.Male)
        { CharacterId = "character:zhang-he", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
