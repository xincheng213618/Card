using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinZhouFeiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"classic-zhou-fei",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
        builder.AddGeneral(new ContentGeneralDefinition("classic:zhou-fei","周妃","zhou_fei","classic:liangyin","wu",3,["classic:kongsheng"],GeneralGender.Female){CharacterId="character:zhou-fei",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
