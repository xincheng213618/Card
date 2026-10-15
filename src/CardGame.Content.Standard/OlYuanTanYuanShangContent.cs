using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlYuanTanYuanShangContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-yuan-tan-yuan-shang",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddGeneral(new("ol:yuan-tan-yuan-shang","袁谭袁尚·OL","ol-yuan-tan-yuan-shang","ol:neifa","qun",4,[],GeneralGender.Male){CharacterId="character:yuan-tan-yuan-shang",VariantId="ordinary",RulesetId="sanguosha-ol"});
 }
}
