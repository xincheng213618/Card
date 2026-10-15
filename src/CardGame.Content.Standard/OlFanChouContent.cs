using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlFanChouContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-fan-chou",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddGeneral(new("ol:fan-chou","樊稠·OL","ol-fan-chou","ol:xingluan","qun",4,[],GeneralGender.Male){CharacterId="character:fan-chou",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
