using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlLiuQiContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-liu-qi",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddGeneral(new("ol:liu-qi","刘琦·OL","ol-liu-qi","ol:wenji","qun",3,["ol:tunjiang"],GeneralGender.Male){CharacterId="character:liu-qi",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
