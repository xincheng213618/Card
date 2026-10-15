using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlLiJueContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-li-jue",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
  b.AddGeneral(new("ol:li-jue","李傕·OL","ol-li-jue","ol:langxi","qun",6,["ol:yisuan"],GeneralGender.Male){CharacterId="character:li-jue",VariantId="ordinary",RulesetId="sanguosha-ol",InitialHp=4});
 }
}
