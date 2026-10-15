using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlCaoChunContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-cao-chun",d=>d with{ExecutionForms=SkillExecutionForm.Trigger|SkillExecutionForm.State,ActionForms=SkillActionForm.Active});
  b.AddGeneral(new("ol:cao-chun","曹纯·OL","ol-cao-chun","ol:shanjia","wei",4,[],GeneralGender.Male){CharacterId="character:cao-chun",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
