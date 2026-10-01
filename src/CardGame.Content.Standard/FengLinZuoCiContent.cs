using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinZuoCiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"classic-zuo-ci",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
        builder.AddGeneral(new ContentGeneralDefinition("classic:zuo-ci","左慈","zuo_ci","classic:huashen","qun",3,["classic:xinsheng"],GeneralGender.Male){CharacterId="character:zuo-ci",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
