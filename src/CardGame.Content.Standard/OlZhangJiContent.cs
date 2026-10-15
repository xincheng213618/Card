using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlZhangJiContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ol-zhang-ji",d=>d with{Tags=d.Id=="ol:tunjun-sp"?SkillTag.Limited:SkillTag.None,ExecutionForms=SkillExecutionForm.Trigger,ActionForms=SkillActionForm.Active});
  builder.AddGeneral(new("ol:zhang-ji","张济·OL","ol-zhang-ji","ol:lueming","qun",4,["ol:tunjun-sp"],GeneralGender.Male){CharacterId="character:zhang-ji",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
