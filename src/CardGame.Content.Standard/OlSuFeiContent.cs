using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlSuFeiContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-su-fei",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddGeneral(new("ol:su-fei","苏飞·OL","ol-su-fei","ol:lianpian","wu",4,[],GeneralGender.Male){CharacterId="character:su-fei",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
