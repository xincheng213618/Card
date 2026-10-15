using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlTangZiContent
{
 internal static void Register(IContentRegistryBuilder builder)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ol-tang-zi",d=>d with{Tags=SkillTag.Locked,ExecutionForms=SkillExecutionForm.State|SkillExecutionForm.Trigger,ActionForms=SkillActionForm.None});
  builder.AddGeneral(new("ol:tang-zi","唐咨·OL","ol-tang-zi","ol:xingzhao","wei",4,[],GeneralGender.Male){CharacterId="character:tang-zi",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
