using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlZhugeGuoContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-zhuge-guo",d=>d with{ExecutionForms=SkillExecutionForm.Trigger,Tags=d.Id=="ol:yuhua"?SkillTag.Locked:SkillTag.None});
        b.AddGeneral(new("ol:zhuge-guo","诸葛果·OL","ol-zhuge-guo","ol:qirang","shu",3,["ol:yuhua"],GeneralGender.Female){CharacterId="character:zhuge-guo",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
