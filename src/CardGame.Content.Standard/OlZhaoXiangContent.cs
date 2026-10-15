using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlZhaoXiangContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-zhao-xiang",d=>d with{ExecutionForms=SkillExecutionForm.Trigger,Tags=d.Id=="ol:fuhan"?SkillTag.Limited:SkillTag.None});
        b.AddGeneral(new("ol:zhao-xiang","赵襄·OL","ol-zhao-xiang","ol:fanghun","shu",4,["ol:fuhan"],GeneralGender.Female)
        {CharacterId="character:zhao-xiang",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
