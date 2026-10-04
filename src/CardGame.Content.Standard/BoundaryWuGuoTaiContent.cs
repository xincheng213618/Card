using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryWuGuoTaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-wu-guo-tai", definition => definition);
        builder.AddGeneral(new ContentGeneralDefinition("boundary:wu-guo-tai", "界吴国太", "boundary_wu_guo_tai", "boundary:ganlu-current", "wu",
            BaseHp: 3, AdditionalSkillIds: ["boundary:buyi-current"], Gender: GeneralGender.Female)
        { CharacterId = "character:wu-guo-tai", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
