using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlBuZhiContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-bu-zhi",d=>d with{ExecutionForms=d.Id=="ol:hongde"?SkillExecutionForm.Trigger:SkillExecutionForm.None,ActionForms=d.Id=="ol:dingpan"?SkillActionForm.Active:SkillActionForm.None});
        b.AddGeneral(new("ol:bu-zhi","步骘·OL","ol-bu-zhi","ol:hongde","wu",3,["ol:dingpan"],GeneralGender.Male)
        {CharacterId="character:bu-zhi",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
