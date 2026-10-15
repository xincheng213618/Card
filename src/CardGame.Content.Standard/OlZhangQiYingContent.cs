using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlZhangQiYingContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ol-zhang-qi-ying",d=>d with{Tags=d.Id=="ol:falu"?SkillTag.Locked:SkillTag.None,ExecutionForms=SkillExecutionForm.State|SkillExecutionForm.Trigger,ActionForms=SkillActionForm.None});
  builder.AddGeneral(new("ol:zhang-qi-ying","张琪瑛·OL","ol-zhang-qi-ying","ol:falu","qun",3,["ol:dianhua","ol:zhenyi"],GeneralGender.Female){CharacterId="character:zhang-qi-ying",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
