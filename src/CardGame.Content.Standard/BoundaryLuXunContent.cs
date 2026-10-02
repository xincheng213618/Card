using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryLuXunContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"boundary-lu-xun",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
        builder.AddGeneral(new ContentGeneralDefinition("boundary:lu-xun","界陆逊","boundary_lu_xun","boundary:qianxun","wu",3,["boundary:lianying"],GeneralGender.Male){CharacterId="character:lu-xun",VariantId="boundary",RulesetId="sanguosha-ol"});
    }
}
