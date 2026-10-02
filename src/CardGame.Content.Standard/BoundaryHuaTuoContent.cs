using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryHuaTuoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("batch9-support","boundary:jijiu"));
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"boundary-hua-tuo",d=>d with{ExecutionForms=SkillExecutionForm.None,ActionForms=SkillActionForm.Active});
        builder.AddGeneral(new ContentGeneralDefinition("boundary:hua-tuo","界华佗","boundary_hua_tuo","boundary:jijiu","qun",3,["boundary:chuli"],GeneralGender.Male){CharacterId="character:hua-tuo",VariantId="boundary",RulesetId="sanguosha-ol"});
    }
}
