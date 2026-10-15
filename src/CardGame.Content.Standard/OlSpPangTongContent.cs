using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlSpPangTongContent
{
 internal static void Register(IContentRegistryBuilder b)
 {
  EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-pang-tong-sp",d=>d with{Tags=d.Id=="ol:songsang"?SkillTag.Limited:d.Id=="ol:zhanji"?SkillTag.Locked:SkillTag.None,ExecutionForms=d.Id=="ol:guolun"?SkillExecutionForm.None:SkillExecutionForm.Trigger,ActionForms=d.Id=="ol:guolun"?SkillActionForm.Active:SkillActionForm.None});
  b.AddGeneral(new("ol:pang-tong-sp","SP庞统·OL","ol-pang-tong-sp","ol:guolun","wu",3,["ol:songsang"],GeneralGender.Male){CharacterId="character:pang-tong",VariantId="sp",RulesetId="sanguosha-ol"});
 }
}
