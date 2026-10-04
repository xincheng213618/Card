using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryMaDaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"boundary-ma-dai",d=>d with { ExecutionForms=SkillExecutionForm.Trigger });
        builder.AddGeneral(new("boundary:ma-dai","界马岱","boundary_ma_dai","classic:mashu","shu",4,["boundary:qianxi"],GeneralGender.Male)
        {CharacterId="character:ma-dai",VariantId="boundary",RulesetId="sanguosha-ol"});
    }
}
